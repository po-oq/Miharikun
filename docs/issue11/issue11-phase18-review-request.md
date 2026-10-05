# Phase 18 レビュー依頼（Opus 向け）

Issue #11「Claude Code に対応する」の Phase 18（共通化。**挙動は変えない**）の実装が終わった。Cursor の読み込みを移し替えた、いちばん危ない所なので、次の Phase に進む前にレビューしてほしい。
（レビュー後の追記：結果は `issue11-phase18-review.html`。A1・A2・B1・B2 に対応済みで、テストは 514 件合格 + スキップ 2。以下は、レビュー前の状態の依頼文。）
指摘は、実コードで事実を確かめて採否を決める（鵜呑みにしない）前提で、**根拠（ファイルと行）つき**で返してほしい。

## 1. 対象

- 基点：`d3b7178`（Phase 18 に入る前の `main`。ブランチ `feature/issue11-claude-code` を切った所）
- 範囲：`d3b7178..30c9092`（`82f5a6a` が Phase 18 本体、`30c9092` が規模のテストの Assert の修正）
- 計画の 18-0〜18-5 の 6 コミット分。変更の一覧：

| 計画 | 主なファイル |
|---|---|
| 18-0 特性テストと A/B ダンプ | `tests/Miharikun.Tests/Core/ProjectEventStoreCharacterizationTests.cs`（新規）、`GoldenSummaryDump.cs`（新規） |
| 18-1 型の追加と分割 | `Agents/IAgent.cs`（`IAgentInfo` / `IHookAgent`）、`AgentCatalog.cs`・`CommonTools.cs`（新規）、`AgentRegistry.cs`、`Sessions/ISessionSource.cs`（新規） |
| 18-2 汎用 Store と Cursor の Source | `Sessions/CursorSessionSource.cs`（新規）、`Sessions/ProjectEventStore.cs`（書き直し）、`tests/.../GenericProjectEventStoreTests.cs`（新規） |
| 18-3 SessionMonitor | `Sessions/SessionMonitor.cs`、`tests/.../SessionMonitorWatchTests.cs`（新規） |
| 18-4 Analyzer から Cursor を追い出す | `Agents/CursorAgent.Normalize.cs`、`Sessions/SessionAnalyzer.cs`、`AgentEvent.cs`（コメント） |
| 18-5 メタ・App・例外ログ | `Meta/SessionMetaService.cs`、`src/Miharikun/App.xaml.cs`、`HookSetup.cs`・`Install/HookInstaller.cs`（コメントのみ）、`tests/.../SessionMetaServiceAgentTests.cs`（新規） |

`git diff --stat`（既存ファイル）：コード 12 ファイル（+約 190 / −約 220）、既存テスト 5 ファイル（構築行ほかのみ）。新規ファイル 9 本（コード 4・テスト 5）。

## 2. 前提（読むもの）

- 仕様：`docs/miharikun-requirements.md` の 5.1・9・Phase 18
- 移し方の決めごと：`docs/issue11/issue11-claude-code-plan.md` の 8.1（差分の規則）・2 章（Phase 18）
- 品質ゲート：同 1 章（6 点）

## 3. 見てほしい所（この順で）

1. **差分の規則（8.1 の表の 7 行）**：それぞれが `CursorSessionSource.ReadNew` / `ReadFile` / `ImportTranscripts` のどこで、どの差分になっているか。特に
   - 「取り込み済み → Hook」が `Replace` になっているか（`ReadFile` の `replace` 変数）
   - 「作り直し＋新しい行」が 1 回の `Replace` にまとまっているか
   - 作り直しで一致しなくなったとき、前にイベントがあったときだけ `Remove` が出ているか
2. **Store の規則**（`ProjectEventStore.Apply`・`Refresh`）：空の `Replace` ＝ `Remove`、空の `Append` は無視、存在しない `Remove` は変更ではない、同じキーの複数の差分、要約キャッシュの破棄、戻り値の重複除去。
3. **テストを変えていないか**：既存 5 ファイルの `git diff` が、構築の行だけか（`CursorNormalizeTests.cs` は新規テストの追加のみ）。`Assert` の行・期待値に変更が無いか。件数が減っていないか（482 + スキップ 1 → 511 + スキップ 2）。
4. **例外**：Store の Source ごとの `try/catch (Exception)` で、他の Source が止まるか。`App.LogUnhandledExceptions` が例外を握りつぶしていないか（3 つとも `Handled` / `SetObserved` を触っていない）。
5. **監視**（`SessionMonitor.EnsureWatchers`）：`CreateIfMissing` が Cursor の events だけ true か。取り直しで Watcher が増え続けない・消えたフォルダの Watcher が残らないか。ロックの中で重い処理をしていないか。`Dispose` と `RefreshNow` の競合。
6. **型の分割**：Hook exe が `IHookAgent` だけを使い、リフレクションが増えていないか（AOT）。
7. **`"default"` の移動**：`SessionAnalyzer` から消え、`CursorAgent.Normalize` の `ModelName` で null にしているか。`model_id` が `"default"` のとき `model` に落ちないのは、旧挙動と同じか。

## 4. 確認済みの結果（品質ゲート 6 点）

| # | 結果 |
|---|---|
| 1 | `dotnet test`：**511 件合格・スキップ 2・失敗 0**（着手前は 482 + スキップ 1。スキップは `PerfFact` と A/B ダンプ） |
| 2 | 既存テストの変更：`ProjectEventStoreTests`・`ProjectEventStoreImportTests`・`SessionMonitorTests`・`MetaTests` は構築の行のみ。`CursorNormalizeTests` は新規テスト 1 件の追加のみ。差分に出る `Assert` の行は、その新規テストの 3 行だけで、既存の `Assert` の行の差分は 0 |
| 3 | `dotnet build src/Miharikun.Core --no-incremental`：**警告 0・エラー 0** |
| 4 | `scripts/publish.ps1`：成功（Hook の NativeAOT 含む。警告 0）。zip は `dist/` に作成 |
| 5 | A/B ダンプ：本物の Cursor のデータのコピー（5 セッション）で、`before`（旧コード）と、18-2・18-4・18-5 の各時点の `summaries-*.json`（要約・検索文字列・イベント数）が**完全一致**（ハッシュ同一）。同条件で 2 回出して一致することも確認（揺れない） |
| 6 | 隔離環境（`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR` をコピーに向けた Debug 版）で、UI Automation により確認：一覧 5 件（導入前の「閉じた（導入前）」を含む）、状態の件数、ステータスタブ、メモ欄・📌（カードの表示）、タイムライン（カードを切り替えて内容が出る）。`app.log` に例外・警告なし |

**できなかった・見ていない確認**
- 目視のスクリーンショットは撮っていない（UI Automation のツリーとテキストで確認）。**ライト/ダークの見た目は未確認**（この Phase で画面は変えていない）。
- AOT 版の Hook exe を、実機の Cursor で動かすことはしていない（`publish.ps1` が通ることまで）。
- A/B ダンプの対象は要約・検索文字列・イベント数。**タイムラインの各行の内容そのもの**はダンプに入れていない（イベント数と要約経由でのみ間接的に確認）。
- 本物のデータのコピーは 5 セッションだけ。「作り直し」「大文字小文字違い」などの稀な経路は、特性テスト（`ProjectEventStoreCharacterizationTests`）で見ている。

## 5. 気になっている所（正直に）

1. **取り込み済み → Hook の切り替えの順序**：計画 8.1 は `Replace` だけだが、実装では「取り込み分の `Remove` → Hook の `Replace`」の順で出している。ID が**大文字小文字だけ違う**とき（特性テスト④。`SessionKey` は大文字小文字を区別、Source 内の辞書は区別しない）、取り込み分が Store に残らないようにするため。この順序は妥当か。
2. **`ReadNew` が途中で `IOException` 以外を投げたとき**：その回に集めた差分は Store に届かないが、ファイルの読み取り位置はすでに進んでいる（取りこぼし）。旧コードはプロセスが落ちていた所なので悪化ではないが、挙動は違う。Source 側でも `Exception` を捕まえて、そこまでの差分は返すべきか。
3. **旧との細かい差**：旧は「行は読めたが `Normalize` が 0 イベントを返した」ときも「変更あり」を返していた（Monitor が「消えた」通知を出す）。新は空の `Append` を無視するので通知しない。画面に影響は無いはずだが、仕様（8.1「空の Append は無視」）どおり。
4. **`SessionMonitor.EnsureWatchers` はロックの中**で、3 秒ごとに `Directory.Exists`（と、Cursor の events だけ `CreateDirectory`・`FileSystemWatcher` の作成）を行う。ネットワークドライブなどで遅い場合にロックを持つ時間が伸びる。「Watcher が張られたこと」自体はテストから直接見えず、フォルダが作られることと通知が届くことで間接的に確かめている。
5. **`ProjectEventStore.GetEvents` は内部のリストをそのまま返す**（旧と同じ）。`SessionMonitor.GetEvents` がコピーを返す前提で、直接の呼び出し元は Monitor だけ。
6. **`ProjectEventStore.ProjectFolder` プロパティを消した**（使っていたのは Store 内部だけ。`MainViewModel.ProjectFolder` は別物）。
7. **既存のバグ（触っていない）**：`ProjectEventStoreTests.Handles_the_scale_in_the_requirements` の末尾の `Assert` が、`// 参考値` というコメントと同じ行にあり、実行されていない。計画の「既存テストは構築部分のみ変更」の決まりに従って直していない。直してよいか。
8. **`CommonTools` は `Shell` だけ**。計画に「ほか」とあったが、使う予定のないものは足していない（Phase 19 で必要になったら足す）。
9. **`AgentCatalog` / `AgentRegistry` は固定の switch**（B9。リフレクションなし）。`AgentCatalog` は今は Cursor だけ。Claude は Phase 19-1 で登録する。
10. **未処理の例外のログ**：`DispatcherUnhandledException` は `Handled` を触らないので、これまでどおりアプリは落ちる（ログだけ増える）。それで合っているか。
