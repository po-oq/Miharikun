# Phase 30-5：5,000 行のタイムラインの性能（Issue #22）

計画 30-5 の確認（「28-2 の前の記録と同じくらい」）。比べるのは `docs/macos-support/phase28-wpf-record.md` の WPF 版（Windows 11）。

- 計測：`tests/Miharikun.UiTests/TimelinePerfTests.cs`（Avalonia.Headless。実際に Skia で描くところまで含める。ダミーのデータ：5,004 イベント・834 ターン・1 行 1〜12 行の返事）。
- 実行：`MIHARIKUN_PERF=1 MIHARIKUN_PERF_OUT=<フォルダ> dotnet test tests/Miharikun.UiTests --filter "DisplayName~Five_thousand"`（通常の `dotnet test` では飛ばす）。結果は `<フォルダ>/timeline-perf.txt`。
- 環境：macOS 26.6.2（Apple Silicon）、.NET 10.0.5、ヘッドレス（実ウィンドウではない）。**Windows の値は、同じコマンドを Windows で回して追記する**（未計測）。

| 操作 | WPF 版（Windows。28-2 の前の記録） | Avalonia 版（mac・ヘッドレス） |
|---|---|---|
| 5,000 行のセッションを選ぶ（タイムラインが出るまで） | 約 2.5 秒 | 約 0.65 秒 |
| 種別の ON/OFF（ツール・思考） | 20〜35 ms | 約 10〜15 ms |
| 検索 1 文字・2 文字・空に | 約 1.0 秒 | 約 10 ms |
| 別のセッションへ | 約 0.23 秒 | 約 0.14 秒 |
| 5,000 行のセッションへ戻る | 約 1.25 秒 | 約 0.65 秒 |
| 全部コピー（文字列を作る） | 約 1.1〜1.2 秒（クリップボードへ入れるまで） | 約 1 ms（文字列を作るだけ。クリップボードは含めない） |

- 作られている行は、画面に見える分だけ（この計測では 2 行 / 1,668 行）。仮想化が効いている。
- 「全部コピー」は、WPF 版はクリップボードへ入れるまでを含んでいた。ここは文字列を作る時間だけなので、そのまま比べられない（クリップボードは実機で確かめる）。
- 検索が速いのは、ヘッドレスでは入力のたびの再描画の待ちが無いため。実機のもたつきは、Windows・mac の目視で確かめる。
