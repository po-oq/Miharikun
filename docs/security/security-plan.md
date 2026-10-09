# セキュリティ診断の対応 実装計画（Issue #31）

> **連動ルール**：この md と `security-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `docs/miharikun-requirements.md`（13 章・12.7・12.10・8.2・9.1・7 章の処理 2、15 章の Phase 42〜47）。設計の資料（診断）は `docs/security/security-review.html`（#1〜#4 と Q1〜Q3）。Phase 46（Claude の会話ログの保険の探索を絞る）は、診断の後の相談で決めたもの（2026-10-09。11 章）。
> **レビューの反映**：`docs/security/security-plan-review.html`（2026-10-09。#1〜#18、Q1＝A・Q2＝A）を反映済み。どの指摘をどこに入れたかは 13 章。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（9 章）**。
> 進め方：ロジック（テスト先行）→ 利用側 → 確認。各フェーズの終わりに mac（隔離環境）で確かめ、要件定義 15 章の `[ ]` → `[x]` と「状況」を更新して報告し、**止まる**。Windows の実機の確認は利用者の PC で行う（Phase 47）。コミットは頼まれたときだけ（ここは区切りの目安）。Phase ごとにコミットを分け、`[x]` と「状況」もその Phase のコミットに入れる。途中は `Refs #31`、最後だけ `Fixes #31`（PR 本文に `Closes #31`）。

## 1. 全体像

```
42 フルパスで起動 ──▶ 43 リンクの振り分け ──▶ 44 同梱 ──▶ 45 md の CSP ──▶ 46 保険の探索 ──▶ 47 実機確認・仕上げ
  2 コミット（#2）       2 コミット（#1）       2 コミット（#3） 1〜2 コミット（#1） 1 コミット      1 コミット（#4・Fixes）
```

- 42・43・46 は互いに独立（どちらからでもよい）。45 は 44 の後（CSP で許すスクリプトが、同梱のファイルだけになってから）。
- 新しい依存パッケージ（NuGet）：無い。**同梱する JS**：mermaid（MIT）、highlight.js（BSD-3-Clause）。ダウンロードは利用者の了承を取ってから（9.9）。
- 守る制約：Core は AOT 互換（`GitLocator`・`AtomicFile`・`ClaudeLocations` は Core。リフレクションを使わない）。Hook の性能（git なし 50ms 以内。git を探すのは git を使うイベントのときだけのまま）。Presentation は Docs を参照しない（今のまま）。`~/.claude` には何も書かない（Phase 46 は読み方を減らすだけ）。
- **既存の md の見た目・動きを変えない**（44・45 は描き方の中身を変える）。下の品質ゲートで確かめる。
- いまのテスト（Phase 41 の時点。mac）：Tests 1197（スキップ 29）、UiTests 65（スキップ 3）。**着手前に数え直し、件数は減らさない**（書き換えるテストは数に入れたまま。**確認を弱める書き換えもしない**：期待の文字を緩める・試験のデータを当たらない形に替えて素通りさせる、をしない）。

### 品質ゲート（Phase 44・45 の完了条件）
1. テストが全部通る（件数を減らさない）。`dotnet build` の警告を増やさない。
2. md のプレビューで、これまでの項目が変わらない：チェックボックス（未／済・入れ子・番号付き・**空行を挟んだリスト**・押しても変わらない・通常の色）、日本語の見出しの id と `#` リンクのページ内スクロール、mermaid の図、コードの色付け（ライト／ダーク）、相対パスの画像、別の md／html へのリンク（アプリ内で選ぶ）、保存での自動の再読み込みと位置の復元、拡大モードの Esc・目次で移る・位置の補正（拡大⇄戻す）、メモのプレビュー（同じ描き方）。html のプレビューは JS が今までどおり動く。
3. 確かめ方：隔離環境（`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダに向ける）の手書きの md・html で、mac は実装するセッションが目視（スクリーンショット）、Windows は Phase 47 で利用者が確かめる。本物のプロジェクトの文書は使わない。

## 2. Phase 42：外部のプログラムをフルパスで起動（#2。テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 42-1 | `GitLocator`（Windows） | Windows の分岐を、名前だけ（`"git"`）を返すのをやめ、フルパスを返す（9.1）。`Find` に環境変数の読み取り（`Func<string, string?> getEnv`）を足す。起動時に `app.log` へ「git: <パス or 見つからない>」を 1 行（`AppComposition`。**`Task.Run` の中で**。確認用） | `GitLocatorTests`：既存の `Windows_uses_git_from_the_path_whatever_exists` を差し替え。PATH の最初の使える項目の `git.exe`／**UNC の項目（`\\server\share\Git\cmd`・`//server/share/Git/cmd`）も使う**／相対・`.`・空・ドライブなし（`\x`）・`\\?\`・`\\.\` の項目は使わない／引用符つき・末尾 `\` の項目／PATH の順を守る（ドライブと UNC が混ざっても前のもの）／PATH に無ければ決まった場所の順（9.1）／環境変数が使える形でなければ使わない／どこにも無ければ null。`[WindowsFact]` 本物の探索がフルパスの実在するファイルを返す。mac のテストは変えない（補助の関数の引数だけ） |
| 42-2 | `ShellOpen`（「フォルダで開く」） | `BuildRevealCommand(bool isMac, string file, string windowsDir)`：Windows は `<windowsDir>\explorer.exe` と、引用符で囲んだ 1 つの文字列の引数 `/select,"<file>"`、mac は `/usr/bin/open` と `-R`・`<file>`（9.2）。`Reveal` は `Environment.GetFolderPath(SpecialFolder.Windows)` を渡し、空なら起動せず `app.log` に 1 行 | `ShellOpenTests`：Windows の期待を `C:\Windows\explorer.exe` と `/select,"C:\work\proj\docs\a b.md"` に、名前に `,` を含むファイルでも 1 つの引用符の中、末尾 `\` つきの `windowsDir` でも `\\` が二重にならない、mac の期待を `/usr/bin/open`（引数は今のまま） |

完了条件：テストが通る。mac の隔離環境で「Finder で表示」が動き、`app.log` に git のフルパスが出る。Windows の確認は Phase 47。

## 3. Phase 43：プレビューのリンクの振り分け（#1）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 43-1 | `LocalLinkRule`（新規。Presentation。テスト先行） | `LocalLinkAction Decide(string fullPath, bool isWindows, LinkProbe probe)` → `Ignore`／`OpenWithDefaultApp`／`Reveal`。`IsUsableLocalPath(string path, bool isWindows)`（**使ってよい形だけを通す**。9.3 の判定 1）。開いてよい種類の一覧。`LinkProbe`（ファイルの有無・フォルダの有無・**リンクの中身を 1 段だけ読む**。`LinkProbe.Real` が本物）。判定は 9.3 | `LocalLinkRuleTests`：9.3 の表の行ごと（`LinkProbe` を差し替え。**ネットワークの行は `FileExists`・`DirectoryExists` が 1 回も呼ばれないこと**も確かめる）。拡張子の大文字小文字。`[MacFact]` 本物のシンボリックリンク（`.txt` の名前で `.command` を指す → `Reveal`、`.txt` を指す → 開く、2 段のリンク） |
| 43-2 | ドキュメント・メモの `OpenLocalLink`、索引に無いときの外部、「既定のアプリで開く」ボタン、プレビューの `Route` | `DocumentsViewModel.OpenLocalLink`・`MemoViewModel.OpenLocalLink`：最初に使えない形のパスを捨てる → アプリ内（今のまま）→ `LocalLinkRule.Decide` の結果で `OpenWithDefaultApp`／`RevealInFileManager`／何もしない（9.4）。**`DocumentsViewModel.OpenExternallyBecauseMissing` と「既定のアプリで開く」ボタン（`OpenExternal`）も `Decide` を通す**。`MarkdownPreview.Route`：`uri.IsUnc` なら何もしない | `DocumentsViewModelTests`・`MemoViewModelTests`：`.txt` は `Opened`、`.bat`・`.command`・`.exe`（中身はただの文字）とフォルダは `Revealed`（`Opened` は空）、使えない形のパス（`\\server\…`・`//server/…`）は両方とも空、無いファイルは両方とも空。既存のメモのテスト `Any_other_existing_file_or_folder_is_opened_with_the_default_app_and_a_missing_one_is_ignored` は、**名前を「ファイルは既定のアプリ、フォルダは見せるだけ、無いものは何もしない」に直し**、フォルダを `Revealed` に、3 つとも確かめる。`[MacFact]` 索引に無い md（リンクのフォルダの中）が `.command` を指す → メモから頼んでも `Revealed`。「既定のアプリで開く」ボタンで、選んでいる md が `.command` を指すリンク → `Revealed`。確認（mac・隔離環境）：9.8 の試験用の md のリンクを押す |

完了条件：テストが通る。mac の隔離環境で、開いてよい種類だけ既定のアプリで開き、ほかは Finder で選ばれた状態になり、何も起動しない。

## 4. Phase 44：mermaid・highlight.js の同梱（#3）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 44-1 | 同梱のファイルと書き出し | 利用者の了承を取ってダウンロード（9.9）。`src/Miharikun.Docs/Assets/vendor/` に置いて埋め込む（csproj。**`LogicalName` を明記**）。`MarkdownAssets`（新規。Docs）：`Version`（**埋め込みの中身のハッシュから作る**。9.5）、ファイルの一覧、`Ensure(string libRoot, Action<string, byte[]> write)` → `<libRoot>/<Version>` を返す（9.5）。`AtomicFile.WriteAllBytes`（Core。`WriteAllText` と同じ置き換えの仕組み） | `MarkdownAssetsTests`（一時フォルダ）：無いとき全部書く、同じ長さならもう書かない、長さが違えば書き直す、返すフォルダ名が `Version`、**`Version` がテストで計算し直した中身のハッシュと同じ**、**`Version` に `mermaid`・`highlight` の文字が入らない**、埋め込みの全ファイルが空でない、`write` が IOException を投げても `Ensure` は投げない。`AtomicFileTests`（新規）：バイト列がそのまま、上書き、一時ファイルが残らない |
| 44-2 | md のページを同梱から読む | `MarkdownRenderer.Render`／`RenderWithOutline` に `libFolder`（`baseFolder` の次。呼ぶ側は名前付き引数）。CDN の URL をやめ、`libFolder` のファイル URL（9.5）。mermaid は ES module の `import()` をやめ、`mermaid.min.js` を普通の `<script>` で読んでから初期化（`securityLevel: 'strict'` を明記）。`MarkdownPreview` の 2 つの描画（ファイル・メモ）で `MarkdownAssets.Ensure(Path.Combine(PreviewDir, "lib"), AtomicFile.WriteAllBytes)` を呼んでから描く。`scripts/dist/THIRD-PARTY-NOTICES.txt`（新規）と、`publish.ps1`・`publish-mac.sh` で zip に入れる | `MarkdownRendererTests`：**テストの `libFolder` は本物の形（`<一時>/preview/lib/<MarkdownAssets.Version>`）にする**。CDN の文字（`cdn.jsdelivr`・`cdnjs`）が無い、mermaid・色付けは `libFolder` のファイル URL（日本語・空白・`#` のフォルダでも %エンコードと HTML エンコード）、`securityLevel: 'strict'`、要るときだけ読む（既存の期待の差し替え。**`Mermaid_is_not_loaded_when_there_is_no_diagram` の `DoesNotContain("mermaid")` と `Highlightjs_is_not_loaded_for_plain_pages_or_mermaid_only_pages` の `DoesNotContain("highlight")` は、そのまま残す**）。`MarkdownOutlineTests` も名前付き引数で `libFolder` を渡す。確認（mac・隔離環境・**ネットを切って**）：品質ゲート 2、とくに mermaid と色付け（ライト／ダーク）、メモ |

完了条件：品質ゲート 1〜3（mac）。md の HTML に CDN の URL が無い。

## 5. Phase 45：md の CSP（#1。テスト先行）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 45-1 | CSP と nonce・script を head に・読み込み中の移動 | `MarkdownRenderer`：ページごとの nonce（9.6）、`<head>` の `charset` の直後に CSP の `<meta>`、Miharikun が入れる `<script>`（`#` リンク・Esc・位置の補正・チェックボックス・highlight.js の 2 つ・mermaid の 2 つ）すべてに `nonce` を付け、**全部 `<head>` に置く**（本文の後ろに置かない。9.6）。チェックボックスは `onclick="return false"` をやめ、クリックを取り消すスクリプトにする（**子孫のセレクタ**。9.6）。生の HTML の `http-equiv` を無害な名前に（9.6）。`PreviewNavigationPolicy`：**md のページの読み込み中は、自分のページ以外の移動を取り消す**（新しい結果 `Cancel`。9.6）。`MarkdownPreview` は `Cancel` なら振り分けない | `MarkdownRendererTests`：CSP の `<meta>` が `<base>` より前、nonce は 1 ページで同じでページごとに違う、Miharikun の `<script` すべてに nonce、**`<body` より後ろに `<script`・`nonce=` が無い**、md に書いた `<script>`・`<img onerror>` には nonce が付かない、チェックボックスの期待（`onclick` が無い・`tabindex="-1"`。**空行を挟んだリスト `- [ ] a\n\n- [x] b` を足す**）、取り消すスクリプトのセレクタが子孫の形、生の HTML の `<meta http-equiv="refresh">` に `http-equiv=` が残らない・コードブロックの中の `http-equiv` の文字は変わらない、`Body_is_not_changed_by_the_added_scripts` は本文に `<script` が無く、本文が見出しの 1 行だけと等しい（今より強い形）。`PreviewNavigationPolicyTests`：md・読み込み中・別の URL → `Cancel`、md・読み込み中・自分のページ → `Allow`、html・読み込み中 → `Allow`（今のまま）、md・読み込み後 → `Route`（今のまま）。既存のテストは引数を足さずに通る（既定は html の扱い） |
| 45-2 | 実機（mac）の要確認 | 9.7 の 5 点を mac の実機で確かめる。だめなら 9.7 の代わりの形で直す（直しが要るときだけコミット） | 確認（mac・隔離環境）：品質ゲート 2 の全部。9.8 の試験用の md で、md に書いたスクリプトが動かない（ページの文字が変わらない）、meta refresh で何も開かない |

完了条件：品質ゲート 1〜3（mac）。md に書いたスクリプト・属性のスクリプト・`javascript:` のリンク・meta refresh が動かない。

## 6. Phase 46：Claude の会話ログの保険の探索を絞る（テスト先行）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 46-1 | `ClaudeFolderName.Skeleton`・`ClaudeLocations.FindDirsByCwd` | **フォルダの名前で先に絞る**（9.10）：フォルダ名の英数字だけを取り出した並び（骨組み）が、対象のパス（そのまま・実パス）の骨組みと前方一致するフォルダだけを開く。開いたフォルダの中は今のまま、各ファイルの先頭の cwd を照らし、合ったら候補（そのフォルダの残りは見ない）。動く条件（候補が 0 件のとき・起動ごとに 1 回）・ログの文は今のまま | `ClaudeLocationsTests`：① `Skeleton`（英数字だけ・小文字・日本語と記号は消える。NFC と NFD で同じ）② 骨組みが違うフォルダは開かない（cwd が対象と合うファイルを置いても候補にならない）③ 骨組みが同じで、記号の数が違うフォルダ（`ClaudeFolderName.For(Project)` の `-` を `--` にしたもの）は見つかる ④ **同じフォルダに別のプロジェクトのセッションが混ざり、いちばん新しいファイルが別のプロジェクトでも見つかる** ⑤ 作業ツリーの形（骨組みが対象＋`claudeworktrees…`）も見つかる ⑥ 名前が短く切られたフォルダ（対象の骨組みの先頭部分）も開く ⑦ 骨組みが空のフォルダ（`-` だけ）は開かない ⑧ `[MacFact]` 実パスの骨組みでも見つかる（一時フォルダのシンボリックリンク）。既存の `Fallback_finds_folders_whose_files_have_a_matching_first_cwd_and_logs_it`・`Fallback_logs_nothing_when_nothing_is_found` と、`ClaudeSessionSourceTests` の保険の経路（`odd-name`・`odd-name-2`）は、**フォルダ名を「骨組みが同じで規則だけ違う名前」に替える**（9.10。名前を替えないと絞り込みで素通りになり、確かめたいことを試さなくなる）。期待は変えない |

完了条件：テストが通る。mac の隔離環境（`MIHARIKUN_CLAUDE_DIR` を一時フォルダに向け、名前の規則に合わない〈記号の数が違う〉フォルダと、関係ないプロジェクトのフォルダを手書きのログで作る）で、合うフォルダのセッションが一覧に出て、`app.log` に「フォルダ名の規則が、想定と違った」の 1 行が出る。

## 7. Phase 47：両 OS の実機確認・仕上げ（#4）

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 47-1 | Windows の実機の確認（利用者） | 9.8 の確認表を、利用者が Windows（WebView2）で行う。実装するセッションは、試験用のフォルダの作り方と手順を渡す | 9.8 の表の全部（git のフルパス・エクスプローラー〈名前に `,` を含むファイル〉・リンクの振り分け・CSP・meta refresh・ネットワークのパスの画像・ネットなしの mermaid と色付け・品質ゲート 2） |
| 47-2 | 仕上げ | `scripts/dist/README-win.txt`（Hook の置き場所の注意〈8.2・#4〉、同梱のライセンスの 1 行）、`README-mac.txt`（同梱のライセンスの 1 行）、`docs/release.md` の「出す前のチェック」（リンクの振り分け・md のスクリプト・meta refresh・ネットなしの mermaid）、要件定義の状況、`docs/security/security-review.html` の状態を「対応済み」に。`Fixes #31` | 文書の目視。テストが全部通る |

完了条件：要件定義 15 章の全体の完了条件を、両 OS の実機で満たす。

## 8. ファイル構成（追加・変更）

```
src/Miharikun.Core/
├─ Git/GitLocator.cs                        42-1 変更：Windows も PATH の使える項目（ドライブ・UNC）と決まった場所から git.exe を探す
├─ Sessions/ClaudeFolderName.cs             46-1 変更：Skeleton（英数字だけの骨組み）を足す
├─ Sessions/ClaudeLocations.cs              46-1 変更：保険の探索を、骨組みが合うフォルダだけに絞る
└─ Storage/AtomicFile.cs                    44-1 変更：WriteAllBytes を足す（置き換えの仕組みは共用）

src/Miharikun.Presentation/
├─ ShellOpen.cs                             42-2 変更：explorer.exe・open をフルパスで、/select は引用符で囲む
├─ LocalLinkRule.cs                         43-1 新規：ローカルのリンクの規則（LocalLinkAction・LinkProbe）
├─ PreviewNavigationPolicy.cs               45-1 変更：md の読み込み中の移動は Cancel
└─ ViewModels/
   ├─ DocumentsViewModel.cs                 43-2 変更：OpenLocalLink・OpenExternallyBecauseMissing・OpenExternal を規則に
   └─ MemoViewModel.cs                      43-2 変更：OpenLocalLink を規則に

src/Miharikun.Docs/
├─ Assets/vendor/                           44-1 新規：mermaid.min.js・highlight.min.js・hljs-github.min.css・hljs-github-dark.min.css・LICENSE-mermaid.txt・LICENSE-highlightjs.txt
├─ Miharikun.Docs.csproj                    44-1 変更：vendor を埋め込む（LogicalName）
├─ MarkdownAssets.cs                        44-1 新規：版（中身のハッシュ）・ファイルの一覧・書き出し
└─ MarkdownRenderer.cs                      44-2 変更：libFolder から読む ／ 45-1 変更：CSP・nonce・script を head に・チェックボックス・http-equiv

src/Miharikun/
├─ AppComposition.cs                        42-1 変更：起動時に git のパスを app.log へ（背景で）
└─ Views/MarkdownPreview.axaml.cs           43-2 変更：Route で UNC を捨てる ／ 44-2 変更：lib を書き出してから描く ／ 45-1 変更：Cancel は振り分けない

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
├─ Core/ClaudeLocationsTests.cs             46-1 変更（既存の保険のテストはフォルダ名だけ替える）
├─ Core/ClaudeSessionSourceTests.cs         46-1 変更（保険の経路のフォルダ名だけ替える）
├─ Presentation/ShellOpenTests.cs           42-2 変更
├─ Presentation/LocalLinkRuleTests.cs       43-1 新規
├─ Presentation/PreviewNavigationPolicyTests.cs 45-1 変更（足すだけ）
├─ Presentation/DocumentsViewModelTests.cs  43-2 変更
├─ Presentation/MemoViewModelTests.cs       43-2 変更
├─ Docs/MarkdownAssetsTests.cs              44-1 新規
├─ Docs/MarkdownRendererTests.cs            44-2・45-1 変更
└─ Docs/MarkdownOutlineTests.cs             44-2 変更（Render の呼び方だけ）
```

## 9. 実装の決めごと（迷いやすい所）

### 9.1 Windows の git の探し方（42-1）
1. `PATH` を `;` で分け、各項目の前後の空白と `"` を取る。
2. **使える形の絶対パス**だけを使う（文字列で判定）：
   - ドライブ文字で始まる（`^[A-Za-z]:[\\/]`）。
   - UNC（`\\server\share\…`・`//server/share/…`。先頭の区切り 2 つの次が `?`・`.`・区切りでない）。PATH は利用者・管理者の設定で、診断 #2 の入口（カレントフォルダ）とは別なので使う。今は名前 `git` で起動していて CreateProcess が PATH の UNC の項目も探すので、使わないと見つからなくなる環境が出る（レビュー #8・Q2＝A）。
   - 使わない：相対（`bin`）・`.`・空・ドライブなしのルート（`\x`）・`\\?\`・`\\.\`（デバイスの形）。
   - テストは mac でも動くので、`Path.IsPathFullyQualified` は使わない（mac では `C:\a` を相対と判定する）。つなぎは `\`（`Path.Combine` は使わない。mac では `/` でつながる）。
3. `<項目（末尾の \ / を除く）>\git.exe` が `fileExists` なら、それを返す（**PATH の順で最初に見つかったもの**。ドライブと UNC で順を変えない）。
4. PATH に無ければ、決まった場所を順に：`%ProgramFiles%\Git\cmd\git.exe` → `%ProgramW6432%\Git\cmd\git.exe` → `%ProgramFiles(x86)%\Git\cmd\git.exe` → `%LOCALAPPDATA%\Programs\Git\cmd\git.exe`。環境変数が無い・空・2. の使える形でないものは飛ばす。
5. どれも無ければ null（App は「不明」、Hook は git を省略。どちらも今の「git が無いとき」と同じ扱い）。
- **カレントフォルダは見ない**（これが目的）。`Lazy` のキャッシュは今のまま（起動中に 1 回）。Hook は git を使うイベントのときだけ探す（`GitProbe.Run` の中。今のまま）ので、git なしの 50ms は変わらない。
- `Find` の引数に `Func<string, string?> getEnv` を足す（既定の `Cached` は `Environment.GetEnvironmentVariable`）。mac の分岐は変えない。
- 起動時の `app.log` の 1 行は `Task.Run` の中で出す（PATH に切れたネットワークドライブ・届かない UNC があると `File.Exists` が数秒待つことがあり、画面のスレッドで呼ぶと起動が止まるため。`PreviewFiles.CleanOld` と同じ形）。
- 今も見つかっていない形は、今のまま見つからない（変えない）：`git.cmd` だけの古い git（.NET は `.exe` しか足さない）、展開されていない `%VAR%` の項目。

### 9.2 「フォルダで開く」のコマンド（42-2）
| OS | ファイル | 引数 |
|---|---|---|
| Windows | `<windowsDir の末尾の \ を除く>\explorer.exe`（`windowsDir` は `Environment.GetFolderPath(SpecialFolder.Windows)`） | `/select,"<file>"`（**引用符で囲んだ 1 つの文字列を `ProcessStartInfo.Arguments` にそのまま入れる**。`ArgumentList` は使わない） |
| mac | `/usr/bin/open` | `-R`、`<file>`（今のまま。`ArgumentList`） |
- エクスプローラーは `/select,` の後ろを `,` で区切って読むので、名前に `,` を含むファイルを選べるよう引用符で囲む（レビュー #10。計画で `Reveal` が実行形式へのリンクの受け皿になり、名前は攻撃する側が決められるため）。Windows のファイル名に `"` は使えないので、囲みが崩れることはない。
- `BuildRevealCommand` は `(string FileName, string[] Arguments, bool Raw)` を返し、`Raw` のとき `Reveal` は `Arguments[0]` を `ProcessStartInfo.Arguments` に入れる（Windows）。mac は `Raw = false` で今のまま `ArgumentList`。
- 47-1 で、空白と `,` を含む名前で、そのファイルが選ばれることを確かめる。だめなら、名前に `,` を含むときは親フォルダを開く（`explorer.exe "<dir>"`）に替える。
- `windowsDir` が空（取れない）なら起動しない（`app.log` に 1 行）。
- `ShellOpen.Open`（`UseShellExecute = true`。既定のアプリ・ブラウザ）は変えない。開く対象はフルパスか URL で、OS の「関連付けで開く」を通る。

### 9.3 ローカルのリンクの規則（43-1。`LocalLinkRule.Decide`）
判定の順（上で決まったら終わり）：
1. **使える形のパスでない → `Ignore`**（`IsUsableLocalPath`。**ファイルシステムに触る前に**、文字列だけで判定する）。
   - Windows：`^[A-Za-z]:[\\/]` で始まるものだけを通す。`\\`・`//`・`\/`・`/\`（どれも UNC になる）、`\??\`・`\\?\`・`\\.\`（デバイスの形）、ドライブなしのルート（`\x`）・相対は全部 `Ignore`。
   - mac：`/` で始まり、`//` で始まらず、`/net/` で始まらない（`/net` は自動マウントでネットワークにつながる）ものだけを通す。
2. Windows で、2 文字目（ドライブの `:`）のほかに `:` を含む（代替データストリーム）→ `Ignore`。
3. **シンボリックリンクを 1 段ずつたどる**（`LinkProbe.ReadLinkOnce`。リンクの中身を読むだけで、先は開かない）。1 段進むごとに、先のパスを判定 1・2 にかけ、だめなら `Ignore`。リンクでなくなったら終わり。**8 段を超えたら `Reveal`**（輪になったリンク）。たどった最後のパスを「先」とする（リンクでなければ無し）。
   - 先を開く（`Exists` で触る）のは、ネットワークでないと分かった後だけ。`ResolveLinkTarget(returnFinalTarget: true)` は使わない（.NET の Windows の実装は、最後までたどるときに先のファイルを開くので、`a.txt → \\server\x.txt` で判定の前に SMB の接続〈資格情報〉が起きる。レビュー #2）。
4. フォルダがある（`DirectoryExists`。リンクの先がフォルダも含む。mac の `.app` もフォルダ）→ `Reveal`。
5. ファイルが無い → `Ignore`。
6. リンクの名前と、先（リンクでなければ無し）の**両方**の拡張子が「開いてよい種類」→ `OpenWithDefaultApp`。それ以外 → `Reveal`。

開いてよい種類（大文字小文字は区別しない）：`.md` `.html` `.htm` `.png` `.jpg` `.jpeg` `.gif` `.webp` `.bmp` `.svg` `.pdf` `.txt`

| 入力（例） | 結果 |
|---|---|
| `\\server\share\a.txt`・`//server/share/a.txt` | Ignore（有無を確かめない） |
| Windows `\/server\share\a.txt`・`/\server\share\a.txt`・`\??\UNC\server\share\a.txt`・`\\?\C:\p\a.txt`・`\a.txt` | Ignore（有無を確かめない） |
| mac `//server/a.txt`・`/net/host/a.txt` | Ignore（有無を確かめない） |
| Windows `C:\p\a.txt:x.exe`・`C:\p\a.exe:x.txt` | Ignore |
| Windows `C:\p\a.txt`（ドライブの `:` だけ） | 次へ（判定 3 以降） |
| mac の名前に `:` を含むファイル（`isWindows = false`） | 判定 2 は使わない |
| フォルダ・フォルダへのリンク・`x.app` | Reveal |
| 無いファイル | Ignore |
| `a.txt`・`A.PDF`・`b.png` | OpenWithDefaultApp |
| `a.bat`・`a.exe`・`a.command`・`a.sh`・`a.lnk`・拡張子なし・`a.txt.`（末尾が `.`）・`a.bat `（末尾が空白） | Reveal |
| `a.txt`（リンク）→ `run.command` | Reveal |
| `a.txt`（リンク）→ `b.txt` | OpenWithDefaultApp |
| `a.txt`（リンク）→ `b.txt`（リンク）→ `run.command` | Reveal |
| `a.txt`（リンク）→ フォルダ | Reveal |
| `a.txt`（リンク）→ `\\server\x.txt` | Ignore（`FileExists`・`DirectoryExists` を呼ばない） |
| `a.txt`（リンク）→ `b.txt`（リンク）→ `\\server\x.txt` | Ignore（同じ） |
| 輪になったリンク（9 段以上） | Reveal |

- `LinkProbe` は 3 つの関数（`FileExists`・`DirectoryExists`・`ReadLinkOnce`〈リンクでなければ null。返すのはフルパス〉）の record。`LinkProbe.Real` は `File.Exists`・`Directory.Exists`・`new FileInfo(p).LinkTarget`（リンクの中身をそのまま読む。相対ならリンクのフォルダを基準に `Path.GetFullPath(target, dir)`）。例外（IOException・UnauthorizedAccessException）は `Reveal` に倒す（起動しない側）。
- `isWindows` は呼ぶ側が `OperatingSystem.IsWindows()` を渡す（テストで両方を試すため）。
- Windows の短い名前（`X~1.HTM`）：今の一覧の拡張子の先頭 3 文字で始まる危ない拡張子は無いので抜け道にならない。**一覧に足すときは、その点を確かめる**（コメントで残す）。
- **残るリスク（要件定義 12.7 に記載）**：mac の Finder のエイリアス（シンボリックリンクではない）と、ファイルごとの「このアプリで開く」の指定（リソースフォーク）は、拡張子で見えない。拡張属性に入るので git の clone では来ないが、zip・dmg では来うる。この計画では対策しない（要るなら別 Issue：`com.apple.ResourceFork` か、`com.apple.FinderInfo` のエイリアスの印があれば `Reveal`）。

### 9.4 VM と Route の順番（43-2）
1. `MarkdownPreview.Route(uri)`：`http`/`https`/`mailto` → 既定のアプリ（今のまま）。`uri.IsUnc` → 何もしない。`uri.IsFile` → `_vm.OpenLocalLink(uri.LocalPath)`。ほかのスキームは何もしない（今のまま）。
2. `DocumentsViewModel.OpenLocalLink(fullPath)`：① `!LocalLinkRule.IsUsableLocalPath(fullPath, isWindows)` なら終わり ② 対象フォルダ内で索引にある md/html はアプリ内で選ぶ（今のまま）③ `Decide` の結果で `_services.OpenWithDefaultApp`／`_services.RevealInFileManager`／何もしない。
3. `MemoViewModel.OpenLocalLink(fullPath)`：① 同じ ② `File.Exists && _resolveInApp` ならドキュメントタブで開く（今のまま。① の後なので `File.Exists` にネットワークのパスは来ない）③ 同じ。
4. `DocumentsViewModel.OpenExternallyBecauseMissing`（メモから頼まれた、対象フォルダ内の md/html で索引に無いもの）も **`Decide` を通す**。名前が `.md` でも中身がシンボリックリンクのことがあり、索引はリンクのフォルダの中に入らない（`DocumentIndexer` の `ShouldRecursePredicate`）ので、`<対象>/linkdir/x.md → run.command` が索引に無いまま、mac の `open` で実行されうる（レビュー #4）。ログの文（「索引に無いので、既定のアプリで開く」）は、結果に合わせて「見せるだけ」も出す。
5. 「既定のアプリで開く」ボタン（`OpenExternal`。選んでいる md/html を開く）も **`Decide` を通す**。選んでいる md が `.command` を指すリンクのとき、ボタンで実行されないように。ふつうの md/html は開いてよい種類なので今のまま開く。
- `Decide` の `isWindows` は VM のコンストラクタで受け取らず、`OperatingSystem.IsWindows()` を呼ぶ（VM のテストは実行している OS のまま）。

### 9.5 同梱のファイルの置き場と書き出し（44-1・44-2）
- 置き場：`<データ>/preview/lib/<Version>/`。
- `Version`：**埋め込みの中身から作る**。全ファイルの「名前・長さ・中身」を名前順に SHA-256 に入れ、`v-` ＋ 16 進の先頭 12 文字（例 `v-3fa2c09b1e7d`）。起動中に 1 回だけ計算（`Lazy`）。手で変える決まりは置かない（替え忘れで、長さが同じ差し替えが書き直されない・新旧の起動が書き直し合う、を防ぐ。レビュー #14）。**版の名前に `mermaid`・`highlight` の文字を入れない**（既存のテストの `DoesNotContain("mermaid")`・`DoesNotContain("highlight")` が本物の形で通るように。レビュー #13）。npm の版（`mermaid 11.x.y` など）は `THIRD-PARTY-NOTICES.txt` とコードのコメントに書く。
- 古い版のフォルダは消さない（消してよい。要件 6 章）。
- 埋め込みのリソース名：csproj で `LogicalName="Miharikun.Docs.vendor.%(Filename)%(Extension)"` を明記する（フォルダ名の `-`・数字を MSBuild が書き換えないように）。
- `PreviewFiles.CleanOld` は `preview/` の直下のファイルだけを消す（`EnumerateFiles` は直下だけ）ので、`lib/` は消えない。**`CleanOld` をサブフォルダまで消すように変えない**（コメントで残す）。
- `Ensure`：各ファイルについて「無い、または長さが埋め込みと違う」ときだけ `write(path, bytes)`。毎回の描画の前に呼ぶ（存在と長さを見るだけで軽い。起動中にデータのフォルダが消されても戻る）。版が中身から決まるので、同じフォルダに違う中身が来ることは無く、長さを見るのは途中で壊れたファイルの保険。書いた後は書き直さないので、表示中の WebView が読んでいるファイルを置き換えることは起きない。`write` の IOException・UnauthorizedAccessException は捨てて描画は続ける（mermaid・色付けが出ないだけ。`app.log` に 1 行）。
- 複数起動が同時に書いても `AtomicFile` の置き換えで壊れない（後勝ち。同じ中身）。
- md のページからの参照は**絶対の file URL**：`<base>` が md のフォルダを指すので、相対パスでは `lib/` に届かない。`MarkdownRenderer.FolderUri(libFolder) + ファイル名` を `WebUtility.HtmlEncode` して属性に入れる（`<base>` と同じ）。
- mermaid：`<head>` に `<script nonce defer src=".../mermaid.min.js"></script>` と、`<script nonce>` で `document.addEventListener('DOMContentLoaded', …)` の中に `mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme })` と `mermaid.run({ querySelector: 'pre.mermaid' })`（`window.mermaid` が無ければ何もしない。`run` の失敗は `catch` で捨てる。今の try/catch と同じ）。`defer` の script は DOMContentLoaded より先に動くので、`window.mermaid` はある。
- highlight.js：`<link rel="stylesheet" href=".../hljs-github(-dark).min.css">`（head）と `<script nonce defer src=".../highlight.min.js">` → `<script nonce>` で DOMContentLoaded の中に今の初期化。要るときだけ読む（今のまま）。

### 9.6 CSP と nonce（45-1）
- nonce：`RandomNumberGenerator.GetBytes(16)` を Base64。ページ（1 回の描画）ごとに作る。
- `<meta http-equiv="Content-Security-Policy" content="script-src 'nonce-<n>'; object-src 'none'; frame-src 'none'; form-action 'none'">` を `<meta charset>` の直後（`<base>`・`<style>` より前）に置く。`style-src`・`img-src` は付けない（md の画像・css・mermaid の `<style>` を今のまま許す）。
- nonce を付けるのは Miharikun が入れる `<script>` だけ：`#` リンクのスクロール、Esc の受け口、位置の補正、チェックボックス、highlight.js（読み込みと初期化）、mermaid（読み込みと初期化）。md の本文（Markdig の出力）には付けない。
- **Miharikun の script は全部 `<head>` に置き、本文の後ろには何も置かない**（レビュー #6）。Markdig は閉じていない生の HTML をそのまま通すので、本文の後ろに `<script nonce="…">` があると、md の最後の `<script src="…" `（`>` なし）がその `nonce="…"` を自分の属性として読む（nonce の横取り）。Chromium には対策があるが、WKWebView は未確認なので、並び順で防ぐ。
  - head の並び：`<meta charset>` → CSP → `<base>` → `<title>` → `<style>` → hljs の css → `<script nonce>`（`#` リンク・Esc・位置の補正・チェックボックス）→ `<script nonce defer src>` と初期化（hljs・mermaid）。
  - 本文を待つもの（位置の補正の最初の `remember()`、hljs・mermaid の初期化）は `DOMContentLoaded` の中で。`#` リンク・チェックボックスは `document` へのクリックの受け口なので、head のままで動く。`scroll`・`resize` の受け口は `window` なので、head のままで動く。
- チェックボックス：`ReadOnlyCheckboxes` の置き換えを `<input type="checkbox" tabindex="-1"`（`onclick` なし）にし、スクリプト `document.addEventListener('click', function (e) { var t = e.target; if (t && t.matches && t.matches('li.task-list-item input[type="checkbox"]')) e.preventDefault(); }, true);` で取り消す（Space キーで押した場合も click が来るので同じく取り消される）。見た目は今のまま（通常の色）。
  - **セレクタは子孫（`li.task-list-item input`）**。空行を挟んだタスクリストは、Markdig が `<li class="task-list-item"><p><input …>` と `<p>` の中に入れるので、子（`>`）では当たらない（Markdig 1.4.0 の出力で確認。レビュー #1）。
- **md のページの読み込み中の移動は取り消す**（レビュー #5）：`PreviewNavigationPolicy.Decide(target, pagePath, isLoadingPage, isMarkdownPage = false)` に新しい結果 `Cancel`（取り消して、振り分けもしない）を足す。md のページで読み込み中に、自分のページ以外へ移ろうとしたら `Cancel`。html のページは今のまま（読み込み中は iframe のために `Allow`）。md は CSP の `frame-src 'none'` で iframe を読まないので、この例外は要らない。`MarkdownPreview.OnNavigationStarted` は `isMarkdownPage: !_pageIsHtml` を渡し、`Cancel` なら `e.Cancel = true` だけ（`Route` しない）。
- **生の HTML の `http-equiv` を無害な名前にする**：Markdig の AST の `HtmlBlock`・`HtmlInline`（生の HTML）だけを対象に、`http-equiv` を `data-http-equiv` に置き換える（大文字小文字を区別しない。コードブロック・本文の文字は変えない）。md に `<meta http-equiv="refresh" content="0;url=…">` を書いても、読み込みの後にブラウザ・既定のアプリ・エクスプローラーが勝手に開かないように（CSP では止まらない。md で使う正当な理由は無い）。
- `<form>` の送信は CSP の `form-action 'none'` で止まる。クリックでの移動はリンクの振り分け（9.3）に来る。
- html のプレビュー・`EscapeListenerScript` の後入れ（`InvokeScript`）は変えない。

### 9.7 要確認（45-2。mac の実機。Windows は 47-1）
| 確かめること | だめなときの代わり |
|---|---|
| mermaid が CSP の下で図を描ける（`eval`・`new Function` を使っていないか） | CSP の `script-src` に `'unsafe-eval'` を足す（nonce の無いスクリプトは動かないままなので、守りは保たれる）。足したら要件定義 12.7 に書く |
| アプリからの `InvokeScript`（目次で移る・再読み込みの位置の復元・`window.scrollY`）が CSP に止められない | 止められたら、ページの中に nonce 付きの受け口の関数を置き、`InvokeScript` はその関数を呼ぶだけにする（呼び出し自体が止められるなら、利用者に相談） |
| Esc（ページの中で押す → `invokeCSharpAction`）が CSP の下でも届く（Avalonia が受け口を「ページに script の要素を足す」形で入れていると止まりうる） | 届かなければ利用者に相談（Issue #28 の試作の代わりの形：独自の URL への移動を取り消して受ける） |
| script を head に移し、`defer`・`DOMContentLoaded` にしても、mermaid・色付け・位置の補正・目次が今までどおり動く | 動かないものだけ原因を調べて直す（本文の後ろに戻すのは不可。9.6） |
| **ネットワークのパスの画像**（`![](//<届かない名前>/x.png)`。`<base>` で `file://<名前>/x.png` になる）を、WebView が読みに行かない（Windows は WebView2 で SMB の接続が起きないか。mac は `/net` の自動マウントが起きないか） | 読みに行くなら、`MarkdownRenderer` で本文の画像・リンクの URL が `//`・`\\`・`file://<ホスト>` で始まるものを無効にする（Markdig の AST の `LinkInline.Url` を空に。生の HTML の `src`・`href` は 9.6 の `http-equiv` と同じく `HtmlBlock`・`HtmlInline` だけを対象に置き換える）。直したら要件定義 12.7 の ⑤ に「画像も」と足し、テストを足す（レビュー #12） |

### 9.8 確認の手順と試験用のファイル（各 Phase・47-1）
- 隔離環境：`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダに向け、一時フォルダの試験用のプロジェクトを対象に起動する。`dotnet build`／`test` の前にアプリを止める。
- 試験用のファイルは手書きし、**中身は害のないものだけ**にする：`.bat`・`.command` は中身を `echo` の 1 行（起動されたら分かるだけ）、`.exe` は中身がただの文字のファイル（拡張子だけ）。md には、それらへのリンク、名前に `,` と空白を含む `.bat`（例 `a, b.bat`）へのリンク、`.txt`・`.png`・`.pdf`・フォルダへのリンク、`<script>` で本文の文字を書き換えるだけのスクリプト、`<img src=x onerror=...>`（同じく文字を書き換えるだけ）、`<meta http-equiv="refresh" content="0;url=https://example.com/">` と `.bat` 向けのもの（別の md に分ける）、`![](//<届かない名前>/x.png)`、空行を挟んだタスクリスト、mermaid とコードブロックを書く。
- 確認表（mac は各 Phase、Windows は 47-1）：

| 操作 | 期待 |
|---|---|
| 試験用のプロジェクトをカレントにして起動し、`app.log` を見る | git のフルパスが出る（Windows は `...\Git\cmd\git.exe` など。カレントのフォルダのパスではない）。未コミットの表示が普通に出る |
| 「フォルダで開く／Finder で表示」 | エクスプローラー／Finder でファイルが選ばれる |
| md の `.txt`・`.png`・`.pdf` へのリンクを押す | 既定のアプリで開く |
| md の `.bat`・`.command`・`.exe`・フォルダへのリンクを押す | 何も起動せず、エクスプローラー／Finder で選ばれた状態になる |
| md の `a, b.bat` へのリンクを押す（Windows） | 何も起動せず、エクスプローラーで `a, b.bat` が選ばれる |
| md の `\\server\share\x.txt` へのリンクを押す | 何も起きない |
| md の `<script>`・`onerror` | 本文の文字が変わらない |
| meta refresh の md を開く（https・`.bat`） | 何も開かない（ブラウザ・エクスプローラーも出ない）。md はそのまま表示される |
| `![](//<届かない名前>/x.png)` の md を開く | 接続が起きない（Windows は待たされない・資格情報の画面が出ない。9.7 の確かめ方で） |
| 空行を挟んだタスクリストのチェックボックスを押す | 変わらない |
| ネットを切って mermaid・コードブロックの md を開く | 図と色付けが出る（ライト／ダーク） |
| 品質ゲート 2 の項目 | 今までどおり |

### 9.9 ダウンロードの段取り（44-1）
- **ダウンロードの前に利用者の了承を取る**（ファイル名・取得元・おおよそのサイズを示す）。取得元は npm のレジストリ（`mermaid`、`@highlightjs/cdn-assets`）。版は、実装の時点の **11 系の最新の安定版**に固定し、計画と要件定義の状況に書き足す。
- 取得したら、npm の `dist.integrity`（sha512）と照らす。使うのは `mermaid/dist/mermaid.min.js`、`@highlightjs/cdn-assets/highlight.min.js`・`styles/github.min.css`・`styles/github-dark.min.css`、両方の `LICENSE`。`mermaid.min.js`（IIFE で `window.mermaid` を作るもの）が無い版なら、止まって相談する。
- 取得した tarball・展開したフォルダは、スクラッチの専用フォルダに置き、使うファイルだけをリポジトリに写す（展開したフォルダの中で何も実行しない）。
- `THIRD-PARTY-NOTICES.txt` には、名前・版・取得元・ライセンスの全文を書く。mermaid の min.js に入っている依存のライセンスは、取得した版の配布物（`dist` のライセンスのファイル、無ければ `package.json` の依存）から拾う（要確認）。

### 9.10 保険の探索の読み方（46-1。`ClaudeLocations.FindDirsByCwd`）
1. 動く条件は今のまま：名前の規則で候補が 0 件のとき、起動ごとに 1 回（`ClaudeSessionSource.CurrentDirs`）。`.claude/projects` が無ければ動かない。
2. **骨組み**（`ClaudeFolderName.Skeleton(string)`）：文字列から ASCII の英数字だけを取り出し、小文字にしたもの。日本語・記号・区切りは消えるので、NFC と NFD で同じになる。例：`C:\zDev\repo\Miharikun` → `czdevrepomiharikun`、フォルダ名 `C--zDev-repo-Miharikun` → 同じ。
3. 対象の骨組み：`ProjectPath.Normalize(対象)` と、`RealPath.Resolve(対象)`（違うときだけ）の 2 つまで。
4. `projects/` の下のフォルダのうち、**フォルダ名の骨組みが空でなく、対象の骨組みのどれかと前方一致する**（フォルダの骨組みが対象の骨組みで始まる＝同じ名前・作業ツリー〈`…claudeworktrees…`〉・記号の数だけ違う名前、または、対象の骨組みがフォルダの骨組みで始まる＝長いパスが短く切られた名前）フォルダだけを開く。**ほかのフォルダは一覧を取るだけで、中のファイルは開かない**。
5. 開いたフォルダの中は**今のまま**：直下の `*.jsonl` の `ReadFirstCwd`（先頭から最大 200 行のうち、最初に cwd を持つ行まで）を照らし、対象のプロジェクト（または作業ツリー）に合うファイルが 1 つでもあれば候補にする（そこでそのフォルダは終わり）。**合わないファイルがあっても打ち切らない**（同じフォルダに別のプロジェクトのセッションが混ざりうるため）。
6. ログの文（見つけたときだけ、フォルダ名を 1 行）は今のまま。会話の中身はログに書かない。

| フォルダ | 結果 |
|---|---|
| 骨組みが対象と違う（別のプロジェクト） | 開かない |
| 骨組みが同じ・記号の数が違う（NFD の日本語名・記号の扱いの違い）で、どれかのファイルの cwd ＝ 対象 | 候補にする |
| 骨組みが同じで、新しいファイルの cwd ＝ 別のプロジェクト、古いファイルの cwd ＝ 対象 | 候補にする（混ざったフォルダ） |
| 骨組みが同じで、どのファイルの cwd も対象でない | 候補にしない |
| 骨組みが対象＋`claudeworktrees…` で cwd ＝ 作業ツリー | 候補にする |
| 骨組みが空（`-` だけのフォルダ名） | 開かない |

- 絞る理由：フォルダ名は英数字以外を全部 `-` にする（`ClaudeFolderName.For`）ので、名前の規則がずれる原因（NFD の日本語名・記号の扱い）は英数字の並びを変えない。英数字の並びで絞れば、保険が拾いたいフォルダを残したまま、ほかのプロジェクトのフォルダを開かずに済む（レビュー #7・Q1＝A）。
- 「1 フォルダにつき 1 ファイルで打ち切る」案はやめた：フォルダ名の規則は英数字以外を区別しないので、`my-app`・`my_app`・`my/app`、日本語の文字数が同じパスは同じフォルダになり、**1 つのフォルダに別のプロジェクトのセッションが混ざりうる**（今のコードもファイルごとに cwd を照らして、合わないものを `Ignored` にしている）。打ち切ると、新しいファイルが別のプロジェクトのとき、対象のセッションが全部、黙って消える。保険がいちばん守りたい「mac の NFD の日本語名」と重なる。
- 取りこぼし（受け入れる）：Claude Code を、Miharikun に渡したパスとも、その実パスとも**英数字が違うパス**（シンボリックリンクを含む論理パスなど）で起動した場合は、絞り込みで外れる（今は拾える）。Claude Code の `cwd` は実パスで来る（`RealPath` のコメント）、mac の起動手順も実パス（`pwd -P`）なので、まれと見て受け入れる。長いパスを短く切って後ろにハッシュを付ける形の名前は、前方一致に当たらないので拾えない（要るなら別 Issue）。

## 10. リスクと対策

| リスク | 対策 |
|---|---|
| md の見た目・動きが変わる（44・45。script を head に移す・チェックボックスの仕組みを替える） | 品質ゲート 2 を mac と Windows で確かめる。既存のテストは期待の差し替えだけ（件数は減らさない・確認を弱めない）。空行を挟んだタスクリストのテストを足す |
| mermaid や `InvokeScript` が CSP で動かない | 45-2 で先に mac の実機で確かめ、9.7 の代わりの形にする |
| Windows で git が見つからなくなる（PATH に無い・変わった場所に入れた） | PATH の UNC の項目も使い、決まった場所も探す。見つからないときは今の「git が無い」と同じ「不明」。`app.log` にパスを出すので原因が分かる。47-1 で利用者の PC で確かめる |
| PATH の届かない UNC・切れたネットワークドライブで、git を探すのが待たされる | 今の CreateProcess も同じく待つので悪くはならない。起動時のログは背景で出す（画面を止めない） |
| 開いてよい種類が狭く、よく使うファイルが開けない | 起動はしないが Finder／エクスプローラーで選ばれるので、そこから開ける。一覧は要件定義 12.7 にあり、足すのは利用者の判断 |
| mac のエイリアス・ファイルごとの「このアプリで開く」は拡張子で見えない | 残るリスクとして要件定義 12.7 に書く（9.3）。git の clone では来ない |
| エクスプローラーの `/select,` の引用符が効かない | 47-1 で `,` と空白を含む名前で確かめ、だめなら親フォルダを開く形に替える（9.2） |
| ネットワークのパスの画像で接続が起きる | 45-2・47-1 で確かめ、起きるなら描画で無効にする（9.7） |
| 同梱で配布物が大きくなる（mermaid は数 MB） | 受け入れる（今の単一ファイルは 130MB 超） |
| ページのスクリプトから http/https のリンクが勝手に開く（html のプレビュー） | ブラウザが開くだけなので受け入れる（要件定義 12.7 に明記）。md のページはスクリプトが動かず、meta refresh も無害にするので起きない |
| ダウンロードしたファイルが改ざんされている | npm の `dist.integrity` で照らす。了承を取ってから取得 |
| 保険の探索の絞り込みで、今まで見つかっていたフォルダが外れる | 外れるのは、英数字の並びが対象とも実パスとも違う名前のフォルダだけ（9.10 の取りこぼし）。見つからないときの今の動き（一覧に出ない）と同じで、`app.log` に何も出ないのも今と同じ |

## 11. 決定事項（確認済み。診断の質問 Q1〜Q3、その後の相談、計画のレビューの Q1・Q2）
1. Q1＝C：開いてよい種類（拡張子の一覧）だけ既定のアプリで開き、ほかは起動せず Finder／エクスプローラーで見せる。
2. Q2＝A：md の生の HTML は残し、CSP（nonce）でスクリプトだけ止める。
3. Q3＝A：mermaid・highlight.js を同梱する（CDN をやめる）。
4. Claude の会話ログの保険の探索（名前で見つからないとき、cwd で探す）は**残す**。外すと、名前の規則がずれたとき（mac の NFD の日本語名・シンボリックリンク経由・Claude Code の版の違い）に、Claude のセッションが黙って一覧から消えるため。代わりに、ほかのプロジェクトのログを読む量を減らす（2026-10-09 の相談。Phase 46）。
5. 計画のレビューの Q1＝A（2026-10-09）：4 の減らし方は、**フォルダの名前の英数字の並び（骨組み）で先に絞り、絞ったフォルダの中は今のまま全ファイルを照らす**。「1 フォルダにつき 1 ファイルで打ち切る」案はやめる（同じフォルダに別のプロジェクトのセッションが混ざりうるため。9.10）。
6. 計画のレビューの Q2＝A（2026-10-09）：Windows の git は、PATH の UNC の項目も使う（`\\?\`・`\\.\` の形は除く。PATH の順を守る）。

計画で決めたこと（仕様にない細部）：
- 開いてよい種類の一覧：`.md` `.html` `.htm` `.png` `.jpg` `.jpeg` `.gif` `.webp` `.bmp` `.svg` `.pdf` `.txt`（Office の文書・`.csv`・`.json` などは入れない。マクロや別のアプリの動きを持ち込まないため）。
- フォルダへのリンクは、開かずに Finder／エクスプローラーで選ばれた状態にする（mac の `.app` もフォルダなので、開くと起動してしまうため）。今はメモのリンクでフォルダを開いていた（テストを直す）。
- パスは「使ってよい形」だけを通す（Windows はドライブ文字で始まるものだけ、mac は `/` で始まり `//`・`/net/` でないもの）。ネットワークの形を数え上げるのでなく、許す形を決める。
- シンボリックリンクは 1 段ずつたどり、各段でネットワークを先に判定する。名前とたどった先の両方で種類を見る。たどれないとき・8 段を超えるときは起動しない側（`Reveal`）に倒す。
- メモから頼まれた「索引に無い md/html」と「既定のアプリで開く」ボタンも同じ規則を通す。
- Windows の `:` を含むパス（代替データストリーム）は何もしない。
- CSP に `object-src 'none'; frame-src 'none'; form-action 'none'` も入れる（GitHub の md の表示でも出ないもの）。`style-src`・`img-src` は付けない。
- Miharikun の script は md のページの `<head>` に置く（本文の後ろに置かない）。md のページの読み込み中の移動は取り消す。生の HTML の `http-equiv` は無害な名前にする。
- mac の「Finder で表示」も `/usr/bin/open` のフルパスにする（.NET は名前だけのとき、mac でもカレントフォルダを PATH より先に見る見込み。要件定義 13 章）。Windows の `/select,` は引用符で囲む。
- 起動時に git のパスを `app.log` に 1 行出す（確認と、見つからないときの原因の調査のため。背景で）。
- 同梱のファイルは `preview/lib/<版>/` に書き出す（埋め込みから。配布物のファイルを増やさない）。版は中身のハッシュから作る。`AtomicFile.WriteAllBytes` を足す。
- `MarkdownRenderer` の `libFolder` は `baseFolder` の次の引数にし、アプリのコードでは名前付き引数で渡す（同じ string の取り違えを防ぐ）。
- highlight.js も mermaid と同じく 11 系の最新の安定版に固定する（今は 11.9.0。見た目がほぼ変わらない範囲）。mermaid の `securityLevel` は `'strict'` を明記する。
- README の Hook の置き場所は、`%USERPROFILE%` の下を先に勧め、`C:\dev` は注意つきで残す（会社の PC で `%USERPROFILE%` の下が動くかは未確認のため。47-1 で利用者に聞く）。

## 12. 実装するセッションへの注意（必ず読む）

### 12.1 始める前に
- **この計画・診断（`docs/security/security-review.html`）・計画のレビュー（`docs/security/security-plan-review.html`）・要件定義の変更が見えることを確かめる**。ブランチ `docs/security-review` にあり、コミットされていないことがある。別の作業ツリー（worktree）や別の環境で始まったセッションには、未追跡のファイルが無い。見つからなければ、推測で進めずに利用者に聞く。
- 計画と要件定義に食い違いが無いか確かめる。あれば要件定義が正。判断がつかなければ聞く。
- `CLAUDE.md` の決まり：main から作業ブランチを切る（または利用者の指示のブランチ）、`git add` はパスを指定、コミット・push・PR は頼まれたときだけ、完了報告にはビルドの有無と出力先・テストの件数・できなかった確認を書く。
- 本物のデータ・本物のプロジェクトの文書は使わない（隔離環境と手書きの試験用のファイル）。試験用の実行形式は作らない（9.8 のとおり、拡張子だけのただの文字のファイル）。
- ダウンロードは了承を取ってから（9.9）。システムへのインストールはしない。
- Issue は #31。途中のコミットは `Refs #31`、最後だけ `Fixes #31`。

### 12.2 各フェーズの終わり
- mac の隔離環境で確かめ（9.8）、要件定義 15 章の `[ ]` → `[x]` と「状況」（テストの件数・確かめたこと・できなかったこと）を更新する。
- 報告には、変えたこと、テスト結果（件数）、ビルドの出力先、できなかった確認を書く。**次のフェーズに進む前に止まって報告する**。
- Phase 45 の後は、利用者が望めばレビューを挟む（差分と品質ゲートの結果を渡す）。Phase 47-1 は利用者の Windows で行うので、手順を渡して待つ。

## 13. レビューの指摘の反映先（`docs/security/security-plan-review.html`）

| # | 指摘 | 反映先 |
|---|---|---|
| 1 | チェックボックスのセレクタが空行ありのリストに当たらない | 9.6・45-1・品質ゲート 2・9.8 |
| 2 | リンクの先を最後までたどる時点で UNC に触れる | 9.3（判定 3・`LinkProbe.ReadLinkOnce`）・43-1 |
| 3 | ネットワークのパスの判定が `\\`・`//` だけ | 9.3（判定 1・`IsUsableLocalPath`）・9.4・43-1 |
| 4 | 索引に無い md を開く経路が規則を通らない | 9.4（4・5）・43-2 |
| 5 | 読み込み中の移動を全部通す（meta refresh） | 9.6・45-1・9.8 |
| 6 | 本文の後ろの nonce の横取り | 9.6・9.5・45-1・9.7 |
| 7 | 保険の探索の打ち切りで、混ざったフォルダを取りこぼす | Q1＝A。9.10・46-1・11 章・要件定義 9.1 |
| 8 | PATH の UNC の項目を使わなくなる | Q2＝A。9.1・42-1・11 章・要件定義 13 章 |
| 9 | 起動時の git のログが画面を止めうる | 9.1・42-1 |
| 10 | エクスプローラーの `/select,` と `,` | 9.2・42-2・9.8 |
| 11 | mac のエイリアス・ファイルごとの「開くアプリ」 | 9.3（残るリスク）・10 章・要件定義 12.7 |
| 12 | ネットワークのパスの画像 | 9.7・9.8 |
| 13 | 版の名前が既存のテストとぶつかる | 9.5・44-2 |
| 14 | 版を手で変える決まり | 9.5・44-1 |
| 15 | 書き出しの条件の、要件と計画の食い違い | 要件定義 12.7 |
| 16 | 埋め込みのリソース名 | 9.5・44-1 |
| 17 | mermaid の `securityLevel` | 9.5・44-2 |
| 18 | メモのテストの名前・`MarkdownOutlineTests` の呼び方 | 43-2・44-2 |

**状態：レビュー反映済み**（2026-10-09。Q1＝A・Q2＝A）。実装に進める。
