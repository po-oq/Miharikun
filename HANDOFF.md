# 引き継ぎ（Issue #28 は完了・v0.7.0 をリリース済み。次の作業待ち）— 2026-10-09

## 現状
- **Issue #28（ドキュメントタブの拡大モード：目次｜プレビュー）は完了**。Phase 38〜41 を実装し、mac・Windows の実機は利用者が確認 OK。PR #30 を main にマージ済み（マージコミット `7cb79e7`）、Issue #28 は閉じた。作業ブランチ `feature/issue28-documents-expand-mode` は、ローカルにもリモートにも**まだ残っている**（削除は頼まれたとき）。
  - 文書：技術調査 `docs/issue28/issue28-tech-investigation.md`／設計 `docs/issue28/issue28-documents-expand-design.html`／要件定義 12.7.1・12.4.1・15 章 Phase 38〜41／実装計画 `docs/issue28/issue28-documents-expand-plan.md`・`.html`／出す前のチェックは `docs/release.md` の「ドキュメントの拡大モード」
  - 実装：Docs の `MarkdownRenderer.RenderWithOutline`・`MarkdownOutline`、Presentation の `DocumentsViewModel.Expand.cs`・`OutlineItemViewModel`・`PreviewScripts`・`IPreviewHost` の新しい口、画面の `MarkdownPreview`・`DocumentsView`・`DepthIndentConverter`・`DashboardView` の Esc。
  - テスト：Tests 1197 合格・スキップ 29、UiTests 65 合格・スキップ 3（PR の CI 4 件も合格）。
- **リリース `v0.7.0` を出した**（2026-10-09。タグ `v0.7.0` は `7cb79e7`。release ワークフロー run 37851854161。両 OS の zip と `.sha256` の 4 ファイルが Release に載った）。出した zip を展開して起動する確認は、していない（実機の確認はビルドしたアプリで行った）。
- 一つ前のリリースは `v0.6.0`（2026-10-08）。
- Issue #7（業務で便利になったかの壁打ち）は閉じた。結果は `docs/issue7/issue7-business-value.md`。そこから Issue #27（検索のヒット箇所）・#28（拡大モード）・#29（README に PATH の使い方）を作った。

## 未実施・保留
1. Issue #27（検索のヒット箇所）・#29（README に PATH の使い方）は未着手。#27 は画面に手が入るので、ペライチから。#29 は文書だけ。
2. Issue #28 の保留（17 章）：今見ている見出しの強調（Q9）。
3. CI の注意（release の実行で出た）：Node.js 20 の actions（`checkout@v4`・`setup-dotnet@v4`・`upload-artifact@v4`・`download-artifact@v4`）が非推奨、`ubuntu-latest` が 2026-10-19 から Ubuntu 26 に切り替わる。いまは動いている。
4. CI の揺れ：PR #26 の push 側の実行で macos の UiTests `DocumentsViewTests.Selecting_a_row_shows_the_overview_and_enables_the_buttons`（`vm.Overview` が null）が 1 回だけ落ち、再実行で通った。原因は未調査。PR #30 の CI では出なかった。また落ちたら調べる（`superpowers:systematic-debugging`）。
5. 前からの保留（変わらず）：Phase 28・30-1・30-2 の後のレビュー（利用者が望めば）、html をダークテーマに対応させる案、Phase 31・32 の後回しの確認（iframe を含む html・Windows の Runtime 未導入の案内・mermaid のダーク）、Issue #23 の本物の Claude のログでの表示と `ai-title`。

## 守ること・作業の決まり
- `CLAUDE.md` の「作業の決まり」「守ること」に従う。返答は日本語、コミット・push・PR・マージ・リリース・ブランチ削除は頼まれたときだけ、`git add` はパス指定（`-A` は使わない）、完了報告に「ビルドしたかと出力先・テスト件数・できなかった確認」を書く、本物の `~/.cursor/hooks.json` やデータは勝手に触らない、`~/.claude/` には書かない（読み取りだけ）。
- `dotnet build`／`test` の前にアプリを止める。利用者が `dotnet run --project src/Miharikun` で起動していることがある（`pgrep -fl Miharikun` で確かめる）。止められないときは `-p:OutDir=` で別の場所に作る。
- 私は画面を撮れない。mac の見た目は `tests/Miharikun.UiTests`（Avalonia.Headless）か、利用者が手元で見る。隔離環境で見せるときは、`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダにし、手書きの md・ダミー会話ログを置いて、`src/Miharikun/bin/Debug/net10.0/Miharikun <プロジェクトのフォルダ>` を起動してもらう。

## 今回の途中で分かったこと（文書に書いていない部分）
- 目次の項目は `Tapped` だけだと、同じ項目を素早く 2 回押したときの 2 回目がダブルクリック扱いで届かない。`Tapped` と `DoubleTapped` の両方を同じ処理にしてある（`DocumentsView`）。
- `RelayCommand` の `Execute` は `CanExecute` を見ない。`ToggleExpanded` は本体でも選択を確かめている。
- VM のテストで、走査の後の結果（選択が解かれる等）は `IsScanning` が false になった直後には揃っていない。結果そのものを `SpinWait.SpinUntil` で待つ。背景スレッドから来る通知を記録するなら `ConcurrentQueue` を使う。
- 計画の「`MarkdownRendererTests` は 48 件」は数え違いで、実際は 30 件（スキップ 3）。件数は減らしていない。
- 実機確認用の隔離環境（スクラッチパッドの `try28`）はセッションと一緒に消える。作り直すなら、対象フォルダに `## [x] Phase N` と `- [ ]` 付きの長い md・見出しなしの md・html を手書きで置く。
- WebView の試作は、スクラッチパッドに置いた捨てる小さな Avalonia アプリで行った（残っていない）。やり方と結果は技術調査の文書にある。
- Avalonia.Controls.WebView 12.1.0 の API の説明（xml）は、パッケージの `lib/net10.0-android36.0/` にしか入っていない。`invokeCSharpAction` は Windows の WebView2 でもそのまま使えた（`WebMessageReceived` の `Body` は `key:Escape` の形で届いた）。
- Markdig の振る舞いを手早く試すには、入っている `dotnet-script`（2.0.0）で `#r "nuget: Markdig, 1.4.0"` と書いた `.csx` を動かす。
- ペライチ・計画の HTML の図は、アプリ内のブラウザで `file://` を開けば、JS で はみ出し・重なりを数えられる（ファイルを直したら開き直す）。
- 前回からの分（変わらず）：`LSMinimumSystemVersion=14.0` と `minos 12.0` の食い違いは意図どおり／release の手動実行の artifact は二重の zip／署名はアドホックのみ／ヘッドレスの UiTests では本物の WebView を作らない（`MarkdownPreview.WebViewDisabled`）／PR を出すと CI は push 側と pull_request 側の 2 回走る。

## 推奨スキル（次のエージェントが Skill で呼ぶ）
- `superpowers:using-superpowers`（開始時）
- `peraichi` → `impl-plan`（大きい修正をするとき。#27 など）
- `superpowers:executing-plans`（計画に従って Phase ごとに実装するとき）
- `superpowers:test-driven-development`（Core・ViewModel の実装）
- `superpowers:verification-before-completion`（完了報告の前）
- `superpowers:systematic-debugging`（CI の揺れ・実機での不具合）
- `superpowers:finishing-a-development-branch`（ブランチの片付けを頼まれたとき）

## 次にやること
頼まれた作業を優先する。決まっていなければ、#27（検索のヒット箇所。ペライチから）か #29（README。文書だけ）を提案する。マージ済みの `feature/issue28-documents-expand-mode` の削除は、頼まれたときだけ。
