# Phase 27：mac の実機確認の記録（Issue #22）

- 日付：2026-10-07 ／ 環境：Apple Silicon、macOS（Darwin 25.6）、.NET SDK 10.0.201、Xcode（`xcode-select -p` が通る）、Apple Git 2.50.1、Cursor（Hobby）
- 会話ログ・イベントは**構造（種類・件数・キー・形）だけ**を数えた。本文・コマンド・パスの中身は記録していない。Miharikun のコードとテストは変えていない。

## 1. テストの基準（Phase 29-2 の入力）
`dotnet test tests/Miharikun.Tests`（mac）：**失敗 117、合格 930、スキップ 5、合計 1052**。Windows は 1047 合格・スキップ 5（合計 1052）。同じ 1052 件のうち、mac で 117 件が落ちる。

落ちたテストを、クラスごとに数えた（Theory は 1 ケース = 1 件）。

| クラス | 件数 | 原因の見立て |
|---|---|---|
| `CursorTranscriptRealtimeTests` | 25 | `C:\` を使うパス／一時フォルダの実パス・論理パスの差（`/var` → `/private/var`）の両方が疑わしい。29-2 で切り分ける |
| `ClaudeLocationsTests` | 14+ | `C:\` のパス・フォルダ名の規則のテスト（Windows の形） |
| `ClaudeSessionSourceTests` | 8 | 同上 |
| `TranscriptImporterTests` | 7+ | slug のテストが `C:\Users\…` |
| `GitClientTests` | 7 | 一時フォルダが `/var` → `/private/var` のリンクで、実パス・論理パスが食い違う（計画 29-2 の想定どおり。製品のコードを直す＝29-4） |
| `MarkdownRendererTests` | 5 | 未分類（29-2 で原因を見る） |
| `HookInstallerPlacementTests` | 4+（Theory ケース多数） | `C:/dev/…` の形 |
| `ProjectSettingsTests` / `ProjectPathTests` / `HookDirInputTests` | 各 3+ | Windows のパス |
| `ProjectMemoStoreTests` / `ProjectEventStoreImportTests` / `ProjectEventStoreCharacterizationTests` / `HookInstallerTests` / `DocumentLinkRuleTests` / `AppSettingsHookDirTests` | 各 2〜6 | Windows のパス |
| `GitHeadAtTests`・`HookRunnerTests` | 各 1 | 実パス・論理パス／Windows のパス |

- 数は「テスト名の行」の集計で、Theory のケース違いは別の行になる。正確な内訳は、29-2 の始めに取り直す（28 でテストが増えているため、計画のとおり）。
- 結論：Windows の意味を確かめるテスト・たまたま `C:\` のテスト・mac で本当に動かないもの（`GitClientTests`・`GitHeadAtTests`）の 3 つに分かれる見込み（計画 29-2）。

## 2. 14.3 の項目ごとの結果

| 項目 | 結果 | 確かめた方法・構造 |
|---|---|---|
| Hobby で `hooks.json` の command が呼ばれる | **確認済み：呼ばれる** | `~/.cursor/hooks.json` に 13 イベントを手で登録し、Cursor で 1 回会話。`beforeSubmitPrompt`・`afterAgentThought`×4・`preToolUse`・`postToolUse`・`afterAgentResponse`・`stop` が届いた（9 行）。`sessionStart` は別の会話 id（`empty-state-draft`）で 2 行 |
| command の起動・空白を含むパス | **確認済み：動く** | Hook を `~/Library/Application Support/Miharikun/bin/Miharikun.Hook`（空白入り）に置き、引用符つきの command（`HookInstaller.BuildCommand` と同じ形）で Cursor から呼ばれた。`hook-error.log` は作られていない |
| 入力の JSON の形 | Windows の想定の形（突き合わせは未実施） | 行の最上位キー：`v`・`agent`・`received_at`・`event`・`payload`（＋`stop` だけ `git`＝`branch`・`head`）。payload のキーは `conversation_id`・`generation_id`・`workspace_roots`・`transcript_path`・`model`・`cursor_version` ほか。イベントごとのキーは上の 6 種類を確認（Windows の実データとの突き合わせは未実施） |
| `workspace_roots` の形 | **確認済み：`/Users/…` の絶対パス** | 1 件。実パスと一致（symlink ではない）。`sessionStart` では空の配列 |
| 日本語 | **確認済み：壊れない** | `prompt` に日本語を含む入力で、UTF-8 のまま保存され、U+FFFD なし。非 ASCII の文字列はすべて NFC |
| `transcript_path` | 最初の 7 イベントは null、`afterAgentResponse` と `stop` で文字列 | 形は `~/.cursor/projects/<slug>/agent-transcripts/<uuid>/<uuid>.jsonl`。ファイルは存在する |
| Cursor の会話ログの場所と slug | **確認済み：仮説どおり** | `projects/<slug>/agent-transcripts/<uuid>/<uuid>.jsonl`。slug は「英数字以外の連続を `-` にして前後の `-` を削る」規則と、`workspace_roots` から作ったものが一致（`/Users/…/repo/github.com/po-oq/…` → `Users-…-po-oq-…`）。27 個ある `~/.cursor/projects/` の下には、数字だけの名前（タイムスタンプ形式）も混在する |
| Claude Code のフォルダ名と `cwd` | **確認済み** | `/Users/x/repo` → `-Users-x-repo`（英数字以外はすべて `-`。`.claude/worktrees` の `/`・`.` も `-` になり `--claude-worktrees-…`）。同規則で作った名前が実在するフォルダと一致（2 件）。`cwd` は `/Users/…` で始まり、`\` を含まない（1 本のログを構造だけ確認。種類は user・assistant・system など 9 種で、壊れた行 0） |
| `xcode-select -p` / git | **確認済み** | `/Applications/Xcode.app/Contents/Developer`。`/usr/bin/git`（Apple Git 2.50.1）。Hook の `git`（`branch`・`head`）は `stop` で取れた |
| symlink 経由のフォルダ | **一部確認済み** | git の `--show-toplevel` は、論理パス（リンク）から入っても**実パスを返す**。Hook は `workspace_roots` を**そのまま**（論理パスのまま）保存する（手動で流して確認）。Claude の `cwd`・Cursor が実際に送る `workspace_roots` が、リンク経由のとき論理・実のどちらかは**保留** |
| 日本語のファイル名の形（NFD か） | **一部確認済み** | APFS は、NFD で作った名前を NFD のまま保持し、同じ文字の NFC の名前で書くと**同じファイル**になる（ファイルは 1 つ）。Finder で作った名前の形は**保留**（NFC で比べる方針は変わらない） |
| 「隔離」の印の付いた Hook を Cursor が呼んだとき | **保留** | 任意項目⑤は未実施。いま置いた Hook は `com.apple.provenance` の印だけで、`com.apple.quarantine` は無い |
| NativeAOT の Hook の発行（mac） | **確認済み** | `dotnet publish src/Miharikun.Hook -c Release -r osx-arm64`：約 7 秒、Mach-O arm64、約 4.4 MB |

## 3. 7.1 の表への対応（Phase 29 で直す内容）
- Hobby でも Hook は呼ばれる → **会話ログの取り込みへの切り替えは要らない**。Hook の mac 対応（29-3）と配布をそのまま進める。
- 空白を含むパスで動く → **既定の置き場所は今の規則（データの `bin/`）のまま**。`~/.miharikun/bin` への変更は不要。
- `workspace_roots`・slug・Claude のフォルダ名は仮説どおり → `ProjectPath`・`CursorTranscriptImporter`・`ClaudeFolderName` の規則は mac の形の行をテストに足すだけ（29-2）。
- 日本語は壊れない → `PayloadRecovery` は mac では何もしない。
- 「隔離」の印・Finder の NFD・リンク経由の `cwd` は保留。予定どおり、印の解除と `--probe`（29-3）、NFC の比較・実パスとの比較（29-4）を入れる（害は無い）。

## 4. 実環境に残ったもの（2026-10-07 時点）
- `~/.cursor/hooks.json`：**新規作成**（13 イベント、command は引用符つき。Phase 27 の前は存在しなかったので、バックアップなし）。
- `~/Library/Application Support/Miharikun/`：`bin/Miharikun.Hook`、`events/cursor/*.jsonl`（確認の会話 1 本と `empty-state-draft` 1 本）。
- 確認の途中で、`--probe`（未実装）つきの Hook を、空の入力で実データの置き場所に向けて 1 回動かしてしまい、`logs/hook-error.log`（2 行）ができた。内容は私の入力のエラーだけで、確認のうえ削除した。
- 残すか戻すかは利用者に確認する（計画 27-1 ⑥）。
