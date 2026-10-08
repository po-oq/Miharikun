# 引き継ぎ（Issue #22・#23 は main にマージ済み）— 2026-10-08

## 現状
- Issue #22（macOS 対応。Phase 27〜33）と Issue #23（Claude Code のセッションタイトル。Phase 37）は、どちらも完了して main にマージ済み。PR は [#25](https://github.com/po-oq/Miharikun/pull/25)・[#26](https://github.com/po-oq/Miharikun/pull/26)。作業ブランチは両方とも削除済み（ローカル・リモート）。
- 各 Phase の中身・結果・計画との違いは、要件定義 15 章の各 Phase の「状況」（`docs/miharikun-requirements.md`）。計画は `docs/macos-support/macos-support-plan.md`、`docs/issue23/issue23-claude-title-plan.md`。リリース手順は `docs/release.md`。ここには書き写さない。
- Issue #23：`custom-title` を `AgentEventKind.TitleChanged` にし、`SessionAnalyzer` が最後の値を `AutoTitle` にする（表示は「手動 → Claude のタイトル → 最初の依頼の先頭 40 文字」）。Cursor は変わらない。実ログ（2.1.197〜2.1.293）の `custom-title` の行は timestamp を持たず、同じ値が繰り返し入る。
- テスト（最後に回した時点）：Tests 1126 合格・スキップ 29、UiTests 52 合格・スキップ 3。
- 両 OS の通し確認（Phase 33）は、手動実行の release ワークフロー（run 37758114529・版 `0.0.0-dev`）の zip で、利用者が mac・Windows の実機で OK とした。

## 未実施・保留
1. **タグを push する本番のリリース**（`docs/release.md`）。release のジョブが GitHub Release に両 OS の zip と `.sha256` を載せる流れは、まだ一度も確かめていない（手動実行では release のジョブがスキップされるため）。頼まれたときだけ行う。
2. Phase 28 の差分と品質ゲートのレビュー（計画 10.2）、30-1 の後・30-2 の後のレビュー。Issue #23 の計画はレビューを挟まず実装した。利用者が望めば別セッションで。
3. html をダークテーマに対応させる案（`prefers-color-scheme` など）。html は配色をファイル側が持つため、WebView の地はダークでも白にしてある。別の課題。
4. 後回しのままの確認（Phase 31・32 の「状況」）のうち、Phase 33 の通し確認に含まれなかったかもしれないもの：iframe を含む html で既定のブラウザが勝手に開かない（mac。手元に該当の html が無い）、Windows の Runtime 未導入の案内、mermaid を含む md のダークでの見え方。
5. Issue #23 の未確認：本物の Claude のログでの表示（隔離環境の手書きダミーでだけ目視 OK）。`ai-title` は実物が無く未対応。Cursor のタイトル（チャット名）は `state.vscdb` を読まない方針のため取れない。
6. CI の揺れ：PR #26 の push 側の実行で macos の UiTests `DocumentsViewTests.Selecting_a_row_shows_the_overview_and_enables_the_buttons`（`vm.Overview` が null）が 1 回だけ落ち、失敗したジョブの再実行（`gh run rerun <run-id> --failed`）で通った。待ち方が不安定な可能性。原因は未調査。また落ちたら調べる（`superpowers:systematic-debugging`）。

## 守ること・作業の決まり
- `CLAUDE.md` の「作業の決まり」「守ること」に従う。返答は日本語、コミット・push・PR・マージ・リリース・ブランチ削除は頼まれたときだけ、`git add` はパス指定（`-A` は使わない）、作業の前に main から作業ブランチを切る、完了報告に「ビルドしたかと出力先・テスト件数・できなかった確認」を書く、本物の `~/.cursor/hooks.json` やデータは勝手に触らない、`~/.claude/` には書かない（読み取りだけ。会話ログの確認は構造だけで、本文は引用しない）。
- 私は画面を撮れない。mac の見た目は `tests/Miharikun.UiTests`（Avalonia.Headless）のスクリーンショットか、利用者が手元で見る。隔離環境で見せるときは、`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダにして、手書きのダミー会話ログを置き、`src/Miharikun/bin/Debug/net10.0/Miharikun <プロジェクトのフォルダ>` を起動してもらう。

## 今回の途中で分かったこと（文書に書いていない部分）
- `LSMinimumSystemVersion=14.0`（`scripts/dist/Info.plist`）と実行ファイルの `minos 12.0`（`libAvaloniaNative` は 11.0）の食い違いは、Info.plist のほうが厳しい値（.NET 10 の対応 OS に合わせて 14.0 未満を弾く）で、意図どおり。直さない。
- release ワークフローの手動実行の artifact は二重の zip（外側を展開すると、中に本物の zip と `.sha256`）。ダウンロードは `gh run download <run-id> --repo po-oq/Miharikun -n <artifact名>`。
- 署名はアドホックのみ。Apple の署名・公証はしない。`xattr` が要るのは、ダウンロードした zip のとき。
- ヘッドレスの UiTests では本物の WebView を作らない（`MarkdownPreview.WebViewDisabled`）。プレビューの中身は実機で見る。
- PR を出すと CI は push 側と pull_request 側の 2 回走る。

## 推奨スキル（次のエージェントが Skill で呼ぶ）
- `superpowers:using-superpowers`（開始時）
- `superpowers:verification-before-completion`（完了報告・リリース前）
- `superpowers:systematic-debugging`（CI の揺れ・リリースの不具合を調べるとき）
- `peraichi` → `impl-plan`（次に大きい修正をするとき。CLAUDE.md の流れ）
- `superpowers:finishing-a-development-branch`（ブランチの片付けを頼まれたとき）

## 次にやること
利用者に、次の作業を聞く（本番のリリース、CI の揺れの調査、別の課題のどれか）。
