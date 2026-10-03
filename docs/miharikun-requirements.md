# Miharikun（みはりくん）要件定義・実装方針（MVP / PoC）

> このドキュメントは実装担当（Claude Code）向け。決定事項はすべて確定済み。
> 「要検証」と書かれた項目は Step 0 の実機検証で確定させ、結果をこのファイルに追記すること。

---

## 1. 目的・背景

社内で Cursor IDE のチャット（Agent）を使って開発している。以下が困りごと。

- チャットのログが長く、あとから見ると全部読まないと「いまどうなっているか」がわからない
- IDE のチャット欄は狭くタブを多く開けない。History はタイトル数文字しか出ず探せない
- 放置したセッションを再開する／似た作業の参考にするために、セッションへ一言残したい
- 実装計画 md をフェーズ単位で複数セッションに分けて実行している（計画との紐付けは MVP 対象外）

### 発想の元になったアイデア
- https://x.com/tokkyo/status/2105860432405954572
- https://x.com/tokkyo/status/2105862649540173872
- https://x.com/tokkyo/status/2104584770856599989

**ゴール**：プロジェクトごとに起動するデスクトップツールで、そのプロジェクトの Cursor セッションを
「一覧で探せる」「状態が3秒でわかる」「概要とメモを残せる」ようにする。

## 2. 用語

| 用語 | 意味 |
|---|---|
| セッション | Cursor のチャット1つ。`conversation_id` で識別 |
| イベント | Cursor Hooks から hook exe に渡される1回分の JSON |
| ターン | ユーザー入力1回（`beforeSubmitPrompt`）〜 `stop` まで |
| 概要 | チャットに1行要約させた返事を取り込んだもの（セッションごとに1つ） |
| メモ | 人間が自由に書くセッションごとのメモ |
| プロジェクトメモ | プロジェクト単位の自由メモ（MVP 対象外） |

## 3. 前提・制約（確定）

- 対象は **Cursor IDE のチャットのみ**。cursor-agent CLI は社内で使用不可のため対象外
- **ツール単体で動作**させる：LLM 呼び出しはしない
- Cursor の `state.vscdb` は**読まない**（非公式・巨大・破損リスク）。コピーも禁止
- トークン数・コスト・レート制限は取得手段がないため**扱わない**
- 管理者権限は不要であること（ユーザー階層 `~/.cursor/hooks.json` のみ使用）
- 対象 OS は Windows。開発は自宅、会社では GitHub Release から exe をダウンロードして使う

## 4. スコープ

### MVP 対象
- ダッシュボードタブ（一覧／詳細／右ペイン）
- Hook exe（イベント記録）と、その導入機能
- 概要・メモ・タイトルの手動編集

### MVP 対象外（タブだけ置く／後で実装）
- **ドキュメントタブ**：プロジェクト配下の .md / .html をツリー表示、クリックで右にプレビュー
- **メモタブ**：プロジェクト単位の自由メモ（TextBox 1つ）
- 実装計画 md とセッションの紐付け、完了判定ミニ表示（✓✓✓✗）、Cursor でチャットを開くボタン、タスク・課題の更新、承認待ちの判定

### 不採用
LLM 要約・要対応抽出、ctx% バー、PID、分類タブ、トークン/コスト/レート制限、関連セッション、テスト項目一覧

## 5. システム構成

```
Cursor IDE ──(stdin JSON)──▶ Miharikun.Hook.exe --agent cursor ──append──▶ events\cursor\{sessionId}.jsonl（生データ）
                                                                                   ▲ watch/tail
             Miharikun.exe (WPF) ── CursorAgent.Normalize() ──▶ 共通イベント ──▶ 状態判定・画面
                                  └─ read/write ─▶ meta\cursor\{sessionId}.json
```

### ソリューション構成
| プロジェクト | 種別 | 役割 |
|---|---|---|
| `Miharikun.Core` | クラスライブラリ | `IAgent`、共通イベントモデル、`CursorAgent`、状態判定、パス正規化。**AOT 互換で書く**（System.Text.Json はソースジェネレーター使用、リフレクション禁止） |
| `Miharikun.Hook` | コンソール, NativeAOT | stdin を受けて JSONL に追記するだけ。高速起動が最優先 |
| `Miharikun` | WPF | 画面。WPF-UI 4.x を使用 |
| `Miharikun.Tests` | xUnit | Core のテスト |

- ターゲット：.NET 10（`net10.0` / `net10.0-windows`）
- UI ライブラリ：WPF-UI（lepoco/wpfui）4.x。選定理由は継続的に保守されていること
- MVVM：CommunityToolkit.Mvvm
- 配布：`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`（App）、NativeAOT（Hook）。両 exe を zip にして GitHub Release に置く

## 5.1 エージェント抽象化（実装方針）

将来 Claude Code / Codex / GitHub Copilot にも対応する予定のため、**エージェント固有の処理を1クラスに閉じ込める**構成にする。
ただし MVP の実装対象は **Cursor のみ**。他エージェントは PoC 結果を見て判断するため、この章以上の設計はしない。

### ルール
1. **共通 interface `IAgent` を定義し、エージェントごとに1クラス実装する**（MVP は `CursorAgent` のみ）
2. **App（状態判定・画面・検索）は共通イベント `AgentEvent` だけを扱う**。Cursor の生 JSON のフィールド（`stop.status`、`workspace_roots` 等）を App 側で直接参照してはならない
3. **Hook exe は生データを保存するだけ**。正規化（生 JSON → 共通イベント）は App が読み込み時に `IAgent.Normalize` で行う（正規化を修正すれば過去データにも反映される）
4. **能力差は `Capabilities` フラグで表現**し、UI はフラグを見て表示の有無を決める
5. **共通の抽象基底クラスは作らない**。2つ目のエージェントを追加するときに、実際に重複した処理だけを切り出す

### interface（目安。実装時に調整してよい）
```csharp
public interface IAgent
{
    string Id { get; }                      // "cursor"
    string DisplayName { get; }
    AgentCapabilities Capabilities { get; }

    // Hook exe 側（NativeAOT 互換で実装すること）
    string? GetEventName(JsonNode payload);
    string? GetSessionId(JsonNode payload);
    bool NeedsGitSnapshot(string eventName);
    HookResponse RespondToHook(string eventName, JsonNode payload);   // stdout と終了コード（7章の表）

    // App 側
    IReadOnlyList<string> GetWorkspaceRoots(RawEventRecord raw);
    IEnumerable<AgentEvent> Normalize(RawEventRecord raw);
}

[Flags]
public enum AgentCapabilities
{
    None = 0, RealtimeHooks = 1, ToolEvents = 2, AssistantText = 4, Thinking = 8,
    Compaction = 16, Subagents = 32, FileEdits = 64, SessionEnd = 128,
    TurnStatus = 256,     // 完了/中断/エラーを区別できる
    Transcript = 512, TokenUsage = 1024,
}

public sealed record SessionKey(string AgentId, string SessionId);

public enum AgentEventKind
{
    SessionStarted, SessionEnded, PromptSubmitted, TurnEnded,
    ToolStarted, ToolSucceeded, ToolFailed,
    AssistantMessage, AssistantThought,
    SubagentStarted, SubagentStopped, FileEdited, Compacted,
}

public sealed record AgentEvent(
    SessionKey Session, long Seq, DateTimeOffset At, AgentEventKind Kind,
    string? Text = null, string? ToolName = null, string? ToolUseId = null,
    string? Command = null, int? ExitCode = null, TurnOutcome? Outcome = null,
    string? FilePath = null, CompactionInfo? Compaction = null,
    GitSnapshot? Git = null, string? Model = null);

public enum TurnOutcome { Completed, Aborted, Error, Unknown }
```

### Cursor のイベント対応表（`CursorAgent.Normalize`）
| Cursor | AgentEventKind | 備考 |
|---|---|---|
| sessionStart | SessionStarted | git 付き |
| sessionEnd | SessionEnded | reason を保持 |
| beforeSubmitPrompt | PromptSubmitted | Text = prompt |
| stop | TurnEnded | Outcome = status（completed/aborted/error）、git 付き |
| preToolUse | ToolStarted | ToolUseId, ToolName, Command |
| postToolUse | ToolSucceeded | Shell は tool_output の exitCode を ExitCode に |
| postToolUseFailure | ToolFailed | |
| afterAgentResponse / afterAgentThought | AssistantMessage / AssistantThought | |
| subagentStart / subagentStop | SubagentStarted / SubagentStopped | |
| afterFileEdit | FileEdited | FilePath |
| preCompact | Compacted | 使用率・trigger 等 |

Cursor の Capabilities：`RealtimeHooks | ToolEvents | AssistantText | Thinking | Compaction | Subagents | FileEdits | SessionEnd | TurnStatus`（Transcript は Step 0 の結果次第、TokenUsage はなし）

### 将来の拡張ポイント（参考・MVP では実装しない）
- 他エージェントは `IAgent` を実装したクラスを追加し、Hook exe を `--agent <id>` で呼び分ける
- エージェントごとに hook の設定場所・イベント名・承認系 hook の返し方が異なるため、それらは各クラスで吸収する
- トークン使用量を取得できるエージェントが入った場合は、`TokenUsage` フラグを持つセッションに限りトークン欄を表示する
- hook 導入処理・過去セッション取り込みも、2つ目のエージェント追加時に interface 化を検討する

## 6. 保存データ

ルート：`%LOCALAPPDATA%\Miharikun\`

| パス | 内容 | 書き手 |
|---|---|---|
| `events\{agentId}\{sessionId}.jsonl` | 生イベント1件1行（Cursor は sessionId = conversation_id） | Hook のみ（追記専用） |
| `events\{agentId}\_app.jsonl` | セッション ID を持たないイベント（MVP では未登録） | Hook |
| `meta\{agentId}\{sessionId}.json` | タイトル・概要・メモ | App |
| `settings.json` | テストコマンドのパターン等 | App |
| `logs\hook-error.log` | Hook の例外ログ | Hook |

### イベント行スキーマ（Hook が書く）
```json
{
  "v": 1,
  "agent": "cursor",
  "received_at": "2026-10-03T11:19:31.123+09:00",
  "event": "beforeSubmitPrompt",
  "git": { "branch": "feature/x", "head": "56c958d..." },
  "payload": { ...Cursorから受け取ったJSONそのまま... }
}
```
- `git` は `sessionStart` と `stop` のときだけ付与。それ以外は省略
- `payload` は加工せず丸ごと保存（後から仕様が増えても対応できるように）

### メタスキーマ（App が書く）
```json
{
  "v": 1,
  "title": "テストタブの追加",
  "titleIsManual": true,
  "summary": { "text": "...", "importedAt": "...", "sourceTurn": 4 },
  "previousSummary": { "text": "...", "importedAt": "...", "sourceTurn": 2 },
  "memo": "6〜8まで。続きは明日",
  "updatedAt": "..."
}
```
- 書き込みは一時ファイル → `File.Replace` でアトミックに。複数起動の競合は後勝ちで可

## 7. Hook exe 仕様

### 登録するイベント
`sessionStart`, `sessionEnd`, `beforeSubmitPrompt`, `preToolUse`, `postToolUse`, `postToolUseFailure`,
`subagentStart`, `subagentStop`, `afterFileEdit`, `afterAgentResponse`, `afterAgentThought`, `preCompact`, `stop`

（`beforeReadFile` 等は MVP では登録しない）

### 起動
`Miharikun.Hook.exe --agent cursor`。`--agent` の値で `IAgent` 実装を選ぶ（MVP は cursor のみ。未知の値は exit 1）

### 処理
1. stdin を UTF-8 で全読み → JSON パース → `hook_event_name`, `conversation_id` を取得
2. `sessionStart` / `stop` のときだけ git 情報を取得
   - 対象は `workspace_roots[0]`。`git -C <root> rev-parse --abbrev-ref HEAD` と `rev-parse HEAD`
   - 各コマンドのタイムアウト 1.5 秒。git リポジトリでない／失敗時は `git` を省略
3. `events\cursor\{conversation_id}.jsonl` に1行追記（`FileShare.Read` で開き、IOException は 20ms 間隔で最大10回リトライ）
4. 出力ルール（`CursorAgent.RespondToHook` に実装）：

| イベント | 出力 | 終了コード |
|---|---|---|
| 承認系（`preToolUse`, `subagentStart`） | **何も出力しない** | **1**（失敗扱い＝判定に参加しない） |
| `beforeSubmitPrompt` | `{"continue":true}` | 0 |
| その他 | `{}` | 0 |
| 例外発生時 | 何も出力しない | 1（fail-open）。`logs\hook-error.log` に記録 |

> **理由**：承認系 hook が `allow` を返すと Cursor 本来の確認ダイアログに影響する可能性がある（要検証）。
> exit 0 で不正 JSON を返すとアクションがブロックされるため、記録専用 hook は exit 1 で抜ける。

### 性能要件
- 1回の処理は git なしで 50ms 以内、git ありで 3 秒以内（`hooks.json` の `timeout` は 5 秒）
- `postToolUse` 等の高頻度イベントでは外部プロセスを起動しない

## 8. Hook の導入機能（App）

- 起動時に `%USERPROFILE%\.cursor\hooks.json` を確認し、Miharikun の hook が未登録なら導入ダイアログを出す
- 導入処理：
  1. 同梱の `Miharikun.Hook.exe` を `%LOCALAPPDATA%\Miharikun\bin\` にコピー
  2. 既存 `hooks.json` を `hooks.json.bak-{日時}` にバックアップ
  3. 既存設定を**壊さずにマージ**（各イベントの配列に自分のエントリを追加。重複は追加しない）
  4. `version` がなければ `1` を設定
- コマンドはフルパス（`/` 区切り）。スペースを含む場合の扱いは Step 0 で確定
- 「hook を削除」メニューも用意（自分のエントリだけ取り除く）

## 9. App：起動とプロジェクト判定

- 起動：`Miharikun.exe [フォルダ]`。省略時はカレントディレクトリ
- プロジェクト一致判定：`Path.GetFullPath` → 末尾区切り除去 → `StringComparison.OrdinalIgnoreCase` で**完全一致**。
  セッションの `workspace_roots` のどれか1つでも一致すれば対象
- タイトルバーに対象フォルダを表示。複数起動を前提とし、App は events を**読み取り専用**で扱う

### ファイル監視
- `FileSystemWatcher` で `events\` を監視、300ms デバウンス。取りこぼし対策で 3 秒ごとのポーリングも併用
- ファイルごとに読み取り済みオフセットを保持し、追記分だけ読む（末尾の不完全な行は次回に回す）
- 初回起動時は全ファイルを読み、`workspace_roots` が一致するものだけをメモリに保持

## 10. 状態判定ルール（Core）

> 状態判定・派生値は**共通イベント（AgentEvent）に対して**実装する。以下の Cursor イベント名は対応表（5.1）経由で読み替えること。
> `TurnOutcome.Unknown` で終わったターン（将来の他エージェント用）は 🟢 として扱う。

### セッション状態（最新イベントで上書き）
| 状態 | 表示 | 条件 |
|---|---|---|
| Running | 🔵 実行中 | 最後のターン系イベントが `beforeSubmitPrompt` 以降で、まだ `stop` が来ていない |
| YourTurn | 🟢 ボスの番 | 最後の `stop.status == "completed"` |
| Aborted | 🟡 中断 | 最後の `stop.status == "aborted"` |
| Error | 🔴 エラー | 最後の `stop.status == "error"` |
| Closed | ⚪ 閉じた | 最後のイベントが `sessionEnd`（reason 問わず） |
| Imported | ⚪ 閉じた（導入前） | transcript からの取り込み（11章） |

- `sessionEnd` の後に `beforeSubmitPrompt` 等が来たら（再開）その時点の状態に戻す

### 実行中ツールの管理
- `preToolUse` で `tool_use_id` を実行中リストに追加
- `postToolUse` / `postToolUseFailure` で該当 ID を除去
- `stop` / `sessionEnd` を受けたら残りを「終了（結果不明）」として全除去（保険）

### 派生値
| 項目 | 算出 |
|---|---|
| タイトル | メタの手動タイトル優先。なければ最初の `beforeSubmitPrompt.prompt` の先頭40文字（改行は空白に） |
| 依頼数 / ターン数 | `beforeSubmitPrompt` の件数 / `stop` の件数 |
| 開始時刻 | `sessionStart` の received_at（なければ最初のイベント） |
| 最後の動き | 最後のイベントの received_at |
| モデル | 最後のイベントの `model_id`（なければ `model`）、`model_params` |
| ブランチ | 最新の `git.branch` |
| ツール呼び出し数 | `postToolUse` + `postToolUseFailure` の件数 |
| サブエージェント | 起動中 = `subagentStart` − `subagentStop`、累計 = `subagentStart` 件数 |
| 圧縮 | `preCompact` の件数と直近の `context_usage_percent`・`trigger` |
| 継続時間 | 開始〜最後の動き（`sessionEnd.duration_ms` があればそれ） |
| 変更ファイル | `afterFileEdit.file_path` の重複除去 |
| テスト実行 | `postToolUse` で `tool_name == "Shell"` かつ command が設定のパターンに一致。成否は `tool_output` の `exitCode == 0` |
| コミット | App が `git log --oneline {開始時head}..{最新stop時head}` を実行（Hook ではやらない） |
| 未コミット | App が `git status --porcelain` を実行し、変更ファイルと突合（シェル経由の変更は対象外と画面に注記） |

テストコマンドの既定パターン（settings.json で変更可）：
`dotnet test`, `npx playwright test`, `npm test`, `npm run test`, `vitest`, `jest`, `pytest`

## 11. 導入前の過去セッション（条件付き）

- Step 0 で「transcript のファイル名 UUID = conversation_id」と「プロジェクト ↔ slug の対応規則」が確認できた場合のみ実装
- 読み込み元：`%USERPROFILE%\.cursor\projects\<slug>\agent-transcripts\` 配下の
  `<uuid>\<uuid>.jsonl` と旧形式 `<uuid>.jsonl` の両方。`subagents\` 配下は除外
- hook のイベントが存在する conversation_id はスキップ（重複防止）
- 状態は Imported、時刻はファイルの更新日時、タイムラインは入力と返事のみ（時刻なし）
- 確認できなかった場合は MVP から外す
- **実装（Phase 9）**：Step 0 で前提を確認できたので実装した（14.1 参照）。`CursorTranscriptImporter`（Core）が transcript を共通イベントにし、`ProjectEventStore` が起動後の最初の `Refresh` で1回だけ取り込む。
  - プロジェクトとの対応は slug（英数字以外の連なりを `-` にして、大文字小文字を無視して比較。パスのスラッシュ形式も可）。記号・日本語を含むパスの slug 規則は未確認。
  - 取り込むのは入力（`<user_query>` の中身）と、assistant の本文だけ。ツール呼び出しは対象外。時刻はすべてファイルの更新日時なので、タイムラインでは時刻を出さない。
  - 「最近の入力」には出さない（時刻が実際のものではないため）。「最近閉じたセッション」にも出ない（`sessionEnd` がない）。
  - 取り込んだ後に同じ conversation_id の hook イベントが現れたら（導入後に再開したなど）、hook のイベントに切り替える。化けて記録済みの本文の補正は未実装。

## 12. 画面要件（ダッシュボードタブ）

ワイヤー：`miharikun-wire.html` を正とする。3ペイン構成（左 一覧 / 中央 詳細 / 右 メモ・タイムライン）。

### ワイヤーの読み方（重要）
ワイヤーは**レイアウトと表示項目を決めるための設計資料**であり、そのまま再現する画面ではない。以下は**実装しないこと**。
- 取得元バッジ（H / T / G / A / M の色付きラベル）と、その凡例（黄色の帯）
- グレーの小さい注記テキスト（「取得元：〜」「状態判定：〜」「※〜」などの説明文）
- 「⏸ あとで相談」枠と「不採用」の一覧
- サンプルデータ（タイトル・時刻・件数などはすべてダミー）
- 見た目（色・フォント・枠線）はラフ。最終的な見た目は WPF-UI の Fluent スタイルに合わせる

ワイヤーと本書が食い違う場合は**本書を優先**する。注記に書かれた算出ロジックは本書 10章・12章に記載済み。

### 12.1 ヘッダー
- タブ：ダッシュボード / ドキュメント（MVP 外、プレースホルダー表示）/ メモ（MVP 外、プレースホルダー表示）
- 対象フォルダパス

### 12.2 左：セッション一覧
- 状態別件数（🔵🟢🟡🔴⚪）
- 全文検索ボックス：対象は全プロンプト、概要、メモ、タイトル、変更ファイル名。インクリメンタル、大文字小文字無視
- フィルタ：実行中のみ / 未コミットあり / メモあり
- 並び順：最後の動きの降順
- カード表示項目：状態バッジ、タイトル（✏️でリネーム）、依頼数・最後の動き・モデル・圧縮回数・ブランチ、
  3行サマリー（12.4 と同じ）、概要の先頭、メモ（あれば）
- カードクリックで選択 → 中央・右ペインを切り替え

### 12.3 中央：セッション詳細
1. **ヘッダー**：タイトル（✏️）、状態、ID 先頭8桁、開始時刻、ブランチ、モデル
2. **セッション概要**：本文、取り込み日時・元ターン番号、ボタン「💬 最後の返事を概要に取り込む」「✏️ 編集」「↩ 1つ前に戻す」
   - 取り込み：最後の `afterAgentResponse.text` を概要にする。既存の概要は `previousSummary` に退避（1件のみ保持）
   - 戻す：`previousSummary` と入れ替え
3. **いまの状況（3行サマリー）**：
   - 📝 最後に頼んだこと：最後の `beforeSubmitPrompt.prompt`
   - 🔧 最後にやってたこと：実行中ツールがあれば「{tool_name}: {要約}（実行中・経過秒）」、なければ最後の postToolUse
   - 💬 最後の返事：最後の `afterAgentResponse.text` の先頭2行
   - **各行クリックで右のタイムラインの該当イベントへスクロールしてハイライト**。該当種別がフィルタで非表示なら自動で表示に切り替える
4. **完了チェック**：✓/✗ ターン終了（最後が stop）、裏の作業なし（起動中サブエージェント0）、コミット済み（未コミット0）
5. **稼働状態（コストの目安）**：ターン数 / ツール呼び出し数、圧縮回数（直近%・auto/manual）、継続時間、サブエージェント起動中/累計、transcript サイズ（`transcript_path` があれば）。「金額・トークンではない」旨を注記
6. **成果**：コミット件数（展開で一覧）、変更ファイル件数（展開で一覧）、テスト実行回数（成功/失敗、展開でコマンド・所要時間・出力末尾）
7. **ターン一覧**：番号、開始時刻、状態（済み/中断/エラー/実行中）、依頼文先頭

### 12.4 右ペイン
1. **メモ**：TextBox（複数行）。フォーカスアウトで自動保存
2. **タイムライン**：フィルタ（入力 / 返事 / 思考 / ツール / 圧縮）。既定は入力・返事・圧縮が ON
   - 入力 = beforeSubmitPrompt、返事 = afterAgentResponse、思考 = afterAgentThought、
     ツール = postToolUse / postToolUseFailure（実行中は preToolUse）、圧縮 = preCompact、その他 = セッション開始・終了
   - 各項目に時刻。仮想化された ListBox を使用（長いセッション対策）
3. **最近の入力**：このプロジェクトの全セッション横断で beforeSubmitPrompt の新しい順10件。行クリックで左のカードを選択
4. **最近閉じたセッション**：sessionEnd の新しい順5件。行クリックで左のカードを選択

### 12.5 イベントの一意 ID
- App 側で「SessionKey + ファイル内の行番号」を ID とする（AgentEvent.Seq）（3行サマリー → タイムラインのジャンプに使用）

## 13. 非機能要件

- 1プロジェクト100セッション・1セッション5,000イベントで、起動3秒以内・操作がもたつかないこと
- Hook の処理が Cursor の操作感を損なわないこと（7章の性能要件）
- 文字コードはすべて UTF-8（BOM なし）
- 例外で落ちないこと：壊れた行はスキップしてログに残す
- イベントファイルの自動削除は MVP ではしない

## 14. Step 0：実機検証

ダンプ用 hook（`step0-dump-hook/`）を入れて普段どおり作業し、以下を確認して本書に追記する。
本体の hook を実際の Cursor に入れた PoC でも確認を進めており、結果は 14.1 に書く（`[x]` は確認済み、`[ ]` は未確認）。

- [x] Windows で hooks.json の command が実行されるか。`~/.cursor` が `%USERPROFILE%\.cursor` か（フルパス・`/` 区切り・スペースなしで確認。**スペースを含むパスは未確認**）
- [ ] `sessionEnd` の発火条件（タブを閉じる／別チャットに切り替える／ウィンドウを閉じる）と reason の値
- [ ] 閉じたチャットを履歴から再開したとき `sessionStart` が再度来るか（来ていない可能性が高い。**要再確認**）
- [x] transcript のファイル名 UUID と conversation_id が一致するか、Windows での slug の付け方
- [x] `transcript_path` が既定で null にならないか（**null になることがある**。`sessionStart` と最初の `beforeSubmitPrompt`）
- [ ] モデルが Auto のときの `model` / `model_id` の値（**未確認**）
- [x] Ask モードで hook が発火するか
- [ ] 承認系 hook を exit 1 で抜けたとき：アクションが通常どおり進むか（Read / Grep / Glob は進んだ。**Shell の確認ダイアログは未確認**）、Cursor の確認ダイアログが維持されるか、Hooks 出力チャンネルのエラー表示が許容範囲か
- [ ] `postToolUse`（Shell）の `tool_output` に `exitCode` が入るか（**未確認**）。`tool_use_id` が pre/post で一致するかは確認済み

### 14.1 実機で分かったこと（Cursor 3.23.12 / Windows、PoC 中の実測）

確認項目の一部が、PoC（本体の hook を実際の Cursor に入れて動かした結果）で分かった。残りは未確認のまま。

- [x] **hooks.json の command は Windows で実行される**。`C:/Users/…/Miharikun.Hook.exe --agent cursor`（`/` 区切り・スペースなし）で動作。`~/.cursor` は `%USERPROFILE%\.cursor`。スペースを含むパスは未確認。
- [x] **transcript の UUID = conversation_id**。`%USERPROFILE%\.cursor\projects\<slug>\agent-transcripts\<uuid>\<uuid>.jsonl`（`subagents\` あり）。**slug は、パスの英数字以外の連なり（`:\` や `\`）を 1 つの `-` にしたもの**（`C:\zDev\repo\Miharikun` → `c-zDev-repo-Miharikun`）。ドライブ文字の大小は一定ではない（同じ `projects\` に `C-Users-…-Temp-…` もある）ので、比較では大文字小文字を無視する。記号・日本語を含むパスの規則は未確認。→ Phase 9 の前提は満たす。
  - 形式：1行1メッセージ `{"role":"user|assistant","message":{"content":[{"type":"text","text":…}|{"type":"tool_use",…}]}}`。user の本文は `<timestamp>…</timestamp><user_query>…</user_query>` で包まれる。**行ごとの時刻はない**（user メッセージ内の `<timestamp>` のみ）。
- [x] **`transcript_path` は常には入らない**。`sessionStart` と最初の `beforeSubmitPrompt` では空で、ツール系・`stop` 以降で入る（`c:\Users\…\agent-transcripts\<uuid>\<uuid>.jsonl`）。
- [x] **モデル**：Agent の `beforeSubmitPrompt` / `stop` は `model:"grok-4.7-high"`、`model_id:"grok-4.7"`、`model_params:[{id,value},…]`（context / reasoning_effort / fast）。**ツール系など多くのイベントは `model:"default"`**（`model_id` なし）→ 表示では `default` を無視する。`sessionStart` は別の値（`cursor-grok-4.6-medium`）。
- [x] **承認系 hook を無出力・exit 1 で抜けても、`Read` / `Grep` / `Glob` の実行は通常どおり進んだ**。Shell の確認ダイアログへの影響は未確認。
- [x] **`tool_use_id` は pre / post で一致**（実測 6/6）。値に改行を含む（`call-…\nfc_…`）。
- [x] **`tool_output` は文字列**（JSON を文字列にしたもの。Read は `{"file_path":…,"content_length":…}`）。`duration` はミリ秒の小数。Shell の `exitCode` は未確認。
- [x] **`workspace_roots` は `/c:/zDev/repo/Miharikun` の形**（先頭に `/`、ドライブ文字は小文字）。→ `ProjectPath` で先頭の `/` を取り除いて比較する。Hook の `git -C` にも同じ処理が必要だった。
- [x] **日本語環境の Windows では、Cursor が渡す入力の日本語が壊れる（Cursor 側の不具合。原因と回避策を特定）**。
  - 症状：`内容教えて` → `蜀・ｮｹ謨吶∴縺ｦ`（UTF-8 のバイト列を CP932 として読み替えた文字列。元には戻せない）。さらに文字列中の `\` `"` が失われて JSON として読めなくなることがある（長い日本語を含む `afterAgentResponse` / `afterAgentThought` など）。入力の先頭には UTF-8 の BOM が付く。
  - 原因：Cursor は Windows で hook を次のように起動する（Cursor 内部のログには正しい入力が出ている）。
    `$OutputEncoding = [System.Text.Encoding]::UTF8; Get-Content -LiteralPath '%TEMP%\cursor-hook-payload-….json' -Raw | & { $input | <command> }`
    一時ファイルは BOM なしの UTF-8 だが、Windows PowerShell 5.1 の `Get-Content` は `-Encoding` なしだと既定のコードページ（日本語環境では CP932）で読む。`pwsh`（PowerShell 7）が PATH にあればそちらが使われ、起きないはず（未確認）。
  - **対応（実装済み）**：Hook は、入力が BOM 付きで非 ASCII を含むとき（または JSON として読めないとき）、元の一時ファイル `cursor-hook-payload-*.json`（hook の実行中だけ存在）を探して正しい内容を読み直す。取り違えないよう、「受け取った文字列 = その一時ファイルを CP932 で読んだもの」と一致する候補だけを採用し、一致を確かめられないときは、イベント名・会話 ID・生成 ID が一意に合うものだけを採用する。見つからなければ従来どおり、イベント名・セッション ID などの ASCII の項目だけを救出して記録する（`_salvaged: true`、元のバイト列は `logs\bad-input\`）。
  - 回避策（コード変更なし）：PowerShell 7 を入れる、または Windows の「ベータ: ワールドワイド言語サポートで Unicode UTF-8 を使用」を有効にする。
  - 修正前に記録されたイベント（化けた本文・本文なしのイベント）は元に戻らない。transcript（正しい日本語で保存されている）から補正する案は未実装。
- [x] **Ask モードでも hook は発火する**。`beforeSubmitPrompt` の `composer_mode` が Agent では `"agent"`、Ask では `"chat"`（`model` は `cursor-grok-4.6-medium`）。
- [ ] **履歴から再開したときの `sessionStart`**：利用者の操作ではタイムラインに出たが、イベントファイルには再開後の 2 回目の `sessionStart` は記録されていない（1 会話につき 1 行のみ）。再開で再度は来ない可能性が高い。**要再確認**。
- [ ] `sessionEnd` の発火条件と reason ／ Shell の `exitCode` と失敗時のイベント ／ Shell の確認ダイアログへの影響 ／ スペースを含むパス ／ モデルが Auto のときの値：**未確認**
- 補足：全イベントの payload に `user_email` が入っている（イベントファイルにそのまま保存される）。

## 15. 実装フェーズ

フェーズ完了時は見出しの `[ ]` を `[x]` に更新すること。

## [x] Phase 1: ソリューション雛形
- 4プロジェクト作成、5.1 の `IAgent`・`AgentEvent`・`AgentCapabilities` を Core に定義、WPF-UI と CommunityToolkit.Mvvm 導入、FluentWindow で3ペインの空画面
- 完了条件：ビルドが通り、空の3ペイン画面が表示される

## [x] Phase 2: Hook exe
- 7章の仕様どおり実装（NativeAOT）。`--agent` 引数、`CursorAgent` の Hook 側メソッド、イベント行モデルとシリアライザー（ソース生成）
- 完了条件：標準入力にサンプル JSON を流すと JSONL が追記され、出力・終了コードが表どおり

## [x] Phase 3: イベント読み込みと状態判定（Core）
- tail 読み込み、`CursorAgent.Normalize`（5.1 対応表）、プロジェクト一致判定、10章の状態判定・派生値（共通イベントのみ使用）
- 完了条件：サンプル JSONL を使った単体テストで状態・派生値が期待どおり

## [x] Phase 4: 一覧とファイル監視
- 左ペイン（件数、カード、検索、フィルタ、並び順）、FileSystemWatcher＋ポーリング
- 完了条件：hook が書いたイベントが数秒以内に一覧へ反映される

## [x] Phase 5: 詳細とタイムライン
- 中央ペイン全セクション、右ペインのタイムライン、3行サマリー → タイムラインのジャンプ
- 完了条件：12.3・12.4 の項目がすべて表示され、ジャンプとフィルタ自動切替が動く

## [x] Phase 6: メタ（タイトル・概要・メモ）
- リネーム、概要取り込み／編集／1つ前に戻す、メモ自動保存、検索対象への反映
- 完了条件：再起動後も内容が保持され、検索でヒットする

## [x] Phase 7: git 連携
- コミット一覧、未コミット判定、ブランチ表示
- 完了条件：git リポジトリでない場合もエラーにならない

## [ ] Phase 8: hook 導入機能と配布
- 8章の導入／削除、バックアップ、マージ。publish 設定と zip 作成スクリプト
- 完了条件：まっさらな環境で zip 展開 → 起動 → 導入 → Cursor で会話するとダッシュボードに出る
- 状況：導入／削除・バックアップ・マージ・起動時ダイアログ・`scripts/publish.ps1` は実装し、単一ファイルの App と導入の流れは実機で確認済み。
  **未完了：Hook の NativeAOT 発行**（C++ ビルドツール＋ Windows SDK が必要。この開発環境には無く未検証）。これが通って zip が作れたら `[x]` にする。

## [x] Phase 9: 過去セッション取り込み（Step 0 の結果次第）
- 11章。条件を満たさない場合はスキップして理由を本書に追記
- 完了条件：hook を入れる前のセッションが「⚪ 閉じた（導入前）」として一覧に出て、詳細・検索・リネーム・メモが使える。hook のイベントがあるセッションは重複しない
- 状況：実装し、実際の `.cursor` の transcript で画面確認済み（仕様と制限は 11章の「実装」）。
  導入前のセッションの詳細は、取れない情報（ターン・ツール・継続時間・完了チェック・コミット・変更ファイル・テスト）を「—」で出し、依頼数と transcript サイズだけを見せる。

## [x] Phase 10: 最近の入力・最近閉じたセッション、タブのプレースホルダー
- 右ペイン下部2リスト（クリックでカード選択）、ドキュメント／メモタブの「MVP 対象外」表示

## 16. テスト方針

- Core は xUnit で網羅：`CursorAgent.Normalize` の対応表どおりの変換、状態遷移（再開・中断・エラー・stop 欠落）、実行中ツールの保険クリア、
  プロジェクト一致判定（大文字小文字・末尾区切り・マルチルート）、壊れた行のスキップ、追記途中の行の扱い
- テスト用 JSONL フィクスチャは Step 0 のダンプから個人情報・社内情報を除去して作成
- Hook は「サンプル JSON を stdin に流して出力・終了コード・追記内容を検証」する結合テスト
- UI は手動確認（ワイヤーと突き合わせ）

## 17. 保留事項（MVP 後に相談）

- 実装計画 md とセッションの紐付け（添付・読み込みの検知＋フェーズ完了マーカーを afterFileEdit で検知する案）
- 一覧カードの完了判定ミニ表示
- 既存チャットを Cursor で開くボタン（公式に会話 ID 指定で開く deeplink は見当たらない）
- タスク・課題の更新表示
- 承認待ち状態の判定
- ドキュメントタブ、メモタブ
