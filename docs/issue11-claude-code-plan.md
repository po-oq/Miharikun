# Issue #11 Claude Code 対応 実装計画

> **連動ルール**：この md と `issue11-claude-code-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `miharikun-requirements.md`（5.1・6・9.1・10・10.1・11.1・12.8・12.9・13・14.2・Phase 18〜22）。設計イメージは `issue11-claude-code-design.html`。レビューと根拠は `issue11-claude-code-review.html`（指摘番号 A1〜A7・B1〜B11・C1〜C4）。この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（8 章）**。
> 進め方は他の Phase と同じ：要件定義 → Core（テスト先行）→ ViewModel → 画面。各 Phase の終わりに隔離環境（`MIHARIKUN_DATA_DIR` ほか）で実機確認し、要件定義の `[x]` と「状況」を更新して、ビルドの有無と出力先を報告する。コミットは頼まれたときだけ（ここは区切りの目安）。

## 1. 全体像

- **5 Phase・20 コミット**。Phase 18 は**挙動を変えない共通化**で、既存のテストが全部通ることが完了条件。ここを先に単独で終わらせて、Claude の追加を安全に載せる。
- 新しい依存パッケージは無い（JSON は既存の `JsonNode`、追記読みは既存の `JsonlTail`）。Core は AOT 互換のまま（リフレクション禁止。JSON の値は `TryGetValue` で取り出す）。
- Hook exe は `IHookAgent` に型が変わるだけ。Phase 18 の終わりに、Hook の NativeAOT 発行が通ることを確認する。

```
Phase 18 共通化（挙動不変） ─▶ 19 Claude の正規化 ─▶ 20 Claude の読み込み ─▶ 21 画面 ─▶ 22 実機確認
   6 コミット                    5 コミット             3 コミット             4 コミット   2 コミット
```

### 品質ゲート（Phase 18 の完了条件）
1. `dotnet test` が全部通る（いまは 482 件 + スキップ 1。18-0 で足す特性テストを含む）。**テスト件数は減らさない**
2. 既存のテストファイルの変更は、**構築部分だけ**（`new ProjectEventStore(…)` / `new SessionMonitor(…)` / `new MetaStore(…)` / `new SessionMetaService(…)` の行）。`git diff` で `Assert` の行に差分が無いことを確かめる（C3）
3. `dotnet build src/Miharikun.Core --no-incremental` が警告 0（AOT 互換）
4. `scripts/publish.ps1`（Hook の NativeAOT を含む）が通る
5. **A/B 比較**（C2）：本物の Cursor のデータのコピーで、18-0 の時点と 18-5 の時点の全セッションの要約をダンプし、差分が 0 件
6. 隔離環境で、**データ入り**（5 と同じコピー）で、Cursor のセッションが従来どおり表示される（一覧・詳細・タイムライン・メモ・ステータス・導入前セッションの取り込み）（C4）

> 5・6 の「本物のデータのコピー」は、`%LOCALAPPDATA%\Miharikun\events\cursor`・`meta\cursor`・`settings.json` と、`%USERPROFILE%\.cursor\projects\` の transcript を、一時フォルダに**コピーするだけ**（本物は読むだけ・書き換えない）。**コピーする前に利用者に了承を取る**。コピーはリポジトリに入れず、確認後に消す。

## 2. Phase 18：共通化（挙動は変えない）

Cursor 専用になっている所を、共通の形に直す。**新しい機能は足さない**（例外のログを足すのは除く）。

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 18-0 | 特性テストと A/B ダンプ | **旧コードのまま**、今の挙動を固定するテストを足す（C1）：①作り直されたファイルが、作り直し後は別プロジェクト／空になる → 一覧から消える ②1 回の Refresh で「取り込み済み → Hook に切り替え」と Hook の追記が同時に来る ③読み込み中の `IOException`（ファイルを排他で開いておく）→ ログに出し、次回読み直す ④取り込み済みの ID と Hook ファイル名の大文字小文字が違う。**A/B ダンプのテスト**（C2）：`MIHARIKUN_GOLDEN_DIR` があるときだけ動く（`PerfFact` と同じ流儀の属性）。`<dir>\data` を `AppPaths` のルート、`<dir>\cursor` を Cursor の場所として読み、全セッションの `SessionSummary`（キー順・時刻は ISO 形式）と検索文字列を `<dir>\summaries-<MIHARIKUN_GOLDEN_LABEL>.json` に書く | 全テスト。利用者の了承を得てコピーを作り、`before` を出しておく |
| 18-1 | 型の追加と分割 | `IAgent` → `IAgentInfo`（Id・表示名・Capabilities）＋ `IHookAgent : IAgentInfo`（Hook 側 4 メソッド）。`CursorAgent` は `IHookAgent`（`Normalize`・`GetWorkspaceRoots` は具体クラスのメソッドとして残す）、`AgentRegistry.Find` は `IHookAgent?` を返す（Hook exe 用）。**`AgentCatalog.Find(agentId) → IAgentInfo?`**（App 用。表示名・Capabilities。固定の switch。B9）。`CommonTools`（`Shell` ほか）、`ISessionSource` / `SessionDelta` / `SessionDeltaKind` / `WatchTarget` を追加（まだ使わない） | 全テスト・Hook のビルド |
| 18-2 | 汎用 `ProjectEventStore` と `CursorSessionSource` | 旧 `ProjectEventStore` の読み込み（Hook 生イベントの tail・`Normalize`・`workspace_roots` 照合・「照合前に捨てた行」の再読み・ファイルの作り直し）と、`CursorTranscriptImporter` の取り込み（「Hook があれば transcript より優先」）を `CursorSessionSource` に移す。**差分は 8.1 の規則表どおりに出す**（A2）。`ProjectEventStore` は差分を適用して、保持・要約のキャッシュ・変更通知だけを行う。**Source ごとに `ReadNew` の例外（`Exception` 全部）を捕まえ、スタックつきでログに書いて、他の Source は続ける**（A6） | ProjectEventStore 系のテスト（構築部分のみ修正）が全部通る。新規（ダミーの Source）：追記・作り直し・消えた・空の Replace＝Remove・空の Append は無視・同じキーに複数の差分・複数 Source・要約のキャッシュの無効化・片方の Source が投げても他方の差分は届く |
| 18-3 | `SessionMonitor` | events フォルダ 1 つの監視をやめ、Store から各 Source の `WatchTargets` を集めて監視する。**ポーリングのたびに取り直し、増えた分だけ Watcher を張る**（無いフォルダは張らずに次回また試す）。`CreateIfMissing` が true のものだけフォルダを作る（Cursor の events は作る＝今と同じ。Claude は作らない）。300ms デバウンス＋ 3 秒ポーリングはそのまま（A7） | SessionMonitor のテスト（構築部分のみ修正）。新規：あとからできたフォルダも監視される・`CreateIfMissing=false` のフォルダは作らない |
| 18-4 | `SessionAnalyzer` から Cursor を追い出す | テスト判定を `CommonTools.Shell` で行う。モデル名 `"default"` の無視を `CursorAgent.Normalize` の中へ移す（Model を null にする）。`Imported` の意味を、コメントで「時刻が推定のもの」と明記 | SessionAnalyzer・CursorNormalize のテスト。新規：Normalize が `"default"` を null にする |
| 18-5 | メタの切り替えと App の組み立て | `SessionMetaService` が、セッションのキー（`AgentId`）で `MetaStore` を切り替える（`Func<string, MetaStore>` か、エージェントごとの辞書）。`App.xaml.cs` を「Source のリストを組み立てる」形にする（この時点では Cursor 1 つ）。`HookInstaller` / `HookSetup` のクラスコメントに「Cursor 専用（別のエージェントが Hook を使うときに抽象化する）」と明記。**捕まえられなかった例外の受け皿**（`AppDomain.UnhandledException`・`DispatcherUnhandledException`・`TaskScheduler.UnobservedTaskException`）で `ex.ToString()` を `app.log` に書く（ログだけ。画面の挙動は変えない。A6） | **品質ゲート 1〜6 をすべて** |

**18-2 の進め方（いちばん注意する所）**：旧 `ProjectEventStore` の処理を、1 関数ずつ Source に移す。既存の `ProjectEventStoreTests` / `ProjectEventStoreImportTests` / `SessionMonitorTests` と 18-0 の特性テストは正として、構築部分だけ直す。

## 3. Phase 19：Claude の正規化（Core）

会話ログの 1 行 → 共通イベント。**状態を持つ変換部品**で、サブエージェントのログにも使える形にする。テスト先行。フィクスチャは、テスト用のビルダー（`ClaudeLogBuilder`）で**手書き**する（実ログのコピーは使わない）。変換の決めごとは **8.2**。

| # | コミット | 内容 | テスト（8.2 の行ごと） |
|---|---|---|---|
| 19-1 | 基本の変換 | `AgentEvent.SubagentId` の追加、`ClaudeCodeAgent`（`IAgentInfo`。Capabilities）を `AgentCatalog` に登録、`ClaudeTranscriptNormalizer`：最初の記録 → SessionStarted、**人の入力の見分け方**（B11）、`text` / `thinking`、`tool_use` / `tool_result`（共通名 `Shell`、成功は ExitCode 0、`Exit code N` → ToolSucceeded + ExitCode N、その他の `is_error` → ToolFailed、Duration）、**裏で動かす Bash は ExitCode null**（A3）、**Output は Shell だけ末尾 2000 字**（B4）、**モデル名 `<synthetic>` は null**（B5）、**編集ツールの一覧**（`Edit`・`Write`・`MultiEdit`・`NotebookEdit`。失敗は FileEdited にしない。B10）、ブランチ（`gitBranch`）を `Git` に。読み飛ばす記録と、壊れた行・知らない種類。**形式の変化のログ**（8.5） | 入力（`origin` あり／無しの古い形式）・`isMeta`・`task-notification` を入力にしない・返答・思考・ツール成功/失敗・終了コード（Bash／PowerShell）・裏の Bash・Output の長さ・`<synthetic>`・ファイル編集（4 種・失敗）・noise・壊れた行・欄の型が違う行で止まらない・ログの回数（同じエラーは 1 回＋件数）・ログに本文が入らない |
| 19-2 | ターンと状態 | `end_turn` → TurnEnded（**本文の行で 1 回だけ**。B1）、`[Request interrupted…]` → Aborted、`isApiErrorMessage` → Error、`AskUserQuestion` と `ExitPlanMode`（呼び出し → ボスの番、結果 → 入力。B2・B3）、スラッシュコマンドの扱い | 思考の行と本文の行の両方に `end_turn` → TurnEnded 1 回・ターンの各ケース・質問待ち→回答（依頼数に数える）・計画の承認待ち・中断 |
| 19-3 | サブエージェント | `Agent` の呼び出し → SubagentStarted（説明・種類）、結果 → **`toolUseResult.status == "completed"` のときだけ** SubagentStopped（SubagentId＝`agentId`）。それ以外は動いているまま、`task-notification` の `<tool-use-id>` が一致したら SubagentStopped（A3）。`SessionSummary` に履歴（説明・種類・開始・終了・経過）と「動いている」を持たせる。Cursor の subagentStart/Stop も同じ形に載せる | 起動中・完了・複数・status が completed でない・task-notification での終わり・Cursor の既存テストが変わらない |
| 19-4 | 停止判定と設定 | `StalledRule.DisplayState`（8.3。**表示用の状態を 1 か所で決める**。A4）、`AppSettingsStore` の `runningTimeoutMinutes`（既定 10、0 以下で無効、壊れた値は既定）。**保存を「全体を読む → キーを変える → 全体を書く」に直す**（今の `SaveTheme` は `theme` だけで上書きしている。知らないキーも残す。A5） | しきい値の境界・無効・サブエージェント実行中は停止にしない・結果待ちのツールがあれば停止にしない・Cursor にも効く・壊れた設定・テーマを保存しても他のキーが残る・時間を保存してもテーマが残る |
| 19-5 | ブランチと時刻からの HEAD | `GitClient.GetHeadAt(branch, at)`（8.4）。HEAD を持たないセッション（Claude）のコミット一覧に使う（A1）。`Uncommitted` / `CommitCheck` で、**作業ツリー（`<対象フォルダ>\.claude\worktrees\` 配下）のファイルは未コミット判定から外す**（B7） | 一時リポジトリ（`GitClientTests` と同じ流儀）：時刻の前後のコミット・ブランチが無い・`HEAD`／`-` で始まる名前は使わない・作業ツリーのファイルだけ → na |

完了条件：8.2 の全行にテストがある。Core は AOT 互換（警告 0）。Cursor の既存テストは変わらない。

## 4. Phase 20：Claude の読み込み（Core）

`ClaudeSessionSource`：どのファイルを、どう読むか。

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 20-1 | 探索と照合 | `ClaudeLocations`（`%USERPROFILE%\.claude`、`MIHARIKUN_CLAUDE_DIR`）、`ClaudeFolderName`（`C:\zDev\repo\Miharikun` → `C--zDev-repo-Miharikun`。英数字以外を 1 文字ずつ `-`）、対象フォルダの候補（同じ名前、または `--claude-worktrees-` が続くもの。大文字小文字は無視）、ファイルの最初の `cwd` を `ProjectPath` で照合（作業ツリー `<対象フォルダ>\.claude\worktrees\<名前>` も同じプロジェクト）。**候補が 0 件のときだけ**、起動ごとに 1 回、全フォルダの各ファイルの**先頭の `cwd` だけ**を読んで探し、ログに「フォルダ名の規則が違った」と残す（B6） | 名前の規則（日本語・記号・ドライブ違い）、worktree、別プロジェクトを拾わない、候補 0 件のときの探索 |
| 20-2 | 追記読みと差分 | `ClaudeSessionSource`：`JsonlTail` で追記分だけ読む・ファイルごとに Normalizer の状態を保持（作り直されたら状態も作り直す）・不完全な最終行は次回・差分は 8.1 の規則・`WatchTargets`（候補フォルダ。`CreateIfMissing=false`。サブフォルダは見ない）。`GetSubagentEvents` は空を返す（将来用の口）。`.claude` には**何も書かない** | 追記・不完全な行・作り直し・消えた・壊れた行・複数セッション・照合前の行・あとからできた作業ツリーのフォルダ |
| 20-3 | 大きなファイルの実測 | 数 MB〜20MB のダミーログを生成して、初回の読み込みと追記の時間・メモリを測る（`PerfFact`。`MIHARIKUN_PERF=1` のときだけ動く）。遅ければ「直近 N 日」の設定を、保留事項から実装に上げる | 計測の記録（要件定義の状況に書く） |

完了条件：隔離フォルダのダミー会話ログで、探索・追記・作業ツリーがテストで通る。Core は AOT 互換。

## 5. Phase 21：App の画面

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 21-1 | 組み立てとバッジ | `App.xaml.cs`：`ClaudeSessionSource` を追加（`.claude\projects` が無くても入れてよい。読むだけで、無ければ何も出ない）。`SessionCardViewModel` にエージェント名（`AgentCatalog` の表示名）、カードと詳細ヘッダーに**バッジ** | 隔離環境（ダミー会話ログを `MIHARIKUN_CLAUDE_DIR` に置く）で、Cursor と Claude が混ざって出る |
| 21-2 | エージェント絞り込み | Core に `AgentFilter`（`StatusFilter` と同じ流儀。件数は他のフィルタの前の全カードから数える）、左ペインのチップ（全て / Cursor / Claude） | `AgentFilter` のテスト、絞り込みの目視 |
| 21-3 | 表示状態・サブエージェント・コミット | カードに `DisplayState`（8.3）を持たせ、**丸・文字・「実行中のみ」の絞り込み・件数・詳細ヘッダー・拡大時のタイトル**は全部これを見る。`Tick` で変わったカードがあれば `CardsView.Refresh()` と `UpdateCounts()`（A4）。詳細の稼働状態に「サブエージェント 動いている N（説明）／ 動いた M」。`Capabilities` に無い項目（Claude の「閉じた」など）を出さない。`LoadCommits`：HEAD が無いセッションは `GetHeadAt` で求めて `GetCommits` へ（A1）。`TimelineViewModel.JumpTo` は **Seq と種類が両方一致する行を優先**（無ければ今どおり Seq だけ。B8） | ライト/ダークの両方で目視（隔離環境）。Claude のダミーで、コミット一覧が出る・停止（動きなし）の表示・絞り込みと件数が一致 |
| 21-4 | 設定画面 | ⚙ メニューに「設定…」（要件 12.9）。アプリ全体の設定のダイアログ（`Views/AppSettingsDialog`）。項目は「停止とみなす時間（分）」だけ（0 で無効。数字以外・負の数は保存できない）。保存はすぐ効く（`MainViewModel` のしきい値を差し替える）。テーマは ⚙ メニューに残す | 隔離環境で、変えた時間がすぐ効く・再起動後も残る・テーマを切り替えても消えない。ライト/ダーク |

完了条件：隔離環境で、Cursor と Claude が一覧に混ざって出る。絞り込みが効く。停止・サブエージェント・コミットの表示が出る。設定画面で変えた時間がすぐ効く。ライト/ダーク両方。

## 6. Phase 22：実機確認・仕上げ

| # | コミット | 内容 |
|---|---|---|
| 22-1 | 本物の Claude セッションの表示 | **このプロジェクトの Claude Code セッションを表示して、成果（コミット・テスト実行）の見え方を確認する**。保存先（`MIHARIKUN_DATA_DIR`）は隔離し、`.claude` は読み取りだけ。未確認の項目を、実機に出たものから確認して 14.2 を更新する：圧縮・再開（`relocated`）・`api_error` と再試行・スラッシュコマンドの表示・**裏で動かした Agent の結果と終わりの記録**・**`ExitPlanMode`**。会話ログを数えるときは利用者の了承を得て、構造だけ（本文は出さない。進行中のセッションは除く）。初回読み込みの時間を実測する。`app.log` に読めない行・知らない種類が出ていないか見る |
| 22-2 | 仕上げ | `README.txt`（`scripts/publish.ps1`）と `docs/release.md` に Claude Code の説明（導入不要・読み取りだけ）と設定画面、`CLAUDE.md` の冒頭と禁止事項を「Cursor と Claude Code」に更新、要件定義の `[x]` と状況 |

## 7. ファイル構成（追加・変更）

```
src/Miharikun.Core/
├─ Agents/
│  ├─ IAgent.cs                       18-1 変更：IAgentInfo / IHookAgent に分割
│  ├─ AgentCatalog.cs                 18-1 新規：AgentId → IAgentInfo（App 用。19-1 で Claude を登録）
│  ├─ CommonTools.cs                  18-1 新規：共通のツール名（Shell ほか）
│  ├─ AgentEvent.cs                   19-1 変更：SubagentId を追加
│  ├─ AgentRegistry.cs                18-1 変更：IHookAgent? を返す（Hook exe 用）
│  ├─ CursorAgent*.cs                 18-1/18-4 変更：IHookAgent。"default" モデルの無視をここへ
│  ├─ ClaudeCodeAgent.cs              19-1 新規：IAgentInfo
│  ├─ ClaudeTranscriptNormalizer.cs   19-1〜3 新規：1 行 → AgentEvent[]（状態を持つ。サブエージェントのログにも使える）
│  └─ CursorTranscriptImporter.cs     18-2 変更なし（CursorSessionSource が使う）
├─ Sessions/
│  ├─ ISessionSource.cs               18-1 新規（SessionDelta・SessionDeltaKind・WatchTarget を含む）
│  ├─ CursorSessionSource.cs          18-2 新規：旧 ProjectEventStore の読み込み＋ Importer の優先ルール
│  ├─ ProjectEventStore.cs            18-2 変更：汎用に（保持・要約キャッシュ・通知・Source ごとの例外の隔離）
│  ├─ SessionMonitor.cs               18-3 変更：WatchTargets を毎回取り直して監視
│  ├─ SessionAnalyzer.cs              18-4/19-3 変更：共通ツール名、Subagents（履歴）
│  ├─ SessionSummary.cs               19-3 変更：Subagents / RunningSubagents
│  ├─ StalledRule.cs                  19-4 新規：DisplayState
│  ├─ ClaudeLocations.cs              20-1 新規：.claude の場所・環境変数
│  ├─ ClaudeFolderName.cs             20-1 新規：パス → Claude 式のフォルダ名
│  ├─ ClaudeSessionSource.cs          20-2 新規
│  ├─ AgentFilter.cs                  21-2 新規
│  ├─ CommitCheck.cs                  19-5 変更：作業ツリーのファイルは na
│  └─ Timeline.cs                     （変更なし。JumpTo の変更は App 側）
├─ Git/GitClient.cs                   19-5 変更：GetHeadAt、Uncommitted で作業ツリーを外す
├─ Meta/SessionMetaService.cs         18-5 変更：エージェントごとに MetaStore を切り替え
├─ Settings/AppSettings.cs            19-4 変更：runningTimeoutMinutes、全体を読んで書く形に
└─ Install/HookInstaller.cs           18-5 変更：コメントに「Cursor 専用」と明記のみ

src/Miharikun.Hook/HookRunner.cs      18-1 変更：IHookAgent を使う（型の変更のみ）

src/Miharikun/
├─ App.xaml.cs                        18-5/21-1 変更：Source のリストを組み立て、捕まえられなかった例外のログ
├─ HookSetup.cs                       18-5 変更：コメントに「Cursor 専用」と明記のみ
├─ ViewModels/SessionCardViewModel.cs, MainViewModel.cs, SessionDetailViewModel.cs   21-1〜4 変更
├─ ViewModels/TimelineViewModel.cs    21-3 変更：JumpTo で種類も見る
├─ Views/AppSettingsDialog.xaml(.cs)  21-4 新規：設定画面
└─ MainWindow.xaml(.cs)               21-1〜4 変更：バッジ、絞り込みチップ、サブエージェント表示、⚙ の「設定…」

tests/Miharikun.Tests/Core/
├─ ProjectEventStoreTests.cs, ProjectEventStoreImportTests.cs, SessionMonitorTests.cs  18-0 特性テスト追加／18-2・18-3 構築部分のみ修正
├─ GoldenSummaryDump.cs               18-0 新規：A/B ダンプ（MIHARIKUN_GOLDEN_DIR のときだけ）
├─ GenericProjectEventStoreTests.cs   18-2 新規：ダミーの Source で
├─ ClaudeLogBuilder.cs                19-1 新規：手書きの会話ログを組み立てる補助
├─ ClaudeNormalizerTests.cs           19-1〜3 新規：8.2 の行ごと
├─ StalledRuleTests.cs                19-4 新規
├─ AppSettingsTests.cs                19-4 追加：他のキーを残す・runningTimeoutMinutes
├─ GitClientTests.cs, CommitCheckTests.cs   19-5 追加
├─ ClaudeLocationsTests.cs            20-1 新規：フォルダ名の規則・worktree・照合
├─ ClaudeSessionSourceTests.cs        20-2 新規
├─ ClaudePerfTests.cs                 20-3 新規（PerfFact）
└─ AgentFilterTests.cs                21-2 新規
```

## 8. 実装の決めごと（迷いやすい所）

### 8.1 差分（`SessionDelta`）の規則（A2）
`SessionDelta(Key, Kind, Events)`、`Kind` は `Append` / `Replace` / `Remove`。

**Store 側**
- `Append`：保持しているイベントの後ろに足す。空の列なら何もしない（セッションを作らない）。
- `Replace`：保持しているイベントを丸ごと置き換える。**空の列なら `Remove` と同じ**。
- `Remove`：セッションを消す（`GetSummary` が null を返す → Monitor が「消えた」と通知。今と同じ）。
- 1 回の `ReadNew` で、同じキーに複数の差分が来てよい（順に適用）。`Refresh` が返す「変わったキー」は重複を除く。
- 適用したキーの要約のキャッシュを捨てる。

**Cursor の Source が出す差分**（旧 `ProjectEventStore` の実際の動きと同じにする）

| 旧の動き | 出す差分 |
|---|---|
| 照合済みのファイルに行が追記された | `Append`（新しいイベント） |
| 照合前に捨てた行があり、今回初めて一致した（先頭から読み直す） | `Append`（先頭からの全イベント。Store は空なので結果は同じ） |
| ファイルが作り直された（truncated）＋ 新しい行が一致した | `Replace`（新しいイベントだけ。1 回にまとめる） |
| ファイルが作り直された ＋ まだ一致しない／行が無い | 前にイベントがあったときだけ `Remove`。無ければ何も出さない |
| transcript から取り込み済み → 同じ ID の Hook ファイルが現れ、一致した | **取り込み分の `Remove` を先に出し**、Hook のファイルはふつうの差分（初めて一致した時点で先頭からの全部の `Append`）。`Remove` が要る理由：Source 内の辞書は大文字小文字を区別せず、Store のキー（`SessionKey`）は区別するので、ID が大文字小文字だけ違うと、Hook の差分だけでは取り込み分が別のキーとして残る（レビュー B1）。**今回読んだ分だけで `Replace` しない**（Store に前の分があると消える） |
| 取り込み済み → 同じ ID の Hook ファイルが現れたが、別のプロジェクト | `Remove`（上と同じ、先に出す取り込み分の `Remove` だけ） |
| 初回の transcript の取り込み（Hook のファイルが無いもの） | `Replace`（取り込んだイベント。`Imported = true`） |

**Claude の Source**：新しいファイル・追記は `Append`、作り直されたら（Normalizer の状態も作り直して）`Replace`、ファイルが消えたら `Remove`。

### 8.2 Claude Code の変換（要件 5.1 の対応表の実装の決めごと）
実ログ（28 本・18,787 行。構造だけ）で確かめた事実を含む。

| 記録 | 変換 | 決めごと |
|---|---|---|
| ファイルの最初の記録（`timestamp` のあるもの） | SessionStarted | 時刻＝最初の `timestamp`。`Git = new GitSnapshot(gitBranch, null)`。**最初の記録に `gitBranch` が無いとき（実ログ 2.1.286 の `queue-operation`）は、最初に `gitBranch` を持つ行の最初のイベントの Git に載せる。以後は変わったときだけ**（Phase 22-1 で追加） |
| **人の入力**：`type=user` の文字の行で、`isMeta` でない・`tool_result` でない・`origin` が無いか `origin.kind == "human"`・`[Request interrupted` で始まらない | PromptSubmitted | 古い版（2.1.156）は `origin` 欄そのものが無い（16 件）。`origin.kind = "task-notification"`（27 件）は入力にしない（B11）。スラッシュコマンドや `<local-command…>` はそのまま文字で |
| `[Request interrupted…]`（`origin` 無し） | TurnEnded（Aborted） | |
| `assistant` の `text` | AssistantMessage | `Model = message.model`。**`<synthetic>` は null**（5 件。B5） |
| `assistant` の `thinking` | AssistantThought | |
| `stop_reason == "end_turn"` | TurnEnded（Completed） | **`text` を含む行で出す**。思考の行と本文の行の両方に付くことがある（449 件中 172 件）。念のため同じ `message.id` では 1 回だけ（B1）。本文の後に出す |
| `isApiErrorMessage` の返答 | TurnEnded（Error） | `system` の `api_error` は使わない |
| `tool_use` | ToolStarted | `ToolName` は共通名（`Bash` / `PowerShell` → `Shell`）。`Command = input.command`。`input.run_in_background` を覚えておく |
| `tool_result`（`is_error = false`） | ToolSucceeded | Shell は ExitCode 0。**ただし裏で動かした Bash（`run_in_background` か、`toolUseResult.backgroundTaskId` あり）は ExitCode null**（A3。実例 1 件）。Duration ＝ 結果の時刻 − 呼び出しの時刻 |
| `tool_result`（内容が `Exit code N` で始まる `is_error`） | ToolSucceeded（ExitCode N） | 実ログで PowerShell 48 件・Bash 20 件 |
| `tool_result`（その他の `is_error`） | ToolFailed | |
| Output | | **Shell だけ末尾 2000 字を残す。ほかは null**（Read の全文などでメモリが膨らむため。B4） |
| 編集の成功（`Edit`・`Write`・`MultiEdit`・`NotebookEdit`） | FileEdited | `FilePath = input.file_path`（`NotebookEdit` は `notebook_path`）。`is_error` のときは出さない（B10） |
| `Agent` の `tool_use` | SubagentStarted | Text＝`description`、ToolName＝`subagent_type`、ToolUseId |
| `Agent` の結果 | SubagentStopped | **`toolUseResult.status == "completed"` のときだけ**（実ログは 84 件すべて completed）。SubagentId＝`agentId`。**ただし結果が `is_error` のときも終わりにする**（19-3 で追加。動いていないのに残らないように） |
| `origin.kind = "task-notification"` の行 | （Agent なら）SubagentStopped | 内容の `<tool-use-id>` が動いている Agent の id と一致したとき。それ以外は読み飛ばす（今は SendMessage のものだけ） |
| `AskUserQuestion` / `ExitPlanMode` の `tool_use` | TurnEnded（Completed） | ユーザーの返事待ち＝ボスの番。`ExitPlanMode` は実例 0 件（手書きのテストだけ） |
| その結果 | PromptSubmitted | **依頼数・ターン数・最近の入力に数える**。Text は「（回答）」＋内容（B3） |
| 上記以外（`attachment`・`queue-operation`・`file-history-*` など） | 読み飛ばす | 知らない種類はログ（8.5） |

Seq ＝ ファイル内の行番号（12.6）。1 行から複数のイベントが出たら、同じ Seq になる（B8 は画面側で対処）。

### 8.3 表示用の状態（`StalledRule.DisplayState`。A4）
入力：要約・いま・`runningTimeoutMinutes`。出力：表示用の状態と、添える文字。

1. 要約の状態が「実行中」でない、またはしきい値が 0 以下 → 要約の状態のまま
2. 「いま − 最後の動き」がしきい値未満 → 実行中
3. 動いているサブエージェントがある、または結果待ちのツール（`RunningTools`）がある → **実行中のまま**、「N 分動きなし」を添える（サブエージェントの中身は別ファイルなので本体は無音。長いコマンド・承認待ちもここ）
4. それ以外 → **停止**（🟡）、「N 分動きなし」を添える（ボスが押した停止と区別するため）

画面は、カードの丸・文字・「実行中のみ」・件数・詳細ヘッダー・拡大時のタイトルを、すべてこの結果で出す。要約そのものは変えない。

### 8.4 ブランチと時刻からの HEAD（A1）
- `GitClient.GetHeadAt(string? branch, DateTimeOffset at)`：`git rev-list -1 --before=<at の ISO 8601> <branch>`。結果が 40 桁の 16 進なら返す。それ以外・失敗は null。
- ブランチ名は、null・空・`HEAD`・`-` で始まるもの・`git check-ref-format --branch <名前>` が失敗するものは使わない（null を返す）。
- 画面（21-3）：`StartHead` / `LatestHead` が無く `Branch` があるセッションは、`GetHeadAt(Branch, StartedAt)` と `GetHeadAt(Branch, LastActivityAt)` を求め、既存の `GetCommits` に渡す（背景スレッド。範囲が変わったときだけ。今の `_commitRange` の仕組み）。
- 採らなかった案：`git commit` の出力から SHA を拾う（実ログで 105 件中 30 件しか拾えない）、reflog（このリポジトリは 10/3 からしか無い）。
- 同じブランチで並行して動いた別のセッションのコミットも混ざる（Cursor の今の方式も同じ）。

### 8.5 形式の変化と例外のログ（A6。要件 11.1・13 章）
- Normalizer は 1 行ごとに例外を捕まえ、その行は飛ばす。値は `TryGetValue` だけで取り出す（`GetValue<T>` は型が違うと投げる）。
- 読めない行：`app.log` に「ファイル名・行番号・記録の種類・`version`・例外の種類とメッセージ」。**同じ種類のエラーは 1 ファイルにつき最初の 1 回だけ**、最後に件数。**行の中身は書かない**。
- 知らない種類の記録・初めて見る `version`：起動ごとに 1 回、件数つき。
- Store：Source ごとに `ReadNew` を `try/catch (Exception)`、`ex.ToString()` をログ、他の Source は続ける。
- App：捕まえられなかった例外（UI スレッド・背景スレッド・Task）を `ex.ToString()` で `app.log` に。
- 画面には出さない。

### 8.6 作業ツリーのファイル（B7）
`<対象フォルダ>\.claude\worktrees\` 配下のファイルは、本体の `git status` では個別に出ない（`.claude/` ごと未追跡として出る）。そのため未コミットの判定から外す。変更ファイルがすべて作業ツリーなら、完了チェックは na「コミットの確認なし（作業ツリーの変更のため不明）」。一部なら、本体のファイルだけで判定する。

## 9. リスクと対策

| リスク | 対策 |
|---|---|
| Phase 18 で Cursor の挙動が変わる | 18-0 の特性テスト、8.1 の差分の規則、品質ゲート 6 点（A/B ダンプの差分 0・データ入りの目視） |
| Hook exe（NativeAOT）が `IHookAgent` の変更で壊れる | 18-1 と 18-5 で `publish.ps1` を通す |
| 会話ログの形式が変わる | 8.5 のログ。知らない種類・初めて見る `version` もログに出す。手書きのビルダーで、形式の変更をテストで気づけるようにする |
| 読み込みの例外でアプリが落ちる | 1 行ごと・Source ごとに例外を捕まえる。捕まえられなかった例外は `app.log` にスタックつきで残す |
| テーマの保存で、停止の時間が消える | `settings.json` は全体を読んで、変えるキーだけ書く（19-4 のテスト） |
| `.claude` を書き換えてしまう | Claude の監視先は `CreateIfMissing=false`。Source は読むだけ（20-2 のテストで、フォルダを作らないことを確かめる） |
| ターン数・テスト成功・裏の作業の誤表示 | 8.2（`end_turn` の重複除去、裏の Bash は ExitCode null、Agent の status） |
| 長いサブエージェント・コマンド・承認待ちを「停止」と出す | 8.3（結果待ち・サブエージェントがあれば停止にしない） |
| 20MB の初回読み込みが遅い・メモリ | 20-3 で実測。Output は Shell の末尾 2000 字だけ。遅ければ、新しい順に読む・「直近 N 日」の設定を足す |
| テストのフィクスチャに会話の内容が入る | 実ログのコピーを使わず、`ClaudeLogBuilder` で構造だけ真似て手書き |
| 圧縮・再開・承認待ち・裏の Agent の記録が未確認 | Phase 22 で実機に出たものを確認。出なければ「保留」 |

## 10. 決定事項（確認済み）

- 構成：汎用 `ProjectEventStore` ＋エージェントごとの `ISessionSource`（要件定義 5.1）。
- 共通化（Phase 18）は、挙動を変えずに先に単独で完了させる。`HookInstaller` / `HookSetup` は名前を変えず、コメントで「Cursor 専用」と明記する。
- Claude Code は会話ログだけで読む（フックなし）。失敗したテストは「成功扱い + 終了コード」、質問待ちはボスの番、「閉じた」状態は無し、停止時間は ⚙ の「設定…」の画面で設定（既定 10 分。`settings.json` に保存）、サブエージェントは「動いている／動いていた」まで（詳細は将来、読める作りにする）。

## 11. 決定事項（追加。確認済み）

1. **Phase を 5 つ（18〜22）に分け、18 を単独で先に完了させる**。
2. 品質ゲート（1 章）の 6 点。特に、Phase 18 でテストの修正を「構築部分のみ」に限る。
3. Phase 22-1 で、**このプロジェクトの本物の Claude Code セッションを読み取り専用で表示する**（保存先は隔離。`.claude` は読むだけ）。
4. 進める順は 18 → 19 → 20 → 21 → 22（19 と 20 は、18 のあとなら並べ替え可能）。
5. **設定画面を作る**（レビュー A5）：⚙ メニューに「設定…」。今は「停止とみなす時間」だけ。今後のアプリ全体の設定もこの画面に足す。**テーマは ⚙ メニューに残す**。
6. **形式の変化と例外はログだけに出す**（レビュー A6）：画面に印は出さない。
7. **レビュー（`issue11-claude-code-review.html`）の指摘は、すべて採る**（A1〜A7・B1〜B11・C1〜C4）。B3 は「回答も依頼として数える」。A1 は「ブランチと時刻から HEAD を求める」。

## 12. 実装するセッションへの注意（必ず読む）

この計画は、別のセッション（Sonnet など）が実装することを前提にしている。始める前と、各 Phase の区切りで、次を守る。

### 12.1 始める前に
- **この計画・`issue11-claude-code-review.html`・`HANDOFF.md` が見えることを確かめる**。これらと要件定義の変更は、コミットされていないことがある。作業ツリー（worktree）で始まったセッションには、未追跡のファイルが無い。見つからなければ、推測で進めずに利用者に聞く（「同じフォルダで開き直すか、先にコミットするか」）。
- 計画（この md）と要件定義（`miharikun-requirements.md`）を読み、**食い違いが無いか確かめてから始める**。食い違いがあれば要件定義が正。判断がつかなければ利用者に聞く（CLAUDE.md）。
- `HANDOFF.md` の「やりとりのルール」と「落とし穴」を読む（コミットは頼まれたときだけ・`git add` はパス指定・完了報告にビルドの有無と出力先・`.claude` と本物の環境は読むだけ・パスを含むコードは Write ツールで書く、など）。

### 12.2 利用者の了承が要る所（止まって聞く）
- **18-0・18-5**：本物の Cursor のデータ（`%LOCALAPPDATA%\Miharikun\events\cursor`・`meta\cursor`・`settings.json`、`%USERPROFILE%\.cursor\projects\` の transcript）を一時フォルダにコピーする前。何をどこにコピーし、確認後に消すことを説明してから。
- **22-1**：会話ログ（`%USERPROFILE%\.claude\projects\`）の構造を数える前。数えるのは種類・欄の有無・件数だけで、本文は出さない。進行中のセッションは読まない。
- インストール・ダウンロード・大量のファイルを作る計測（HANDOFF 2 章）。

### 12.3 Phase 18 の後は、Opus のレビューを挟む（利用者に頼む）
Phase 18 は、Cursor の読み込みを移し替える、いちばん危ない所。**18-5 が終わったら、次の Phase に進まずに止まり、利用者に「Opus にレビューを頼んでほしい」と伝える**。そのとき、次の内容を**利用者に渡せる文章（レビューの依頼文）**として書く。依頼文は `docs/issue11-phase18-review-request.md` に保存し、チャットにも貼る。

依頼文に必ず入れること：
1. **対象**：Phase 18 のコミットの範囲（`git log --oneline` の最初と最後の hash）と、変更したファイルの一覧（`git diff --stat`）。
2. **前提**：仕様は要件定義 5.1、移し方の決めごとは計画 8.1、品質ゲートは計画 1 章。
3. **見てほしい所**（Opus にこの順で確かめてもらう）：
   - **差分の規則**：計画 8.1 の表の 7 行それぞれが、`CursorSessionSource` のどこで、どの差分（Append / Replace / Remove）になっているか。特に「取り込み済み → Hook」が `Replace` になっているか、「作り直し＋新しい行」が 1 回の `Replace` にまとまっているか。
   - **Store の規則**：空の `Replace` ＝ `Remove`、空の `Append` は無視、同じキーの複数の差分、要約のキャッシュの破棄、`Refresh` の戻り値の重複除去。
   - **テストを変えていないか**：既存のテストファイルの `git diff` が、構築部分（`new ProjectEventStore(…)` など）だけか。`Assert` の行・期待値に変更が無いか。テスト件数が減っていないか。
   - **例外**：Source ごとの `try/catch` で、他の Source が止まらないか。捕まえられなかった例外の受け皿が、画面の挙動を変えていないか（例外を握りつぶして続行していないか）。
   - **監視**：`CreateIfMissing` が Cursor だけ true か。監視先の取り直しで、Watcher が増え続けたり、消えたフォルダの Watcher が残ったりしないか。ロックの中で重い処理をしていないか。
   - **型の分割**：Hook exe が `IHookAgent` だけを使い、リフレクションが増えていないか（AOT）。
   - **`"default"` の移動**：`SessionAnalyzer` から消え、`CursorAgent.Normalize` で null にしているか。
4. **確認済みの結果**：品質ゲート 6 点それぞれの結果（テスト件数、AOT の警告数、`publish.ps1`、A/B ダンプの差分の件数、目視で見た画面）。できなかったものは、できなかったと書く。
5. **気になっている所**：実装していて迷った所・自信が無い所を、正直に書く。

レビューの指摘が返ってきたら、`superpowers:receiving-code-review` に従い、実コードで事実を確かめてから採否を決める（鵜呑みにしない）。採らないものは理由を利用者に説明する。

### 12.4 推測で設計した所（Phase 22 で確かめる）
実例が無いので、安全側に倒して設計している。実装ではテストを手書きし、Phase 22 で実物を確かめて、違えば直す：
- 裏で動かした Agent の結果（`toolUseResult.status` が `completed` 以外の形）と、終わりの通知（`task-notification`）
- `ExitPlanMode` の結果の形
- 会話の圧縮（コンパクト）の記録

### 12.5 各 Phase の終わり
- 隔離環境（`MIHARIKUN_DATA_DIR` ほか）で実機確認し、要件定義の `[x]` と「状況」を更新する。
- 報告には、ビルドの有無と出力先、テスト件数、できなかった確認を書く。**次の Phase に進む前に止まって報告する**。

**状態：確定（レビュー反映済み）**。次は、Phase 18-0 から実装する。
