# 引き継ぎ（Issue #31 は完了・main にマージ済み。次の作業待ち）— 2026-10-09

## 現状
- **Issue #31（セキュリティ診断の対応。Phase 42〜47）は完了**。PR #32 を main にマージ済み（マージコミット `e82e049`）、Issue #31 は閉じた。作業ブランチ `feature/issue31-security` と `docs/security-review` は、ローカル・リモートとも**削除済み**（いまブランチは `main` だけ）。**リリースはまだ出していない**（最後のタグは `v0.7.0`。#31 の変更は次のリリースに入る）。
  - 文書：診断 `docs/security/security-review.html`（状態は「対応済み」）／計画のレビュー `docs/security/security-plan-review.html`／実装計画 `docs/security/security-plan.md`・`.html`／要件定義 13 章・12.7・12.10・8.2・9.1・15 章 Phase 42〜47／Windows 実機の確認の手順 `docs/security/windows-check.md` と試験用フォルダを作る `docs/security/New-SecurityTestProject.ps1`／出す前のチェックは `docs/release.md` の「セキュリティ（Issue #31…）」。
  - 実装（Phase ごと）：42 `GitLocator`（Windows もフルパス）・`ShellOpen`（`explorer.exe`／`/usr/bin/open` のフルパス、`/select,"…"`）／43 `LocalLinkRule`（開いてよい種類だけ既定のアプリ、ほかは Finder／エクスプローラーで見せる）・`DocumentsViewModel`・`MemoViewModel`／44 `MarkdownAssets`＋同梱の mermaid 11.17.2・highlight.js 11.12.0（`src/Miharikun.Docs/Assets/vendor/`、`preview/lib/<版>/` に書き出す）・`AtomicFile.WriteAllBytes`・`scripts/dist/THIRD-PARTY-NOTICES.txt`／45 `MarkdownRenderer` の CSP（nonce・script は全部 head）・`PreviewNavigationPolicy.Cancel`・生の HTML の `http-equiv` を無害化／46 `ClaudeFolderName.Skeleton`・`ClaudeLocations.FindDirsByCwd`（骨組みで絞る）。
  - テスト：Tests 1315 合格・スキップ 30、UiTests 65 合格・スキップ 3（PR の CI 4 件も合格）。
  - 確認：Windows（WebView2）の実機で、`windows-check.md` の確認表を**全項目 OK**（利用者）。mac は、git のフルパス・保険の探索は隔離環境で、md の表示は Chromium で確認。
- 直前の Issue #28（拡大モード）は `v0.7.0`（2026-10-09）で出した。その前は `v0.6.0`。
- Issue #7 の壁打ちから、Issue #27（検索のヒット箇所）・#29（README に PATH の使い方）が残っている（#28 は完了）。

## 未実施・保留
1. **mac のアプリ内プレビュー（WKWebView）での目視が、まだ誰もしていない**：ページの中の Esc・目次で移る・保存での再読み込みの位置の復元（計画 9.7 の 2・3）、ネットワークのパスの画像（同 5）。Windows では OK。mac で動かなければ、計画 9.7 の「だめなときの代わり」で直す。
2. 同梱した mermaid の依存の `khroma` は、npm の license 欄が取れず未確認（`THIRD-PARTY-NOTICES.txt` にもそう書いた）。公開前に見る。依存ごとのライセンス全文までは載せていない（mermaid.min.js の末尾のコメントに残っている）。
3. Issue #27（検索のヒット箇所）は未着手（画面に手が入るので、ペライチから）。#29（README に PATH の使い方）は文書だけ。
4. 残るリスク（要件定義 12.7）：mac の Finder のエイリアスと、ファイルごとの「このアプリで開く」の指定は、拡張子で見えない（対策していない。要るなら別 Issue）。Claude の保険の探索は、対象とも実パスとも英数字が違うパスで Claude Code を起動した場合は拾えない（受け入れた）。
5. Issue #28 の保留（17 章）：今見ている見出しの強調（Q9）。
6. CI の注意：Node.js 20 の actions（`checkout@v4`・`setup-dotnet@v4`・`upload-artifact@v4`・`download-artifact@v4`）が非推奨、`ubuntu-latest` が 2026-10-19 から Ubuntu 26 に切り替わる。いまは動いている。
7. CI の揺れ：PR #26 の push 側で macos の UiTests `DocumentsViewTests.Selecting_a_row_shows_the_overview_and_enables_the_buttons` が 1 回だけ落ち、再実行で通った。原因は未調査（PR #30・#32 では出なかった）。また落ちたら調べる（`superpowers:systematic-debugging`）。
8. 前からの保留（変わらず）：Phase 28・30-1・30-2 の後のレビュー（利用者が望めば）、html をダークテーマに対応させる案、Phase 31・32 の後回しの確認、Issue #23 の本物の Claude のログでの表示と `ai-title`。

## 守ること・作業の決まり
- `CLAUDE.md` の「作業の決まり」「守ること」に従う。返答は日本語、コミット・push・PR・マージ・リリース・ブランチ削除は頼まれたときだけ、`git add` はパス指定（`-A` は使わない）、完了報告に「ビルドしたかと出力先・テスト件数・できなかった確認」を書く、本物の `~/.cursor/hooks.json` やデータは勝手に触らない、`~/.claude/` には書かない（読み取りだけ）。
- `dotnet build`／`test` の前にアプリを止める。利用者が `dotnet run --project src/Miharikun` で起動していることがある（`pgrep -fl Miharikun` で確かめる）。止められないときは `-p:OutDir=` で別の場所に作る。
- 私は画面を撮れない。mac の見た目は `tests/Miharikun.UiTests`（Avalonia.Headless）か、利用者が手元で見る。隔離環境で見せるときは、`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダにし、手書きの md・ダミー会話ログを置いて、`src/Miharikun/bin/Debug/net10.0/Miharikun <プロジェクトのフォルダ>` を起動してもらう。

## 今回の途中で分かったこと（文書に書いていない部分）
- **mac は引数なしで起動するとフォルダ選択の画面で待つ**（`app.log` も `data/` もできない）。隔離環境の確認では `Miharikun <プロジェクトのフォルダ>`（`pwd -P` の実パス）で起動する。
- 空行を挟んだタスクリストは Markdig が `<li class="task-list-item"><p><input …> a</p></li>` にする（子ではなく孫）。CSS の `.task-list-item > input` はこの形に当たらない（今までと同じ見た目。直していない）。チェックボックスの取り消しスクリプトは子孫のセレクタ。
- Markdig の生の HTML を書き換えるには、`doc.Descendants<HtmlBlock>()` の `Lines.Lines[i].Slice` と `HtmlInline.Tag` を直す（`Markdig.Helpers.StringSlice`）。
- `mermaid.min.js`（npm の `dist/`）は IIFE で、末尾で `globalThis.mermaid` を作る。`securityLevel: 'strict'` でも、CSP の `script-src 'nonce-…'` だけで `unsafe-eval` なしで図が描けた（Chromium）。
- 同梱ファイルの版は中身のハッシュ（`v-` ＋ 12 文字）。`mermaid`・`highlight` の文字を入れない（既存の `DoesNotContain` のテストのため）。npm の tarball は `npm pack` で取り、`openssl dgst -sha512 -binary | base64` で `dist.integrity` と照らした。
- アプリ内のブラウザは `file://` のページには JS を実行できない。HTML の確認は、スクラッチで `python3 -m http.server` を立て、`file:///…/lib/` の URL を `/preview/lib/` に置換した写しを開くと、CSP・mermaid・クリックの確認ができる（WKWebView ではなく Chromium）。出力の HTML は、スクラッチの小さな console プロジェクト（`Miharikun.Docs` を参照）から作った。
- `GitLocator` の Windows の探し方は文字列だけで判定する（`Path.IsPathFullyQualified` は mac で `C:\a` を相対と見る）。`ShellOpen.BuildRevealCommand` は `(FileName, Arguments, Raw)` を返し、Windows は `Raw`（引用符で囲んだ 1 つの文字列を `Arguments` に入れる）。
- 保険の探索のテストは、フォルダ名を「骨組みが同じで記号の数だけ違う名前」（`For(Project).Replace("-", "--")`）にしないと、絞り込みで素通りして確かめたいことを試さなくなる。絞り込みを一時的に外して、2 つのテストが落ちることを確認した。
- コミットの分け方：Phase 44 と 45 は `MarkdownRenderer` などを同じファイルで書き換えるので 1 コミットにした。要件定義の `[x]`・状況は、`git diff -U0` のハンクを `git apply --cached --unidiff-zero` で Phase ごとに分けて入れた。
- 前回までの分（変わらず）：目次は `Tapped` と `DoubleTapped` の両方／`RelayCommand` の `Execute` は `CanExecute` を見ない／VM のテストで走査の後は結果そのものを待つ／WebView の試作は捨てた小さな Avalonia アプリ／Avalonia.Controls.WebView の API の説明は `lib/net10.0-android36.0/` にしかない／`dotnet-script` で Markdig を試せる／`LSMinimumSystemVersion=14.0` と `minos 12.0` の食い違いは意図どおり／署名はアドホックのみ／ヘッドレスの UiTests では本物の WebView を作らない（`MarkdownPreview.WebViewDisabled`）／PR を出すと CI は 2 回走る。

## 推奨スキル（次のエージェントが Skill で呼ぶ）
- `superpowers:using-superpowers`（開始時）
- `peraichi` → `impl-plan`（大きい修正をするとき。#27 など）
- `superpowers:executing-plans`（計画に従って Phase ごとに実装するとき）
- `superpowers:test-driven-development`（Core・ViewModel の実装）
- `superpowers:verification-before-completion`（完了報告の前）
- `superpowers:systematic-debugging`（CI の揺れ・実機での不具合）
- `superpowers:finishing-a-development-branch`（ブランチの片付けを頼まれたとき）

## 次にやること
頼まれた作業を優先する。決まっていなければ、mac の実機での目視（上の保留 1）を利用者に頼むか、#27（検索のヒット箇所。ペライチから）／#29（README。文書だけ）を提案する。リリースを出す場合は、`docs/release.md` の「出す前のチェック」（#31 のセキュリティの項目を含む）を先に確かめる。
