# macOS 対応（画面を Avalonia 12 に移す） 実装計画（Issue #22）

> **連動ルール**：この md と `macos-support-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `miharikun-requirements.md`（3・4・5・6・7・8・**8.1**・**9**・9.1・11・**12.5**・**12.7**・12.10・**12.11**・13・**14.3**・16・17 章・**Phase 27〜33**）。設計の資料は `macos-support-design.html`（候補の比較・決定 Q1〜Q7・署名なしの配布の手順）。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（7 章）**。
> 進め方：mac の実機確認 → ViewModel の切り出し（挙動不変）→ Core・テストの mac 対応 → 画面（Avalonia）→ 配布 → 通し確認。各 Phase の終わりに、決めた OS（1 章の表）で確かめ、要件定義の `[x]` と「状況」を更新して、ビルドの有無と出力先を報告する。コミットは頼まれたときだけ（ここは区切りの目安）。途中のコミットは `Refs #22`、最後だけ `Fixes #22`。作業ブランチは `feature/issue22-macos-support`（1 つ。main に入るのは Avalonia 版だけ）。
> **レビュー**：`macos-support-review.html`（2026-10-06）の指摘 #1〜#19 と決定 Q1〜Q5 を反映した（要件定義も反映済み）。

## 1. 全体像

```
27 mac の実機確認 ──▶ 28 ViewModel の切り出し ──▶ 29 Core・テストの mac 対応 ──▶ 30 Avalonia でダッシュボード
   mac・1 コミット        Windows・3 コミット（挙動不変）   mac・4 コミット                  mac＋Win 確認・6 コミット
                                                                                                   │
33 通し確認・仕上げ ◀── 32 配布 ◀──────────── 31 ドキュメント・メモタブ ◀──────────────────────┘
   両 OS・1 コミット        mac＋Win・4 コミット        mac＋Win 確認・4 コミット
```

| Phase | 主に作業する OS | 理由 |
|---|---|---|
| 27 | mac | mac でしか確かめられない |
| 28 | Windows | WPF の画面で「挙動が変わらない」を確かめられる最後の機会 |
| 29 | mac（最初に CI を作る：29-1） | mac でテストを回して落ちる所を直すのが作業そのもの。Windows は CI で確かめる |
| 30・31 | mac（区切りごとに Windows でも起動して確かめる） | mac の見た目・日本語入力・WKWebView をその場で見られる。mac の確認は、開発用の `.app`（30-1）を Finder から開いて行う（`dotnet run` では、ファイルとフォルダの許可・メニュー・Dock の動きが違う） |
| 32 | 32-1 は Windows（か CI）、32-2 は mac、32-3 は CI | mac の `.app` は mac で組む。Windows の発行（App の単一ファイル・Hook の NativeAOT）は Windows か CI でしか確かめられない |
| 33 | 両方 | 実機での通し確認 |

- **OS を移るとき・CI を見るとき**：作業した OS からもう一方へは、コミットを運ぶ（push → pull。作業ツリーは OS をまたげない）。`CLAUDE.md` の決まり（コミット・push は頼まれたときだけ）に沿って、次の区切りで止まり、利用者にコミットと push を頼む：27 の後、28 の後、29-1 の後（CI を見るため）、29 の後、30・31 の各区切り（Windows で起動して確かめる前）。

- 新しい依存パッケージ：`Avalonia`・`Avalonia.Desktop`・`Avalonia.Themes.Fluent`（Phase 30。12 系の最新。調査時点で 12.1.x）、`Avalonia.Controls.WebView`（Phase 31。MIT。Windows は WebView2、mac は WKWebView）。`SukiUI`（Phase 30-2 の見比べで使う。MIT。7.0 系は Avalonia 12.0.3 以上に対応。安定版を使い、nightly は使わない。DataGrid・ColorPicker なども一緒に入る）。FluentTheme を選んだら外す（7.8）。`Avalonia.Headless.XUnit`（Phase 30-3。テスト。画面を出さずに Avalonia の部品を動かす。版は Avalonia にそろえる）。**外すもの**：`WPF-UI`・`Microsoft.Web.WebView2`（Phase 30-1。Runtime の見分けは `NativeWebView.AdapterInfo` で行う。それで足りなければ、Windows だけで使う形で戻す。7.11）。
- 守る制約：Core は AOT 互換のまま（リフレクションを使わない。Hook が使う）。App は共通イベント `AgentEvent` だけを扱う。`~/.claude/` には何も書かない。本物の環境（Windows の `%LOCALAPPDATA%\Miharikun\`・`%USERPROFILE%\.cursor\hooks.json`、mac の `~/Library/Application Support/Miharikun/`・`~/.cursor/hooks.json`）を勝手に書き換えない。
- **Phase 28 は挙動を変えない切り出し**。Windows の WPF 版の画面が変わらないことが完了条件（品質ゲート）。Phase 30・31 は、同じ確認項目を Avalonia 版（両 OS）でもう一度通す。
- いまのテスト：**842 件合格 + スキップ 5**（`PerfFact`。2026-10-06、Windows）。**件数は減らさない**。mac では Windows だけのテストが飛ぶので、OS ごとの件数を記録する（7.15）。

### 品質ゲート（Phase 28 の完了条件。Phase 30・31 の完了条件でも、Avalonia 版で同じ項目を通す）
1. `dotnet test` が全部通る（842 件を減らさない。28-3 で ViewModel のテストが増える）。
2. `dotnet build src/Miharikun.Core --no-incremental` が警告 0。アプリのビルドも警告 0。
3. 既存の機能が従来どおり（隔離環境。下の一覧を全部）：
   - **一覧**：状態ごとの件数、ステータスのチップ（「全て」・複数選択の OR・件数は全カードから）、エージェントのチップ（1 つだけ）、検索（全プロンプト・概要・メモ・タイトル・変更ファイル名）、フィルタ（実行中のみ・未コミットあり・メモあり）、並び（最後の動きの新しい順。動いたカードが上へ移る）、カードの選択、**並べ替えても選んでいるカードの選択が外れない**（Avalonia は Move で外れる不具合があるので、選択中は動かさない。7.3）、✏️ のリネーム（Enter・Esc・フォーカスアウト）、**選択中のカードが隠れたときの動き**（絞り込み・検索のほか、「実行中のみ」で停止に変わった・「未コミットあり」でコミットした・「メモあり」でメモを消した・拡大中に隠れた。28-2 の前に WPF 版で記録し、同じであること。7.3）、停止の表示（1 秒ごと）。
   - **詳細**：ステータスのボタン（押す・もう一度で未設定）、概要の取り込み・編集・1 つ前に戻す、3 行サマリーのクリック → タイムラインへジャンプ（フィルタが OFF なら ON）、完了チェック、稼働状態、成果（コミット・変更ファイル・テストの展開）、ターン一覧のジャンプ。
   - **右ペイン**：メモ（フォーカスアウトで保存・セッション切り替えで保存）、タイムラインの種別チップ・検索（「3/48件」・✕）・行のクリックで全文の開閉・ダブルクリックでコピー（バブル「コピーしました」）・全部コピー、拡大モード（Esc で戻る・状態が保たれる）、最近の入力・最近閉じたセッション（クリックでカードを選ぶ。隠れていればフィルタを外す）、5,000 行のタイムラインで種別の ON/OFF・検索の 1 文字・セッションの切り替えがもたつかない（28-2 の前に WPF 版で時間を測って記録し、同じくらいであること。7.3）。
   - **ドキュメントタブ**：Phase 16・24 で確かめた項目（ツリー・一覧・絞り込み・概要・md（チェックボックス・日本語の見出し id・mermaid・コードの色付け・相対パスの画像）・`#` リンク・md→md と html→md のリンク・html の相対 css/js/画像・自動再読み込み・タブを切り替えて戻っても表示とスクロール位置が戻る（Avalonia では WebView を作り直さない：7.17）・テーマの切り替えで md が作り直される・「フォルダで開く」・「既定のアプリで開く」・除外設定のダイアログ）。
   - **メモタブ**：Phase 25 で確かめた項目（空の案内・編集・保存・Ctrl+S・キャンセルの確認・タブを移っても入力が残る・読めないメモ・外での書き換え・未保存で閉じるときの確認［保存］［保存しない］［キャンセル］・メモのリンクからドキュメントタブへ）。
   - **⚙**：テーマ（OS に合わせる・ライト・ダーク）、設定…（停止とみなす時間）、ドキュメントの設定…、Hook の導入・削除（`MIHARIKUN_CURSOR_DIR` の一時フォルダで）、起動時の Hook の確認。
4. 確かめ方：隔離環境（`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダに）。Windows は UI Automation（`AutomationId`）と DevTools プロトコル（WebView2）で、これまでと同じ方法。mac は利用者の目視を基本にする（7.20）。本物のデータは使わない。

## 2. Phase 27・28：mac の実機確認と ViewModel の切り出し

### 2.1 Phase 27：mac の実機確認（Step 0。mac）

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 27-1 | 確認の記録（要件定義 14.3） | ①mac の準備（利用者が入れる：.NET 10 SDK、Xcode Command Line Tools、git、gh。Claude Code）。②`dotnet test tests/Miharikun.Tests` を mac で回し、**落ちたテストの一覧と件数**を記録する（Phase 29-2 の基準。コードは変えない）。ソリューション全体のビルドは WPF のせいで失敗するので、プロジェクトを指定する（10.1）。③Hook を mac で発行（`dotnet publish src/Miharikun.Hook -c Release -r osx-arm64 -o <一時>`）し、`~/Library/Application Support/Miharikun/bin/Miharikun.Hook` に置いて、`~/.cursor/hooks.json` に**手で**登録する（**利用者の了承の後、バックアップしてから**。コマンドの形は `HookInstaller.BuildCommand` と同じ：空白があれば引用符）。Cursor（Hobby）で 1〜2 回会話する。④14.3 の各項目を**構造だけ**数える（events の種類・件数、payload のキー、`workspace_roots` の形、日本語の入力が壊れていないか、Cursor の会話ログの場所と slug、Claude Code のフォルダ名と `cwd` の形、`xcode-select -p`、シンボリックリンクを経由したフォルダ（`/tmp/…` など）での `cwd`・`workspace_roots`・git のルートの形（実パスか論理パスか）、Finder で作った日本語のファイル名の形（NFD か））。⑤任意：Hook に「隔離」の印を付けて（`xattr -w com.apple.quarantine "0081;00000000;Safari;" <Hook>`）、Cursor から呼ばれたときに何が起きるか。⑥確認が終わったら、手で入れた登録を残すか戻すかを利用者に聞く。⑦任意：WKWebView の読み取り範囲の試作（捨てる小さな Avalonia の画面。7.11 の組み合わせ。30-1 の最初でもよい） | 14.3 の項目が「確認済み」か「保留」に整理されている。7.1 の表の「結果」の列が埋まり、Phase 29 で直す内容が決まる |

完了条件：14.3 が整理され、7.1 の表の対応が決まる。本文・コマンド・パスの中身は記録しない。

### 2.2 Phase 28：ViewModel の切り出し（挙動は変えない。Windows）

```
前：src/Miharikun（WPF）の中に ViewModel ──▶ WPF の型に依存
      CollectionViewSource・DispatcherTimer・Clipboard・MessageBox・ShellOpen・AppLog を直接使う
      テストから参照できない（net10.0-windows）

後：src/Miharikun.Presentation（net10.0。新規）
      ├─ ViewModels/*（移す。名前空間 Miharikun.ViewModels のまま）
      ├─ IPreviewHost・PreviewSource（移す）、HookSetup（移す。非同期に）、AppLog・ShellOpen（移す）
      ├─ Services/IUiServices・IUiTimer（新規。画面の仕組みへの口）
      └─ Services/ViewList（新規。絞った一覧を、要素を使い回して合わせる。選択中は動かさない）
          ▲ 実装
          ├─ src/Miharikun（WPF）：WpfUiServices（Phase 28。Phase 30 で消える）
          ├─ src/Miharikun（Avalonia）：AvaloniaUiServices（Phase 30）
          └─ tests：FakeUiServices（タイマーを手で進める）
```

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 28-1 | Presentation プロジェクトと口 | `src/Miharikun.Presentation/Miharikun.Presentation.csproj`（`net10.0`、`CommunityToolkit.Mvvm` 8.4.2、Core を参照。Docs は参照しない＝ VM は Markdig を使っていない）。`Miharikun.slnx`・テストのプロジェクトに追加。`Services/IUiServices`・`IUiTimer`（7.2）、`Services/ViewList`（7.3。選択中の要素は動かさない）。`AppLog`・`ShellOpen` を Presentation へ移す（中身は変えない。画面のプロジェクトから呼ぶので `public` にする。`ShellOpen.RevealInExplorer` は `Reveal` に名前を変える。`ShellOpen` は 29-4 で mac を足す）。WPF 側に `WpfUiServices`（`DispatcherTimer`・`ClipboardHelper`・`MessageBox`・`ShellOpen` を包む）。この時点では VM はまだ WPF のプロジェクトにある | `ViewListTests`（7.3 の表の行ごと。選択中の要素に Move が出ないことも）。`dotnet test` 全部通る。WPF 版が起動する |
| 28-2 | ViewModel を移す | `ViewModels/*.cs` と `Views/IPreviewHost.cs`（`IPreviewHost`・`PreviewSource`。名前空間を `Miharikun.ViewModels` に）、`HookSetup` を Presentation へ移す。WPF の型を使わない形にする：`MainViewModel.CardsView` → `VisibleCards`（7.3。選択中は動かさない）、`TimelineViewModel.View` → `VisibleItems`（7.3。種別・検索・切り替えは入れ物ごと差し替え）、`DispatcherTimer` → `IUiTimer`（7.4。1 秒の時計は `MainWindow` から `MainViewModel` へ移す）、`ClipboardHelper` → `IUiServices.SetClipboardTextAsync`（7.5）、`ShellOpen` → `IUiServices`、`MemoViewModel.ConfirmDiscard`（`Func<bool>`）→ `IUiServices.ConfirmAsync`（`CancelCommand` を非同期に。7.5）、`HookSetup` の `MessageBox` → `IUiServices`（非同期。owner の引数をなくす）、`AppLog.Write` の直接呼び出し → コンストラクタで受け取る `Action<string>`。`MainViewModel` の git の状態を取る所は差し替えられる形に（`Func<GitStatus?>` など）、`RefreshGit`・`LoadCommits` は Task を返す中身＋イベント用の包みに分ける（7.5）。WPF 側は配線だけ：`MainWindow.xaml` のバインド先（`VisibleCards`・`Timeline.VisibleItems`）、`App.xaml.cs` の組み立て、`MainWindow.xaml.cs`（時計・確認の関数を消す） | 品質ゲート 1〜4。**始める前に、WPF 版で 7.3 の「28-2 の前の記録」を取る**（選択中のカードが隠れたとき・5,000 行の速さ ほか） |
| 28-3 | ViewModel のテスト | `tests/Miharikun.Tests/Presentation/`：`FakeUiServices`（タイマーを手で進める・確認の答えを決めておく・クリップボードの成否）。git の状態は差し替え、UI スレッドへの受け渡しはその場で実行する `SynchronizationContext` にする（7.5）。一覧（絞り込みの組み合わせ・並び・選択の保ち方（並べ替えで選択中の要素が動かない）・隠れたカードを選ぶとフィルタが外れる）、タイムライン（種別・検索・件数の文字・全部コピーの中身・追記で表示中の行が増える・ジャンプでフィルタが ON）、メモ（キャンセルの確認：はい／いいえ・変更なしならすぐ戻る）、Hook の導入の流れ（確認で「いいえ」なら何もしない） | `dotnet test` が増えて全部通る（件数を記録） |

**注意**：28-2 は「移して、型を差し替える」だけ。表示の文言・並び・選択・フォーカス・スクロールの呼び出し（`CardScrollRequested`・`ScrollRequested`）の箇所と順番を変えない。`MainViewModel.Apply` の中の「カードを足す → 一覧を合わせる → 件数 → 最近の入力」の順も同じ。VM のテストは 28-2 の後になる（それまでは WPF のプロジェクトの中にあって、テストから参照できない）。

## 3. Phase 29：Core・テストの mac 対応（mac。Windows は CI）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 29-1 | CI でテスト（最初に作る） | `.github/workflows/test.yml`（新規。push・pull_request・手動）：Windows（`dotnet build Miharikun.slnx` → `dotnet test`）と macOS（プロジェクトを指定してビルド → `dotnet test` → `dotnet publish src/Miharikun.Hook -r osx-arm64` で NativeAOT が通るか）。7.20。このあとの mac の作業で Windows を壊していないかを、これで見る。CI を見る前に、利用者にコミットと push を頼む（1 章） | Windows は緑。mac は 27-1 の一覧と同じテストだけが落ちる（29-2・29-4 で直す） |
| 29-2 | テストの OS 対応 | 始める前に mac でもう一度テストを回し、基準を取り直す（28 で Presentation のテストが増えたため）。「mac で落ちたテスト」を 3 つに分ける（7.15）：Windows の意味を確かめるもの（ドライブ文字・`/c:/` の形・`\` 区切り・`~RF*.TMP`・CP932）→ `[WindowsFact]`／`[WindowsTheory]`（`PerfFact` と同じ作り。mac では飛ぶ）。たまたま `C:\` を使っているだけのもの → `TestPaths`（OS の絶対パスを作る）に直す。**mac で本当に動かないもの**（例：一時フォルダが `/var` → `/private/var` のリンクなので、`GitClientTests` が実パスと論理パスの違いで落ちる）→ テストは変えず、製品のコードを直す（29-4）。mac の形の行を足す（`ProjectPath`：`/Users/x/proj` の比較、`ClaudeFolderName`：`/Users/x/repo` → `-Users-x-repo` ほか、14.3 で分かったもの）→ `[MacFact]` か両 OS | Windows の件数は減らない。mac で残る落ちは「製品のコードを直すもの」だけ（29-4 で通る）。OS ごとの件数（合格・スキップ）を記録 |
| 29-3 | Hook と導入の mac 対応 | `HookInstaller.HookExeName` を OS で（Windows `Miharikun.Hook.exe`、mac `Miharikun.Hook`）。`IsOurs` は拡張子を除いた `Miharikun.Hook` で見る（両 OS の名前に当たる）。Hook に `--probe`（何も読まず・書かず、`ok` を出して 0 で終わる。7.12）。`HookInstaller` の mac の導入・更新は、同じフォルダの一時ファイルにコピーしてから名前を付け替える（7.12）。`HookSetup`：導入の後、mac は印を外し（`/usr/bin/xattr -d com.apple.quarantine`）、両 OS で `--probe` を試す。だめなら `xattr` の 1 行を案内（実際の `.app` の場所・コピーできる形・App Translocation のときの案内。7.12）。Hook の案内の文の mac の言い方（要件 12.11）。27-1 の結果で導入先を変える場合はここ（7.1） | `HookRunnerTests`：`--probe` は 0・`ok`・ファイルを作らない。`HookInstallerTests`：名前（OS）・`IsOurs`（両方の名前・古いパス）・空白を含む導入先の `BuildCommand`・上書きで中身が新しくなる（mac は名前の付け替え）。`HookSetup` の流れ（Fake：probe 失敗で案内が出る・案内の `.app` の場所） |
| 29-4 | git・パス・シェルの mac 対応 | `Core/Git/GitLocator`（7.13。mac で Command Line Tools が無いと `/usr/bin/git` がインストールのダイアログを出すため。開発フォルダの git の実体を直接使う）を `GitClient` と Hook の `GitProbe` で使う。`ProjectPath.Normalize` で NFC にそろえる（7.14）。シンボリックリンク：App は対象フォルダの実パスも持ち（`Core/Projects/RealPath`）、`ProjectPath.Matches` は論理・実のどちらかが合えば一致。`GitClient` は status のパスを、作業フォルダからの相対（`--show-cdup`）で論理のルートに付ける（7.14）。ドキュメントの相対パスを NFC にそろえて比べる（`DocumentIndex`・`GitIgnoreMatcher`・`DocumentLinkRule`。7.14）。`ShellOpen`：mac は `open`・`open -R`（7.2）。ボタンの文字「フォルダで開く／Finder で表示」（7.2）。14.3 で分かったそのほかの違い | `GitLocatorTests`（候補の順・無いとき null。ファイルの有無は差し替えて試す）、`ProjectPathTests`（NFC と NFD が一致・論理と実のどちらでも一致）、`RealPathTests`（mac）、リンク経由のプロジェクトの `GitClientTests`（mac）、NFD の名前のファイルに NFC のリンク（`DocumentIndex`）、`ShellOpen` のコマンドの組み立て（OS ごと） |

完了条件：両 OS の `dotnet test` が通り（件数を記録。29-2 で分けた「製品のコードを直すもの」も通る）、CI が緑。Core は AOT 互換（警告 0）。mac の NativeAOT の Hook が通る。WPF 版は Windows で起動する（CI のビルド＋手元で 1 回）。

## 4. Phase 30・31：画面（Avalonia）

### 4.1 Phase 30：Avalonia でダッシュボード（mac。区切りごとに Windows でも起動）

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 30-1 | WPF を Avalonia に置き換える（土台） | **始める前に**：WPF 版（28 の終わり）を別の場所にビルドして残す（`-p:OutDir=`。30・31 の Windows の確認で見比べる）。**最初に**：WKWebView の読み取り範囲の試作（捨てる小さな画面。7.11 の組み合わせ。27 で済んでいなければ）と、開発用の `.app`（`scripts/publish-mac.sh` の開発用：Info.plist・配置・アドホック署名。7.19）。そのあと `src/Miharikun/` の WPF のファイル（`*.xaml`・`*.xaml.cs`・`Themes/*.xaml`・`Behaviors/`・`AssemblyInfo.cs`・`ClipboardHelper.cs`・`WpfUiServices`・`ThemeService.cs`・`WebViewEnvironment.cs`）を消し（`PreviewFiles.cs` は WPF に依らないので残す）、同じ場所・同じ名前（`Miharikun`）の Avalonia のプロジェクトにする（`net10.0`。7.9。csproj は今の `IncludeNativeLibrariesForSelfExtract`・`SatelliteResourceLanguages`・`DebugType` を引き継ぐ）。`Program.cs`、`App.axaml(.cs)`（組み立て：7.7。ログ・テーマはウィンドウを出す前。`Name="Miharikun"`、mac のアプリのメニューに既定の項目を出さない：要件 12.11）、`AvaloniaUiServices`（7.2）、`Views/MessageDialog`（OK／はい・いいえ。案内の 1 行は選んでコピーできる形）、`ThemeService`（7.8）、`Themes/Colors.Light.axaml`・`Colors.Dark.axaml`（今と同じキー。`ThemeDictionaries` から読む。7.8）、フォント（7.10）、`MainWindow`：共通ヘッダー（タブ 3 つ・対象フォルダ・⚙ メニュー）とタブの枠だけ（中身は重ねて `IsVisible` で切り替える：7.17）、mac の起動時のフォルダ選択（7.7）、閉じるときの流れの骨組み（7.6）、起動時の Hook の確認（7.16） | WKWebView の試作の結果を要件 12.7 に書く（だめなら 7.11 の代わりの形）。両 OS で起動する。mac（開発用の `.app` を Finder から開く）：引数なしでフォルダを選ぶ画面 → キャンセルで終わる・選ぶと開く、`open -n -a` で 2 つ開ける、アプリのメニューに「About Avalonia」が出ない、書類フォルダの中のプロジェクトを開いたときの許可（TCC）の出方と、拒否したときの見え方を記録（要件 8.1）。⚙ のテーマが効く（OS 追従も）。Hook の確認（隔離環境）。日本語の字形（中国語風の字にならない） |
| 30-2 | テーマを見比べて決める・日本語入力 | カード一覧と詳細のヘッダーを FluentTheme で実際に作る（試作で捨てない）。`App.axaml` のテーマの 1 行を `SukiTheme` に差し替えて、同じ画面を SukiUI でも出す（7.8 の「見比べの進め方」）。両 OS・ライト/ダークのスクリーンショットを並べて利用者に見せる。SukiUI は 7.8 の ①〜④ も確かめて、結果を添える。**利用者が決める**。選ばれなかった方のパッケージ・スタイルは外してからコミットする。決めたテーマと結果を要件 12.5 に書く。日本語入力（IME）を、検索・リネーム・メモの入力欄で両 OS で試す（変換中の表示・確定・Enter の扱い）。おかしければ止まって相談 | 利用者の決定。テーマ・SukiUI の ①〜④・IME の結果を 12.5 に記録 |
| 30-3 | 左ペイン | 状態ごとの件数、ステータスのチップ（複数）、エージェントのチップ（1 つ）、検索、フィルタ、カードの一覧（`VisibleCards`・仮想化・選択）、カードの中身（状態の丸・バッジ・各行）、✏️ のリネーム（7.18）、`CardScrollRequested` → `ScrollIntoView`（7.9）。選択の保ち方（7.3）を `tests/Miharikun.UiTests`（Avalonia.Headless。新規）で確かめる：並べ替え・追加・絞り込みで `SelectedItem` が保たれる／隠れたら null。両 OS の CI で回す（7.20） | 品質ゲート 3 の「一覧」。`UiTests` が両 OS で通る |
| 30-4 | 中央ペイン | 詳細のすべてのブロック（ヘッダー・ステータスのボタン・概要・3 行サマリーのジャンプ・完了チェック・稼働状態・成果の展開・ターン一覧） | 品質ゲート 3 の「詳細」 |
| 30-5 | 右ペイン | メモ（フォーカスアウトで保存）、タイムライン（チップ・検索・件数・`VisibleItems`（入れ物ごと差し替え・末尾は追加。7.3）・可変の高さの仮想化・行のクリックで開閉・ダブルクリックでコピーとバブル・全部コピー）、拡大モード（Esc・幅の保存と復元）、最近の入力・最近閉じたセッション、選んだときに末尾へ・ジャンプのスクロール（7.9） | 品質ゲート 3 の「右ペイン」。5,000 イベントのセッション（`ClaudeLogBuilder` で作るダミー）で、スクロール・ジャンプ・種別の ON/OFF・検索がもたつかない（28-2 の前の記録と同じくらい） |
| 30-6 | ⚙ のダイアログ・Hook | 設定…（`AppSettingsDialog`）、Hook の導入・削除の確認とお知らせ（`IUiServices`）、テーマのチェックの表示 | 品質ゲート 3 の「⚙」（ドキュメントの設定…は 31-2） |

完了条件：要件 12.1〜12.5・12.8・12.9・12.11 が両 OS で動く（ライト/ダーク）。品質ゲート 3 の「一覧・詳細・右ペイン・⚙」を Avalonia 版で通す。テーマを 12.5 に書いた。

### 4.2 Phase 31：ドキュメント・メモタブ（mac。区切りごとに Windows でも起動）

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 31-1 | `MarkdownPreview` | 読み取り範囲の試作は 30-1 で済ませた（結果は要件 12.7）。読めなかったときは、7.11 の代わりの形（`loadFileURL:allowingReadAccessToURL:`）を入れる。確かめる：`NavigationStarted` の取り消し・`NewWindowRequested`・`Refresh()` でスクロールが保たれるか、iframe を含む html（mac は iframe にも `NavigationStarted` が来る）。Windows：WebView2 の作業フォルダ（`EnvironmentRequested` の `UserDataFolder`）と、Runtime 未導入の見分け方（`AdapterInfo.Type`）。そのあと `Views/MarkdownPreview`（Avalonia。`IPreviewHost` はそのまま）：7.11 の置き換え表のとおり | 両 OS でドキュメントの md・html が出る。iframe を含む html で、既定のブラウザが勝手に開かない |
| 31-2 | ドキュメントタブ | `Views/DocumentsView`（ツリー：子孫の件数・展開と選択のバインド、一覧、絞り込み、概要カード、ボタン（「フォルダで開く」は mac では「Finder で表示」）、空・走査中の表示）、`Views/DocumentSettingsDialog`。タブの中身は画面に載せたまま `IsVisible` で切り替え、WebView を作り直さない（7.17）。`Start()` はタブが最初に見えたとき | 品質ゲート 3 の「ドキュメントタブ」。タブを行き来しても、プレビューのスクロール位置が保たれる |
| 31-3 | メモタブ | `Views/MemoView`（バー・プレビューと入力欄の入れ替え・等幅フォント・Ctrl/⌘+S。7.18）、`Views/UnsavedMemoDialog`（［保存］［保存しない］［キャンセル］）、閉じるときの流れ（7.6。mac の ⌘Q・Dock の「終了」、OS のサインアウト・ログアウト・シャットダウンも。最小化なら前に出す・2 回目の Closing・ダイアログは Post で出す）、メモのリンク → ドキュメントタブ（7.17）、`FocusEditorRequested` | 品質ゲート 3 の「メモタブ」。⌘Q・Dock の「終了」・ウィンドウの閉じるボタン・Windows の Alt+F4 のそれぞれで、未保存の確認が出る。最小化した状態での ⌘Q・Dock の「終了」でも出る。Windows のサインアウト・mac のログアウトで確認が出て、キャンセルで OS の終了が止まる |
| 31-4 | テーマの追従・仕上げ | テーマの切り替えで md を作り直す、ダークで読み込みの間に白く光らない（7.11）、Windows の Runtime 未導入の案内（`AdapterInfo` で見分ける。コードと文言） | ライト/ダークを両 OS で |

完了条件：要件 12.7・12.10 が両 OS で動く。品質ゲート 3 の「ドキュメントタブ・メモタブ」を Avalonia 版で通す。

## 5. Phase 32・33：配布と仕上げ

### 5.1 Phase 32：配布（32-1 は Windows か CI、32-2 は mac、32-3 は CI）

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 32-1 | Windows の発行を Avalonia に | Windows か CI で行う。`scripts/publish.ps1`：App（Avalonia）の単一ファイル・自己完結（`win-x64`。Avalonia の Skia などのネイティブ DLL も単一ファイルに入れる：30-1 で引き継いだ `IncludeNativeLibrariesForSelfExtract`）。配布 zip の `README.txt` の文を `scripts/dist/README-win.txt` に移す（`{VERSION}` を置き換える）。NativeWebView（WebView2）が単一ファイルで動くか | zip を展開 → 起動 → ドキュメント・メモタブのプレビューが出る |
| 32-2 | mac の発行 | `scripts/publish-mac.sh`（30-1 の開発用を配布用に仕上げる。7.19）：App（`osx-arm64`・自己完結・単一ファイルにしない）と Hook（NativeAOT）→ `Miharikun.app`（`scripts/dist/Info.plist` を元に）→ アドホック署名 → `codesign --verify` → `README.txt`（`scripts/dist/README-mac.txt`。要件 8.1・9 章の手順：印の外し方・`open -n -a Miharikun --args "$(pwd -P)"`・ファイルとフォルダの許可）と一緒に `ditto` で zip → SHA-256 | mac で zip を展開 → `xattr` の 1 行 → 起動 → Hook の導入（印の解除と `--probe`）→ Cursor で会話 → 表示。版を上げて再導入（Hook の更新）→ Cursor で会話。`.app` を「アプリケーション」以外に置いたときの案内 |
| 32-3 | リリースのワークフロー | `.github/workflows/release.yml`：Windows のジョブ（今のもの）と macOS のジョブ（`publish-mac.sh`）。release のジョブは両方の zip と `.sha256` を載せる（Windows の zip にも `.sha256` を付ける） | 手動実行（`workflow_dispatch`）で両方の zip ができる |
| 32-4 | README・リリース手順 | `docs/release.md`（mac のジョブ・出す前のチェックに mac の項目：印の外し方・Hook の導入・フォルダの選択）。配布 zip の README（Windows・mac）を見直す | 手順どおりに試運転できる |

完了条件：手動実行で両 OS の zip ができ、展開 →（mac は印を外す）→ 起動 → Hook の導入 → 会話 → 表示、が両 OS で通る（利用者）。

### 5.2 Phase 33：通し確認・仕上げ（両 OS）

| # | コミット | 内容 |
|---|---|---|
| 33-1 | 仕上げ | 実機での通し確認（Windows：Cursor・Claude Code。mac：Cursor（Hobby）・Claude Code）。`CLAUDE.md` の 1 行目を「Windows・macOS のデスクトップツール（C# / Avalonia）」に、「macOS 対応を進めている」の行を消す。要件定義の `[x]` と「状況」、設計の資料の状態。`Fixes #22` |

## 6. ファイル構成（追加・変更）

```
src/Miharikun.Presentation/                      28-1 新規（net10.0。画面の仕組みを参照しない）
├─ Miharikun.Presentation.csproj                  28-1 新規：CommunityToolkit.Mvvm・Core を参照
├─ Services/IUiServices.cs・IUiTimer.cs           28-1 新規：画面の仕組みへの口（7.2・7.4）
├─ Services/ViewList.cs                           28-1 新規：絞った一覧を要素を使い回して合わせる。選択中は動かさない（7.3）
├─ AppLog.cs・ShellOpen.cs                        28-1 移す（WPF から。public に。RevealInExplorer → Reveal）／29-4 変更：ShellOpen に mac
├─ HookSetup.cs                                   28-2 移す（非同期に）／29-3 変更：印の解除・--probe・案内（.app の場所）
├─ ViewModels/*.cs                                28-2 移す（名前空間はそのまま）：VisibleCards・VisibleItems・IUiTimer・IUiServices・git の差し替え口
└─ ViewModels/IPreviewHost.cs                     28-2 移す（Views/IPreviewHost.cs から。名前空間 Miharikun.ViewModels）

src/Miharikun.Core/
├─ Install/HookInstaller.cs                       29-3 変更：HookExeName を OS で・IsOurs・mac は一時ファイル → 名前の付け替え・文言
├─ Git/GitLocator.cs                              29-4 新規：git の場所（mac の CLT。開発フォルダの git の実体）
├─ Git/GitClient.cs                               29-4 変更：GitLocator を使う・status のパスを作業フォルダ基準（--show-cdup）
├─ Projects/ProjectPath.cs                        29-4 変更：NFC にそろえる・論理と実のどちらでも一致
├─ Projects/RealPath.cs                           29-4 新規：mac の実パス（libc の realpath を LibraryImport。AOT 互換）
└─ Documents/DocumentIndex.cs・GitIgnoreMatcher.cs・DocumentLinkRule.cs   29-4 変更：相対パスを NFC にそろえて比べる

src/Miharikun.Hook/
├─ HookRunner.cs                                  29-3 変更：--probe
└─ GitProbe.cs                                    29-4 変更：GitLocator を使う

src/Miharikun/                                    28-1/28-2 変更（WPF の配線）→ 30-1 Avalonia に置き換え
├─ Miharikun.csproj                               30-1 変更：net10.0・Avalonia（WPF-UI・WebView2 を外す。IncludeNativeLibrariesForSelfExtract などは引き継ぐ）／30-2 SukiUI を選んだら足す／31-1 WebView を足す
├─ Program.cs・App.axaml(.cs)                     30-1 新規：起動と組み立て（7.7。Name="Miharikun"・mac のアプリのメニュー）
├─ AvaloniaUiServices.cs                          30-1 新規（7.2）
├─ ThemeService.cs・Themes/Colors.Light.axaml・Colors.Dark.axaml   30-1 新規（7.8。ThemeDictionaries から読む）
├─ Themes/AppStyles.axaml                         30-2 新規：自前の見た目（どちらのテーマでも使う。7.8）
├─ MainWindow.axaml(.cs)                          30-1 新規：ヘッダー・タブ（中身は重ねて IsVisible）・フォルダ選択・閉じる流れ／30-3〜30-6 ペイン
├─ Views/MessageDialog.axaml(.cs)                 30-1 新規（案内の 1 行はコピーできる形）
├─ Views/AppSettingsDialog.axaml(.cs)             30-6 新規（WPF 版を書き直す）
├─ Views/MarkdownPreview.axaml(.cs)              31-1 新規（WPF 版を書き直す。7.11。PreviewFiles.cs はそのまま使う）
├─ Views/DocumentsView・DocumentSettingsDialog    31-2 新規（WPF 版を書き直す）
├─ Views/MemoView・UnsavedMemoDialog              31-3 新規（WPF 版を書き直す）
└─ （WPF の *.xaml・*.xaml.cs・Themes/*.xaml・Behaviors/・AssemblyInfo.cs・ClipboardHelper.cs・WpfUiServices.cs・WebViewEnvironment.cs）  30-1 削除

tests/Miharikun.Tests/
├─ Miharikun.Tests.csproj                         28-1 変更：Presentation を参照
├─ Presentation/ViewListTests.cs                  28-1 新規
├─ Presentation/FakeUiServices.cs・*ViewModelTests.cs  28-3 新規
├─ OsFactAttributes.cs・TestPaths.cs              29-2 新規：WindowsFact／MacFact・OS の絶対パス
├─ Core/*Tests.cs（mac で落ちたもの）            29-2 変更
├─ Hook/HookRunnerTests.cs・Core/HookInstallerTests.cs  29-3 変更
└─ Core/GitLocatorTests.cs・ProjectPathTests.cs・RealPathTests.cs・GitClientTests.cs・Document*Tests.cs   29-4 新規・変更

tests/Miharikun.UiTests/                          30-3 新規：Avalonia.Headless.XUnit。一覧の選択の保ち方（7.3）

.github/workflows/test.yml                        29-1 新規／30-3 変更：UiTests も回す
.github/workflows/release.yml                     32-3 変更：macOS のジョブ・両方の zip と .sha256
scripts/publish.ps1                               32-1 変更
scripts/publish-mac.sh                            30-1 新規（開発用の .app）／32-2 変更（配布用に仕上げる）
scripts/dist/Info.plist                           30-1 新規（7.19）
scripts/dist/README-win.txt・README-mac.txt       32-1・32-2 新規
docs/release.md                                   32-4 変更
docs/miharikun-requirements.md                    各 Phase の終わりに [x] と「状況」（27-1 は 14.3、30-1 は 12.7（WKWebView の試作）と 8.1（TCC）、30-2 は 12.5）
CLAUDE.md                                         33-1 変更
Miharikun.slnx                                    28-1 変更：Presentation を追加／30-3 変更：UiTests を追加
```

## 7. 実装の決めごと（迷いやすい所）

### 7.1 Phase 27 の結果で決まること
| 確かめること（14.3） | 結果が「はい」 | 結果が「いいえ」のとき |
|---|---|---|
| Hobby で hooks.json の command が呼ばれる | そのまま | mac の Cursor は会話ログの取り込み（要件 11 章）で出す。Hook の mac 対応（29-3）と配布は行い、mac 実機での Hook の確認は「保留」。Issue #17 の「取り込み直し」と合わせて利用者と相談 |
| 空白を含むパス（`~/Library/Application Support/…`）で起動できる | 導入先は今の規則（データの `bin/`） | 導入先を `~/.miharikun/bin/Miharikun.Hook` にする（mac だけ。29-3。要件 8 章に書く） |
| `workspace_roots` が `/Users/…` の形 | `ProjectPath` はそのまま | 見つかった形を `ProjectPath.Normalize` で直す（29-4。テスト） |
| 日本語の入力が壊れない | `PayloadRecovery` は mac では何もしない | 壊れ方を調べて相談（推測で直さない） |
| 「隔離」の印の付いた Hook を Cursor が呼ぶと止まる | 導入時の印の解除と `--probe` が必須（7.12。予定どおり） | 印の解除は念のため残す |
| Cursor の会話ログが `~/.cursor/projects/<slug>/agent-transcripts/`、slug は Windows と同じ規則 | `CursorTranscriptImporter` はそのまま | 規則を合わせる（29-4。テスト） |
| Claude Code のフォルダ名が `-Users-x-repo` の形 | `ClaudeFolderName` はそのまま | 規則を合わせる。合わなくても、候補 0 件のときの `cwd` での探索（要件 9.1）で見つかる |
| `xcode-select -p` が通る（Command Line Tools がある） | `GitLocator` は開発フォルダの git の実体を使える（7.13） | 7.13 のとおり（git は「不明」） |
| 日本語を含むフォルダ・ファイルがある（Finder で作った名前が NFD か） | NFC と NFD の違いを 1 件確かめる。ドキュメントの相対パスも NFC で比べる（7.14。予定どおり） | 保留（7.14 の NFC はそのまま入れる。害は無い） |
| シンボリックリンクを経由したフォルダで、Claude の `cwd`・Cursor の `workspace_roots`・git のルートが実パスで来る | 実パスとも比べる（7.14。予定どおり） | 実パスとも比べる作りは残す（害は無い）。来た形を記録する |

### 7.2 `IUiServices`（画面の仕組みへの口）
| メンバー | 役目 | WPF（28） | Avalonia（30） | テスト（Fake） |
|---|---|---|---|---|
| `IUiTimer CreateTimer(TimeSpan interval, Action tick)` | UI スレッドで定期的に呼ぶ | `DispatcherTimer` | `Avalonia.Threading.DispatcherTimer` | 手で `Fire()` |
| `Task<bool> SetClipboardTextAsync(string text)` | クリップボードへ | `ClipboardHelper.TrySetText`（今のやり直しのまま） | `TopLevel.Clipboard.SetTextAsync`（例外は false） | 成否を決めておける |
| `void OpenWithDefaultApp(string target)` | URL・ファイル・フォルダを既定のアプリで | `ShellOpen.Open` | `ShellOpen.Open` | 呼ばれた値を記録 |
| `void RevealInFileManager(string file)` | ファイルを選んだ状態で開く | `ShellOpen.Reveal`（`explorer.exe /select,`） | `ShellOpen.Reveal`（mac は `open -R`） | 記録 |
| `string RevealButtonText` | 「フォルダで開く」ボタンの文字（要件 12.11） | 「フォルダで開く」 | Windows「フォルダで開く」、mac「Finder で表示」 | 固定 |
| `Task<bool> ConfirmAsync(string title, string message)` | はい／いいえ | `MessageBox`（オーナーはメインウィンドウ） | `MessageDialog` | 答えを決めておける |
| `Task ShowMessageAsync(string title, string message, MessageKind kind)` | お知らせ（情報・注意） | `MessageBox` | `MessageDialog` | 記録 |

- `ShellOpen`（Presentation。画面のプロジェクトから呼ぶので `public`）：`Open(target)` は今のまま `UseShellExecute = true`（.NET は mac で `open` を使う。29-4 で mac 実機で確認）。`Reveal(file)`（今の `RevealInExplorer` の名前を変える）：Windows `explorer.exe /select,<file>`、mac `open -R <file>`（`ArgumentList` に渡す）。失敗はログだけ（今と同じ）。
- 3 択のダイアログ（未保存で閉じる）と、設定ダイアログ 2 つは、画面の側（`MainWindow`）が直接出す（今と同じ。VM を通さない）。
- オーナー（どのウィンドウの上に出すか）は `IUiServices` の実装が持つ（メインウィンドウ）。VM はウィンドウを知らない。

### 7.3 絞った一覧（`ICollectionView` の代わり）
`ViewList.SyncTo(ObservableCollection<T> target, IReadOnlyList<T> desired, T? pinned = default)`：`target` を `desired` と同じ並びにする。**要素は同じインスタンスを使い回し、`Reset` を出さない**（`Clear` → `Add` で作り直すと、`ListBox` が選択を外し、双方向のバインドで `Selected` が null になるため。WPF も Avalonia も同じ）。**`pinned`（選択中の要素）は `Move` しない**：WPF の `ListBox` は `Move` しても選択を保つが、Avalonia（12.1.3）は `Move` を「取り除く＋足す」として扱い、動かした要素が選択中なら選択を外す（`SelectionNodeBase` の `Move` の扱い。未解決の不具合報告 #16279・#17819）。選んでいる（見ている）セッションほど動きが来て上へ移るので、そのままだと Avalonia 版では、そのたびに詳細とタイムラインが空になる（レビュー #1・Q1）。

| 操作 | 結果（テストの 1 行） |
|---|---|
| 初期（`target` 空） | `desired` の順に `Insert` |
| 絞り込みで 1 件が外れる | その 1 件だけ `Remove`。ほかの要素は同じインスタンス・同じ順 |
| 絞り込みを解いて 1 件が戻る | 正しい位置に `Insert` |
| 並びが変わる（ほかのカードが上へ来た） | 動いたカードを `Move`（`Remove`＋`Insert` にしない） |
| 並びが変わる（選択中＝`pinned` のカードが上へ移る） | `pinned` は動かさず、間にあるほかの要素を `pinned` の後ろへ `Move` して、同じ並びにする。結果の並びは `desired` と同じ。`pinned` には `Move` も `Remove` も出ない |
| 変化なし | 通知を出さない |
| 全部入れ替わる | `Remove` と `Insert` だけで合わせる（`Reset` を出さない。`pinned` が残るなら動かさない） |
| `pinned` が `desired` に無い（隠れた） | `pinned` を `Remove`（今と同じく選択が外れる） |

- `MainViewModel.VisibleCards`：`Cards` を `Visible(c)` で絞り、`LastActivityAt` の新しい順（同じ時刻は `Cards` に入った順。LINQ の `OrderByDescending` は安定。今の `ListCollectionView` の並べ替えは安定でないので、同じ時刻のときだけ並びが変わることがある：実害は無い）に並べたものを、`pinned = Selected` で `SyncTo` する。今 `CardsView.Refresh()` を呼んでいる箇所（11 か所）で同じように呼ぶ。
- **保険**：`SyncTo` の間（`_syncingCards`）に `ListBox` から来た `Selected = null` は無視し、終わったら選び直す（`OnPropertyChanged(nameof(Selected))`）。`DocumentsViewModel` の `_syncing` と同じ考え方。`[ObservableProperty]` のままでは null を止められないので、`Selected` は手書きのプロパティにする。
- **確かめ方**：`ViewListTests`（28-1。表の行ごと。`pinned` に `Move`・`Remove` が出ないこと）と、Avalonia の `ListBox` を画面なしで動かす `tests/Miharikun.UiTests`（30-3。Avalonia.Headless。並べ替え・追加・絞り込みで `SelectedItem` が保たれる／隠れたら null）。WPF（Phase 28）は `Move` でも選択を保つので、`pinned` があっても無くても今と同じ動きになる（28 の品質ゲートで確かめる）。
- **選択中のカードが隠れたとき**：`VisibleCards` から消える → `ListBox` が選択を外し、`Selected = null`（入力途中のメモを保存・タイムラインを空・検索語を消す・拡大を戻す・詳細を空：`MainViewModel.OnSelectedChanged`）。今の WPF も同じ（`ICollectionView.Refresh()` の Reset で、WPF の `ListBox` は無くなった選択を外す。WPF のソースで確認）。最近の入力・閉じたセッションのクリック（`SelectSession`）は、隠れていればフィルタを外す（今のまま）。
- **28-2 の前の記録**（WPF 版・隔離環境。結果を「変えない」と「変わってよい（良い方向）」に分けて書き、品質ゲートで照らす）：
  - 変えない：選択中のカードが隠れるきっかけごとの動き（絞り込み・検索／「実行中のみ」で 1 秒ごとの更新で停止に変わる／「未コミットあり」で 5 秒ごとの git の後にコミット済みになる／「メモあり」でメモを消す／拡大中に隠れる → 拡大も戻る）、並べ替えで選択が保たれること、5,000 行のタイムラインで種別の ON/OFF・検索の 1 文字・セッションの切り替えにかかる時間。
  - 変わってよい（良い方向）：カードの ✏️ で入力中に 6 秒待つ（今は 5 秒ごとの `CardsView.Refresh()` の Reset で行が作り直され、入力欄が確定される可能性がある。`SyncTo` は変化が無ければ通知しない）、タイムラインに 31 件以上が一度に増えたときのスクロール（今は入れ物ごと差し替えで飛ぶ）、行の「コピーしました」の消え方（7.4）。
- `TimelineViewModel.VisibleItems`：末尾の追記（`SetEvents` で末尾に足した行）は、表示の条件に合えば `Add`。それ以外（種別・検索の変更・セッションの切り替え・30 件超の差し替え）は、**入れ物ごと差し替える**（新しい `ObservableCollection` を作ってプロパティを変える。通知 1 回。今の `View.Refresh()`・`Replace` と同じ重さ）。タイムラインは選択を使わない（行の強調は `IsHighlighted`）ので困らない。`SyncTo`（1 件ずつの通知）を使うと、5,000 行で数千回の通知になって重い（レビュー #7）。`CountText` と「全部コピー」は `VisibleItems` から作る。

### 7.4 タイマー
| どこ | 間隔 | いま | 後 |
|---|---|---|---|
| 時計（◯分前・停止の表示・git の見直し） | 1 秒 | `MainWindow` の `DispatcherTimer` → `MainViewModel.Tick()` | `MainViewModel` が `IUiTimer` を持つ（`StartClock()`。組み立ての最後に呼ぶ。`Dispose` で止める）。画面の側に時計を持たない |
| ドキュメントの作り直し | 200ms | `DocumentsViewModel` の `DispatcherTimer`（Background） | `IUiTimer`。Avalonia は `DispatcherPriority.Background` |
| メモの読み直し | 300ms | `MemoViewModel` の `DispatcherTimer` | `IUiTimer` |
| 行の「コピーしました」 | 1.5 秒 | **行ごとに** `DispatcherTimer` を作っている（5,000 行なら 5,000 個） | **行ごとに作らない**。`TimelineViewModel` が 1 つのタイマーで「最後にコピーした行」を戻す。行の VM は作るときに `TimelineViewModel` のコピーの処理（`Func<TimelineItemViewModel, Task>`）を受け取り、`CopyCommand` はそれを呼ぶ。別の行をコピーしたら、前の行の「コピーしました」はすぐ戻る（「コピーしました」の間はマウスが無くてもバブルが出るので、1.5 秒以内に別の行をコピーすると、前の行の表示が早く戻る。ほぼ変わらない。28-2 の前の記録に入れる：7.3） |
| 「全部コピー」の「コピーしました」 | 1.5 秒 | `DispatcherTimer` | `IUiTimer` |

- 優先度：今のタイマーはどれも Background（WPF の `DispatcherTimer()` の既定。ドキュメントは明示）。Avalonia の `DispatcherTimer()` の既定も Background なので、`CreateTimer` は Background で作る（優先度の引数は要らない）。

### 7.5 非同期になるもの（Avalonia のダイアログとクリップボードは待つ形）
- メモのキャンセル：`CancelCommand` を `AsyncRelayCommand` に。確認の間に押し直されても 2 回目は動かない（`AsyncRelayCommand` の既定）。**確認の答えが返ってから、もう一度 `IsEditing`・`IsDirty` を見る**（確認中に状態が変わっていたら何もしない）。
- 行のコピー・全部コピー：`SetClipboardTextAsync` が true のときだけ「コピーしました」にする（今と同じ）。
- Hook の導入：`CheckAtStartupAsync`・`InstallFromMenuAsync`・`UninstallFromMenuAsync`。起動時の確認は、ウィンドウが出てから（7.16）。
- `async void` はイベントから呼ぶ所だけ（例外をログに残す小さな包み。今の `MarkdownPreview.OnPreviewChanged` と同じ `async void`＋try/catch の書き方）。今 `async void` の `MainViewModel.RefreshGit`・`LoadCommits` は、「Task を返す中身」と「イベント・時計から呼ぶ包み」に分け、テストは中身を await する。git の状態を取る所は差し替えられる形（`Func<GitStatus?>` など）にし、テストで本物の git を起動しない。テストの UI スレッドへの受け渡しは、その場で実行する `SynchronizationContext` にする（28-2・28-3）。

### 7.6 閉じるときの流れ（Avalonia の `Closing` は待てない）
1. `Window.Closing(e)`：`_closeConfirmed` なら何もしない（閉じる）。フォルダを選ぶ前（組み立ての前。7.7）も何もしない。
2. `_viewModel.Flush()`（セッションのメモ。今と同じ。入力欄は入力のたびに VM に入るので、⌘Q のようにフォーカスが動かない閉じ方でも最新が保存される）。
3. プロジェクトのメモが未保存でなければ、そのまま閉じる。
4. 未保存なら、**先に** `e.Cancel = true`。最小化していれば戻して `Activate()`（ダイアログが見えないまま、終了が黙って取り消されたように見えるのを防ぐ）。ダイアログは `Closing` の中で待たず、`Dispatcher.UIThread.Post` で出す（mac の終了の処理の最中に、入れ子の画面を作らない）：メモタブを選ぶ → `UnsavedMemoDialog`：
   - ［保存］→ `TrySaveForClose()` が true なら `_closeConfirmed = true` → `Close()`。false なら閉じない（理由はバーに出る）。
   - ［保存しない］→ `_closeConfirmed = true` → `Close()`。
   - ［キャンセル］・ダイアログを閉じた → 何もしない（閉じない）。
5. ダイアログを出している間（`_closing`）や、ほかの確認（メモの破棄の確認・設定・Hook のダイアログ）を出している間に、もう一度閉じようとしたら、`e.Cancel = true` だけにする（2 つ目のダイアログは出さない）。
6. mac の ⌘Q・Dock の「終了」：アプリ全体の終了の要求 → ウィンドウの `Closing` に来る。`Closing` で取り消すと終了も取り消される（Avalonia のソースで確認：`ClassicDesktopStyleApplicationLifetime` の `DoShutdown` は、取り消した窓が残れば終了をやめる。mac 側は `ShutdownReplyCancel`）。**31-3 で、⌘Q・Dock の「終了」・閉じるボタン・Windows の Alt+F4 のそれぞれで確かめる**。閉じ直しは `Close()`。`Shutdown()` は取り消しを無視する（`force`）ので、閉じる流れでは使わない。
7. OS のサインアウト・ログアウト・シャットダウン（レビュー Q2＝B）：Avalonia は Windows の `WM_QUERYENDSESSION` と mac のログアウトでも終了の要求を出し、`Closing` に来る。**いつもと同じ確認を出し、OS の終了を止める**（Windows は「このアプリがサインアウトを妨げています」の画面になり、mac はログアウトが取り消される）。特別なコードは書かない（Avalonia の既定のまま）。WPF の「既知の制限（確認なしに失われる）」はなくなる（要件 12.10）。**31-3 で、Windows のサインアウトと mac のログアウトを確かめる**。
8. 片付け（今の `App.OnExit`）：`MainViewModel`・`DocumentsViewModel`・`MemoViewModel`・`SessionMonitor` を Dispose（アプリの終了のとき 1 回）。
9. Info.plist に `NSSupportsSuddenTermination` を入れない（入れると、確認なしに終わることがある。7.19）。

### 7.7 起動と組み立て（mac のフォルダ選択）
1. 引数から `-psn_` で始まるもの（古い macOS が Finder から渡すことがある）を除く。
2. 引数があれば、その 1 つ目をフルパスにして対象フォルダにする（今と同じ。在るかは確かめない）。mac は実パス（realpath）も求めて持つ（7.14）。
3. 引数が無いとき：
   - Windows：カレントディレクトリ（今と同じ）。
   - mac：先にメインウィンドウを出す（タイトル「Miharikun」、中身は「対象のフォルダを選んでください」だけ）→ `StorageProvider.OpenFolderPickerAsync`（1 つだけ・題「Miharikun で開くフォルダ」）→ キャンセル・空なら終了（`desktop.Shutdown()` でよい。守るものが無い）→ 選んだら `TryGetLocalPath()` を対象フォルダにする。
4. 組み立ては 2 つに分ける。**フォルダに依らないもの**（ログ → 未処理の例外のログ → 設定 → テーマ）は、ウィンドウを出す前に済ませる（選ぶ前の例外もログに残り、空のウィンドウが保存したテーマで出る。今の `App.OnStartup` もログ・テーマを窓より先に作る）。**フォルダに依るもの**（Source のリスト → Store → Monitor → メタ → `MainViewModel` → `HookSetup` → WebView の作業フォルダ → 一時 HTML の掃除 → `DocumentsViewModel` → `MemoViewModel`。順は今と同じ）は `AppComposition`（App の中）にまとめ、フォルダが決まってから呼ぶ。そのあと各 VM を画面に渡し、`_monitor.Start()` → `RefreshGit()` → `StartClock()` → 起動時の Hook の確認（7.16）。`MainWindow` は「選ぶ前の中身」と「組み立て後の中身」の 2 段にする（Windows も同じ道を通り、選ぶ画面を出さないだけ）。
5. タイトルバーに対象フォルダ（今と同じ「Miharikun - <フォルダ>」）。

### 7.8 テーマと色
- `AppTheme.System` → `Application.RequestedThemeVariant = ThemeVariant.Default`（OS に追従。実行中の変更にも）、`Light` → `ThemeVariant.Light`、`Dark` → `ThemeVariant.Dark`。保存は今と同じ（`settings.json` の `theme`）。
- `IsDark` は `ActualThemeVariant == ThemeVariant.Dark`。`ActualThemeVariantChanged` で `Changed` を出し、ドキュメント・メモの md を作り直す（今と同じ。1 回の変更で 1 回だけ）。
- 色：要件 12.5 のとおり、ライト用・ダーク用のファイルに分ける（`Themes/Colors.Light.axaml`・`Themes/Colors.Dark.axaml`。今の `Colors.Light.xaml`・`Colors.Dark.xaml` と同じキー：`StateRunningBrush` ほか）。`App.axaml` の `ResourceDictionary.ThemeDictionaries`（`Light`・`Dark`）から `ResourceInclude` で読む。画面は `DynamicResource`（今と同じ）。辞書の差し替えのコード（今の `SetColors`）は要らなくなる。
- 背景（12.5）：Windows は `TransparencyLevelHint = Mica`、mac は `Blur`、`Background` は透明。効かなければ単色（Avalonia 12 の不具合 #21082 はタイトルバーの拡張と組み合わせたとき。下のとおり拡張しないので当たらない見込み）。
- **タイトルバーは両 OS とも OS 標準**（中身をタイトルバーに広げない）。WPF-UI の独自のタイトルバーはやめる。Windows は OS 標準で閉じる・最小化が右（要件 12.11 の「今に近い形」）。

**見比べの進め方（30-2。要件 12.5）**
- テーマを 1 行で差し替えられるように作る：`App.axaml` の `Application.Styles` の先頭はテーマ（`FluentTheme` か `SukiTheme`）だけ。自前の見た目は `Themes/AppStyles.axaml`（新規）、色は `Themes/Colors.Light.axaml`・`Colors.Dark.axaml`、フォントは 7.10 にまとめ、テーマの後ろに置いて上書きする。どちらのテーマでも同じファイルで動くようにする。
- 見比べる画面：カード一覧（数枚・状態の違うもの）と詳細のヘッダー（ステータスのボタン・バッジ）、右ペインの見出し（340px でチップ 5 つと検索ボックス）。両 OS・ライト/ダーク。隔離環境のダミーのデータだけを使う。
- スクリーンショットは 2 つのテーマを並べて利用者に見せる。決まるまで、ほかのペイン（30-3〜）に進まない。

**SukiUI を選んだときの決めごと**
- `App.axaml` の `Application.Styles` に `SukiTheme` を入れる（FluentTheme の代わり）。色の辞書（`Themes/Colors.Light.axaml`・`Colors.Dark.axaml`）・`Themes/AppStyles.axaml`・フォント（7.10）は、その後ろに置いて上書きする。
- ウィンドウは**ふつうの `Window`**（OS 標準のタイトルバー）。SukiUI 専用のウィンドウ（独自のタイトルバー）は使わない（要件 12.11：mac は閉じる・最小化が左の OS 標準）。
- ライト/ダーク：上の `RequestedThemeVariant` で切り替える。SukiUI の切り替えの仕組み（基本の色）が `ThemeVariant` に連動するか、`ThemeVariant.Default` で OS の変更に追従するかを確かめる。連動しなければ、`ThemeService` で SukiUI 側の切り替えも呼ぶ。
- **SukiUI のウィンドウの中に重ねて出すダイアログ・通知（トースト）は使わない**。WebView（ドキュメント・メモ）は OS の部品なので、ウィンドウの中に重ねたものはその下に隠れる（要件 12.7）。確認・お知らせは今までどおり別のウィンドウ（`MessageDialog`・`UnsavedMemoDialog`・設定ダイアログ）。
- 背景のアニメーション（SukiUI の動く背景）は使わない（常に開いておく道具なので、CPU を使わない）。部品の動き（押したとき・開閉）は SukiUI の既定のまま。
- 自前の見た目（状態の丸・エージェントとステータスのバッジ・チップ・カードの枠・タイムラインの行）は今の色の辞書のキーを使って作る。SukiUI の既定のスタイルとぶつかる所（`ListBoxItem` の選択の色・`ToggleButton` の形など）は、セレクタで上書きする。

| SukiUI で確かめること（30-2。見比べの材料） | 見る所 | だめなとき（利用者に添える） |
|---|---|---|
| ① ふつうの `Window` で崩れない | 背景・余白・角・影 | SukiUI 専用のウィンドウが要るなら、要件 12.11 と合わないことを添える |
| ② 情報の多い 3 ペインに収まる | カードの行数・詳細の詰まり具合。**右ペインが 340px で、種別のチップ 5 つと検索ボックスが 1 行**（要件 12.4） | 余白・文字の大きさをセレクタで詰めてから見せる。詰められなければ、そのことを添える |
| ③ 日本語が OS のフォントで出る | SukiUI が独自のフォントを指定していないか。漢字の字形（7.10） | アプリ全体のフォントの指定で上書きする |
| ④ 「OS に合わせる」で、実行中の OS のライト/ダークの変更に追従する | 両 OS で、アプリを開いたまま OS の設定を変える | `ThemeService` で SukiUI 側も切り替える。それでもだめなら、そのことを添える |

### 7.9 WPF → Avalonia の書き換え表
| WPF（今） | Avalonia（後） | 気をつけること |
|---|---|---|
| `Style.Triggers` の `DataTrigger`（30 か所）・`MultiDataTrigger` | スタイルのクラス（`Classes.running="{Binding IsRunning}"`）＋セレクタ、または変換（converter） | 状態の丸・バッジ・チップの色の出し分けを取りこぼさない。**状態ごとの見た目の表**（状態 × ライト/ダーク）を作って見比べる |
| `Trigger IsMouseOver` | `:pointerover` | |
| `BooleanToVisibilityConverter`・`InverseBooleanToVisibilityConverter` | `IsVisible="{Binding X}"`・`IsVisible="{Binding !X}"` | |
| チップの `ControlTemplate`（`ListBoxItem`・`CheckBox`） | `ToggleButton`・`ListBoxItem` のスタイル（`:checked`・`:selected`） | 件数つきの文字（`Label`）はそのまま |
| `MouseBinding LeftClick`／`LeftDoubleClick`（タイムラインの行） | `Tapped`／`DoubleTapped`（コードビハインドからコマンド） | 1 回目のクリックで開閉、2 回目でコピー（コピー側で開閉を戻す）。**今と同じ順で来るか両 OS で確かめる** |
| `KeyBinding`（Enter・Esc・Ctrl+S） | `KeyBindings`。Ctrl/⌘ は 7.18 | |
| 添付プロパティ `FocusWhenVisible`・`LostFocusCommand` | 同じ名前の添付プロパティを Avalonia で作る | 表示された直後のフォーカスは `Dispatcher.UIThread.Post`（`Input` 相当）で |
| `ListBox`（`VirtualizingPanel.IsVirtualizing`） | `ListBox`（既定で仮想化） | 可変の高さ。`ScrollIntoView` は並びが変わった後に `Post`（今の `BeginInvoke(Background)` と同じ）。並べ替え（`Move`）で選択が外れる不具合があるので、カードの一覧は 7.3 の `pinned` で合わせる |
| `TreeView`＋`HierarchicalDataTemplate`＋`ItemContainerStyle`（`IsExpanded`・`IsSelected`） | `TreeView`＋`TreeDataTemplate`（`Children`）＋`TreeViewItem` のスタイルで `IsExpanded`・`IsSelected` を双方向にバインド | 展開と選択を VM が持つ（今と同じ） |
| `GridSplitter`・`Expander`・`TabControl` | 同名（`TabControl` は見出しだけに使い、タブの中身は 1 つの `Grid` に重ねて `IsVisible` で切り替える：7.17） | 拡大モードの幅の保存と復元は今の `ApplyTimelineExpanded` と同じ |
| ⚙ の `ContextMenu` | `Button.Flyout`（`MenuFlyout`）。テーマは `ToggleType="Radio"` の `IsChecked` | |
| `ui:TextBox` の案内文・クリアの ✕（`ClearButtonEnabled`。一覧とタイムラインの検索） | `TextBox` の Watermark（12 で名前が変わっていれば、その名前）。✕ は FluentTheme の `TextBox` の `clearButton` のクラス（SukiUI は 30-2 で確かめる） | 要件 12.4 の「クリア（✕）を添える」を取りこぼさない |
| `AutomationProperties.AutomationId` | 同じ | Windows の UI Automation での確認に使う。今の ID を残す |
| `ToolTip` | `ToolTip.Tip` | |

### 7.10 フォント（日本語の字形）
- 文字：Windows `Yu Gothic UI, Meiryo UI, Segoe UI`、mac `Hiragino Sans, Hiragino Kaku Gothic ProN`。等幅：Windows `Consolas, BIZ UDGothic, MS Gothic`、mac `Menlo, Osaka-Mono, Hiragino Sans`。
- アプリ全体（`Window` のスタイル）で、OS ごとに指定する（Avalonia の `OnPlatform`）。指定しないと、日本語の漢字が中国語の字形のフォントで描かれることがある（**30-1 で両 OS の字形を確かめる**）。
- 絵文字（📝 🔧 💬 📌 📄）：Avalonia はカラーで出る見込み。状態は今と同じ色つきの丸で示す（絵文字に頼らない）。

### 7.11 プレビュー（WebView2 → NativeWebView）
| 今（WPF・WebView2） | 後（NativeWebView） | 確かめること（30-1 の試作・31-1） |
|---|---|---|
| `WebViewEnvironment`（環境を 1 つ共用・作業フォルダ `webview2\`） | Windows：`EnvironmentRequested` で作業フォルダを `webview2\` に。mac：設定なし | 引数の型と、2 つの WebView（ドキュメント・メモ）で同じ作業フォルダを使えるか |
| `IsRuntimeInstalled`（`GetAvailableBrowserVersionString`） | Windows だけ：`NativeWebView.AdapterInfo.Type` が WebView2 でなければ案内を出す（Avalonia.Controls.WebView 12.1.0 は、Runtime が無いと WebView1（EdgeHTML）に切り替えようとするので、作れたかどうかでは見分けられない）。それで足りなければ `Microsoft.Web.WebView2` を Windows のときだけ使って今の方法で（`OperatingSystem.IsWindows()`）。mac は確かめない | `AdapterInfo` の値の見方（Runtime 入りの実機では値を見るだけ） |
| `DefaultBackgroundColor`（ダークで白く光らない） | `NativeWebView.Background`（作られたときに adapter の既定の背景に渡る）。足りなければ、読み込みが終わる（`NavigationCompleted`）まで案内の領域を出しておく | ダークで白く光らないか |
| `EnsureCoreWebView2Async`（画面に載ってから） | 作られる前に `Navigate` してよい（NativeWebView が最後のページを覚え、作られたら開く） | |
| `NavigationStarting`（`e.Cancel`） | `NavigationStarted`（`e.Cancel`・`e.Request`） | 自分で開いたページ（と `#` の移動）は通す（今の `_pagePath` の比較と同じ）。mac は iframe の読み込みにも `NavigationStarted` が来る（一番上のページかを見ていない）→ iframe を含む html で、既定のブラウザが勝手に開かないか（だめなら相談。例：自分で開いたページの読み込みが終わるまでの http(s) は通す） |
| `NewWindowRequested`（`e.Handled`） | `NewWindowRequested` | `target="_blank"` と `window.open` |
| `CoreWebView2.Reload()`（スクロールを保つ） | `Refresh()` | スクロールが保たれるか |
| `Navigate(file:///…)` | `Navigate(new Uri(file:///…))`（mac は `loadRequest` で開く。WebKit はファイルのとき、まずファイル全体を読める許可を作る。Miharikun はサンドボックスに入らないので、読める見込み） | **30-1 の試作で**：mac の WKWebView で、一時 HTML から `<base>` 先の画像・css を読めるか。html を直接開いたときの相対の css/js/画像（`../`・絶対パスも）。書類フォルダの中のプロジェクト（TCC）でも |

- **mac で読めなかったときの代わり**（レビュー Q4＝A）：`TryGetPlatformHandle()` で WKWebView を取り、`loadFileURL:allowingReadAccessToURL:` を objc_msgSend で呼んで開く（読める範囲＝一時 HTML とプロジェクトの共通の親。例：ホーム）。見え方・リンクの振り分けは今と同じ。それでもだめなら相談（推測で進めない）。
  - 前の案（`NavigateToString`＋独自のスキーム＋`WebResourceRequested`）は使わない。Avalonia.Controls.WebView 12.1.0 では作れないため：`WebResourceRequestedEventArgs` は `Request` だけで中身を返す口が無い／mac では移動の判断の中でしか出ず、画像・css の読み込みには来ない／mac の `EnvironmentRequested` に独自のスキームを登録する口が無い／Windows の `NavigateToString` は `baseUri` を捨てる。作れたとしても、対象フォルダの外（`../`・絶対パス）が読めず、要件 12.7 とずれる。
- **タブを切り替えても WebView を壊さない**（レビュー Q3＝A）：`BeginReparenting` を使わずに画面から外すと、NativeWebView は中の WebView を壊し、戻ると作り直して読み直す（スクロールが先頭へ・重い・白く光る恐れ）。タブの中身は画面に載せたまま `IsVisible` で切り替える（見えないときは隠すだけで壊さない。7.17）。
- 2 つ目の WebView（メモ）は、メモタブを最初に表示したときに作る（今と同じ）。

### 7.12 Hook の導入（mac の印の解除と、試しの起動）
1. `installer.Install()`（Core。今と同じ：Hook をデータの `bin/` にコピーし、hooks.json に登録）。mac のコピー（版を上げたときの更新も）は、同じフォルダの一時ファイルに写してから `File.Move(tmp, dest, overwrite: true)` で名前を付け替える（新しいファイルになる。署名つきの実行ファイルを同じ場所で上書きすると、次の起動で止められることがあるため。Cursor が Hook を動かしている最中の書き換えも避けられる）。Windows は今のまま（使用中のやり直しあり）。
2. mac だけ：`/usr/bin/xattr -d com.apple.quarantine <導入した Hook>` を実行（印が無いときの失敗は成功とみなす。結果はログ）。
3. 両 OS：`<導入した Hook> --probe` を実行（3 秒で打ち切り）。終了コード 0 で `ok` が出れば成功。
4. 3 が失敗したら：お知らせ「Hook を起動できませんでした。ターミナルで次の 1 行を実行してから、もう一度 ⚙ の『Hook を導入』を選んでください。」＋ `xattr -dr com.apple.quarantine "<いま開いている .app の場所>"`（mac。`Environment.ProcessPath` から `.app` の場所を求める。場所に `/AppTranslocation/` を含むときは、先に「Miharikun.app を『アプリケーション』に移してから開き直してください」と添える）。1 行は選んでコピーできる形（`SelectableTextBlock` か「コピー」ボタン）。Windows は「セキュリティソフトなどで止められていないか確かめてください」。登録は戻さない（次の導入で直る）。
5. `--probe`（`HookRunner`）：引数に `--probe` があれば、標準入力を読まず、データのフォルダにも触らず、`ok` を出して 0 を返す。`--agent` より先に見る。
6. 起動時の確認（`CheckAtStartupAsync`）では `--probe` を走らせない（起動を遅くしない）。導入・再導入のときだけ。
7. 文言：今の「Miharikun.exe と同じフォルダ」「Hook exe」は、mac では「Miharikun.app の中」「Hook」と言う（例「Miharikun.app の中に Hook が見つかりません」。要件 12.11）。`HookInstaller` と `HookSetup` の文を OS で替える。

### 7.13 git の場所（mac の Command Line Tools）
- mac で Command Line Tools も Xcode も無いと、`/usr/bin/git` は「コマンドライン・デベロッパツールをインストールしますか」のダイアログを出す。App は 5 秒ごとに git を呼ぶので、ダイアログが出続ける。Hook も `sessionStart`・`stop` で呼ぶ。
- `GitLocator.Find()`（Core。結果はプロセスの中で覚える）：
  - Windows：`"git"`（今と同じ。PATH から）。
  - mac：①PATH の中の `git` のうち `/usr/bin/git` 以外 ②`/opt/homebrew/bin/git` ③`/usr/local/bin/git` ④開発フォルダの git の実体（`/var/db/xcode_select_link` のリンク先の `usr/bin/git`、`/Library/Developer/CommandLineTools/usr/bin/git`、`/Applications/Xcode.app/Contents/Developer/usr/bin/git`）の順で、在る（ファイルが在る）最初のもの。無ければ null。`/usr/bin/git`（中継）は使わない：フォルダがあっても中が使えない（macOS を上げた後など）とインストールのダイアログを出すことがあるため。実体を直接使うので少し速い。
  - 結果はプロセスの中で覚えるので、起動中に Command Line Tools を入れたときは再起動が要る。
  - null のとき、`GitClient` と `GitProbe` は git が無いときと同じ（「不明」・`git` を省略）。
- Finder や Dock から開いたアプリの PATH は短い（`/usr/bin:/bin:/usr/sbin:/sbin`）ので、②③ を直接見る。
- Hook の速さ（要件 7 章：git なし 50ms）を守るため、プロセスは起動しない（ファイルの有無だけ見る）。

### 7.14 パスの比較（mac）
- mac のファイル名は、日本語が NFD（濁点が分かれた形）で返ることがある。`ProjectPath.Normalize` の最後で NFC にそろえる（`string.Normalize(NormalizationForm.FormC)`）。Windows では変わらない。
- Hook は `InvariantGlobalization` なので、`Normalize` は何もしない（元の文字列を返す）。Hook は git の場所に使うだけなので害は無い。NFC で比べるのは App の中だけ。
- `AppPaths` のプロジェクトのファイル名（`{slug}-{hash8}`）は `ProjectPath.Normalize` の結果から作るので、同じフォルダなら同じ名前になる。
- `ClaudeFolderName` は今のまま（Claude Code の規則に合わせているだけ）。合わなくても、候補 0 件のときの `cwd` での探索で見つかる（要件 9.1）。
- 大文字小文字は今のまま無視する（mac の既定のファイルシステムも区別しない。区別するボリュームは対象外：要件 9 章）。
- **シンボリックリンク**（レビュー Q5＝A）：mac は `/tmp` → `/private/tmp`、`/var` → `/private/var`（一時フォルダ）など、リンクが多い。Claude Code の `cwd` と git のルート（`rev-parse --show-toplevel`）は実パスで来る。ターミナルの `$PWD` は論理パスになりうる。
  - App は対象フォルダの実パスも持つ（`Core/Projects/RealPath`：libc の `realpath` を `LibraryImport`。AOT 互換。mac だけ。Windows は今のまま）。`ProjectPath.Matches` は、論理パス・実パスのどちらかが一致すれば対象。
  - `GitClient.GetStatus` は、status のパスを `rev-parse --show-toplevel`（実パス）でなく、作業フォルダからの相対（`git rev-parse --show-cdup`）で論理のルートに付ける（`Uncommitted.Files` が対象フォルダ（論理）で比べるため）。
  - README のターミナルの開き方は `open -n -a Miharikun --args "$(pwd -P)"`（要件 9 章）。
  - テスト：mac は一時フォルダ自体が `/var` → `/private/var` のリンクなので、今の `GitClientTests` が落ちる見込み。テストは変えずに、この直しで通す（7.15）。
- **ドキュメントの相対パス**：索引のキー（`DocumentIndex`）・リンク（`DocumentLinkRule`・`OpenLocalLink`）・最後のファイル（`lastOpened`）・除外の判定（`GitIgnoreMatcher`）は、NFC にそろえて比べる（Finder など mac のアプリが作る日本語の名前は NFD のことがあり、md に打ったリンクは NFC。形が違うと、リンクがアプリ内で開けず既定のアプリで開いてしまう）。ファイルを開くときは、APFS が形の違いを無視するので、そろえた名前のままで開ける見込み（29-4 で確かめる）。

### 7.15 テストの OS の扱い
- `WindowsFactAttribute`・`WindowsTheoryAttribute`・`MacFactAttribute`・`MacTheoryAttribute`（`tests/Miharikun.Tests/OsFactAttributes.cs`）：`PerfFactAttribute` と同じ作り（コンストラクタで、その OS でなければ `Skip` を入れる）。
- 分け方：
  - Windows の意味を確かめるテスト（ドライブ文字・`/c:/`・`\` 区切り・`~RF*.TMP`・CP932 の修復・`explorer.exe`）→ `Windows…`。消さない。
  - たまたま `C:\` を使っているだけのテスト → `TestPaths.Abs("work", "proj")`（Windows `C:\work\proj`、mac `/work/proj`）か一時フォルダに直して、両 OS で動かす。
  - **mac で本当に動かないテスト**（製品のコードが mac で正しくない。例：一時フォルダが `/var` → `/private/var` のリンクなので、`GitClientTests` が実パスと論理パスの違いで落ちる）→ テストは変えず、製品のコードを直す（29-4。7.14）。`Windows…` に入れて隠さない。
  - mac の形を確かめるテスト（`/Users/x/…`・`open -R`・`GitLocator` の mac の順）→ `Mac…` か、OS を引数で渡せる形にして両 OS で動かす。
- 件数：各 Phase の報告に、Windows と mac それぞれの「合格・スキップ」を書く。Windows の合格は減らさない。
- 基準：29-2 の始めに、mac でもう一度テストを回して基準を取り直す（28 で Presentation のテストが増え、27-1 の一覧には入っていないため）。
- 画面の部品の動き（一覧の選択の保ち方）は `tests/Miharikun.UiTests`（Avalonia.Headless。30-3）で確かめ、両 OS の CI で回す（7.20）。件数は `Miharikun.Tests`（842 件の基準）とは別に数える。

### 7.16 起動時の Hook の確認
- 今：`MainWindow.Loaded` の後、`ApplicationIdle` で `CheckAtStartup`。
- 後：`MainWindow.Opened` の後、`Dispatcher.UIThread.Post(…, DispatcherPriority.Background)` で `CheckAtStartupAsync`。mac で引数が無いときは、フォルダを選んで組み立てが終わってから。

### 7.17 タブの始まりとメモのリンク
- タブの中身（ダッシュボード・ドキュメント・メモ）は、3 つとも画面に載せたまま、`IsVisible` で切り替える（`TabControl` は見出しだけに使う。レビュー Q3＝A）。Avalonia の `TabControl` は選んだタブの中身だけを画面に載せるので、そのまま使うと、外れたタブの WebView が壊され、戻ると作り直される（7.11）。
- ドキュメント・メモのタブは、最初に表示されたとき（そのタブが選ばれたとき）に `Start()`（起動を遅くしない。今と同じ。2 回目以降は VM の中で何もしない）。中身は起動時から載っているので、`AttachedToVisualTree` では呼ばない。WebView も、今と同じく最初のプレビューのときに作る。
- メモのリンク → ドキュメントタブ：タブを選んでから、`Dispatcher.UIThread.Post(() => documents.OpenFromOutside(rel), DispatcherPriority.Loaded)`（今の「切り替えてから選ぶ」と同じ順。`DocumentsViewModel.OpenFromOutside` の 4 分岐はそのまま）。

### 7.18 キー操作
- 保存（メモ）：Avalonia の `PlatformHotkeyConfiguration.CommandModifiers`（Windows は Ctrl、mac は ⌘）＋ S。編集中だけ効く（今と同じ `CanExecute`）。
- リネーム：Enter で確定、Esc で取り消し、フォーカスアウトで確定（今と同じ）。
- 拡大モード：Esc で戻す（入力欄で先に Esc を使ったときは除く。今と同じ）。
- メモの入力欄：Tab はフォーカス移動（タブ文字を入れない。今と同じ）。改行は OS の改行が入る（`MemoEditor` は改行の違いを変更とみなさない。今と同じ）。

### 7.19 mac の `.app` と zip（30-1 の開発用・32-2 の配布用）
```
Miharikun-v{版}-osx-arm64.zip
└─ Miharikun-v{版}-osx-arm64/
   ├─ Miharikun.app/Contents/
   │  ├─ Info.plist     CFBundleExecutable=Miharikun、CFBundleIdentifier=io.github.po-oq.miharikun、
   │  │                 CFBundleName=Miharikun、CFBundlePackageType=APPL、CFBundleShortVersionString・CFBundleVersion={版}、
   │  │                 LSMinimumSystemVersion=（.NET 10 の対応範囲に合わせる。要確認）、NSHighResolutionCapable=true
   │  ├─ MacOS/         Miharikun（App）・*.dylib・*.dll（自己完結）・Miharikun.Hook（NativeAOT）
   │  └─ Resources/     （アイコンは今は無い）
   └─ README.txt        scripts/dist/README-mac.txt（{VERSION} を置き換え）
```
- 手順：`dotnet publish src/Miharikun -c Release -r osx-arm64 --self-contained -p:Version={版}`（単一ファイルにしない）→ `dotnet publish src/Miharikun.Hook -c Release -r osx-arm64 -p:Version={版}` → 上の形に並べる → `codesign --force --deep --sign - Miharikun.app` → `codesign --verify --deep --strict Miharikun.app` → `ditto -c -k --keepParent <フォルダ> <zip>` → `shasum -a 256 <zip> > <zip>.sha256`。
- 同梱の Hook は App と同じ `Contents/MacOS/`（`Environment.ProcessPath` のフォルダ）なので、`HookInstaller.CreateDefault(paths, appDir)` の「App と同じフォルダ」の規則はそのまま使える。
- 開発用の `.app`（30-1）も同じ形で作る（`scripts/publish-mac.sh` の開発用。版は `0.0.0-dev`。Hook も入れる）。mac の画面の確認は、これを Finder から開いて行う（`dotnet run` では、ファイルとフォルダの許可・メニュー・Dock の動きが違う）。
- 署名はアドホックだけ。`--options runtime`（ハードンドランタイム）は付けない（付けると、.NET の JIT に権限（entitlements）が要る）。Info.plist に `NSSupportsSuddenTermination` を入れない（7.6）。
- README（mac）：印の外し方（要件 8.1）、ターミナルからの開き方 `open -n -a Miharikun --args "$(pwd -P)"`（要件 9 章）、ファイルとフォルダの許可（書類フォルダなどの中のプロジェクトは、許可を求められたら「許可」。版を入れ替えると、もう一度聞かれることがある。要件 8.1）。

### 7.20 CI と画面の確認
- `test.yml`（29-1）：`on: push・pull_request・workflow_dispatch`。ジョブ 2 つ：`windows-latest`（`dotnet build Miharikun.slnx` → `dotnet test`）、`macos-latest`（Core・Docs・Hook・Presentation・テストを指定してビルド → `dotnet test` → Hook の `osx-arm64` の NativeAOT の発行）。Phase 30 で WPF が消えたら、mac もソリューション全体をビルドする形にする。30-3 から `tests/Miharikun.UiTests`（Avalonia.Headless）も両方で回す。リポジトリは公開なので、macOS のランナーも無料の範囲。CI は push で動くので、見る前に利用者にコミットと push を頼む（1 章）。
- 画面の確認：Windows は、これまでの UI Automation（`AutomationId`）と DevTools プロトコル（WebView2）。Avalonia も UI Automation に対応している（ID は 7.9 のとおり残す）。DevTools プロトコルは、`EnvironmentRequested` の `AdditionalBrowserArguments`（`--remote-debugging-port`）で開く。mac は利用者の目視を基本にする（自動の操作・撮影のために、システムの権限を変えない）。開発用の `.app` を Finder から開いて見る（7.19）。

## 8. リスクと対策

| リスク | 対策 |
|---|---|
| ViewModel の切り出しで、WPF 版の挙動が変わる | Phase 28 を単独で終わらせ、品質ゲートを通す。文言・並び・選択・スクロールの呼び出しを変えない。選択中のカードが隠れたときの動きなどは、変える前に記録する（7.3 の「28-2 の前の記録」） |
| 一覧を作り直す・並べ替えるたびに選択が外れる（Avalonia は `Move` でも外す：#16279・#17819） | `Reset` を出さず、選択中は動かさない `ViewList.SyncTo`＋ VM の保険（7.3）。`ViewListTests`（行ごと）と `UiTests`（Avalonia.Headless）で確かめる |
| mac の Cursor（Hobby）で Hook が呼ばれない | Phase 27 で最初に確かめる。だめなら会話ログの取り込みで出す（7.1） |
| 空白を含むパスで Hook が起動しない | Phase 27 で確かめ、だめなら導入先を `~/.miharikun/bin/` に（7.1） |
| 署名なしの Hook が止められ、記録が黙って残らない | 導入時に印を外し、`--probe` で確かめ、だめなら 1 行を案内（7.12） |
| mac で git がインストールのダイアログを出し続ける | `GitLocator`（7.13） |
| mac の WKWebView でプロジェクトの画像などを読めない | 30-1 で試作（前倒し）。`loadRequest` で読める見込み。だめなら `loadFileURL:allowingReadAccessToURL:`（7.11）。独自のスキームは今の部品では作れない |
| Avalonia の `Closing` が待てず、未保存のメモを失う | 先に取り消してからダイアログ（Post で出す）→ 決まったら閉じ直す（7.6）。最小化中は前に出す。2 回目の Closing は取り消すだけ。OS のサインアウト・ログアウトでも同じ確認（Q2）。⌘Q・Dock・Alt+F4・サインアウト・ログアウトで確かめる |
| 日本語入力（IME）がおかしい | 30-2 で先に試す。おかしければ止まって相談 |
| 日本語が中国語の字形で出る | OS ごとにフォントを指定（7.10）。30-1 で確かめる |
| `DataTrigger` の書き換えで色の出し分けを取りこぼす | 状態ごとの見た目の表を作り、両 OS・ライト/ダークで見比べる（7.9） |
| ダブルクリックでコピーするときの開閉の順が変わる | `Tapped`／`DoubleTapped` の順を両 OS で確かめる（7.9） |
| タイムラインの行ごとのタイマーが重い（5,000 個） | コピーしたときだけ 1 つ（7.4） |
| mac で開発すると Windows を壊しても気づかない | `test.yml` の Windows のジョブ（29-1。最初に作る）。30・31 の区切りごとに Windows で起動する |
| テーマの見比べで作業が膨らむ | 30-2 は 1 画面だけ。テーマは 1 行で差し替えられる作りにする（7.8）。決まるまで次のペインに進まず、決めたら迷わない。配置・項目は変えない |
| SukiUI が情報の多い画面に合わない（余白が広い・340px にチップと検索が収まらない） | 7.8 の ①〜④ を確かめて、見比べの材料として利用者に添える |
| SukiUI のウィンドウの中に重ねるダイアログ・通知が WebView の下に隠れる | 使わない。確認・お知らせは別のウィンドウ（7.8） |
| SukiUI の背景のアニメーションで CPU を使い続ける | 使わない（7.8） |
| Avalonia 12 が新しく、不具合に当たる | 12 系の最新の修正版。難しい所（WebView・仮想化した一覧のジャンプ・IME・閉じる流れ）を先に試す。分かっているもの：並べ替えで選択が外れる（7.3）、WebView の口の不足（7.11） |
| mac の「隔離」の印の手順を利用者が間違える | README（7.19）に 2 つのやり方と SHA-256 の確かめ方。Hook の導入の失敗時に 1 行を画面で案内（実際の `.app` の場所・コピーできる形） |
| タブを切り替えるたびに WebView が作り直される（スクロールが先頭へ・重い・白く光る） | タブの中身は載せたまま `IsVisible` で切り替える（7.17） |
| mac のシンボリックリンクで、Claude のセッションが出ない・未コミットが 0 件に見える | 実パスとも比べる・git は作業フォルダ基準（7.14）。mac で落ちるテストを `Windows…` で隠さない（7.15） |
| 書類フォルダなどの許可（TCC）で読めない・版ごとに聞き直される | 30-1 で開発用の `.app` で確かめる。README に書く（7.19）。読めないときに理由が出るか見る |
| mac で Hook を上書きすると、次の起動で止められる | 一時ファイルに写して名前を付け替える（7.12） |
| タイムラインの絞り込みで、1 件ずつの通知が数千回になる | 入れ物ごと差し替える（7.3） |
| Windows で Runtime が無いと WebView1 に切り替わり、案内が出ない | `AdapterInfo.Type` で見分ける（7.11） |
| CI・OS の行き来に push が要り、確かめないまま進む | 区切りで止まって、利用者にコミットと push を頼む（1 章） |

## 9. 決定事項（確認済み。設計の質問 Q1〜Q7）
1. Q1：画面は Avalonia 12 に作り直し、Windows と mac で 1 つにする。
2. Q2：一気に切り替える。1 つの作業ブランチで作り、main に入るのは Avalonia 版だけ（WPF と Avalonia を main で混ぜない）。
3. Q3：mac は Apple の署名・公証をせずに配る（アドホック署名。利用者が初回に「隔離」の印を外す。要件 8.1）。
4. Q4：対象は Apple Silicon（`osx-arm64`）だけ。
5. Q5：mac で Cursor（Hobby）と Claude Code を使う。Windows の Hobby では Hook が動いている。mac は Phase 27 で確かめる。
6. Q6：mac で引数が無いときは、起動時にフォルダを選ぶ画面を出す。ターミナルからは `open -n -a Miharikun --args "$(pwd -P)"`（レビューで `$PWD` から変更）。
7. Q7：見た目のテーマは、**FluentTheme と SukiUI の 2 つで試作し、見比べて利用者が決める**（2026-10-06。30-2。配置・項目は変えない）。

計画のレビュー（`macos-support-review.html`）の決定（2026-10-06。すべて推奨どおり）：
1. Q1：並べ替えても選択を外さない。`ViewList.SyncTo` は選択中の要素を動かさず、周りを動かして並べる。VM の保険（同期中に来た null は無視して選び直す）も入れる。Avalonia.Headless のテストで確かめる（7.3。要件 12.2）。
2. Q2：OS のサインアウト・ログアウト・シャットダウンでも、未保存のメモの確認を出す（Avalonia の既定のまま。OS の終了は止まる。7.6。要件 12.10）。
3. Q3：タブの中身は載せたまま `IsVisible` で切り替え、WebView を作り直さない（7.17。要件 12.7）。
4. Q4：WKWebView で読めなければ、`TryGetPlatformHandle()` と `loadFileURL:allowingReadAccessToURL:` で読める範囲を指定して開く。独自のスキームは使わない（7.11。要件 12.7）。
5. Q5：mac では対象フォルダの実パスも持ち、論理・実のどちらかが合えば一致。git は作業フォルダ基準（`--show-cdup`）。README は `"$(pwd -P)"`（7.14。要件 9 章）。

計画で決めたこと（要件にない細部）：
- 新しいプロジェクトの名前は `Miharikun.Presentation`。ViewModel の名前空間は `Miharikun.ViewModels` のまま。`IPreviewHost`・`PreviewSource` も `Miharikun.ViewModels` へ。
- 画面の仕組みへの口は `IUiServices` 1 つ（7.2）。3 択のダイアログと設定ダイアログは画面の側が出す。
- `HookSetup`・`AppLog`・`ShellOpen` を Presentation へ移す。`HookSetup` は非同期に。
- 1 秒の時計は `MainViewModel` が持つ（画面の側に持たない）。行の「コピーしました」のタイマーは 1 つ。
- 絞った一覧は `Reset` を出さずに合わせ、選択中の要素は動かさない（`ViewList.SyncTo`）。タイムラインは、末尾の追記は追加、それ以外は入れ物ごと差し替える。
- `MainViewModel` の git の状態を取る所は差し替えられる形に。`RefreshGit`・`LoadCommits` は Task を返す中身＋包み。
- タイトルバーは両 OS とも OS 標準。色はテーマの辞書（`ThemeDictionaries` から `Colors.Light.axaml`・`Colors.Dark.axaml` を読む）。フォントは OS ごとに指定。
- 組み立ては、フォルダに依らないもの（ログ・テーマ）をウィンドウの前に、フォルダに依るものを選んだ後に。
- 開発用の `.app` を 30-1 で作り、mac の確認はそれを Finder から開いて行う。WKWebView の試作も 30-1 に前倒し。
- Windows の Runtime 未導入は `NativeWebView.AdapterInfo` で見分ける。
- テーマは `App.axaml` の 1 行で差し替えられる作りにする（自前の見た目は `Themes/AppStyles.axaml`）。SukiUI を選んだときは、ふつうの `Window` で使い、SukiUI のウィンドウの中に重ねるダイアログ・通知と、背景のアニメーションは使わない（7.8）。
- Hook に `--probe` を足し、導入のときは両 OS で試す。mac は導入時に印を外す（`/usr/bin/xattr`）。mac の Hook のコピーは、一時ファイル → 名前の付け替え。
- mac の git は `GitLocator` で探す（開発フォルダの git の実体を直接使う）。パスは NFC にそろえて比べる（プロジェクトのパスとドキュメントの相対パス）。
- テストは `WindowsFact`／`MacFact` と `TestPaths` で OS に分ける（消さない）。mac で本当に動かないものは、テストを飛ばさず製品のコードを直す。
- CI に `test.yml`（push・PR・手動。Windows と macOS）を、Phase 29 の最初に足す。画面の部品の動きは `tests/Miharikun.UiTests`（Avalonia.Headless）。
- mac の App は単一ファイルにしない（`.app` の中に並べる）。バンドル ID は `io.github.po-oq.miharikun`。zip には `.app` と `README.txt` を入れ、両 OS の zip に `.sha256` を付ける。配布の README の文は `scripts/dist/` に置く。
- Phase 27 で空白を含むパスがだめなら、mac の Hook の導入先は `~/.miharikun/bin/`。
- Phase 28 は Windows で行う（WPF で確かめる最後の機会）。Phase 29〜32 は mac が中心（32-1 は Windows か CI）。OS を移る前・CI を見る前に、利用者にコミットと push を頼む。

## 10. 実装するセッションへの注意（必ず読む）

### 10.1 始める前に
- **この計画・`macos-support-design.html`・`macos-support-review.html`・要件定義の変更・`CLAUDE.md` が見えることを確かめる**。mac で始めるなら、Windows でコミットして push してから、mac で pull する（未追跡のファイルは mac に無い。OS を移るたびに同じ：1 章の区切り）。`HANDOFF.md` は git に入っていない（ずっと守る決まりは `CLAUDE.md` の「作業の決まり」にある）。見つからなければ、推測で進めずに利用者に聞く。
- 計画と要件定義に食い違いが無いか確かめる。あれば要件定義が正。判断がつかなければ聞く。
- `CLAUDE.md` の「作業の決まり」と「守ること」を読む（コミットは頼まれたときだけ・`git add` はパス指定・完了報告にビルドの有無と出力先・本物の環境に触らない・`~/.claude/` は読むだけ・会話ログは構造だけ数える）。
- **mac で作業するとき**：
  - Phase 30-1 までは、`dotnet build Miharikun.slnx` は WPF のプロジェクトのせいで失敗する。Core・Docs・Hook・Presentation・テストを指定してビルドする（例：`dotnet test tests/Miharikun.Tests`）。
  - Phase 29-2 までは、mac で落ちるテストがある（27-1 の一覧。29-2 の始めに取り直す）。**新しく壊したのか、元から落ちているのかを、その一覧で見分ける**。
  - mac の画面の確認は、開発用の `.app`（30-1）を Finder から開いて行う（`dotnet run` では、ファイルとフォルダの許可・メニュー・Dock の動きが違う）。
  - Windows 版の Hook（NativeAOT・`win-x64`）は mac では作れない。CI か Windows で。
  - `~/.cursor/hooks.json` を書き換えるのは、利用者の了承の後だけ（バックアップしてから）。
  - ユーザー単位のスキル（superpowers など）が mac に入っていなければ、利用者に入れてもらう。プロジェクトのスキル（`peraichi`・`impl-plan`）はリポジトリにある。
- **Windows で作業するとき**：アプリの出力先は Phase 30 までは `src/Miharikun/bin/Debug/net10.0-windows/`、30 からは `src/Miharikun/bin/Debug/net10.0/`。`dotnet build`／`test` の前にアプリを止める。

### 10.2 各 Phase の終わり
- 1 章の表の OS で確かめ、要件定義の `[x]` と「状況」を更新する（27-1 は 14.3、30-1 は 12.7（WKWebView の試作）と 8.1（TCC）、30-2 は 12.5 にも）。
- 報告には、ビルドの有無と出力先（OS ごと）、テスト件数（Windows・mac それぞれの合格・スキップ）、できなかった確認を書く。**次の Phase に進む前に止まって報告する**。
- レビューを挟む所（利用者が望めば）：Phase 28 の後（切り出しの差分と品質ゲートの結果）、30-1 の後（WKWebView の試作の結果と、代わりの形を使うか・TCC の出方）、30-2 の後（テーマの決定・IME の結果）。

**状態：レビュー済み**（`macos-support-review.html`。2026-10-06。指摘 #1〜#19 と決定 Q1〜Q5 を反映。要件定義も反映済み）。Phase 27 から始められる。
