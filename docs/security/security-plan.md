# セキュリティ診断の対応 実装計画（Issue #31）

> **連動ルール**：この md と `security-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `docs/miharikun-requirements.md`（13 章・12.7・12.10・8.2・9.1・7 章の処理 2、15 章の Phase 42〜47）。設計の資料（診断）は `docs/security/security-review.html`（#1〜#4 と Q1〜Q3）。Phase 46（Claude の会話ログの保険の探索を絞る）は、診断の後の相談で決めたもの（2026-10-09。11 章）。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（9 章）**。
> 進め方：ロジック（テスト先行）→ 利用側 → 確認。各フェーズの終わりに mac（隔離環境）で確かめ、要件定義 15 章の `[ ]` → `[x]` と「状況」を更新して報告し、**止まる**。Windows の実機の確認は利用者の PC で行う（Phase 47）。コミットは頼まれたときだけ（ここは区切りの目安）。Phase ごとにコミットを分け、`[x]` と「状況」もその Phase のコミットに入れる。途中は `Refs #31`、最後だけ `Fixes #31`（PR 本文に `Closes #31`）。

## 1. 全体像

```
42 フルパスで起動 ──▶ 43 リンクの振り分け ──▶ 44 同梱 ──▶ 45 md の CSP ──▶ 46 保険の探索 ──▶ 47 実機確認・仕上げ
  2 コミット（#2）       2 コミット（#1）       2 コミット（#3） 1〜2 コミット（#1） 1 コミット      1 コミット（#4・Fixes）
```

- 42・43・46 は互いに独立（どちらからでもよい）。45 は 44 の後（CSP で許すスクリプトが、同梱のファイルだけになってから）。
- 新しい依存パッケージ（NuGet）：無い。**同梱する JS**：mermaid（MIT）、highlight.js（BSD-3-Clause）。ダウンロードは利用者の了承を取ってから（9.9）。
- 守る制約：Core は AOT 互換（`GitLocator`・`AtomicFile` は Core。リフレクションを使わない）。Hook の性能（git なし 50ms 以内。git を探すのは git を使うイベントのときだけのまま）。Presentation は Docs を参照しない（今のまま）。`~/.claude` には何も書かない（Phase 46 は読み方を減らすだけ）。
- **既存の md の見た目・動きを変えない**（44・45 は描き方の中身を変える）。下の品質ゲートで確かめる。
- いまのテスト（Phase 41 の時点。mac）：Tests 1197（スキップ 29）、UiTests 65（スキップ 3）。**着手前に数え直し、件数は減らさない**（書き換えるテストは数に入れたまま）。

### 品質ゲート（Phase 44・45 の完了条件）
1. テストが全部通る（件数を減らさない）。`dotnet build` の警告を増やさない。
2. md のプレビューで、これまでの項目が変わらない：チェックボックス（未／済・入れ子・番号付き・押しても変わらない・通常の色）、日本語の見出しの id と `#` リンクのページ内スクロール、mermaid の図、コードの色付け（ライト／ダーク）、相対パスの画像、別の md／html へのリンク（アプリ内で選ぶ）、保存での自動の再読み込みと位置の復元、拡大モードの Esc・目次で移る・位置の補正（拡大⇄戻す）、メモのプレビュー（同じ描き方）。html のプレビューは JS が今までどおり動く。
3. 確かめ方：隔離環境（`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダに向ける）の手書きの md・html で、mac は実装するセッションが目視（スクリーンショット）、Windows は Phase 47 で利用者が確かめる。本物のプロジェクトの文書は使わない。

## 2. Phase 42：外部のプログラムをフルパスで起動（#2。テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 42-1 | `GitLocator`（Windows） | Windows の分岐を、名前だけ（`"git"`）を返すのをやめ、フルパスを返す（9.1）。`Find` に環境変数の読み取り（`Func<string, string?> getEnv`）を足す。起動時に `app.log` へ「git: <パス or 見つからない>」を 1 行（`AppComposition`。確認用） | `GitLocatorTests`：既存の `Windows_uses_git_from_the_path_whatever_exists` を差し替え。PATH の最初の絶対パスの `git.exe`／相対・`.`・空・UNC・ドライブなしの項目は使わない／引用符つき・末尾 `\` の項目／PATH に無ければ決まった場所の順（9.1）／環境変数が相対なら使わない／どこにも無ければ null。`[WindowsFact]` 本物の探索がフルパスの実在するファイルを返す。mac のテストは変えない |
| 42-2 | `ShellOpen`（「フォルダで開く」） | `BuildRevealCommand(bool isMac, string file, string windowsDir)`：Windows は `<windowsDir>\explorer.exe`、mac は `/usr/bin/open`（9.2）。`Reveal` は `Environment.GetFolderPath(SpecialFolder.Windows)` を渡し、空なら起動せず `app.log` に 1 行 | `ShellOpenTests`：Windows の期待を `C:\Windows\explorer.exe` に、末尾 `\` つきの `windowsDir` でも `\\` が二重にならない、mac の期待を `/usr/bin/open`（引数は今のまま） |

完了条件：テストが通る。mac の隔離環境で「Finder で表示」が動き、`app.log` に git のフルパスが出る。Windows の確認は Phase 47。

## 3. Phase 43：プレビューのリンクの振り分け（#1）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 43-1 | `LocalLinkRule`（新規。Presentation。テスト先行） | `LocalLinkAction Decide(string fullPath, bool isWindows, LinkProbe probe)` → `Ignore`／`OpenWithDefaultApp`／`Reveal`。`IsNetworkPath(string)`。開いてよい種類の一覧。`LinkProbe`（ファイルの有無・フォルダの有無・リンクのたどった先。`LinkProbe.Real` が本物）。判定は 9.3 | `LocalLinkRuleTests`：9.3 の表の行ごと（`LinkProbe` を差し替え）。拡張子の大文字小文字。`[MacFact]` 本物のシンボリックリンク（`.txt` の名前で `.command` を指す → `Reveal`、`.txt` を指す → 開く） |
| 43-2 | ドキュメント・メモの `OpenLocalLink`、プレビューの `Route` | `DocumentsViewModel.OpenLocalLink`・`MemoViewModel.OpenLocalLink`：最初にネットワークのパスを捨てる → アプリ内（今のまま）→ `LocalLinkRule.Decide` の結果で `OpenWithDefaultApp`／`RevealInFileManager`／何もしない（9.4）。`MarkdownPreview.Route`：`uri.IsUnc` なら何もしない | `DocumentsViewModelTests`・`MemoViewModelTests`：`.txt` は `Opened`、`.bat`・`.command`・`.exe`（中身はただの文字）とフォルダは `Revealed`（`Opened` は空）、ネットワークのパスは両方とも空、無いファイルは両方とも空。既存のメモのテスト（`[other, _dir]` を `Opened`）は、フォルダを `Revealed` に直す。確認（mac・隔離環境）：9.8 の試験用の md のリンクを押す |

完了条件：テストが通る。mac の隔離環境で、開いてよい種類だけ既定のアプリで開き、ほかは Finder で選ばれた状態になり、何も起動しない。

## 4. Phase 44：mermaid・highlight.js の同梱（#3）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 44-1 | 同梱のファイルと書き出し | 利用者の了承を取ってダウンロード（9.9）。`src/Miharikun.Docs/Assets/vendor/` に置いて埋め込む（csproj）。`MarkdownAssets`（新規。Docs）：`Version`（フォルダ名）、ファイルの一覧、`Ensure(string libRoot, Action<string, byte[]> write)` → `<libRoot>/<Version>` を返す（9.5）。`AtomicFile.WriteAllBytes`（Core。`WriteAllText` と同じ置き換えの仕組み） | `MarkdownAssetsTests`（一時フォルダ）：無いとき全部書く、同じ長さならもう書かない、長さが違えば書き直す、返すフォルダ名に版、埋め込みの全ファイルが空でない。`AtomicFileTests`（新規）：バイト列がそのまま、上書き、一時ファイルが残らない |
| 44-2 | md のページを同梱から読む | `MarkdownRenderer.Render`／`RenderWithOutline` に `libFolder`（`baseFolder` の次。呼ぶ側は名前付き引数）。CDN の URL をやめ、`libFolder` のファイル URL（9.5）。mermaid は ES module の `import()` をやめ、`mermaid.min.js` を普通の `<script>` で読んでから初期化。`MarkdownPreview` の 2 つの描画（ファイル・メモ）で `MarkdownAssets.Ensure(Path.Combine(PreviewDir, "lib"), AtomicFile.WriteAllBytes)` を呼んでから描く。`scripts/dist/THIRD-PARTY-NOTICES.txt`（新規）と、`publish.ps1`・`publish-mac.sh` で zip に入れる | `MarkdownRendererTests`：CDN の文字（`cdn.jsdelivr`・`cdnjs`）が無い、mermaid・色付けは `libFolder` のファイル URL（日本語・空白・`#` のフォルダでも %エンコードと HTML エンコード）、要るときだけ読む（既存の期待の差し替え）。確認（mac・隔離環境・**ネットを切って**）：品質ゲート 2、とくに mermaid と色付け（ライト／ダーク）、メモ |

完了条件：品質ゲート 1〜3（mac）。md の HTML に CDN の URL が無い。

## 5. Phase 45：md の CSP（#1。テスト先行）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 45-1 | CSP と nonce | `MarkdownRenderer`：ページごとの nonce（9.6）、`<head>` の `charset` の直後に CSP の `<meta>`、Miharikun が入れる `<script>`（`#` リンク・Esc・位置の補正・チェックボックス・highlight.js の 2 つ・mermaid の 2 つ）すべてに `nonce`。チェックボックスは `onclick="return false"` をやめ、クリックを取り消すスクリプトにする（9.6） | `MarkdownRendererTests`：CSP の `<meta>` が `<base>` より前、nonce は 1 ページで同じでページごとに違う、Miharikun の `<script` すべてに nonce、md に書いた `<script>`・`<img onerror>` には nonce が付かない、チェックボックスの期待（`onclick` が無い・`tabindex="-1"`）、`Body_is_not_changed_by_the_added_scripts` の切り出しを `<script` に |
| 45-2 | 実機（mac）の要確認 | 9.7 の 3 点（mermaid が CSP の下で描ける、`InvokeScript` が CSP に止められない、Esc の `invokeCSharpAction`）を mac の実機で確かめる。だめなら 9.7 の代わりの形で直す（直しが要るときだけコミット） | 確認（mac・隔離環境）：品質ゲート 2 の全部。9.8 の試験用の md で、md に書いたスクリプトが動かない（ページの文字が変わらない） |

完了条件：品質ゲート 1〜3（mac）。md に書いたスクリプト・属性のスクリプト・`javascript:` のリンクが動かない。

## 6. Phase 46：Claude の会話ログの保険の探索を絞る（テスト先行）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 46-1 | `ClaudeLocations.FindDirsByCwd` | フォルダごとに、`*.jsonl` を新しい順に見て、**cwd が取れた最初の 1 ファイルで、そのフォルダの判断を打ち切る**（合えば候補、合わなければそのフォルダは終わり。9.10）。cwd が取れないファイルは次のファイルへ。動く条件（候補が 0 件のとき・起動ごとに 1 回）・ログの文は今のまま | `ClaudeLocationsTests`：① 新しいほうのファイルの cwd が別のプロジェクトなら、同じフォルダの古いほうに合うものがあっても候補にしない（打ち切りの確認）② 新しいほうに cwd が無ければ（cwd の無い記録だけ）古いほうを見る ③ 新しい順（更新日時をテストで決める）④ 既存の `Fallback_finds_folders_whose_files_have_a_matching_first_cwd_and_logs_it`・`Fallback_logs_nothing_when_nothing_is_found` と、`ClaudeSessionSourceTests` の保険の経路（`odd-name`）はそのまま通る |

完了条件：テストが通る。mac の隔離環境（`MIHARIKUN_CLAUDE_DIR` を一時フォルダに向け、名前の規則に合わないフォルダと、関係ないプロジェクトのフォルダを手書きのログで作る）で、合うフォルダのセッションが一覧に出て、`app.log` に「フォルダ名の規則が、想定と違った」の 1 行が出る。

## 7. Phase 47：両 OS の実機確認・仕上げ（#4）

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 47-1 | Windows の実機の確認（利用者） | 9.8 の確認表を、利用者が Windows（WebView2）で行う。実装するセッションは、試験用のフォルダの作り方と手順を渡す | 9.8 の表の全部（git のフルパス・エクスプローラー・リンクの振り分け・CSP・ネットなしの mermaid と色付け・品質ゲート 2） |
| 47-2 | 仕上げ | `scripts/dist/README-win.txt`（Hook の置き場所の注意〈8.2・#4〉、同梱のライセンスの 1 行）、`README-mac.txt`（同梱のライセンスの 1 行）、`docs/release.md` の「出す前のチェック」（リンクの振り分け・md のスクリプト・ネットなしの mermaid）、要件定義の状況、`docs/security/security-review.html` の状態を「対応済み」に。`Fixes #31` | 文書の目視。テストが全部通る |

完了条件：要件定義 15 章の全体の完了条件を、両 OS の実機で満たす。

## 8. ファイル構成（追加・変更）

```
src/Miharikun.Core/
├─ Git/GitLocator.cs                        42-1 変更：Windows も PATH の絶対パスと決まった場所から git.exe を探す
├─ Sessions/ClaudeLocations.cs              46-1 変更：保険の探索を、フォルダごとに cwd が取れた最初の 1 ファイルで打ち切る
└─ Storage/AtomicFile.cs                    44-1 変更：WriteAllBytes を足す（置き換えの仕組みは共用）

src/Miharikun.Presentation/
├─ ShellOpen.cs                             42-2 変更：explorer.exe・open をフルパスで
├─ LocalLinkRule.cs                         43-1 新規：ローカルのリンクの規則（LocalLinkAction・LinkProbe）
└─ ViewModels/
   ├─ DocumentsViewModel.cs                 43-2 変更：OpenLocalLink を規則に
   └─ MemoViewModel.cs                      43-2 変更：OpenLocalLink を規則に

src/Miharikun.Docs/
├─ Assets/vendor/                           44-1 新規：mermaid.min.js・highlight.min.js・hljs-github.min.css・hljs-github-dark.min.css・LICENSE-mermaid.txt・LICENSE-highlightjs.txt
├─ Miharikun.Docs.csproj                    44-1 変更：vendor を埋め込む
├─ MarkdownAssets.cs                        44-1 新規：版・ファイルの一覧・書き出し
└─ MarkdownRenderer.cs                      44-2 変更：libFolder から読む ／ 45-1 変更：CSP・nonce・チェックボックス

src/Miharikun/
├─ AppComposition.cs                        42-1 変更：起動時に git のパスを app.log へ
└─ Views/MarkdownPreview.axaml.cs           43-2 変更：Route で UNC を捨てる ／ 44-2 変更：lib を書き出してから描く

scripts/
├─ dist/THIRD-PARTY-NOTICES.txt             44-2 新規：mermaid・highlight.js のライセンス
├─ dist/README-win.txt・README-mac.txt      47-2 変更：Hook の置き場所の注意（Windows）・ライセンスの 1 行
├─ publish.ps1                              44-2 変更：NOTICES を zip に入れる
└─ publish-mac.sh                           44-2 変更：同じ

docs/
├─ miharikun-requirements.md                各 Phase：15 章の [x] と状況
├─ release.md                               47-2 変更：出す前のチェック
└─ security/security-review.html            47-2 変更：状態を「対応済み」に

tests/Miharikun.Tests/
├─ Core/GitLocatorTests.cs                  42-1 変更
├─ Core/AtomicFileTests.cs                  44-1 新規
├─ Core/ClaudeLocationsTests.cs             46-1 変更
├─ Presentation/ShellOpenTests.cs           42-2 変更
├─ Presentation/LocalLinkRuleTests.cs       43-1 新規
├─ Presentation/DocumentsViewModelTests.cs  43-2 変更
├─ Presentation/MemoViewModelTests.cs       43-2 変更
├─ Docs/MarkdownAssetsTests.cs              44-1 新規
└─ Docs/MarkdownRendererTests.cs            44-2・45-1 変更（MarkdownOutlineTests も、Render の引数が変わるなら呼び方だけ）
```

## 9. 実装の決めごと（迷いやすい所）

### 9.1 Windows の git の探し方（42-1）
1. `PATH` を `;` で分け、各項目の前後の空白と `"` を取る。
2. **ドライブ文字で始まる絶対パス**（`^[A-Za-z]:[\\/]`）だけを使う。相対（`bin`）・`.`・空・UNC（`\\server\…`）は飛ばす。
   - テストは mac でも動くので、`Path.IsPathFullyQualified` は使わない（mac では `C:\a` を相対と判定する）。文字列で判定し、つなぎは `\`（`Path.Combine` は使わない。mac では `/` でつながる）。
3. `<項目（末尾の \ / を除く）>\git.exe` が `fileExists` なら、それを返す（最初に見つかったもの）。
4. PATH に無ければ、決まった場所を順に：`%ProgramFiles%\Git\cmd\git.exe` → `%ProgramW6432%\Git\cmd\git.exe` → `%ProgramFiles(x86)%\Git\cmd\git.exe` → `%LOCALAPPDATA%\Programs\Git\cmd\git.exe`。環境変数が無い・空・ドライブ文字で始まらないものは飛ばす。
5. どれも無ければ null（App は「不明」、Hook は git を省略。どちらも今の「git が無いとき」と同じ扱い）。
- **カレントフォルダは見ない**（これが目的）。`Lazy` のキャッシュは今のまま（起動中に 1 回）。Hook は git を使うイベントのときだけ探す（`GitProbe.Run` の中。今のまま）ので、git なしの 50ms は変わらない。
- `Find` の引数に `Func<string, string?> getEnv` を足す（既定の `Cached` は `Environment.GetEnvironmentVariable`）。mac の分岐は変えない。

### 9.2 「フォルダで開く」のコマンド（42-2）
| OS | ファイル | 引数 |
|---|---|---|
| Windows | `<windowsDir の末尾の \ を除く>\explorer.exe`（`windowsDir` は `Environment.GetFolderPath(SpecialFolder.Windows)`） | `/select,<file>`（今のまま 1 つ） |
| mac | `/usr/bin/open` | `-R`、`<file>`（今のまま） |
- `windowsDir` が空（取れない）なら起動しない（`app.log` に 1 行）。
- `ShellOpen.Open`（`UseShellExecute = true`。既定のアプリ・ブラウザ）は変えない。開く対象はフルパスか URL で、OS の「関連付けで開く」を通る。

### 9.3 ローカルのリンクの規則（43-1。`LocalLinkRule.Decide`）
判定の順（上で決まったら終わり）：
1. ネットワークのパス（`\\` か `//` で始まる）→ `Ignore`。**ファイルシステムに触る前に**判定する。
2. Windows で、2 文字目（ドライブの `:`）のほかに `:` を含む（代替データストリーム）→ `Ignore`。
3. フォルダがある（`Directory.Exists`。フォルダへのリンクも含む。mac の `.app` もフォルダ）→ `Reveal`。
4. ファイルが無い → `Ignore`。
5. シンボリックリンクなら、たどった先（最後まで）を取る。たどった先がネットワークのパス → `Ignore`、フォルダ → `Reveal`。
6. リンクの名前と、たどった先（リンクでなければ無し）の**両方**の拡張子が「開いてよい種類」→ `OpenWithDefaultApp`。それ以外 → `Reveal`。

開いてよい種類（大文字小文字は区別しない）：`.md` `.html` `.htm` `.png` `.jpg` `.jpeg` `.gif` `.webp` `.bmp` `.svg` `.pdf` `.txt`

| 入力（例） | 結果 |
|---|---|
| `\\server\share\a.txt`・`//server/share/a.txt` | Ignore（有無を確かめない） |
| Windows `C:\p\a.txt:x.exe`・`C:\p\a.exe:x.txt` | Ignore |
| Windows `C:\p\a.txt`（ドライブの `:` だけ） | 次へ（判定 3 以降） |
| mac の名前に `:` を含むファイル（`isWindows = false`） | 判定 2 は使わない |
| フォルダ・フォルダへのリンク・`x.app` | Reveal |
| 無いファイル | Ignore |
| `a.txt`・`A.PDF`・`b.png` | OpenWithDefaultApp |
| `a.bat`・`a.exe`・`a.command`・`a.sh`・`a.lnk`・拡張子なし・`a.txt.`（末尾が `.`） | Reveal |
| `a.txt`（リンク）→ `run.command` | Reveal |
| `a.txt`（リンク）→ `b.txt` | OpenWithDefaultApp |
| `a.txt`（リンク）→ フォルダ | Reveal |
| `a.txt`（リンク）→ `\\server\x.txt` | Ignore |

- `LinkProbe` は 3 つの関数（`FileExists`・`DirectoryExists`・`ResolveFinalTarget`〈リンクでなければ null〉）の record。`LinkProbe.Real` は `File.Exists`・`Directory.Exists`・`new FileInfo(p).ResolveLinkTarget(returnFinalTarget: true)?.FullName`（例外〈IOException・UnauthorizedAccessException〉は null 扱いでなく `Reveal` に倒す。起動しない側に倒す）。
- `isWindows` は呼ぶ側が `OperatingSystem.IsWindows()` を渡す（テストで両方を試すため）。

### 9.4 VM と Route の順番（43-2）
1. `MarkdownPreview.Route(uri)`：`http`/`https`/`mailto` → 既定のアプリ（今のまま）。`uri.IsUnc` → 何もしない。`uri.IsFile` → `_vm.OpenLocalLink(uri.LocalPath)`。ほかのスキームは何もしない（今のまま）。
2. `DocumentsViewModel.OpenLocalLink(fullPath)`：① `LocalLinkRule.IsNetworkPath` なら終わり ② 対象フォルダ内で索引にある md/html はアプリ内で選ぶ（今のまま）③ `Decide` の結果で `_services.OpenWithDefaultApp`／`_services.RevealInFileManager`／何もしない。
3. `MemoViewModel.OpenLocalLink(fullPath)`：① 同じ ② `File.Exists && _resolveInApp` ならドキュメントタブで開く（今のまま。① の後なので `File.Exists` に UNC は来ない）③ 同じ。
- `DocumentsViewModel.OpenExternallyBecauseMissing`（メモから頼まれた、対象フォルダ内の md/html で索引に無いもの）は、開いてよい種類なので今のまま。
- 「既定のアプリで開く」ボタン（選んでいる md/html を開く）は利用者の操作なので今のまま。

### 9.5 同梱のファイルの置き場と書き出し（44-1・44-2）
- 置き場：`<データ>/preview/lib/<Version>/`。`Version` は `MarkdownAssets` の定数（例 `mermaid-11.x.y_hljs-11.x.y`）。**ファイルを替えたら必ず変える**（古いページと混ざらないように）。古い版のフォルダは消さない（消してよい。要件 6 章）。
- `PreviewFiles.CleanOld` は `preview/` の直下のファイルだけを消す（`EnumerateFiles` は直下だけ）ので、`lib/` は消えない。**`CleanOld` をサブフォルダまで消すように変えない**（コメントで残す）。
- `Ensure`：各ファイルについて「無い、または長さが埋め込みと違う」ときだけ `write(path, bytes)`。毎回の描画の前に呼ぶ（存在と長さを見るだけで軽い。起動中にデータのフォルダが消されても戻る）。書いた後は書き直さないので、表示中の WebView が読んでいるファイルを置き換えることは、版の替わり目でしか起きない。`write` の IOException・UnauthorizedAccessException は捨てて描画は続ける（mermaid・色付けが出ないだけ。`app.log` に 1 行）。
- 複数起動が同時に書いても `AtomicFile` の置き換えで壊れない（後勝ち）。
- md のページからの参照は**絶対の file URL**：`<base>` が md のフォルダを指すので、相対パスでは `lib/` に届かない。`MarkdownRenderer.FolderUri(libFolder) + ファイル名` を `WebUtility.HtmlEncode` して属性に入れる（`<base>` と同じ）。
- mermaid：`<script nonce src=".../mermaid.min.js"></script>` の後に `<script nonce>` で `mermaid.initialize({ startOnLoad: false, theme })` と `mermaid.run({ querySelector: 'pre.mermaid' })`（`window.mermaid` が無ければ何もしない。`run` の失敗は `catch` で捨てる。今の try/catch と同じ）。
- highlight.js：`<link rel="stylesheet" href=".../hljs-github(-dark).min.css">`（head）と `<script nonce src=".../highlight.min.js">` → `<script nonce>`（今の初期化と同じ）。要るときだけ読む（今のまま）。

### 9.6 CSP と nonce（45-1）
- nonce：`RandomNumberGenerator.GetBytes(16)` を Base64。ページ（1 回の描画）ごとに作る。
- `<meta http-equiv="Content-Security-Policy" content="script-src 'nonce-<n>'; object-src 'none'; frame-src 'none'; form-action 'none'">` を `<meta charset>` の直後（`<base>`・`<style>` より前）に置く。`style-src`・`img-src` は付けない（md の画像・css・mermaid の `<style>` を今のまま許す）。
- nonce を付けるのは Miharikun が入れる `<script>` だけ：`#` リンクのスクロール、Esc の受け口、位置の補正、チェックボックス、highlight.js（読み込みと初期化）、mermaid（読み込みと初期化）。md の本文（Markdig の出力）には付けない。
- チェックボックス：`ReadOnlyCheckboxes` の置き換えを `<input type="checkbox" tabindex="-1"`（`onclick` なし）にし、スクリプト `document.addEventListener('click', function (e) { var t = e.target; if (t && t.matches && t.matches('li.task-list-item > input[type="checkbox"]')) e.preventDefault(); }, true);` で取り消す（Space キーで押した場合も click が来るので同じく取り消される）。見た目は今のまま（通常の色）。
- md に `<meta http-equiv="refresh">`・`<form>` があっても、移動はリンクの振り分け（9.3）に来るので、何も起動しない（CSP では止まらないものの受け皿）。
- html のプレビュー・`EscapeListenerScript` の後入れ（`InvokeScript`）は変えない。

### 9.7 要確認（45-2。mac の実機。Windows は 47-1）
| 確かめること | だめなときの代わり |
|---|---|
| mermaid が CSP の下で図を描ける（`eval`・`new Function` を使っていないか） | CSP の `script-src` に `'unsafe-eval'` を足す（nonce の無いスクリプトは動かないままなので、守りは保たれる）。足したら要件定義 12.7 に書く |
| アプリからの `InvokeScript`（目次で移る・再読み込みの位置の復元・`window.scrollY`）が CSP に止められない | 止められたら、ページの中に nonce 付きの受け口の関数を置き、`InvokeScript` はその関数を呼ぶだけにする（呼び出し自体が止められるなら、利用者に相談） |
| Esc（ページの中で押す → `invokeCSharpAction`）が CSP の下でも届く | 届かなければ利用者に相談（Issue #28 の試作の代わりの形：独自の URL への移動を取り消して受ける） |

### 9.8 確認の手順と試験用のファイル（各 Phase・47-1）
- 隔離環境：`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダに向け、一時フォルダの試験用のプロジェクトを対象に起動する。`dotnet build`／`test` の前にアプリを止める。
- 試験用のファイルは手書きし、**中身は害のないものだけ**にする：`.bat`・`.command` は中身を `echo` の 1 行（起動されたら分かるだけ）、`.exe` は中身がただの文字のファイル（拡張子だけ）。md には、それらへのリンク、`.txt`・`.png`・`.pdf`・フォルダへのリンク、`<script>` で本文の文字を書き換えるだけのスクリプト、`<img src=x onerror=...>`（同じく文字を書き換えるだけ）、mermaid とコードブロックを書く。
- 確認表（mac は各 Phase、Windows は 47-1）：

| 操作 | 期待 |
|---|---|
| 試験用のプロジェクトをカレントにして起動し、`app.log` を見る | git のフルパスが出る（Windows は `...\Git\cmd\git.exe` など。カレントのフォルダのパスではない）。未コミットの表示が普通に出る |
| 「フォルダで開く／Finder で表示」 | エクスプローラー／Finder でファイルが選ばれる |
| md の `.txt`・`.png`・`.pdf` へのリンクを押す | 既定のアプリで開く |
| md の `.bat`・`.command`・`.exe`・フォルダへのリンクを押す | 何も起動せず、エクスプローラー／Finder で選ばれた状態になる |
| md の `\\server\share\x.txt` へのリンクを押す | 何も起きない |
| md の `<script>`・`onerror` | 本文の文字が変わらない |
| ネットを切って mermaid・コードブロックの md を開く | 図と色付けが出る（ライト／ダーク） |
| 品質ゲート 2 の項目 | 今までどおり |

### 9.9 ダウンロードの段取り（44-1）
- **ダウンロードの前に利用者の了承を取る**（ファイル名・取得元・おおよそのサイズを示す）。取得元は npm のレジストリ（`mermaid`、`@highlightjs/cdn-assets`）。版は、実装の時点の **11 系の最新の安定版**に固定し、計画と要件定義の状況に書き足す。
- 取得したら、npm の `dist.integrity`（sha512）と照らす。使うのは `mermaid/dist/mermaid.min.js`、`@highlightjs/cdn-assets/highlight.min.js`・`styles/github.min.css`・`styles/github-dark.min.css`、両方の `LICENSE`。`mermaid.min.js`（IIFE で `window.mermaid` を作るもの）が無い版なら、止まって相談する。
- 取得した tarball・展開したフォルダは、スクラッチの専用フォルダに置き、使うファイルだけをリポジトリに写す（展開したフォルダの中で何も実行しない）。
- `THIRD-PARTY-NOTICES.txt` には、名前・版・取得元・ライセンスの全文を書く。mermaid の min.js に入っている依存のライセンスは、取得した版の配布物（`dist` のライセンスのファイル、無ければ `package.json` の依存）から拾う（要確認）。

### 9.10 保険の探索の読み方（46-1。`ClaudeLocations.FindDirsByCwd`）
1. 動く条件は今のまま：名前の規則で候補が 0 件のとき、起動ごとに 1 回（`ClaudeSessionSource.CurrentDirs`）。`.claude/projects` が無ければ動かない。
2. `projects/` の下のフォルダごとに、直下の `*.jsonl` を**更新日時の新しい順**（同じなら名前順）に並べる。
3. 1 つずつ `ReadFirstCwd`（先頭から最大 200 行のうち、最初に cwd を持つ行まで。今のまま）を呼ぶ：
   - cwd が取れない（cwd の無い記録だけ・読めない）→ 次のファイルへ。
   - cwd が取れた → 対象のプロジェクト（または作業ツリー）なら候補に足す。**どちらでも、そのフォルダはここで終わり**（残りのファイルは開かない）。
4. ログの文（見つけたときだけ、フォルダ名を 1 行）は今のまま。会話の中身はログに書かない。

| フォルダの中（新しい順） | 結果 |
|---|---|
| 1 つ目の cwd ＝ 対象 | 候補にする。2 つ目以降は開かない |
| 1 つ目の cwd ＝ 別のプロジェクト、2 つ目の cwd ＝ 対象 | 候補にしない。2 つ目は開かない |
| 1 つ目に cwd が無い、2 つ目の cwd ＝ 対象 | 候補にする |
| どのファイルにも cwd が無い | 候補にしない（全ファイルの先頭を見る。今と同じ） |

- 打ち切ってよい理由：Claude Code は起動したフォルダのパスからフォルダ名を作るので、1 つのフォルダには同じパスのセッションしか入らない（作業ツリーは別のフォルダ）。
- 取りこぼし：パスの記号だけが違う 2 つのプロジェクト（`my-proj` と `my_proj` など）は同じフォルダ名になるが、そのフォルダは名前の規則で候補になるので、保険は使われない（今のまま）。保険の側で取りこぼすのは「名前の規則が合わない」かつ「別のプロジェクトとフォルダを共有する」場合だけで、受け入れる（10 章）。
- 名前の側で、NFD・実パスの形も候補にする案は入れない（保険で拾えるため。要るなら別 Issue）。

## 10. リスクと対策

| リスク | 対策 |
|---|---|
| md の見た目・動きが変わる（44・45） | 品質ゲート 2 を mac と Windows で確かめる。既存のテストは期待の差し替えだけ（件数は減らさない） |
| mermaid や `InvokeScript` が CSP で動かない | 45-2 で先に mac の実機で確かめ、9.7 の代わりの形にする |
| Windows で git が見つからなくなる（PATH に無い・変わった場所に入れた） | 決まった場所も探す。見つからないときは今の「git が無い」と同じ「不明」。`app.log` にパスを出すので原因が分かる。47-1 で利用者の PC で確かめる |
| 開いてよい種類が狭く、よく使うファイルが開けない | 起動はしないが Finder／エクスプローラーで選ばれるので、そこから開ける。一覧は要件定義 12.7 にあり、足すのは利用者の判断 |
| 同梱で配布物が大きくなる（mermaid は数 MB） | 受け入れる（今の単一ファイルは 130MB 超） |
| ページのスクリプトから http/https のリンクが勝手に開く | ブラウザが開くだけなので受け入れる（要件定義 12.7 に明記） |
| ダウンロードしたファイルが改ざんされている | npm の `dist.integrity` で照らす。了承を取ってから取得 |
| 保険の探索を打ち切ったせいで、同じフォルダの別のファイルにある対象のセッションを見逃す | 1 つのフォルダには同じパスのセッションしか入らない（9.10）。見逃すのは「名前の規則が合わない」かつ「別のプロジェクトとフォルダを共有する」場合だけで、受け入れる |

## 11. 決定事項（確認済み。診断の質問 Q1〜Q3 と、その後の相談）
1. Q1＝C：開いてよい種類（拡張子の一覧）だけ既定のアプリで開き、ほかは起動せず Finder／エクスプローラーで見せる。
2. Q2＝A：md の生の HTML は残し、CSP（nonce）でスクリプトだけ止める。
3. Q3＝A：mermaid・highlight.js を同梱する（CDN をやめる）。
4. Claude の会話ログの保険の探索（名前で見つからないとき、cwd で探す）は**残す**。外すと、名前の規則がずれたとき（mac の NFD の日本語名・シンボリックリンク経由・Claude Code の版の違い）に、Claude のセッションが黙って一覧から消えるため。代わりに、ほかのプロジェクトのログを読む量を減らす：フォルダごとに cwd が取れた最初の 1 ファイルで打ち切る（2026-10-09 の相談。Phase 46）。

計画で決めたこと（仕様にない細部）：
- 開いてよい種類の一覧：`.md` `.html` `.htm` `.png` `.jpg` `.jpeg` `.gif` `.webp` `.bmp` `.svg` `.pdf` `.txt`（Office の文書・`.csv`・`.json` などは入れない。マクロや別のアプリの動きを持ち込まないため）。
- フォルダへのリンクは、開かずに Finder／エクスプローラーで選ばれた状態にする（mac の `.app` もフォルダなので、開くと起動してしまうため）。今はメモのリンクでフォルダを開いていた（テストを直す）。
- シンボリックリンクは、名前とたどった先の両方で種類を見る。たどれないときは起動しない側（`Reveal`）に倒す。
- Windows の `:` を含むパス（代替データストリーム）は何もしない。
- CSP に `object-src 'none'; frame-src 'none'; form-action 'none'` も入れる（GitHub の md の表示でも出ないもの）。`style-src`・`img-src` は付けない。
- mac の「Finder で表示」も `/usr/bin/open` のフルパスにする（.NET は名前だけのとき、mac でもカレントフォルダを PATH より先に見る見込み。要件定義 13 章）。
- 起動時に git のパスを `app.log` に 1 行出す（確認と、見つからないときの原因の調査のため）。
- 同梱のファイルは `preview/lib/<版>/` に書き出す（埋め込みから。配布物のファイルを増やさない）。`AtomicFile.WriteAllBytes` を足す。
- `MarkdownRenderer` の `libFolder` は `baseFolder` の次の引数にし、アプリのコードでは名前付き引数で渡す（同じ string の取り違えを防ぐ）。
- highlight.js も mermaid と同じく 11 系の最新の安定版に固定する（今は 11.9.0。見た目がほぼ変わらない範囲）。
- README の Hook の置き場所は、`%USERPROFILE%` の下を先に勧め、`C:\dev` は注意つきで残す（会社の PC で `%USERPROFILE%` の下が動くかは未確認のため。47-1 で利用者に聞く）。
- 保険の探索は、フォルダの中のファイルを更新日時の新しい順に見る（いまの Claude Code の書き方に近いものから）。名前の側で NFD・実パスの形も候補にする案は入れない。

## 12. 実装するセッションへの注意（必ず読む）

### 12.1 始める前に
- **この計画・診断（`docs/security/security-review.html`）・要件定義の変更が見えることを確かめる**。ブランチ `docs/security-review` にあり、コミットされていないことがある。別の作業ツリー（worktree）や別の環境で始まったセッションには、未追跡のファイルが無い。見つからなければ、推測で進めずに利用者に聞く。
- 計画と要件定義に食い違いが無いか確かめる。あれば要件定義が正。判断がつかなければ聞く。
- `CLAUDE.md` の決まり：main から作業ブランチを切る（または利用者の指示のブランチ）、`git add` はパスを指定、コミット・push・PR は頼まれたときだけ、完了報告にはビルドの有無と出力先・テストの件数・できなかった確認を書く。
- 本物のデータ・本物のプロジェクトの文書は使わない（隔離環境と手書きの試験用のファイル）。試験用の実行形式は作らない（9.8 のとおり、拡張子だけのただの文字のファイル）。
- ダウンロードは了承を取ってから（9.9）。システムへのインストールはしない。
- Issue は #31。途中のコミットは `Refs #31`、最後だけ `Fixes #31`。

### 12.2 各フェーズの終わり
- mac の隔離環境で確かめ（9.8）、要件定義 15 章の `[ ]` → `[x]` と「状況」（テストの件数・確かめたこと・できなかったこと）を更新する。
- 報告には、変えたこと、テスト結果（件数）、ビルドの出力先、できなかった確認を書く。**次のフェーズに進む前に止まって報告する**。
- Phase 45 の後は、利用者が望めばレビューを挟む（差分と品質ゲートの結果を渡す）。Phase 47-1 は利用者の Windows で行うので、手順を渡して待つ。

**状態：レビュー待ち**（別セッションに、この計画のレビューを頼む）。
