# 引き継ぎ（Issue #28：Phase 38〜40 実装済み・mac 確認 OK。残りは Windows の実機確認）— 2026-10-09

## 現状
- **Issue #28（ドキュメントタブの拡大モード：目次｜プレビュー）**。作業ブランチ `feature/issue28-documents-expand-mode`。**Phase 38〜40 は実装済みで、mac の実機は利用者が確認 OK**（要件定義 15 章は `[x]`）。Phase 41 は文書分（`docs/release.md` のチェック項目）だけ済んでいて、**41-1（Windows の実機確認）が未実施のため `[ ]`**。
  - 技術調査：`docs/issue28/issue28-tech-investigation.md`／設計：`docs/issue28/issue28-documents-expand-design.html`／要件定義 12.7.1・12.4.1・15 章 Phase 38〜41／実装計画（確定。md と HTML の対）：`docs/issue28/issue28-documents-expand-plan.md`・`.html`
  - 実装：Docs の `MarkdownRenderer.RenderWithOutline`・`MarkdownOutline`（38）、Presentation の `DocumentsViewModel.Expand.cs`・`OutlineItemViewModel`・`PreviewScripts`・`IPreviewHost` の新しい口（39）、画面の `MarkdownPreview`・`DocumentsView`・`DepthIndentConverter`・`DashboardView` の Esc（40）。
  - テスト：Tests 1197 合格・スキップ 29、UiTests 65 合格・スキップ 3。
- リリース `v0.6.0` を出した（2026-10-08。タグ `v0.6.0` は `247ce9a`。release ワークフロー run 37767777027）。release のジョブが GitHub Release に両 OS の zip と `.sha256` の 4 ファイルを載せる流れを、これで初めて確かめた。
- Issue #7（業務で便利になったかの壁打ち）は閉じた。結果は `docs/issue7/issue7-business-value.md`。そこから Issue #27（検索のヒット箇所）・#28（拡大モード）・#29（README に PATH の使い方）を作った。

## 未実施・保留
1. **Issue #28 の Windows（WebView2）の実機確認**（41-1。利用者の作業）。見る項目は `docs/release.md` の「ドキュメントの拡大モード」。とくに、ページの中の Esc・目次で移る・位置の補正が Chromium の働きと二重にならない。`invokeCSharpAction` が使えない／`Body` の形が違うときは計画 7.4 の代わりの形、位置が二重にずれるときは md のページに `overflow-anchor: none`。済んだら要件定義の Phase 41 を `[x]` にし、最後のコミットに `Fixes #28`（PR 本文に `Closes #28`）。
2. Issue #28 の保留（17 章）：今見ている見出しの強調（Q9）。
3. Issue #27・#29 は未着手。#27 は画面に手が入るので、ペライチから。#29 は文書だけ。
4. CI の注意（release の実行で出た）：Node.js 20 の actions（`checkout@v4`・`setup-dotnet@v4`・`upload-artifact@v4`・`download-artifact@v4`）が非推奨、`ubuntu-latest` が 2026-10-19 から Ubuntu 26 に切り替わる。いまは動いている。
5. CI の揺れ：PR #26 の push 側の実行で macos の UiTests `DocumentsViewTests.Selecting_a_row_shows_the_overview_and_enables_the_buttons`（`vm.Overview` が null）が 1 回だけ落ち、再実行で通った。原因は未調査。また落ちたら調べる（`superpowers:systematic-debugging`）。Issue #28 で同じテストファイルに足すので、待ち方に気をつける。
6. 前からの保留（変わらず）：Phase 28・30-1・30-2 の後のレビュー（利用者が望めば）、html をダークテーマに対応させる案、Phase 31・32 の後回しの確認（iframe を含む html・Windows の Runtime 未導入の案内・mermaid のダーク）、Issue #23 の本物の Claude のログでの表示と `ai-title`。

## 守ること・作業の決まり
- `CLAUDE.md` の「作業の決まり」「守ること」に従う。返答は日本語、コミット・push・PR・マージ・リリース・ブランチ削除は頼まれたときだけ、`git add` はパス指定（`-A` は使わない）、完了報告に「ビルドしたかと出力先・テスト件数・できなかった確認」を書く、本物の `~/.cursor/hooks.json` やデータは勝手に触らない、`~/.claude/` には書かない（読み取りだけ）。
- `dotnet build`／`test` の前にアプリを止める。利用者が `dotnet run --project src/Miharikun` で起動していることがある（`pgrep -fl Miharikun` で確かめる）。止められないときは `-p:OutDir=` で別の場所に作る。
- 私は画面を撮れない。mac の見た目は `tests/Miharikun.UiTests`（Avalonia.Headless）か、利用者が手元で見る。隔離環境で見せるときは、`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダにし、手書きの md・ダミー会話ログを置いて、`src/Miharikun/bin/Debug/net10.0/Miharikun <プロジェクトのフォルダ>` を起動してもらう。

## 今回の途中で分かったこと（文書に書いていない部分）
- WebView の試作は、スクラッチパッドに置いた捨てる小さな Avalonia アプリ（本体と同じ版の Avalonia 12.1.3・Avalonia.Controls.WebView 12.1.0）で行った。セッションと一緒に消えるので残っていない。やり方と結果は技術調査の文書にある。作り直すなら、`NativeWebView` を 1 つ置いた画面で、自動の確認は `InvokeScript` とログで行い、実キーの Esc だけ利用者に押してもらう（ウィンドウが 1 分ほど出ることを先に伝える）。
- Avalonia.Controls.WebView 12.1.0 の API の説明（xml）は、パッケージの `lib/net10.0-android36.0/` にしか入っていない。`invokeCSharpAction` の仕組みは公式の文書が薄く、DLL の文字列と試作で確かめた。
- Markdig の振る舞いを手早く試すには、入っている `dotnet-script`（2.0.0）で `#r "nuget: Markdig, 1.4.0"` と書いた `.csx` を動かす（パッケージはキャッシュにある）。
- ペライチ・計画の HTML の図は、アプリ内のブラウザで `file://` を開けば、`svg-check.js` 相当の JS を実行して、はみ出し・重なりを数えられる（ファイルを直したら開き直す。再読み込みでは古いまま）。
- ダッシュボードの Esc が、ほかのタブでも効く不具合は、利用者が mac の実機で再現を確かめた（Issue #28 の Phase 40 で直す予定）。
- 前回からの分（変わらず）：`LSMinimumSystemVersion=14.0` と `minos 12.0` の食い違いは意図どおり／release の手動実行の artifact は二重の zip／署名はアドホックのみ／ヘッドレスの UiTests では本物の WebView を作らない（`MarkdownPreview.WebViewDisabled`）／PR を出すと CI は push 側と pull_request 側の 2 回走る。

## 推奨スキル（次のエージェントが Skill で呼ぶ）
- `superpowers:using-superpowers`（開始時）
- `superpowers:executing-plans`（計画に従って Phase ごとに実装するとき）
- `superpowers:test-driven-development`（Phase 38・39。テスト先行）
- `superpowers:verification-before-completion`（各 Phase の完了報告の前）
- `superpowers:systematic-debugging`（CI の揺れ・実機での不具合）
- `peraichi` → `impl-plan`（ほかの大きい修正をするとき。#27 など）
- `superpowers:finishing-a-development-branch`（ブランチの片付けを頼まれたとき）

## 次にやること
Windows の確認の結果を聞き、直す点があれば直す（41-1）。問題なければ要件定義の Phase 41 を `[x]` にして `Fixes #28` のコミット、PR は頼まれたときだけ。ほかの作業を頼まれたら、そちらを優先する。
