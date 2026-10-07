# 引き継ぎ（Issue #22：macOS 対応）— 2026-10-07（Phase 30 の終わり。Windows での確認待ち）

## 現状
- 作業ブランチ：`feature/issue22-macos-support`（push 済み。最新コミット `efd15b2`）。Phase 27・28・29 は完了（`[x]`）。**Phase 30（画面を WPF → Avalonia 12 に置き換え）は 30-1〜30-6 を mac で実装済みだが、`[ ]` のまま**（Windows での確認が残っているため）。
- 各 Phase の中身・結果・計画との違いは、要件定義 15 章の各 Phase の「状況」に書いてある（`docs/miharikun-requirements.md`）。ここには書き写さない。計画は `docs/macos-support/macos-support-plan.md`（md と html は対）。記録：`phase27-mac-check.md`・`phase28-wpf-record.md`・`phase30-timeline-perf.md`・`theme-compare/index.html`。
- **未コミットの変更（コミットは頼まれたときだけ。頼まれたら `git add` はパス指定）**：`src/Miharikun/Views/TimelinePanel.cs`（見積もりの自動補正）・`src/Miharikun/Views/RightPaneView.axaml.cs`（`EstimateRowHeight` を public に）・`tests/Miharikun.UiTests/TimelineScrollTests.cs`。直前の CI で Windows の UiTests が 2 件落ちたのを直すもの：①全体の高さの見積もりが Windows のフォントで実際の 1.28 倍だった→測った高さから見積もりを補正する ②「1 ノッチの位置の動き」は先頭の行を保つ補正で内部の位置が動くので尺度として不適切→行の画面上の動きで測る。mac では全部合格（UiTests 41 合格・スキップ 3、Tests 1085 合格・スキップ 29）。**コミット・push の後、CI（Windows・macOS）が緑になるか確認する。**
- CI の状況：`efd15b2` は、Windows の `Miharikun.Tests` が通り、`Miharikun.UiTests` の上の 2 件だけ落ちている（未コミットの変更が直す）。ほかの揺れは、`SessionMonitorWatchTests`（直した）と `MainViewModelTests`・git の重なり防止（直した）。

## 次にやること
1. **利用者が Windows で確認中**（`dotnet run --project src/Miharikun`。隔離環境の変数で）。見る点：見た目（ライト・ダーク・日本語の字形）、「OS に合わせる」の追従、⚙ →「Hook を導入 / 再導入」のあとの試し起動（`--probe`。Phase 29 から持ち越し）、日本語入力（検索・名前の変更・概要・メモ）、設定ダイアログの「参照…」、長いタイムラインのスクロール。結果を聞いて、問題があれば直す。
2. 問題が無ければ、Phase 30 を `[x]` にして状況を更新し、**止まって報告**（計画の決まり。勝手に 31 へ進まない）。
3. その次は 31-1（`MarkdownPreview`。Avalonia.Controls.WebView）。WKWebView の試作の結果は要件 12.7 にある：iframe にも `NavigationStarted` が来る、`Refresh()` ではスクロール位置が保たれない（31-1 で自前で戻す）、`NativeWebView.Background` は未設定（31-4 で白く光らないか確認）。

## 守ること・作業の決まり
- `CLAUDE.md` の「作業の決まり」「守ること」（返答は日本語・コミット/push は頼まれたときだけ・`git add` はパス指定・完了報告にビルドの有無と出力先とテスト件数・本物の環境を書き換えない）。
- 画面の確認：mac は開発用の `.app`（`scripts/publish-mac.sh` → `dist/mac/Miharikun.app`。`/dist/` は git 無視）を利用者が Finder で見る。**私は画面を撮れない**（macOS の画面収録・アクセシビリティの許可が要るため、試すと許可の確認で止まる）。代わりに `tests/Miharikun.UiTests`（Avalonia.Headless + Skia）で撮る：`MIHARIKUN_SHOTS=<フォルダ> dotnet test tests/Miharikun.UiTests --filter "DisplayName~Capture"` → `light.png`・`dark.png`・`settings-*.png`。5,000 行の性能は `MIHARIKUN_PERF=1 MIHARIKUN_PERF_OUT=<フォルダ>`。
- 旧 WPF 版は、コミット `1ca62c7` に全部ある（見比べが必要なら Windows でそのコミットをビルドする）。WPF の XAML を見る：`git show 1ca62c7:src/Miharikun/MainWindow.xaml`。

## 決めたこと・分かったこと（コードや文書に書いていない理由の部分）
- テーマ：FluentTheme（見た目は SukiUI に寄せる）。SukiUI は外した（理由は要件 12.5 と `theme-compare`）。
- Avalonia 12 には `ItemsRepeater` が無く、`ListBox` の仮想化は行の高さがまちまちな長い一覧で位置が崩れる（全体の高さの見積もり違い）。そこで `Views/TimelinePanel` を自作した（行ごとの高さを持つ・測り直しても先頭の行を動かさない・測った高さから見積もりを補正）。テストは `TimelineScrollTests`。
- 行へのマウスのホバーで高さが変わる作り（`IsVisible` の出し入れ）は、長い欄でスクロールの連鎖を起こす。透明度で切り替える。
- `FocusWhenVisible` は入力欄自身の `IsVisible` を見る（親だけを隠す作りだと働かない）。エージェントのチップは `SelectedValue` の双方向バインドだと空文字の Key で null が書き戻されるので、コードビハインドで写している。
- Avalonia の `Tapped` は 2 回目にも来る→行のクリックは `PointerPressed` の `ClickCount`。
- テストの揺れの原因は 3 つとも「CI でスレッドプールや最初の読み込みが遅れる」。待ち方を固定の時間でなく「終わったこと」で待つ。
- 未確認：日本語入力（IME）、「OS に合わせる」の OS の外観変更への追従、`LSMinimumSystemVersion=14.0` と実行ファイルの `minos 12.0` の食い違い（要確認）。

## mac での作業の注意
- `dotnet build Miharikun.slnx` が通る（WPF は消えた）。`dotnet test tests/Miharikun.Tests`（xunit v2）と `tests/Miharikun.UiTests`（xunit v3。Avalonia.Headless.XUnit が v3 のため別プロジェクト）。
- macOS の `sed -i` は `sed -i ''`。`timeout` コマンドは無い（`perl -e 'alarm N; exec @ARGV'`）。ループで長く回すと 10 分で打ち切られるので、短く区切る。
- Windows の Hook（NativeAOT・`win-x64`）は mac では作れない（CI か Windows）。mac の Hook は `dotnet publish src/Miharikun.Hook -c Release -r osx-arm64 -o <一時>`。
- 本物の `~/.cursor/hooks.json` は Phase 27 で利用者の了承のうえ新規に作ってあり、残す決定（`~/Library/Application Support/Miharikun/bin/Miharikun.Hook` は古い版で `--probe` が無い）。触る前に説明して了承を取る。

## 推奨スキル
- `superpowers:using-superpowers`（開始時）
- `superpowers:verification-before-completion`（完了報告の前）
- `superpowers:systematic-debugging`（CI の揺れ・画面の不具合を調べるとき。原因を測ってから直す）
- `impl-plan`（計画を直すとき。md と html は対）

## 未決
- Phase 28 の差分と品質ゲートのレビュー（計画 10.2）、30-1 の後・30-2 の後のレビュー（利用者が望めば別セッションで）。
