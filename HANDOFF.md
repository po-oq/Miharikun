# 引き継ぎ（Issue #28 の実装計画がレビュー待ち）— 2026-10-09

## 現状
- **Issue #28（ドキュメントタブの拡大モード：目次｜プレビュー）を進めている**。作業ブランチ `feature/issue28-documents-expand-mode`（`main` から切った。push 済み、`695dc1b`）。コードはまだ変えていない。CLAUDE.md の「大きい修正の流れ」のうち、技術調査 → ペライチ → 利用者の回答 → 要件定義の更新 → 実装計画まで済み。**次は別セッションでの計画のレビュー**。
  - 技術調査（WebView の試作 Q1〜Q7）：`docs/issue28/issue28-tech-investigation.md`
  - 設計（ペライチ。確定。Q1〜Q9 はすべて推奨で決定）：`docs/issue28/issue28-documents-expand-design.html`
  - 要件定義：12.7.1（拡大モード。新設）、12.4.1（ダッシュボードの Esc を見えているときだけにする行）、15 章の Phase 38〜41（`[ ]`）、16・17 章（`docs/miharikun-requirements.md`）
  - 実装計画（md と図解 HTML の対。状態：レビュー待ち）：`docs/issue28/issue28-documents-expand-plan.md`／`.html`
- リリース `v0.6.0` を出した（2026-10-08。タグ `v0.6.0` は `247ce9a`。release ワークフロー run 37767777027）。release のジョブが GitHub Release に両 OS の zip と `.sha256` の 4 ファイルを載せる流れを、これで初めて確かめた。
- Issue #7（業務で便利になったかの壁打ち）は閉じた。結果は `docs/issue7/issue7-business-value.md`。そこから Issue #27（検索のヒット箇所）・#28（拡大モード）・#29（README に PATH の使い方）を作った。
- テスト（最後に回した時点。Issue #23 のとき）：Tests 1126 合格・スキップ 29、UiTests 52 合格・スキップ 3。その後 main のコードは変わっていない（文書だけ）。今回は回していない（アプリが `dotnet run` で起動中だったため）。

## 未実施・保留
1. **Issue #28 の計画のレビュー**（下の依頼文）→ 指摘を実コードで確かめて採否を決め、md と HTML の両方に反映 → 計画の状態を「確定（レビュー反映済み）」→ Phase 38 から実装（各 Phase の終わりで止まって報告）。
2. Issue #28 の未確認：Windows（WebView2）での試作 Q1〜Q7 にあたる動き（ページからの知らせ・Esc・位置の補正）。計画の Phase 41 で利用者が実機で見る。
3. Issue #27・#29 は未着手。#27 は画面に手が入るので、ペライチから。#29 は文書だけ。
4. CI の注意（release の実行で出た）：Node.js 20 の actions（`checkout@v4`・`setup-dotnet@v4`・`upload-artifact@v4`・`download-artifact@v4`）が非推奨、`ubuntu-latest` が 2026-10-19 から Ubuntu 26 に切り替わる。いまは動いている。
5. CI の揺れ：PR #26 の push 側の実行で macos の UiTests `DocumentsViewTests.Selecting_a_row_shows_the_overview_and_enables_the_buttons`（`vm.Overview` が null）が 1 回だけ落ち、再実行で通った。原因は未調査。また落ちたら調べる（`superpowers:systematic-debugging`）。Issue #28 で同じテストファイルに足すので、待ち方に気をつける。
6. 前からの保留（変わらず）：Phase 28・30-1・30-2 の後のレビュー（利用者が望めば）、html をダークテーマに対応させる案、Phase 31・32 の後回しの確認（iframe を含む html・Windows の Runtime 未導入の案内・mermaid のダーク）、Issue #23 の本物の Claude のログでの表示と `ai-title`。

## 計画のレビューの依頼文（別セッションに貼る）
```
docs/issue28/issue28-documents-expand-plan.md（図解は issue28-documents-expand-plan.html）をレビューしてください。
仕様の正は docs/miharikun-requirements.md の 12.7.1・12.4.1（Esc の行）・15 章 Phase 38〜41、設計の資料は docs/issue28/issue28-documents-expand-design.html と docs/issue28/issue28-tech-investigation.md。
実コード（src/Miharikun.Docs/MarkdownRenderer.cs、src/Miharikun/Views/MarkdownPreview.axaml.cs、src/Miharikun/Views/DocumentsView.axaml(.cs)、src/Miharikun/Views/DashboardView.axaml.cs、src/Miharikun.Presentation/ViewModels/DocumentsViewModel.cs・IPreviewHost.cs・MemoViewModel.cs、src/Miharikun.Presentation/PreviewNavigationPolicy.cs）と突き合わせ、
特に ①プレビュー（NativeWebView）を親から外さずに列を隠す方法（7.8）で WebView が作り直されないか、目次の列の幅の扱いに抜けがないか ②目次の流れ（7.2）で、描画の version・CurrentTarget の比較・再読み込みと別ファイルの区別に、古い目次が出る・目次が消える経路がないか ③Esc の 2 つの受け口（7.4）とダッシュボードの修正で、タブをまたいで効く・効かない・入力欄の Esc を奪う経路がないか ④拡大の状態の表（7.1）・目次の作り直しの表（7.7）の抜け・矛盾 ⑤メモタブと共用の MarkdownRenderer・MarkdownPreview を変えて、メモや通常のプレビューの動きが変わらないか を見てください。
結果は docs/issue28/issue28-documents-expand-review.html に、問題ごとの根拠（file:line）と解決策つきでまとめてください。コードは変えないでください。
```

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
- `superpowers:receiving-code-review`（計画のレビューの指摘を受けて反映するとき）
- `superpowers:test-driven-development`（Phase 38・39。テスト先行）
- `superpowers:verification-before-completion`（各 Phase の完了報告の前）
- `superpowers:systematic-debugging`（CI の揺れ・実機での不具合）
- `peraichi` → `impl-plan`（ほかの大きい修正をするとき。#27 など）
- `superpowers:finishing-a-development-branch`（ブランチの片付けを頼まれたとき）

## 次にやること
Issue #28 の計画のレビューを別セッションに頼む（上の依頼文）。レビューが返ってきたら、指摘ごとに実コードで確かめて採否を決め、計画の md と HTML の両方に反映してから、Phase 38 の実装に入る。ほかの作業を頼まれたら、そちらを優先する。
