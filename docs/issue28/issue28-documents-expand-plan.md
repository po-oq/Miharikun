# ドキュメントタブの拡大モード 実装計画（Issue #28）

> **連動ルール**：この md と `issue28-documents-expand-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `docs/miharikun-requirements.md`（12.7.1 ドキュメントの拡大モード、12.4.1 の Esc の行、15 章 Phase 38〜41）。設計の資料は `docs/issue28/issue28-documents-expand-design.html`（確定。Q1〜Q9 はすべて推奨）、技術調査（試作 Q1〜Q7）は `docs/issue28/issue28-tech-investigation.md`。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（7 章）**。
> 進め方：Docs（テスト先行）→ ViewModel（テスト先行）→ 画面 → 両 OS の実機確認・仕上げ。各フェーズの終わりに `dotnet test` と隔離環境で確かめ、要件定義 15 章の該当 Phase の「状況」を更新して報告する。コミットは頼まれたときだけ（ここは区切りの目安）。途中のコミットは `Refs #28`、最後だけ `Fixes #28`（PR 本文に `Closes #28`）。
> 作業ブランチ：`feature/issue28-documents-expand-mode`（`main` から切った）。

## 1. 全体像

```
Phase 38 目次（Docs） ──▶ Phase 39 ViewModel ──▶ Phase 40 画面 ──▶ Phase 41 実機確認・仕上げ
  2 コミット（テスト先行）    2 コミット（テスト先行）   3 コミット            2 コミット（Fixes #28）
```

- 新しい依存パッケージ：無い（Markdig 1.4.0・Avalonia.Controls.WebView 12.1.0 のまま。`System.Text.Json` は .NET に入っている）。
- 守る制約：
  - `Miharikun.Presentation`（ViewModel）は `Miharikun.Docs`（Markdig）を参照しない。Docs も Core を参照しない。見出しの一覧は、App（`MarkdownPreview`）で Docs の型から Presentation の型へ写す（7.2）。
  - プレビュー（`MarkdownPreview`）を親から外さない・別の場所へ動かさない（外すと WebView が作り直され、ページと位置を失う。試作 Q4）。
  - WebView の上に Avalonia の要素を重ねない（12.7）。目次は WebView の隣の列に置く。
  - メモタブも同じ `MarkdownPreview` を使う。メモの見た目・動きは変えない。
- **共通化だけのフェーズは無い**。ただし `MarkdownRenderer`（38）と `MarkdownPreview`（40-1）はメモタブと共用なので、品質ゲート 3 で今までの動きが変わらないことを確かめる。
- いまのテスト：Tests 1126 合格・スキップ 29、UiTests 52 合格・スキップ 3（HANDOFF.md の値。その後 main のコードは変わっていない。着手前に `dotnet test` で取り直す）。うち `MarkdownRendererTests` は 48 件。**件数は減らさない**。

### 品質ゲート（各フェーズの終わり）
1. テストが全部通る（件数を減らさない。スキップの数が増えない）。
2. `dotnet build` が警告を増やさない。
3. 今までの動きが変わらない（Phase 38・40 の後は必ず。隔離環境で目視）：
   - ドキュメントタブの通常の画面：ツリー・一覧・概要カード・md のプレビュー（チェックボックス・mermaid・色付け・相対パスの画像）・`#見出し` のリンク・別の md へのリンク（アプリ内で選ぶ）・http のリンク（既定のブラウザ）・保存で再読み込み（位置が戻る）・テーマの切り替え・タブを切り替えて戻ってもそのまま・html のプレビュー
   - メモタブ：プレビュー・メモの中のリンク → ドキュメントタブ・編集の入れ替え・テーマ
   - ダッシュボード：タイムラインの拡大と Esc（ダッシュボードが見えているとき）
4. 確かめ方：隔離環境（`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダに。対象フォルダも一時フォルダで、手書きの md・html を置く。7.10）。本物のデータは使わない。`dotnet build`／`test` の前にアプリを止める（止められないときは `-p:OutDir=` で別の場所に作る）。

## 2. Phase 38：目次（Docs。テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 38-1 | `MarkdownRenderer.RenderWithOutline`・`MarkdownOutline`・`MarkdownHeading` | md を 1 回だけ解析し（`Markdown.Parse` → `ToHtml`）、HTML と見出しの一覧を返す（7.5）。`Render` は中で `RenderWithOutline(...).Html` を返す（呼び出し側は変えない） | 新規 `Docs/MarkdownOutlineTests`：id が HTML の `id` と一致（同じ名前・日本語・`[x]`・引用とリストの中・setext）／`[x]` `[X]` `[ ]` と無し／記号を外した文字／節のタスク数（下の段の分を含む・入れ子のリスト・番号付き・コードブロックの中は数えない・最初の見出しより前は数えない）／空の見出しを出さない／`Render` と `RenderWithOutline(...).Html` が同じ |
| 38-2 | ページの JS | md のページの最後に、Esc の受け口と位置の補正を足す（7.6）。Esc の受け口は `MarkdownRenderer.EscapeListenerScript`（`<script>` なしの本文）として公開し、html にも入れられるようにする | `MarkdownRendererTests` に追加：2 つのスクリプトが 1 回ずつ入る・本文（`<body>` の中の Markdig の出力）は変わらない。既存の 48 件はそのまま通る |

完了条件：テストが通る。品質ゲート 1〜3（3 はメモタブと通常のプレビュー。ページの見た目が変わらないこと）。**ここで止まって報告する。**

## 3. Phase 39：ViewModel（テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 39-1 | 目次の口と `DocumentsViewModel` の目次 | `IPreviewHost` に `OnOutline`・`OnPageEscape`・`HeadingScrollRequested` を足す（7.2〜7.4）。`OutlineHeading`（Presentation の型）、`OutlineItemViewModel`。`DocumentsViewModel`：`Outline`（使い回して中身を合わせる。7.7）・`ProgressText`・`HasProgress`・`IsOutlineEmpty`・古い結果を捨てる・別のファイルに替わったら目次を空に・再読み込みでは保つ。`MemoViewModel` は新しい口で何もしない | 新規 `Presentation/DocumentsExpandViewModelTests`：7.7 の表の行ごと（使い回し・増減・進み具合・古い結果・ファイルの切り替え・再読み込み・html） |
| 39-2 | 拡大の状態と見出しへ移る | `IsExpanded`・`ToggleExpandedCommand`（選択が無いと押せない）・`Collapse()`（戻すだけ）・`OnPageEscape` → `Collapse`・選択が無くなったら戻す・`IsOutlineVisible`・`JumpToHeadingCommand` → `HeadingScrollRequested(id)`。新規 `PreviewScripts`（Presentation。見出しへ移るスクリプトの文字列・ページの知らせの判定。7.3・7.4） | 同じテストに 7.1 の表の行ごと（Esc 2 回・通常での Esc・ファイルが消えたら戻る・リンクで別の md／html・同じ項目を 2 回押す）。`PreviewScriptsTests`：id の文字（`"`・`\`・`'`・`</script>`・改行・日本語）を入れても壊れない文字列になる、`key:Escape` と `"key:Escape"` だけを Esc とみなす |

完了条件：テストが通る。品質ゲート 1・2。**ここで止まって報告する。**

## 4. Phase 40：画面

| # | コミット | 内容 | 確認（隔離環境・mac） |
|---|---|---|---|
| 40-1 | `MarkdownPreview` | 描いた md の見出しを写して `OnOutline` へ（html・案内・無しは空）。`WebMessageReceived` で `PreviewScripts.IsEscapeMessage` なら `OnPageEscape`。html は読み込みの後で `EscapeListenerScript` を入れる。`HeadingScrollRequested` で見出しへ移る（読み込み中なら後で）（7.2〜7.4） | 品質ゲート 3（メモタブ・通常のプレビューが変わらない）。目次・Esc の目視は、40-2・40-3 の後にまとめて行う（40-1 の時点では、画面に拡大ボタンも目次の列も無い） |
| 40-2 | `DocumentsView`：拡大ボタン・目次の列・隠し方 | 内容カードの見出しに「⤢ 拡大／⤡ 戻す」と、拡大中の「— タイトル　相対パス」。内容カードの中を「目次｜区切り｜プレビュー」の 3 列に（プレビューは列 2 に置いたまま動かさない）。上段・ツリー・一覧・概要カードを隠し、幅を保存・復元（7.8）。目次の項目（字下げ・✅/⬜・タスク数・押して移る・Enter）・進み具合・「見出しがありません」（7.7）。新規 `Converters/DepthIndentConverter` | UiTests（`DocumentsViewTests` に追加）：拡大で上段・ツリー・一覧・概要が隠れ、内容カードが全幅／戻すと元の幅／目次の列は md の拡大中だけ（html では出ない）／`MarkdownPreview` が同じインスタンスで画面に載ったまま／目次の項目の文字・タスク数・進み具合。mac の実機：md で拡大・目次で移る・同じ項目をもう一度・保存で目次が替わる（目次の位置がそのまま）・リンクで別の md／html・メモのリンクから・テーマ・タブの切り替え・拡大⇄戻すで位置がずれない・区切り線のドラッグでも位置がずれない |
| 40-3 | Esc | `DocumentsView` がウィンドウの KeyDown で Esc を受ける（ドキュメントタブが見えていて拡大中だけ。7.4）。`DashboardView` の Esc を、ダッシュボードが見えているときだけにする（12.4.1） | UiTests（新規 `DocumentsEscapeTests`）：拡大中の Esc で戻る／通常の Esc は `Handled` にしない／ドキュメントが隠れていると戻らない／ダッシュボードが隠れているとタイムラインの拡大は戻らない／両方拡大していて見えている方だけ戻る。既存の `RightPaneTests` の Esc はそのまま通る。mac の実機：Esc（ページの中・目次・ボタンの後・どこにも無い）、ダッシュボードで拡大したままドキュメントタブで Esc |

完了条件：12.7.1 の各動作を mac の実機で確かめる（ライト/ダークの両方）。品質ゲート 1〜3。**ここで止まって報告する**（Windows の確認は Phase 41）。

## 5. Phase 41：両 OS の実機確認・仕上げ

| # | コミット | 内容 |
|---|---|---|
| 41-1 | Windows の確認（と、要れば直す） | 利用者が Windows の実機で、40-2・40-3 の mac の確認と同じ項目を見る（とくに：ページの中の Esc・目次で移る・位置の補正が Chromium の働きと二重にならない）。`invokeCSharpAction` が使えない・`Body` の形が違うときは、7.4 の代わりの形にする。位置が二重にずれるときは、md のページに `overflow-anchor: none` を足す |
| 41-2 | 仕上げ | 要件定義 15 章の Phase 38〜41 を `[x]` に・「状況」を書く。`docs/release.md` の「出す前のチェック」に拡大モードの確認を足す。計画の状態を「確定」に。HANDOFF.md は頼まれたら。`Fixes #28` |

## 6. ファイル構成（追加・変更）

```
src/Miharikun.Docs/
├─ MarkdownRenderer.cs                 38-1 変更：RenderWithOutline（1 回の解析で HTML と見出し）。Render はその Html を返す
│                                      38-2 変更：ページの JS（Esc の受け口・位置の補正）。EscapeListenerScript を公開
└─ MarkdownOutline.cs                  38-1 新規：MarkdownHeading・MarkdownRendering（型）と Extract（見出し・✅/⬜・節のタスク数）

src/Miharikun.Presentation/
├─ PreviewScripts.cs                   39-2 新規：ScrollToHeading(id)・IsEscapeMessage(body)
└─ ViewModels/
   ├─ IPreviewHost.cs                  39-1 変更：OutlineHeading、OnOutline・OnPageEscape・HeadingScrollRequested
   ├─ OutlineItemViewModel.cs          39-1 新規：目次の 1 行（Depth・DisplayText・TaskText・Update）
   ├─ DocumentsViewModel.cs            39-1・39-2 変更：SetSelectedPath で選択が無くなったら戻す、RaisePreview でファイルが替わったら目次を空に
   ├─ DocumentsViewModel.Expand.cs     39-1・39-2 新規（partial）：拡大の状態・目次・進み具合・見出しへ移る
   └─ MemoViewModel.cs                 39-1 変更：新しい口は何もしない

src/Miharikun/
├─ Views/MarkdownPreview.axaml.cs      40-1 変更：OnOutline へ渡す・WebMessageReceived・html への受け口・見出しへ移る
├─ Views/DocumentsView.axaml           40-2 変更：拡大ボタン・見出しの文字・目次の列・IsVisible
├─ Views/DocumentsView.axaml.cs        40-2 変更：列の隠し方と幅（7.8）・目次の項目を押す／Enter
│                                      40-3 変更：ウィンドウの KeyDown で Esc
├─ Views/DashboardView.axaml.cs        40-3 変更：Esc は IsEffectivelyVisible のときだけ
└─ Converters/DepthIndentConverter.cs  40-2 新規：段の深さ → 左の余白

tests/Miharikun.Tests/
├─ Docs/MarkdownOutlineTests.cs                        38-1 新規
├─ Docs/MarkdownRendererTests.cs                       38-2 変更：ページの JS
├─ Presentation/DocumentsExpandViewModelTests.cs       39-1・39-2 新規
└─ Presentation/PreviewScriptsTests.cs                 39-2 新規

tests/Miharikun.UiTests/
├─ DocumentsViewTests.cs               40-2 変更：拡大・目次の列・プレビューが動かない
└─ DocumentsEscapeTests.cs             40-3 新規：Esc とタブ

docs/miharikun-requirements.md         各 Phase の終わり：[x]・状況
docs/release.md                        41-2 変更：出す前のチェック
```

## 7. 実装の決めごと（迷いやすい所）

### 7.1 拡大の状態（`DocumentsViewModel`）

状態は「通常」と「拡大中」の 2 つ。拡大中は、開いているのが md なら「目次｜プレビュー」、html なら「プレビューだけ」（目次の列を出さない）。

| 操作 | 結果 |
|---|---|
| 初期（起動・タブを最初に開く） | 通常（`IsExpanded = false`）。ファイルを選んでいないので「⤢ 拡大」は押せない（`CanExecute = HasSelection`） |
| ファイルを選んで「⤢ 拡大」 | 拡大中。上段・ツリー・一覧・概要カードを隠す。md なら目次の列を出す（`IsOutlineVisible`）。html なら出さない |
| 拡大中に「⤡ 戻す」 | 通常。開いているファイル・プレビューの位置・ツリーと一覧の選択・列の幅（区切り線で動かした幅も）が元のまま |
| 拡大中に Esc（ページの中にフォーカス） | 通常（ページの知らせ → `OnPageEscape` → `Collapse`） |
| 拡大中に Esc（目次・ボタン・どこにも無い） | 通常（ウィンドウの KeyDown → `Collapse`。`e.Handled = true`） |
| 通常のときに Esc | 何もしない。`e.Handled` も立てない |
| Esc が続けて 2 回届く | 通常のまま（`Collapse` は戻すだけ。切り替えない） |
| ドキュメントタブが見えていないときの Esc | ドキュメントの拡大は変わらない |
| ダッシュボードが見えていないときの Esc | タイムラインの拡大は変わらない（40-3 の修正。今はどのタブでも戻る） |
| 拡大中にタブを切り替えて戻る | 拡大のまま。プレビューの位置もそのまま |
| 拡大中に md の中のリンクで別の md へ | 拡大のまま。目次を空にしてから、新しい目次に替える |
| 拡大中にリンクで html へ／html から md へ | 拡大のまま。目次の列を隠す／出す |
| 拡大中にメモタブのリンクから「ドキュメントで開く」 | 拡大のまま、そのファイルに替わる |
| 拡大中に開いている md が保存される | 拡大のまま。目次を作り直す（同じ位置の項目は使い回す。7.7）。プレビューは今のとおり元の位置に戻る |
| 拡大中にテーマを変える | 拡大のまま。目次は同じ中身（再読み込み） |
| 拡大中に開いているファイルが消える・除外される | 通常に戻る（`SetSelectedPath(null)` のとき `IsExpanded = false`）。プレビューは空 |
| 拡大中に全再走査（フォルダの変更・設定の保存） | 走査中は待つ（`RefreshNow` の今の決まり）。走査の後もファイルがあれば拡大のまま |
| アプリを閉じて開く | 通常（保存しない） |

- `Collapse()` は `public`（ウィンドウの KeyDown から呼ぶ）。戻り値 `bool`（戻したら true）にして、View は true のときだけ `e.Handled = true` にする。
- `IsOutlineVisible` = `IsExpanded && CurrentTarget is { Kind: DocumentKind.Markdown }`。`IsExpanded` と選択の変化で通知する。

### 7.2 目次の流れ（描く → VM へ）

1. VM が `RaisePreview(reload)` を出す（今のまま）。**別のファイルに替わるとき**（`reload == false` で、前に目次を作ったファイルと違う）は、その前に `Outline` を空にする。再読み込み（保存・テーマ・「再読み込み」）では空にしない（目次がちらつかず、目次の位置も保つ）。
2. `MarkdownPreview.ShowAsync` が種類で分ける：
   - md のファイル：背景で `MarkdownRenderer.RenderWithOutline` → 一時 HTML を書く → `version` が古ければ捨てる（目次も渡さない）→ 見出しを `OutlineHeading` に写して `_vm.OnOutline(source, headings)` → `Navigate`／`Refresh`。
   - html のファイル・メモ（`PreviewSource.Markdown`）・案内・null・ファイルが無い・読めない・WebView を作れない：`_vm.OnOutline(source, [])`（メモの VM は何もしない）。
3. `DocumentsViewModel.OnOutline(source, headings)`：`source` が `CurrentTarget` と違えば捨てる（record の比較）。同じなら 7.7 の決まりで `Outline` を合わせ、`ProgressText` を作り直す。
- WebView が無いとき（Windows の Runtime 未導入・ヘッドレスのテスト）は目次が来ない（要件どおり）。テストでは `OnOutline` を直接呼ぶ（7.10）。
- 写し替え（Docs の `MarkdownHeading` → Presentation の `OutlineHeading`）は `MarkdownPreview` に置く。2 つの型は同じ項目：`Level`（1〜6）・`Text`・`Id`・`Done`（`bool?`。true＝`[x]`、false＝`[ ]`、null＝印なし）・`TasksDone`・`TasksTotal`。

### 7.3 見出しへ移る

1. 目次の項目を押す（`Tapped`）か、項目で Enter → `JumpToHeadingCommand.Execute(item)` → `HeadingScrollRequested(item.Id)`。**同じ項目でも毎回出す**（一覧の選択の変化は使わない）。
2. `MarkdownPreview.OnHeadingScrollRequested(id)`：
   - `_web` が無い → 何もしない。
   - 読み込み中（`_loadingPage`）→ `_pendingHeadingId = id`、`_restoreScrollY = null`（利用者の操作を、再読み込みの位置の復元より優先する）。
   - それ以外 → `_web.InvokeScript(PreviewScripts.ScrollToHeading(id))`。
3. `OnNavigationCompleted`：`_pendingHeadingId` があれば移って消す。無ければ今のとおり `_restoreScrollY` を戻す。新しいファイルを開くとき（`ShowAsync` の `Navigate` の前）は `_pendingHeadingId` を消す。
- `PreviewScripts.ScrollToHeading(id)` は `(function(){var el=document.getElementById("…");if(el)el.scrollIntoView();})()`。`…` は `JsonEncodedText.Encode(id)`（`"`・`\`・`<`・改行・U+2028 などを逃がす。反射を使わない）。`location.hash` は使わない（`<base>` のため別の URL への移動になる）。
- 見出しが見つからない（目次が古い）ときは何もしない（例外にしない）。

### 7.4 Esc の 2 つの受け口

- **ウィンドウ**：`DocumentsView` が `TopLevel` の `KeyDown`（Bubble）を受ける（`DashboardView` と同じ付け方・外し方）。条件：`e.Key == Key.Escape && !e.Handled && IsEffectivelyVisible && _vm.Collapse()`、成り立てば `e.Handled = true`。
- **ダッシュボードの修正**：`DashboardView.OnKeyDown` の条件に `IsEffectivelyVisible` を足す（`MainWindow` はタブの中身の `IsVisible` を切り替えるので、隠れたタブでは false）。
- **ページ**：md は 38-2 のスクリプト。html は `OnNavigationCompleted` で、表示中が html なら `_web.InvokeScript(MarkdownRenderer.EscapeListenerScript)`（スクリプトは `window.__miharikunEsc` で 2 回入らない）。`MarkdownPreview` が `WebMessageReceived` を受け、`PreviewScripts.IsEscapeMessage(e.Body)` なら `Dispatcher.UIThread.Post(() => _vm?.OnPageEscape())`。ほかの知らせは捨てる（ログにも中身を書かない）。
- `IsEscapeMessage`：`key:Escape` と、前後を `"` で囲んだ `"key:Escape"` だけを true（Windows で `Body` が JSON の形で来るかもしれない。要確認・Phase 41）。
- メモタブのページの Esc：`MemoViewModel.OnPageEscape` は何もしない（メモには拡大が無い）。
- 代わりの形（Phase 41 で、Windows で `invokeCSharpAction` が使えないと分かったときだけ）：ページが `location.href = 'miharikun-msg:escape'` に移ろうとし、`NavigationStarted` で scheme が `miharikun-msg` なら取り消して Esc とみなす（`PreviewNavigationPolicy` に分け道を足す）。

### 7.5 見出しの一覧の作り方（Docs）

- `RenderWithOutline(markdown, baseFolder, isDark, title)`：`var doc = Markdown.Parse(markdown, Pipeline)` → `var body = ReadOnlyCheckboxes(doc.ToHtml(Pipeline))` → `MarkdownOutline.Extract(doc)`。`Parse` → `ToHtml` の HTML は `Markdown.ToHtml` と同じ（試作で確認）。id は `HeadingBlock.GetAttributes().Id` をそのまま使う（自前で作らない）。
- `Extract` は `doc.Descendants()` を 1 回なめる（ブロックもインラインも、本文の順に来る。Markdig 1.4.0 で確認）。開いている見出しのスタックを持ち：
  - `HeadingBlock`（段 L）：スタックから段 L 以上を下ろし、新しい見出しを積む。
  - `Markdig.Extensions.TaskLists.TaskList`：スタックのすべての見出しの `TasksTotal` を 1 増やし、`Checked` なら `TasksDone` も 1 増やす（下の段の見出しの分も、上の見出しに入る）。最初の見出しより前のタスクは、どこにも数えない。
  - `TaskList` は、プレビューでチェックボックスになるものだけ（コードブロック・段落の中の `[x]` は来ない。試作で確認）。
- 文字：`HeadingBlock.Inline` をたどる。`LiteralInline` → 文字、`CodeInline` → 中身、`ContainerInline`（強調・リンク）→ 中をたどる、`LineBreakInline` → 空白、`HtmlEntityInline` → 変換後の文字、`HtmlInline` → 捨てる、`AutolinkInline` → URL。最後に空白の連続を 1 つにして前後を `Trim`。
- 印：文字の先頭が `^\[([ xX])\](\s+|$)` なら `Done` を決めて、その部分を外す。
- 文字が空になった見出し（`## ` だけ・`## [x]` だけ）は出さない（進み具合にも数えない）。
- id が無い（来ないはず）ときは、項目は出すが、押しても移らない（7.3 の「見つからない」と同じ）。

### 7.6 ページの JS（md のページ。38-2）

`MarkdownRenderer.Render` の最後（今の `#見出し` のスクリプトの後）に、次の 2 つを足す。ドキュメントとメモのどちらの md にも入る。

```js
// Esc の受け口（EscapeListenerScript。html にも InvokeScript で入れる）
(function () {
  if (window.__miharikunEsc) return;
  window.__miharikunEsc = true;
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !e.defaultPrevented && typeof invokeCSharpAction === 'function')
      invokeCSharpAction('key:Escape');
  });
})();

// 幅が変わっても、見ていた所を保つ（md のページだけ）
(function () {
  var anchor = null, anchorTop = 0, adjusting = false;
  function remember() {
    var els = document.querySelectorAll('h1,h2,h3,h4,h5,h6,p,li,pre,table,blockquote');
    for (var i = 0; i < els.length; i++) {
      var t = els[i].getBoundingClientRect().top;
      if (t >= 0) { anchor = els[i]; anchorTop = t; return; }
    }
  }
  window.addEventListener('scroll', function () { if (!adjusting) remember(); }, { passive: true });
  window.addEventListener('resize', function () {
    if (!anchor) return;
    adjusting = true;
    window.scrollBy(0, anchor.getBoundingClientRect().top - anchorTop);
    requestAnimationFrame(function () { adjusting = false; });
  });
  remember();
})();
```

- `e.defaultPrevented` のときは知らせない（html のページが自分で Esc を使っている：ダイアログを閉じるなど）。
- `overflow-anchor` は今は触らない（Chromium の働きを残す）。Windows で二重にずれたら、41-1 で `body { overflow-anchor: none; }` を足す。
- 位置の補正は、拡大⇄戻すのほか、区切り線のドラッグ・ウィンドウの大きさの変更でも働く（要件どおり。通常の画面の見た目は変わらない）。

### 7.7 目次の表示と、作り直し（`OutlineItemViewModel`・`DocumentsViewModel`）

| 入力 | 結果 |
|---|---|
| 見出し 0 件の md を拡大 | 目次の列に「見出しがありません」（`IsOutlineEmpty`）。進み具合は出さない |
| `[x]`／`[ ]` 付きの見出しが 1 つ以上 | 目次の上に「✅ 済み / 全部」（`ProgressText`。数えるのは見出しの印だけ） |
| 印付きの見出しが無い | 進み具合を出さない（`HasProgress = false`） |
| 項目の文字 | `Done` が true なら「✅ 」、false なら「⬜ 」を先頭に付ける（`DisplayText`）。長ければ省略（`TextTrimming`）し、ツールチップに全文 |
| 節のタスクがある | 右に「済み/全部」（`TaskText`。例「2/5」）。0 件なら出さない |
| 段の字下げ | `Depth` = `Level` − その md の見出しの最小の段（`##` から始まる md でも、いちばん浅い段は字下げしない）。余白は 1 段 14px（`DepthIndentConverter`） |
| 再読み込み（同じファイル）で見出しが同じ | `Outline` の中身は変えない（同じインスタンス・通知なし）。目次のスクロール位置がそのまま |
| 再読み込みで一部が変わった（文字・印・タスク数） | 同じ位置の項目は `Update` でプロパティだけ変える（変わった所だけ通知）。項目の数が増減したら、末尾で足す・消す。`Clear()` はしない |
| 別のファイルに替わった | `Clear()` してから新しい目次（7.2 の 1） |
| `CurrentTarget` と違う `source` の `OnOutline` | 捨てる |

- `Outline` は `ObservableCollection<OutlineItemViewModel>` を 1 つだけ持ち、入れ物ごとは差し替えない（`ItemsSource` を差し替えると、一覧のスクロール位置が先頭に戻るため）。
- 一覧の選択（`SelectedItem`）は見た目だけ。移るのは 7.3 の押したとき。作り直しで選択がずれても、プレビューは動かさない。

### 7.8 列の隠し方と幅（`DocumentsView`）

- **プレビューは動かさない**：内容カードの中の Grid を `ColumnDefinitions="260,8,*"`（目次・区切り・プレビュー）にし、見出しの行は `Grid.ColumnSpan="3"`、`MarkdownPreview` は `Grid.Row="1" Grid.Column="2"` に置く（XAML の書き換えで、実行中に親は変わらない）。
- **拡大**（`ApplyExpanded(true)`。`DashboardView.ApplyTimelineExpanded` と同じやり方）：ツリー（列 0）と一覧（列 2）の幅を覚えてから、`MinWidth = 0`・`Width = 0`、ツリー・一覧・区切り 2 本を `IsVisible = false`。上段（`DockPanel`）と概要カード（`Border`）は XAML で `IsVisible="{Binding !IsExpanded}"`。
- **戻す**（`ApplyExpanded(false)`）：`MinWidth` を 120／160 に、幅を覚えた値に（無ければ 200／260）、`IsVisible = true`。
- **目次の列**（`ApplyOutlineVisible`）：隠すときは目次の列の幅を覚えて（0 より大きければ）、目次と区切りの列を `Width = 0`・`MinWidth = 0`、目次と区切りを `IsVisible = false`。出すときは `MinWidth = 160`、幅を覚えた値（無ければ 260）、区切りの列を 8。**`Auto` に頼らない**（区切り線を動かすと列が px になり、隠しても空白が残るため）。
- VM の `IsExpanded`・`IsOutlineVisible` の変化を `PropertyChanged` で受けて呼ぶ（`DashboardView` と同じ）。`DataContext` が付いたときにも 1 回呼ぶ。
- 拡大中の見出しの文字：`Overview.Title` と `Overview.RelativePath`（概要は背景で読むので、一瞬空でもよい）。`IsVisible="{Binding IsExpanded}"`。
- ボタン：`Content` は `BoolTextConverter` で「⤢ 拡大|⤡ 戻す」（タイムラインと同じ）、`ToolTip.Tip`「目次とプレビューを画面いっぱいに広げる／戻す（Esc でも戻る）」、`AutomationId="DocExpandButton"`。目次は `AutomationId="DocOutline"`、進み具合は `DocProgress`、空の文は `DocOutlineEmpty`。
- ウィンドウの最小幅は 900 のまま（目次 260 ＋ プレビュー 240 以上が入る）。

### 7.9 起動・初期化のタイミング

- `DocumentsViewModel.Start()` はタブが最初に見えたとき（今のまま）。選択が無い間は「⤢ 拡大」を押せない。前回のファイルの復元（`RestoreLastOpened`）でも、7.2 の流れで目次が来る。
- `MarkdownPreview` は `HeadingScrollRequested` を `OnDataContextChanged` で付け外しする（`PreviewChanged` と同じ所）。`WebMessageReceived` は WebView を作るとき（`EnsureWebView`）に付ける。
- 拡大の状態は VM が持つ。View が作り直されることは無い（タブは載せたまま隠す）が、`DataContext` が付いたときに `ApplyExpanded`・`ApplyOutlineVisible` を今の値で呼ぶ。

### 7.10 テストと確かめ方

- VM のテスト：`DocumentsViewModelTests` と同じ組み立て（一時フォルダ・`ImmediateSynchronizationContext`・`FakeUiServices`）。`OpenFromOutside("docs/a.md")` で選び、`vm.OnOutline(vm.CurrentTarget!, …)` で目次を直接渡す。ファイルが消えた場合は、ファイルを消して `Rescan()` → 走査の終わりを待つ。
- UiTests：`MarkdownPreview.WebViewDisabled = true`（`TestSetup`）なので、プレビューは目次を空で渡す。目次はテストから `OnOutline` で渡す。Esc は `window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None)`（`RightPaneTests` と同じ）。タブの代わりに、`Grid` に `DashboardView`（`MainVmHarness`）と `DocumentsView` を重ねて置き、`IsVisible` を切り替える。
- 実機（隔離環境）：対象フォルダに手書きの md を置く。`plan.md`（`## [x] Phase 1`〜`## [ ] Phase 6`、各 Phase に `- [ ]` を数個と `###` の小見出し、mermaid 1 つ、長い本文、`other.md` と `page.html` へのリンク）、`other.md`（見出しなし）、`page.html`（自前の keydown で Esc を使わないもの）。保存は手元のエディタで行う。

## 8. リスクと対策

| リスク | 対策 |
|---|---|
| Windows（WebView2）で、ページからの知らせ・Esc・位置の補正を確かめていない | 41-1 で実機確認。知らせが使えなければ 7.4 の代わりの形、`Body` の形が違えば `IsEscapeMessage` で吸収、二重にずれれば `overflow-anchor: none` |
| 共用の `MarkdownRenderer`・`MarkdownPreview` を変えて、メモタブや通常のプレビューが変わる | 品質ゲート 3。`Render` の HTML の本文は変えず、足すのは最後のスクリプトだけ。既存の `MarkdownRendererTests` を変えずに通す |
| プレビューを動かして WebView が作り直される | 7.8：XAML で最初から列 2 に置き、実行中は列の幅と `IsVisible` だけを変える。UiTests で同じインスタンスが載ったままを確かめる |
| 目次の id と本文の id が食い違う | 1 回の解析から両方を作る（7.5）。テストで HTML の `id` と突き合わせる |
| 古い描画の結果の目次が、新しいファイルに出る | `version` の確認の後で渡す（7.2）。VM でも `CurrentTarget` と比べて捨てる |
| 保存のたびに目次がちらつく・先頭に戻る | 7.7：入れ物を差し替えず、同じ位置の項目を使い回す |
| ダッシュボードとドキュメントの Esc がぶつかる | 両方とも `IsEffectivelyVisible` の条件（7.4）。UiTests で、重ねた 2 つのうち見えている方だけが戻ることを確かめる |
| html のページの JS が `invokeCSharpAction` を勝手に呼ぶ | `key:Escape` だけを受ける。届いても拡大を戻すだけ。中身はログに書かない |
| html の iframe の中にフォーカスがあると Esc が届かない | 受け入れる（要件どおり。「⤡ 戻す」で戻せる） |
| `TaskList` を数える範囲が、プレビューのチェックボックスと食い違う | Markdig が `TaskList` にしたもの＝チェックボックスになるものだけを数える（7.5）。テストで入れ子・番号付き・コードブロックを固定 |

## 9. 決定事項（確認済み。設計の質問 Q1〜Q9）
1. Q1：html も拡大できる。目次の列は出さず、プレビューを全幅に。
2. Q2：目次はすべての段（`#`〜`######`）を字下げで出す。
3. Q3：目次の上に進み具合の集計を出す（`[x]`／`[ ]` 付きの見出しがあるときだけ）。
4. Q4：見出しの右に、その節（下の段の分も含む）のタスクの数「2/5」。集計は見出しだけを数える。
5. Q5：拡大中は上段（読み直し・絞り込み・件数）も隠す。
6. Q6：位置の補正は md のページで常に効かせる。html には入れない（Esc の受け口だけ）。
7. Q7：本文の幅に上限は付けない。
8. Q8：ダッシュボードの Esc の不具合を一緒に直す。
9. Q9：今見ている見出しの強調はしない（保留。17 章）。

計画で決めたこと（仕様にない細部。違えば言ってください）：
- 見出しが 0 件の md を拡大したら、目次の列に「見出しがありません」と出す（列は隠さない）。
- 目次の字下げは、その md のいちばん浅い段を 0 にする（`##` から始まる md でも左に寄せる）。1 段 14px。
- 文字が空になる見出し（`## [x]` だけ等）は目次に出さず、進み具合にも数えない。
- 目次の項目は、押したとき（`Tapped`）と Enter で移る。一覧の選択の変化では移らない。
- 読み込み中に目次を押したら、再読み込みの位置の復元より、押した見出しを優先する。
- 別のファイルに替わるときは目次をすぐ空にし、再読み込みでは空にしない。
- `Collapse()` は戻したかどうかを返し、View は戻したときだけ `Handled` にする（通常の Esc を邪魔しない）。
- ページの Esc は `e.defaultPrevented` のときは知らせない（html が自分で Esc を使っている場合）。
- 拡大中の見出しは「— タイトル　相対パス」（タイトルは概要カードと同じ）。
- 目次の列の初期幅は 260、最小 160。拡大をやめても、目次の幅は覚えておく（アプリを閉じるまで）。
- Phase 番号は 38〜41（Issue #23 の 37 の次）。コミット番号は 38-1〜41-2。

## 10. 実装するセッションへの注意（必ず読む）

### 10.1 始める前に
- 作業ブランチ `feature/issue28-documents-expand-mode` にいること。このブランチには、設計の html・技術調査・この計画・要件定義の変更が**まだコミットされていない**ことがある（未追跡の `docs/issue28/`、変更中の `docs/miharikun-requirements.md`）。別の作業ツリーでは見えない。見つからなければ、推測で進めずに利用者に聞く。
- 計画と要件定義（12.7.1・12.4.1・15 章の Phase 38〜41）に食い違いが無いか確かめる。あれば要件定義が正。判断がつかなければ聞く。
- `CLAUDE.md` の決まり：返答は日本語、コミット・push・PR・マージは頼まれたときだけ、`git add` はパス指定（`-A` は使わない）、`dotnet build`／`test` の前にアプリを止める、完了報告に「ビルドしたかと出力先・テスト件数・できなかった確認」、本物のデータ・`~/.cursor/hooks.json` は触らない、`~/.claude/` には書かない。
- 着手前に `dotnet test` を回して、いまの件数を控える。

### 10.2 各フェーズの終わり
- 38・39 は、テストを先に書いて落ちるのを確かめてから実装する。
- フェーズの終わりに、品質ゲートを確かめ、要件定義の該当 Phase の「状況」を書き、`[x]` にして（そのフェーズのコミットに入れる）、テストの件数・ビルドの有無と出力先・できなかった確認を報告する。**次のフェーズに進む前に止まって報告する**。
- Phase 40 の実機確認は mac で、利用者に見てもらう（私は画面を撮れない。UiTests のスクリーンショットは補助）。Phase 41 の Windows の確認は利用者の作業。
- 39 の後に、利用者が望めば差分のレビューを挟む。

**状態：レビュー待ち**（別セッションに、この計画のレビューを頼む）。
