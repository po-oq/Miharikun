# Issue #28 技術調査：ドキュメントタブの拡大モード（目次｜プレビュー）

- 日付：2026-10-09
- 対象：Issue #28（`gh issue view 28`）。この文書は「既存の実装の調査」と「技術的な懸念を試作で潰した結果」。仕様の決定はペライチ → 要件定義で行う
- 試作：捨てる小さな画面（スクラッチパッドに置き、リポジトリには入れない）。Avalonia 12.1.3・Avalonia.Controls.WebView 12.1.0（本体と同じ版）、mac（WKWebView）。サンプルの md は手書き

## 1. いまの作り（関係する所だけ）

| 所 | 中身 | 拡大モードとの関係 |
|---|---|---|
| `Views/DocumentsView.axaml` | 上段（読み直し・絞り込み）＋ 5 列の Grid（ツリー 200｜区切り｜一覧 260｜区切り｜右 *）。右は「ドキュメント概要」カードと「内容」カード（見出し行のボタン 3 つ＋`MarkdownPreview`） | 列の幅と `IsVisible` で隠す。プレビューは動かさない（3 章） |
| `Views/MarkdownPreview.axaml.cs` | md を読んで HTML にし（`MarkdownRenderer.Render`）、一時 HTML を `NativeWebView` で開く。リンクの振り分け（`PreviewNavigationPolicy`）、再読み込みのスクロール位置を戻す処理 | 目次を作る所・見出しへ移る所・Esc を受ける所になる。メモタブと共用 |
| `ViewModels/IPreviewHost.cs` | プレビューと使う側（ドキュメント・メモの VM）の口 | 目次・見出しへの移動・Esc の口を足す。メモの VM は何もしない |
| `ViewModels/DocumentsViewModel.cs` | 選択・概要・保存の検知（`OnWatcherChanges` → `RaisePreview(true)`）・リンクで別の md（`OpenLocalLink` → `SelectPath`） | 拡大の状態と目次を持つ。保存・リンクの流れはそのまま使える |
| `Miharikun.Docs/MarkdownRenderer.cs` | Markdig（`UseAutoIdentifiers(GitHub)` → `UseAdvancedExtensions`）で HTML にする。`#見出し` のクリックは JS の `scrollIntoView` | 同じ解析の結果から目次を取る |
| `Views/DashboardView.axaml.cs` | タイムラインの拡大（12.4.1）。隣の列を幅 0・`IsVisible=false` にして広げ、戻すときは幅を戻す。Esc はウィンドウ（`TopLevel`）の KeyDown で受ける | 同じやり方をまねる。Esc の受け方に不具合あり（5 章） |

- `Miharikun.Presentation`（ViewModel）は `Miharikun.Docs`（Markdig）を参照しない決まり（csproj の注記）。Docs も Core を参照しない。目次の型の受け渡しは、App（`MarkdownPreview`）で写し替えることになる。
- md の CSS（`markdown-base.css`）は本文の幅に上限が無い。全幅にすると 1 行がとても長くなる。

## 2. 試作で確かめたこと（mac・WKWebView）

| # | 確かめたこと | 結果 |
|---|---|---|
| Q1 | ページの JS から C# へ知らせる口 | **使える**。Avalonia が `invokeCSharpAction(文字列)` を `file://` のページにも入れていて（`typeof` が `function`）、C# の `NativeWebView.WebMessageReceived` に届く。`InvokeScript` から呼んでも、ページの中のタイマーから呼んでも届いた |
| Q2 | ページの `keydown` で Esc を受けて C# へ | **届く**（合成の `KeyboardEvent` で確認）。実際のキーを押す確認は未（6 章） |
| Q3 | 日本語・`"` を含む id の見出しへ C# から移る | **移れる**。`InvokeScript("document.getElementById(<JSON の文字列>).scrollIntoView()")` で、見出しが上端（top=0）に来た。`InvokeScript` の戻り値は、数は `7401`、文字列は引用符なしの生の文字 |
| Q4 | 拡大⇄戻すで WebView が作り直されないか | **作り直されない**。隣の列を隠し、WebView の列を広げるだけなら、ページの変数（`window.__mark`）が残った |
| Q4' | 拡大で幅が変わったときのスクロール位置 | **ずれる**。幅 780→960 px で、上端に出していた見出しが **1,188 px 上へ消えた**（`scrollY` は同じで、折り返しが減って中身が短くなるため。WKWebView には幅の変化で位置を保つ働き（scroll anchoring）が無い）。戻すと元の位置に戻る（幅が同じに戻るため） |
| Q5 | ページの JS で位置を補正 | **直る**。スクロールのたびに「上端に見えている要素とその位置」を覚え、`resize` で同じ位置へ戻すと、拡大の後も戻した後も top=0 のまま |
| Q6 | 保存の自動再読み込み（`Refresh()`）の後も Q1 の口があるか | **ある** |
| Q7 | 実際のキーの Esc（利用者が押した。2026-10-09） | **1 回の Esc は、どちらか一方にだけ届く**。ページの中をクリックした後は、ページの `keydown` → `WebMessageReceived` にだけ届き、**Avalonia の KeyDown には来ない**（ページの受け口が要る）。入力欄の後は Avalonia の KeyDown（発生元 `TextBox`）。フォーカスを取らない所（試作の黄色の枠）をクリックした後は Avalonia の KeyDown（発生元 `NativeWebView` か、前にフォーカスがあった `TextBox`）。両方に届いた Esc は無かった。どの場所でも Esc を受けられた |
| — | 見出しの id を C# で取れるか（Markdig 1.4.0） | **取れる**。`Markdown.Parse` の `HeadingBlock.GetAttributes().Id` が、HTML の `id` と全部一致した（同じ名前の見出しの `-1`、`[x]`、`[ ]`、日本語、絵文字、引用・リストの中の見出し、setext 形式）。`Parse` → `ToHtml` の HTML は `Markdown.ToHtml` と同じ。コードブロックの中・生の `<h2>` は見出しにならない。空の見出しは id が `section` で文字が空 |

## 3. 懸念と、潰した結果

1. **プレビューを動かすと WebView が作り直される**（Avalonia は親から外すと OS の部品を壊す。`BeginReparenting` という逃げ道はある）
   → プレビューは同じ Grid の中に置いたまま、目次の列を左に足し、ツリー・一覧・概要の列と行を隠す。Q4 で、作り直されないことを確認した。
2. **目次の見出しとページの見出しの id が食い違う**
   → md を 1 回だけ解析し、同じ結果から HTML と目次を作る（`Parse` → `ToHtml`）。id は Markdig が付けたものをそのまま使う（自前で作らない）。
3. **目次から見出しへ移る方法**
   → `InvokeScript` で `getElementById(...).scrollIntoView()`（Q3）。`location.hash` は使わない（`<base>` のせいで別の URL への移動になり、リンクの振り分けに引っかかる）。読み込み中（保存の再読み込みなど）に押されたときは、読み込みが終わってから移る。
4. **プレビューにフォーカスがあるときの Esc**（Issue の「確かめること」）
   → ページの `keydown` で Esc を受け、`invokeCSharpAction('key:Escape')` で知らせる（Q1・Q2・Q7。mac ではページにフォーカスがあると Avalonia にキーが来ないので、これが要る）。ウィンドウの KeyDown でも受ける（入力欄・目次にフォーカスがあるとき）。md はページに書き込み、html は読み込み後に `InvokeScript` で入れる。Avalonia 側にもキーが来る場合に備え、**Esc は「戻す」だけ（切り替えない）**にして、2 回届いても害が無いようにする。受け取る文は `key:Escape` だけ（html は利用者のファイルの JS が動くので、ほかの文では何もしない）。
5. **拡大で幅が変わると、見ていた所がずれる**（Q4'）
   → md のページに位置の補正の JS を入れる（Q5）。html は利用者のファイルなので、入れるかは決める（ペライチで）。
6. **目次をどこで作るか**（ViewModel は Markdig を使わない決まり）
   → `Miharikun.Docs` に「見出しの一覧を取る」処理を足し（`[x]`・`[X]`・`[ ]` の判定と、強調・コード・リンクを外した文字もここで）、`MarkdownPreview` が描くときに一緒に作って、`IPreviewHost` 経由で VM に渡す。目次は描いた中身と常に揃う。WebView が無いとき（Windows の Runtime 未導入・ヘッドレスのテスト）は目次が来ないので、VM・画面のテストは目次を直接渡して確かめる。

## 4. 実装で気をつけること（計画に入れる）

- 目次の項目は「押したとき」に移る（一覧の選択の変化では動かさない）。同じ項目をもう一度押しても移れるように。保存で目次を作り直したときは、目次のスクロール位置を保ち、勝手にプレビューを動かさない。
- 拡大中に選んでいたファイルが無くなった（消えた・除外された）ら、拡大を解く（タイムラインの拡大と同じ）。
- 拡大の状態は VM に持ち、保存しない。タブを切り替えて戻っても拡大のまま。
- 列を隠すときは `MinWidth` を外し、戻すときに幅（区切り線で動かした幅も）を戻す（`DashboardView` と同じ）。
- メモタブも同じ `MarkdownPreview` を使う。メモでは目次・Esc の口は何もしない。

## 5. 見つけた既存の不具合

- **ダッシュボードの Esc が、ほかのタブでも効く**：`DashboardView` はウィンドウの KeyDown で Esc を受けるが、ダッシュボードが見えているかを見ていない。タイムラインを拡大したままドキュメントタブへ移って Esc を押すと、見えないタイムラインの拡大が戻る（2026-10-09、利用者が mac の実機で再現を確認）。ドキュメントの拡大も同じ受け方にすると、先に登録されたダッシュボード側が Esc を取ってしまい、ドキュメントの拡大が戻らない。→ 両方とも「そのタブが見えているときだけ」にする（`IsEffectivelyVisible`）。

## 6. まだ確かめていないこと

- **Windows（WebView2）**：Q1〜Q7。WebView2 は Esc を「アクセラレータキー」として扱うので、Avalonia 側にも来るかもしれない（どちらに来ても 3-4 の作りで動く）。Windows の実機で確かめる。`invokeCSharpAction` が `file://` のページに入らなかったときの代わりは、独自の URL への移動（`NavigationStarted` で取り消して受ける）。
- Chromium（WebView2）には幅の変化で位置を保つ働きがあるので、Q5 の補正と二重に効かないか（補正は「覚えた要素の位置の差」だけ動かすので、すでに合っていれば動かない見込み）。
