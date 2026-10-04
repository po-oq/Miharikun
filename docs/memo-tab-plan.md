# メモタブ 実装計画（Issue #14）

> **連動ルール**：この md と `memo-tab-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `miharikun-requirements.md`（2・4・6・12.1・**12.10**・16 章・Phase 23〜26）。設計イメージは `memo-tab-design.html`。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（7 章）**。
> 進め方は他の Phase と同じ：Core（テスト先行）→ ViewModel → 画面。各 Phase の終わりに隔離環境（`MIHARIKUN_DATA_DIR`）で確認し、要件定義の `[x]` と「状況」を更新して、ビルドの有無と出力先を報告する。コミットは頼まれたときだけ（ここは区切りの目安）。途中のコミットは `Refs #14`、最後だけ `Fixes #14`。

## 1. 全体像

```
Phase 23 Core ──▶ 24 プレビューの共通化 ──▶ 25 メモタブの画面 ──▶ 26 仕上げ
  3 コミット         1〜2 コミット（挙動不変）     2 コミット              1 コミット
```

- 新しい依存パッケージは無い（md→HTML は既存の `Miharikun.Docs.MarkdownRenderer`、書き込みは既存の `AtomicFile`）。
- Core は AOT 互換のまま（リフレクションを使わない）。Hook exe には触らない。
- **Phase 24 は挙動を変えない共通化**。ドキュメントタブの見た目・リンク・自動再読み込みが変わらないことが完了条件（2 章の品質ゲート）。
- いまのテスト：792 件合格 + スキップ 4（`PerfFact`）。**件数は減らさない**。

### 品質ゲート（Phase 24 の完了条件）
1. `dotnet test` が全部通る（件数を減らさない）。
2. `dotnet build src/Miharikun.Core --no-incremental` が警告 0。
3. 隔離環境のドキュメントタブで、Phase 16 で確かめた項目が従来どおり：md（チェックボックス・日本語の見出し id・mermaid・コードの色付け・相対パスの画像）、`#` リンクのページ内移動、md→md と html→md のリンク（アプリ内で選択）、html の相対 css・js・画像、ファイル保存での自動再読み込み、タブを切り替えて戻っても表示が戻る、テーマの切り替えで md が作り直される、ダーク。
4. 見る方法は Phase 16 と同じ（DevTools プロトコル `--remote-debugging-port` か UI Automation。スクリーンショットは隔離環境のダミーのデータだけ）。

## 2. Phase 23：Core（テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 23-1 | `ProjectMemoStore` | `AppPaths.ProjectMemoFile(projectPath)`（`ProjectSettingsFile` と同じ `{slug}-{hash8}` に `.memo.md`。名前を作る処理は共通にする）。`ProjectMemoStore(AppPaths, log)`：`Load(projectPath) → MemoFile(Text, LastWriteTime?)`（無い→ 空・null。`FileShare.ReadWrite \| Delete` で読む。BOM があれば外す）、`Save(projectPath, text)`（`AtomicFile.WriteAllText`。フォルダが無ければ作る。失敗は例外のまま呼び手へ） | 一時フォルダ：パス（設定の json と同じ接頭辞・大文字小文字・末尾区切り）・無いとき・書いて読む・上書き・BOM なしで書く・BOM 付きのファイルも読める・空文字の保存・同じフォルダの設定の json を消さない／変えない・更新日時 |
| 23-2 | `MemoEditor`（状態） | 画面に依存しない、編集の状態の部品（7.1）。`Saved`・`IsEditing`・`Draft`・`IsDirty`・`BeginEdit()`・`Saved(text)`（保存できた）・`Discard()`・`OnExternalChange(text)` → 「表示を作り直すか」を返す | 7.1 の表の行ごと：編集の始め・変更の有無・保存・破棄・編集中の外の変更（下書きを変えない・破棄後は新しい内容）・プレビュー中の外の変更（同じ内容なら作り直さない）・改行コードの違い（7.1） |
| 23-3 | `DocumentLinkRule` | プレビューのリンク（ローカルファイル）を、ドキュメントタブで開けるかの判定（7.3）。`TryGetInAppPath(root, fullPath, matcher, out relativePath)` | 対象フォルダ内の md/html/htm（大文字小文字）・対象フォルダ外・`..` を含む・別ドライブ・除外フォルダ内（親の除外も）・除外されたファイル・md/html 以外・フォルダ・`/` 区切りに直す |

完了条件：テストが通る。Core は AOT 互換（警告 0）。

## 3. Phase 24：md プレビューの共通化（挙動は変えない）

```
前：DocumentPreview（UserControl） ──▶ DocumentsViewModel 専用（PreviewTarget・OpenLocalLink・PreviewDir・IsDark）

後：MarkdownPreview（UserControl） ──▶ IPreviewHost（interface）
                                         ├─ DocumentsViewModel（PreviewSource.File を出す）
                                         └─ MemoViewModel（Phase 25。PreviewSource.Markdown / Message を出す）
    WebView2 の環境は WebViewEnvironment で 1 つを共用
```

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 24-1 | 共通の部品に切り出す | `Views/MarkdownPreview`（今の `DocumentPreview` を改名して一般化。7.2）。`IPreviewHost`（`PreviewDir`・`IsDark`・`Log`・`OpenLocalLink`・`PreviewChanged`）と `PreviewSource`（`File` / `Markdown` / `Message`）。`WebViewEnvironment`（`CoreWebView2Environment` を 1 回だけ作って共用。7.2）。`DocumentsViewModel` は `IPreviewHost` を実装し、`PreviewTarget` → `PreviewSource.File` に変えて出す（中身の処理は変えない）。`DocumentsView.xaml` は `MarkdownPreview` を使う | 品質ゲート 1〜4 |

**注意**：ドキュメントの選択・保存・テーマ変更・再読み込みで `PreviewChanged` を出す箇所と順番は、変えない。`reload=true` のときの `Reload()`（スクロール位置を保つ）も同じ。

## 4. Phase 25：メモタブの画面

| # | コミット | 内容 | 確認（隔離環境） |
|---|---|---|---|
| 25-1 | 表示・編集・保存・キャンセル | `ViewModels/MemoViewModel`（`IPreviewHost` を実装。中身は `MemoEditor` と `ProjectMemoStore`）。`Views/MemoView`（上のバー＋ `MarkdownPreview` ＋ `TextBox`。7.4）。`MainWindow.xaml` のメモタブのプレースホルダーを `MemoView` に差し替え、`App.xaml.cs` で組み立てる。タブが最初に表示されたときに読み込む（`DocumentsView` と同じ）。テーマの変更で作り直す。空のときの案内。保存（ボタン・Ctrl+S）・失敗したら理由を出して編集のまま・キャンセル（変更があれば確認） | 書く→保存→プレビューに出る・再起動後も残る・キャンセル（変更なし→すぐ戻る／あり→確認→破棄）・Ctrl+S・タブを移って戻っても入力が残る・空の案内・保存先のファイルが `projects\` にでき、設定の json はそのまま |
| 25-2 | 閉じるときの確認・リンク・外の変更 | `MainWindow.Closing`：未保存なら確認（7.5）。リンク：`DocumentLinkRule` で判定し、ドキュメントタブに切り替えて選ぶ（`DocumentsViewModel.OpenFromOutside`。7.3）。それ以外は今と同じ振り分け。メモのファイルの監視（7.6）：プレビュー中は読み直し、編集中は取り込まない | 未保存で閉じる（保存／保存しない／キャンセル）・保存に失敗したら閉じない（ファイルを読み取り専用にして試す）・メモのリンクからドキュメントタブへ（タブを一度も開いていないとき・開いたあと）・除外フォルダ内の md は既定のアプリ・http は既定ブラウザ（コードのみでも可）・別のエディタで `.memo.md` を書き換える → プレビュー中は変わる・編集中は変わらない |

完了条件：12.10 の各動作を隔離環境で確かめる。ライト/ダークの両方。ドキュメントタブの品質ゲート 3 をもう一度（共通化の後で壊れていないか）。

## 5. Phase 26：仕上げ

| # | コミット | 内容 |
|---|---|---|
| 26-1 | 仕上げ | 配布 zip の `README.txt`（`scripts/publish.ps1`）と `docs/release.md` のチェックにメモタブを足す。要件定義の `[x]` と「状況」。`CLAUDE.md` は変えない（守ることに変化が無い）。`Fixes #14` |

## 6. ファイル構成（追加・変更）

```
src/Miharikun.Core/
├─ Storage/AppPaths.cs                 23-1 変更：ProjectMemoFile（名前の作り方を ProjectSettingsFile と共通に）
├─ Memo/ProjectMemoStore.cs            23-1 新規：読む・書く・更新日時
├─ Memo/MemoEditor.cs                  23-2 新規：編集の状態（画面に依存しない）
└─ Documents/DocumentLinkRule.cs       23-3 新規：リンク → ドキュメントタブで開けるか

src/Miharikun/
├─ WebViewEnvironment.cs               24-1 新規：CoreWebView2Environment を 1 つだけ作って共用
├─ Views/MarkdownPreview.xaml(.cs)     24-1 新規（DocumentPreview を改名・一般化。DocumentPreview は消す）
├─ Views/IPreviewHost.cs               24-1 新規：IPreviewHost・PreviewSource
├─ Views/DocumentsView.xaml            24-1 変更：MarkdownPreview を使う
├─ ViewModels/DocumentsViewModel.cs    24-1 変更：IPreviewHost／25-2 変更：OpenFromOutside
├─ ViewModels/MemoViewModel.cs         25-1 新規
├─ Views/MemoView.xaml(.cs)            25-1 新規
├─ MainWindow.xaml(.cs)                25-1 変更：メモタブ・タブの名前（切り替え用）／25-2 変更：閉じるときの確認・ドキュメントタブへの切り替え
└─ App.xaml.cs                         25-1 変更：MemoViewModel の組み立て

scripts/publish.ps1（README.txt）、docs/release.md   26-1 変更

tests/Miharikun.Tests/Core/
├─ ProjectMemoStoreTests.cs            23-1 新規
├─ MemoEditorTests.cs                  23-2 新規
└─ DocumentLinkRuleTests.cs            23-3 新規
```

## 7. 実装の決めごと（迷いやすい所）

### 7.1 編集の状態（`MemoEditor`）
| 操作 | 結果 |
|---|---|
| 初期 | `Saved` ＝ 読み込んだ内容、`IsEditing = false` |
| `BeginEdit()` | `IsEditing = true`、`Draft = Saved`、編集の始めの内容 `EditBase = Saved` を覚える |
| `Draft` を変える | `IsDirty = Draft != EditBase`（**改行は `\r\n` と `\n` を同じとみなして比べる**。WPF の TextBox は Enter で `\r\n` を入れ、読み込んだ `\n` はそのまま残すため、混ざっても「変更あり」にしない） |
| 保存できた `Saved(text)` | `Saved = text`、`IsEditing = false` |
| 保存できなかった | 何も変えない（編集のまま。VM が理由を出す） |
| `Discard()` | `IsEditing = false`、`Draft` を捨てる（表示は `Saved`） |
| `OnExternalChange(text)` プレビュー中 | `Saved = text`。前と同じ内容なら「作り直さない」を返す（自分の保存で監視が反応したとき） |
| `OnExternalChange(text)` 編集中 | `Saved = text` だけ（`Draft`・`EditBase` は変えない）。破棄すると新しい内容が出る。保存すると後勝ち |

保存は文字をそのまま書く（改行コードを直さない）。

### 7.2 プレビューの共通化
- `PreviewSource`：
  - `File(FullPath, Kind)`：ドキュメント。html はそのまま、md は読んで HTML に（`<base>` は md のフォルダ）。今の `PreviewTarget` の処理と同じ。
  - `Markdown(Text, BaseFolder, CacheKey, Title)`：メモ。`MarkdownRenderer.Render(Text, BaseFolder, isDark, Title)` → `PreviewFiles.Write(previewDir, CacheKey, html)`。**`BaseFolder` は対象フォルダ**、`CacheKey` はメモのファイルのパス（一時 HTML の名前がドキュメントの md と重ならない）。
  - `Message(Text)`：WebView2 を出さずに案内の文字だけ（空のメモ）。
- `IPreviewHost`：`PreviewDir`、`IsDark`、`Log(string)`、`OpenLocalLink(string fullPath)`、`event Action<PreviewSource?, bool> PreviewChanged`。`WebViewDataDir` は `WebViewEnvironment` へ移す。
- **WebView2 の環境は 1 つを共用する**（`WebViewEnvironment.GetAsync(dataDir)`。最初の 1 回だけ `CoreWebView2Environment.CreateAsync`）。同じ作業フォルダで環境を 2 つ作ると、設定が違ったときに失敗するため。Runtime の有無の確認も、ここに 1 つ。
- 2 つ目の WebView2（メモ）は、メモタブを最初に表示したときに作る（起動を遅くしない）。
- リンクの振り分け（`NavigationStarting` / `NewWindowRequested`）は部品の中のまま。ローカルファイルだけ `host.OpenLocalLink` に渡す。

### 7.3 メモのリンクからドキュメントタブへ
1. `MemoViewModel.OpenLocalLink(fullPath)`：`File.Exists` かつ `DocumentLinkRule.TryGetInAppPath(root, fullPath, 今の除外設定, out rel)` なら、`DocumentsViewModel.OpenFromOutside(rel)` を呼び、`MainWindow` にドキュメントタブへの切り替えを頼む（イベント）。それ以外は `ShellOpen.Open`（今の 12.7 の ③④ と同じ）。
2. `DocumentsViewModel.OpenFromOutside(rel)`：
   - 索引にあれば、今の `SelectPath` と同じ（ツリー・一覧・最後のファイルが追従）。
   - 無ければ（まだ走査していない・走査中）、**選ぶ予定として覚えて**、`Start()` を呼ぶ。最初の走査が終わったら、`RestoreLastOpened` の代わりに予定のファイルを選ぶ。後から別の予定が来たら上書き。
   - 走査が終わっても索引に無ければ（判定の後に消えた・除外された）、既定のアプリで開き、ログに残す。
3. 除外設定は `DocumentsViewModel` が持っている `GitIgnoreMatcher` を使う（`DocumentsViewModel.IsInAppDocument(fullPath, out rel)` として公開し、中で `DocumentLinkRule` を呼ぶ）。
4. タブの切り替え：`MainWindow.xaml` の `TabControl` と各 `TabItem` に `x:Name` を付け、`DocumentsTab` を選ぶ。

### 7.4 画面（`MemoView`）
- 上のバー（`DockPanel`）：左に「プロジェクトのメモ」＋状態の文字（最終更新 `yyyy/MM/dd HH:mm`／「編集中」／「編集中（未保存）」）＋保存の失敗の理由（赤）。右に、プレビュー中は「✎ 編集」、編集中は「保存」「キャンセル」。
- 下：`MarkdownPreview` と `TextBox` を同じセルに置き、`IsEditing` で Visibility を入れ替える（重ねない）。
- `TextBox`：`AcceptsReturn=True`、`TextWrapping=Wrap`、`VerticalScrollBarVisibility=Auto`、`FontFamily="Consolas, BIZ UDGothic, MS Gothic"`、`AcceptsTab=False`（Tab でフォーカスが移る。タブ文字は入れない）、`Text` は `UpdateSourceTrigger=PropertyChanged`。「✎ 編集」を押したら入力欄にフォーカスし、カーソルは先頭。
- Ctrl+S：`MemoView` の `InputBindings`（編集中だけ効く。`CanExecute` で）。
- キャンセルの確認・閉じるときの確認は、既存（`HookSetup`）と同じ `MessageBox`（オーナーはメインウィンドウ）。VM は確認の関数（`Func<bool>`）を受け取る。
- 空の案内：`Saved` が空・空白だけなら `PreviewSource.Message("メモはまだありません。右上の『編集』で書けます")`。

### 7.5 未保存のまま閉じる
- `MainWindow.Closing` で、今の `_viewModel.Flush()`（セッションのメモ）の後に、`memo.IsDirty` なら、メモタブを選んでから確認する：「プロジェクトのメモが保存されていません。保存しますか？」（はい＝保存／いいえ＝保存しない／キャンセル）。
- はい：保存。失敗したら `e.Cancel = true`（理由はバーに出る）。いいえ：そのまま閉じる。キャンセル：`e.Cancel = true`。
- 編集中でも変更が無ければ、聞かずに閉じる。

### 7.6 メモのファイルの監視
- `FileSystemWatcher`（`projects\` フォルダ、`Filter` ＝ メモのファイル名、`LastWrite | FileName | Size`、`Changed / Created / Renamed / Deleted`）。`AtomicFile` は置き換えで書くので、`Renamed` と `Changed` の両方を拾う。
- 300ms まとめてから UI スレッドで読み直し、`MemoEditor.OnExternalChange` に渡す。作り直すと返ったときだけ `PreviewChanged`。
- `projects\` が無ければ作ってから監視する（アプリのデータフォルダなので作ってよい）。
- 監視はメモタブを最初に表示したときに始める。ただし閉じるときの確認は、タブを開いていなければ編集もしていないので要らない。

## 8. リスクと対策

| リスク | 対策 |
|---|---|
| 共通化でドキュメントタブの挙動が変わる | Phase 24 を単独で終わらせ、品質ゲート 3 を確かめる。`PreviewChanged` を出す箇所・順番を変えない |
| WebView2 を 2 つ作ると失敗する・遅い | 環境を 1 つ共用（7.2）。メモの WebView2 はタブを最初に開いたときに作る |
| 編集中の文字を失う | 保存の失敗では編集のまま。外の変更は編集中に取り込まない。閉じるときに確認。タブを移っても VM が持つ |
| 自分の保存で監視が反応して描き直す | 同じ内容なら作り直さない（7.1） |
| 改行コードの混在で「未保存」と誤表示 | 比べるときだけ改行を揃える（7.1） |
| リンク先のドキュメントがまだ索引に無い | 予定として覚え、最初の走査の後に選ぶ（7.3） |
| WebView2 の上に WPF を重ねて隠れる | バーは WebView2 の外。エディタとは Visibility で入れ替え |

## 9. 決定事項（確認済み。設計イメージの Q1〜Q8）
1. 保存先は `%LOCALAPPDATA%\Miharikun\projects\{slug}-{hash8}.memo.md`。
2. 対象フォルダ内の md/html へのリンクは、ドキュメントタブに切り替えて開く。
3. 未保存で閉じるときは確認（保存／保存しない／キャンセル）。
4. キャンセルは、変更があれば確認。
5. Ctrl+S で保存。Esc でキャンセルはしない。
6. メモは 1 プロジェクトに 1 つ。
7. ダッシュボードの全文検索の対象にしない。
8. 空のときは案内を出す。

計画で決めたこと（要件にない細部）：保存の失敗は編集のまま／閉じるときの保存が失敗したら閉じない／WebView2 が無くても編集・保存はできる／空のまま保存すると空のファイル／Tab キーはフォーカス移動（タブ文字を入れない）／改行コードは比べるときだけ揃え、保存は書いたまま。

## 10. 実装するセッションへの注意（必ず読む）

### 10.1 始める前に
- **この計画・`memo-tab-design.html`・`HANDOFF.md`・要件定義の変更が見えることを確かめる**。これらはコミットされていないことがある。作業ツリー（worktree）で始まったセッションには未追跡のファイルが無い。見つからなければ、推測で進めずに利用者に聞く。
- 計画と要件定義（12.10）に食い違いが無いか確かめる。あれば要件定義が正。判断がつかなければ聞く。
- `HANDOFF.md` の「やりとりのルール」と「落とし穴」を読む（コミットは頼まれたときだけ・`git add` はパス指定・完了報告にビルドの有無と出力先・本物の `%LOCALAPPDATA%\Miharikun\` は使わず `MIHARIKUN_DATA_DIR` で隔離・ビルドの前にアプリを止める・Core を直したら App を再ビルドしてから確認）。

### 10.2 各 Phase の終わり
- 隔離環境で確かめ、要件定義の `[x]` と「状況」を更新する。
- 報告には、ビルドの有無と出力先、テスト件数、できなかった確認を書く。**次の Phase に進む前に止まって報告する**。
- Phase 24 の後は、利用者が望めば Opus のレビューを挟む（共通化の差分と品質ゲートの結果を渡す）。

**状態：レビュー待ち**（別セッションの Opus に、この計画のレビューを頼む）。
