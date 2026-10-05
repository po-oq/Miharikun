# Issue #9 ドキュメントタブ 実装計画

> **連動ルール**：この md と `issue9-documents-plan.html`（図入りの説明）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `miharikun-requirements.md` の 12.7 / 6章 / Phase 13。この計画はその実装の分け方と、作るファイルの一覧。
> 進め方は他の Phase と同じ：要件定義 → Core（テスト先行）→ ViewModel → 画面。各 Phase の終わりに実機（隔離環境）で確認し、報告にビルドの有無と出力先を書く。

## 1. 全体像

- **5 Phase・12 コミット**（コミットは頼まれたときだけ。ここは「区切りの目安」）
- 作るもの：Core にロジック（除外・走査・索引・概要・設定）、新プロジェクト `Miharikun.Docs` に md→HTML、App に画面とプレビュー
- 新しい依存パッケージ：`Markdig`（md→HTML。**`Miharikun.Docs` だけ**）、`Microsoft.Web.WebView2`（**App だけ**）
- mermaid.js は CDN から読む（同梱しない）

### 層の分け方（なぜ Docs を別プロジェクトにするか）

| 層 | 置くもの | 参照してよい物 |
|---|---|---|
| `Miharikun.Core` | gitignore 判定・走査・索引・ツリー・絞り込み・概要・プロジェクト設定・Watcher | 標準ライブラリのみ（**AOT 互換のまま**。Hook も参照している） |
| `Miharikun.Docs`（新規） | md→HTML（Markdig、チェックボックス、mermaid、色付け、CSS） | Markdig のみ（Core は参照しない） |
| `Miharikun`（App） | 画面・ViewModel・WebView2・設定ダイアログ | Core、Docs、WebView2 |
| `Miharikun.Tests` | Core と Docs のテスト | Core、Docs |

- 理由：Markdig を Core に入れると、NativeAOT の Hook にも依存が混ざり、AOT 警告の原因になる（CLAUDE.md「Core はリフレクションを使わず AOT 互換」）。Docs を分ければ Core は無傷で、md 変換もテストできる（WPF の App はテストから参照できないため）
- Core の gitignore 判定は NuGet を使わず**自前の小さい実装**（`*` `?` `**` と上記の記法だけ。正規表現は使わず手書きのマッチで、速くて AOT 互換）

## 2. Phase 一覧

### Phase 13: Core（除外・走査・索引・概要・設定）
画面なし。テスト先行。

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 13-1 | 除外パターン | `GitIgnoreMatcher`（コメント・`!`・末尾 `/`・先頭/途中の `/`・`*` `?` `**`・大文字小文字無視・後勝ち）、`DefaultIgnore`（既定リスト） | `GitIgnoreMatcherTests` |
| 13-2 | 走査と索引 | `DocumentIndexer`（背景走査・枝刈り・拡張子絞り・バッチで通知・キャンセル）、`DocumentIndex`（追加/削除/更新）、`DocumentTree`（フォルダ別の子孫件数）、`DocumentFilter`（直下のみ・「すべて」・パス絞り込み・更新日降順） | `DocumentIndexerTests`（一時フォルダ）、`DocumentTreeTests`、`DocumentFilterTests` |
| 13-3 | 概要・設定・Watcher | `DocumentOverview`（タイトル抽出・行数・サイズ・作成/更新）、`ProjectSettingsStore`（`projects\{slug}-{hash8}.json` の読み書き、除外テキストと最後に開いたファイル）、`DocumentWatcher`（FileSystemWatcher＋デバウンス＋除外適用） | `DocumentOverviewTests`、`ProjectSettingsTests`、`DocumentWatcherTests` |

完了条件：1万ファイルの一時フォルダ生成テストが数秒以内に通る。`dotnet build --no-incremental` が警告 0（AOT 互換）。

### Phase 14: Docs（md → HTML）
画面なし。新プロジェクト `Miharikun.Docs`。

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 14-1 | プロジェクト追加とチェックボックス | `Miharikun.Docs.csproj`（Markdig）、`MarkdownRenderer`（`UseAdvancedExtensions`、タスクリスト）、`<base>` の付与 | `MarkdownRendererTests`：チェックボックス 6 ケース（未/済/大文字 X/入れ子/番号付き/コードブロック内・見出し内は変換しない） |
| 14-2 | mermaid・色付け・テーマ・CSS | mermaid（Markdig 標準の `<pre class="mermaid">` ＋ CDN スクリプト）、コードの色付け（highlight.js CDN）、ライト/ダーク CSS（埋め込みリソース）、タスクリストの黒丸消し | 同テストに追加（mermaid ブロックの出力、テーマ別 CSS） |

完了条件：6 ケースがすべて想定どおり。要件定義書そのもの（本物の md）を変換して、HTML がエラーなく出る。

### Phase 15: 画面（ツリー・一覧・絞り込み・概要・設定）
プレビューは空のまま（概要カードまで）。

| # | コミット | 内容 |
|---|---|---|
| 15-1 | タブの骨格と走査 | `DocumentsViewModel`、`FolderNodeViewModel`、`DocumentRowViewModel`、`DocumentsView`（ツリー｜一覧｜右ペイン）、MainWindow の「ドキュメント」タブのプレースホルダーを差し替え、走査の進捗表示、「読み直し」、「パスで絞り込み」 |
| 15-2 | 概要カード・復元・Watcher | 概要カード、最後に開いたファイルの保存と復元、Watcher の差分反映（追加/削除/名前変更/更新） |
| 15-3 | 設定ダイアログ | ⚙ メニューに「ドキュメントの設定…」、`DocumentSettingsDialog`（gitignore 形式のテキスト、既定に戻す、保存で即再走査） |

完了条件：実機（隔離環境）で、1万ファイル超のフォルダでも画面が固まらない。除外の変更が即反映され、再起動後も残る。ライト/ダーク両方で確認。

### Phase 16: プレビュー（WebView2）
| # | コミット | 内容 |
|---|---|---|
| 16-1 | html のプレビュー | WebView2 の組み込み（ユーザーデータは `%LOCALAPPDATA%\Miharikun\webview2\`）、Runtime が無いときの案内、html を `file:///` でそのまま表示（相対/絶対の css・js・画像、JavaScript 実行）、リンクの扱い（http は既定ブラウザ） |
| 16-2 | md のプレビュー | `MarkdownRenderer` の出力を一時 HTML（`%LOCALAPPDATA%\Miharikun\preview\`）に書いて表示、md 間リンクをアプリ内で開く（ツリー・一覧も追従）、テーマ連動、起動時に一時フォルダを掃除 |
| 16-3 | 自動再読込とボタン | ファイル保存で自動再読み込み、「再読み込み」「フォルダで開く」「既定のアプリで開く」 |

完了条件：相対 css・js・画像入りの html が崩れず表示される。本物の要件定義書で、チェックボックス・表・コード・mermaid が正しく出る。別 md へのリンクがアプリ内で開く。

### Phase 17: 仕上げ
| # | コミット | 内容 |
|---|---|---|
| 17-1 | 性能実測と配布確認 | 1万・5万ファイルの実測（ツリーが出るまでの時間、メモリ）、遅ければ索引キャッシュを検討（別 Issue）、Release の単一ファイル発行で WebView2 が動くか確認、要件定義 12.7・5章（ソリューション構成）・`[x]` の更新、`docs/release.md` の確認 |

完了条件：`scripts/publish.ps1` の成果物を実機で起動し、ドキュメントタブが動く。Runtime 未導入時の案内も確認。

## 3. ファイル構成（追加・変更）

```
src/
├─ Miharikun.Core/
│  ├─ Documents/                          ← 新規フォルダ
│  │  ├─ GitIgnoreMatcher.cs      13-1  除外判定（IsIgnored(相対パス, フォルダか)）
│  │  ├─ GitIgnorePattern.cs      13-1  1行ぶんの規則（手書きの glob マッチ）
│  │  ├─ DefaultIgnore.cs         13-1  既定の除外テキスト
│  │  ├─ DocumentEntry.cs         13-2  1ファイルの情報（相対パス・種別・更新日時・サイズ）
│  │  ├─ DocumentIndexer.cs       13-2  背景走査（枝刈り・バッチ通知）
│  │  ├─ DocumentIndex.cs         13-2  メモリ上の索引（追加/削除/更新）
│  │  ├─ DocumentTree.cs          13-2  フォルダ階層と子孫込み件数
│  │  ├─ DocumentFilter.cs        13-2  直下/すべて・絞り込み・並び
│  │  ├─ DocumentOverview.cs      13-3  タイトル・行数・サイズ・日時
│  │  ├─ DocumentChangeClassifier.cs 13-3 イベント→差分/再走査の振り分け（純粋関数）
│  │  └─ DocumentWatcher.cs       13-3  FileSystemWatcher＋デバウンス
│  ├─ Settings/
│  │  └─ ProjectSettingsStore.cs  13-3  projects\{slug}-{hash8}.json
│  └─ Storage/AppPaths.cs         13-3  変更：ProjectSettingsFile() を追加
│
├─ Miharikun.Docs/                        ← 新規プロジェクト（Markdig）
│  ├─ Miharikun.Docs.csproj       14-1
│  ├─ MarkdownRenderer.cs         14-1  md→HTML（タスクリスト・<base>・mermaid）
│  └─ Assets/
│     ├─ markdown-base.css        14-2  埋め込みリソース（共通のスタイル）
│     ├─ markdown-light.css       14-2  埋め込みリソース（ライトの色）
│     └─ markdown-dark.css        14-2
│
└─ Miharikun/                             （App）
   ├─ Miharikun.csproj            16-1  変更：WebView2 パッケージ、Docs への参照
   ├─ MainWindow.xaml             15-1  変更：ドキュメントタブを差し替え、⚙ と対象フォルダ表示を共通ヘッダーへ移動、⚙ に項目追加(15-3)
   ├─ ThemeService.cs             16-2  変更：Changed イベントを追加
   ├─ Views/                             ← 新規フォルダ
   │  ├─ DocumentsView.xaml(.cs)  15-1  ツリー｜一覧｜右ペイン
   │  ├─ DocumentPreview.xaml(.cs)16-1  WebView2 を包む部品
   │  └─ DocumentSettingsDialog.xaml(.cs) 15-3
   ├─ ViewModels/
   │  ├─ DocumentsViewModel.cs    15-1
   │  ├─ FolderNodeViewModel.cs   15-1
   │  ├─ DocumentRowViewModel.cs  15-1
   │  ├─ DocumentOverviewViewModel.cs 15-2
   │  └─ DocumentSettingsViewModel.cs 15-3
   ├─ PreviewFiles.cs             16-2  md の一時 HTML の書き出し・掃除
   └─ Themes/Colors.*.xaml        15-1  変更：ツリー・一覧用の色を追加（直書きしない）

tests/Miharikun.Tests/
├─ Miharikun.Tests.csproj         13-1/14-1  変更：Docs への参照
├─ Core/
│  ├─ GitIgnoreMatcherTests.cs    13-1
│  ├─ DocumentIndexerTests.cs     13-2
│  ├─ DocumentTreeTests.cs        13-2
│  ├─ DocumentFilterTests.cs      13-2
│  ├─ DocumentOverviewTests.cs    13-3
│  ├─ DocumentWatcherTests.cs     13-3
│  └─ ProjectSettingsTests.cs     13-3
└─ Docs/
   └─ MarkdownRendererTests.cs    14-1/14-2

Miharikun.slnx                    14-1  変更：Miharikun.Docs を追加
```

実行時に増えるデータは次の 3.5 に詳しく書く。

## 3.5 実行時に増えるデータ（詳細）
すべて `%LOCALAPPDATA%\Miharikun\` の下（`MIHARIKUN_DATA_DIR` で移せる。検証は隔離フォルダで行い、本物のデータには触れない）。

| 場所 | 中身 | 書く人・タイミング | 大きさ | 消えたら | 掃除 |
|---|---|---|---|---|---|
| `projects\{slug}-{hash8}.json` | プロジェクトごとの設定：除外パターン（gitignore 形式のテキスト）、最後に開いたファイル、対象フォルダのパス（見分け用）。例：`C:\work\proj` → `c-work-proj-1a2b3c4d.json` | App。設定ダイアログで保存したとき、ファイルを選んだとき | 1KB 未満 | 除外は既定値に戻り、最後のファイルの復元がなくなるだけ | しない（利用者の設定なので残す） |
| `webview2\` | WebView2（Edge エンジン）の作業フォルダ：キャッシュ、Cookie、localStorage、GPU キャッシュ。mermaid.js（CDN）のキャッシュ、html 内の JavaScript が保存したものもここ | WebView2。プレビューを初めて開いたとき作られる | 数十MB（使うと増える） | 消して問題なし。次のプレビューで作り直す | 自動ではしない（要望があれば「キャッシュを消す」を足す） |
| `preview\` | md を HTML にした一時ファイル。**1 つの md につき 1 ファイル**（名前はパスのハッシュ。同じ md は上書きなので無限には増えない）。`<base>` を元フォルダにして画像・相対リンクを効かせるために必要 | App。md を選んだとき・保存で再読み込みしたとき | 1ファイル数十KB〜数MB | 次に開くとき作り直す | 起動時に、**1 日より古いファイルだけ**消す（複数起動中の別の Miharikun が使っているファイルを消さないため） |

- どれも**文書そのものは変えない**（読み取り専用）。`preview\` には会社の md の内容のコピーが一時的に残る（同じ PC・同じ利用者の領域。気になるなら起動時の掃除を「全部消す」に変えられる）
- `webview2\` を専用の場所にする理由：標準では exe の隣（`Miharikun.exe.WebView2`）に作られ、ダウンロードフォルダなどを汚す／書き込めない場所で失敗するため
- 複数の Miharikun を起動しても `webview2\` は共有して動く（WebView2 の標準の動作）

## 4. データの流れ

```
対象フォルダ ─ DocumentIndexer（背景。除外で枝刈り）─▶ DocumentIndex ─▶ DocumentTree / DocumentFilter ─▶ ツリー・一覧
                          ▲ ProjectSettingsStore（除外テキスト）          ▲ DocumentWatcher（差分）
一覧で選択 ─▶ DocumentOverview（概要カード）
          ├─ .html ─▶ WebView2 が file:/// で直接開く
          └─ .md   ─▶ MarkdownRenderer（Markdig＋mermaid＋CSS）─▶ preview\*.html ─▶ WebView2
```

## 5. リスクと対策

| リスク | 対策 |
|---|---|
| 1万ファイル超で遅い | 枝刈り・背景走査・バッチ通知・仮想化。13-2 のテストと 17-1 の実測で確認。遅ければキャッシュを別 Issue に |
| WebView2 が単一ファイル発行で動かない | 16-1 の時点で Release 発行を試す（17-1 まで待たない） |
| Runtime 未導入 | 起動時に確認して案内表示。ツリー・一覧・概要は使える |
| md の相対リンクが効かない | `<base>` に元フォルダの `file:///` を付ける。16-2 で実機確認 |
| ツリーの更新で画面がちらつく | Watcher はデバウンスして差分だけ反映。ツリーの展開状態を保つ |

## 6.5 レビュー反映（Opus 5.5 のレビュー 16 件 → すべて採用）
レビュー原本：`issue9-documents-review.html`。指摘は実コードと突き合わせて確認した（⚙ の位置、`ThemeService`、`ProjectPath`、Markdig の出力）。**不採用なし**。ただし 2 点は、レビューの案を実測で直した。

- **直した点 A（見出し id）**：レビューの `#` リンク用スクリプトは `decodeURIComponent(href)` で id を探す案だが、`UseAdvancedExtensions()` の既定の見出し id は ASCII だけで、日本語見出しは `id="section"` になる（実測）。リンクは `#%E6%A6%82%E8%A6%81` なので一致しない。→ `UseAutoIdentifiers(AutoIdentifierOptions.GitHub)` を **`UseAdvancedExtensions()` より前**に呼ぶ（実測：`id="概要"`、重複は `概要-1`）。これで同じスクリプトが動く
- **直した点 B（mermaid）**：レビューは `<div class="mermaid">` と書くが、実測では Markdig 標準が `<pre class="mermaid">` を出す。変換は不要で、改行を守る CSS（`white-space:pre`）も不要

| Phase | 追加・変更 | 指摘 |
|---|---|---|
| 13-1 | `GitIgnoreMatcher.IsPathExcluded(relPath)`（`a`、`a\b`、`a\b\c.md` と祖先を順に判定し、1つでも当たれば除外）。走査の枝刈りと同じ結果になることをテスト | 4 |
| 13-2 | 走査は `FileSystemEnumerable`＋`ShouldRecursePredicate`（枝刈り）＋`ShouldIncludePredicate`（拡張子）、`IgnoreInaccessible = true`、`ReparsePoint`（ジャンクション等）には入らない。バッチに**世代番号**（再走査ごと +1。古い世代は捨てる）。**Watcher は走査より先に開始**。索引は相対パス（大文字小文字無視）をキーにした辞書（重複しない）。正しさのテストは数百ファイルで、1万ファイルの計測は `[Trait("Category","Perf")]` に分離（通常の `dotnet test` から外す） | 5, 11, 12 |
| 13-3 | **Watcher の方針**：ファイルの差分（追加/削除/更新/rename）だけ自分で索引へ反映し、**フォルダの作成/削除/rename・`Error`（バッファあふれ）は全再走査を予約**（1秒デバウンス、走査中なら終わってから 1 回だけ）。`Filters` は空（拡張子で絞らない）、`NotifyFilter = FileName｜DirectoryName｜LastWrite｜Size`、`InternalBufferSize = 64KB`。除外は親フォルダまでさかのぼって判定。フォルダの Changed と対象外ファイルは捨てる。「イベント → 差分 or 再走査」の振り分けは純粋関数 `DocumentChangeClassifier` に切り出して行ごとにテスト（実物の Watcher のテストは 1〜2 本）。デバウンスは `SessionMonitor` に揃える。**設定の保存は「読む → 該当キーだけ変える → 全体を `AtomicFile` で書く」**（知らないキーも残す。`JsonNode`）。ファイル名のキーは `ProjectPath.Normalize` を流用。`lastOpened` が消えたファイルなら復元しない（テスト） | 4, 10 |
| 14-1 | Docs は **`net10.0`（`-windows` にしない）、Core への参照なし**。タスクリストは Markdig 標準（6 ケースは「Markdig の出力の確認テスト」。最初にテストで出力を固定してから CSS）。見出し id は直した点 A。`#` で始まるリンクをページ内スクロールに横取りする小さなスクリプトを出力に入れる。base の URL は `new Uri(folder + "\\").AbsoluteUri`（末尾 `/` 必須）で作り、日本語・空白・`#`・`%` を含むフォルダ名のテストを足す（html を開く `file:///` URL も同じ関数） | 2, 6, 13 |
| 14-2 | mermaid は標準の `<pre class="mermaid">`（直した点 B）。mermaid にもテーマを渡す（`initialize({theme: dark ? 'dark' : 'default'})`）。**コードの色付け：highlight.js を CDN から読み、`language-mermaid` 以外の code に適用**（テーマ別の hljs CSS も CDN。繋がらないときは色なし）。テストは「`class="language-xx"` が付く」「hljs の script が入る」まで | 3, 6 |
| 15-1 | **⚙ ボタンと対象フォルダ表示を、ダッシュボードタブの中から TabControl の外の共通ヘッダーへ移す**（`Grid.Row="1"` の同じセルに右上寄せで重ねるのが手数最少）。ToolTip を「Hook の導入・削除、テーマ、ドキュメントの設定」に。ダッシュボードの拡大モード（12.4.1）の見た目が変わらないか確認。**索引を変えるのは UI スレッド 1 か所**（バッチも Watcher の差分も `Dispatcher.InvokeAsync` で送る。Core の `DocumentIndex` はスレッド非安全でよい）。画面への反映は 200ms 程度でまとめ、ツリーの展開状態はフォルダの相対パスで覚えて戻す。走査結果・選択・展開状態は View ではなく `DocumentsViewModel` に持つ（タブ切替で View が外れるため） | 1, 5, 9 |
| 15-2 | 概要の読み込み（行数・タイトル）は `Task.Run`、選択が変わったら破棄。タイトル抽出は先頭 64KB だけ。UTF-8 前提、読めなければファイル名にフォールバック | 14 |
| 15-3 | 設定ダイアログは**別 Window（`ShowDialog`）**（WebView2 の上に重なるオーバーレイは隠れるため） | 9 |
| 16-1 | **リンクの振り分け**（`NavigationStarting` と `NewWindowRequested` の両方）：①http/https → キャンセルして既定ブラウザ ②対象フォルダ内の .md/.html/.htm → キャンセルしてアプリ内で選択（html→html も同じ。ツリー・一覧・lastOpened も追従）③対象フォルダ外・除外フォルダ内の .md/.html → 既定のアプリ ④その他のローカルファイル・フォルダ → 既定のアプリ／エクスプローラー ⑤`#xxx` だけ → そのまま通す。**airspace**：WebView2 の上に WPF 要素を重ねない（Runtime 案内・読み込み中表示は `Visibility` でプレビュー領域と入れ替える）。タブ切替で WebView2 が作り直されるので、`Loaded` で「選択中のファイルを表示」を呼び直す。確認項目に「タブを行き来しても表示が戻る」を足す | 8, 9 |
| 16-2 | `ThemeService` に `event Action<bool> Changed`（isDark。⚙ からの変更と OS 変更の両方で発火）を足し、`DocumentsViewModel` は受けたら表示中の md を作り直す（html は変えない）。preview の書き込みは `AtomicFile`、IOException は握ってログ（複数起動の同時書き込み対策） | 7, 15 |

- 5章のリスク表から「要件定義の Phase 13 見出しは着手時に書き換える」の行を削除した（要件定義は既に Phase 13〜17。指摘 16）
- レビュー 2 の代案（`WebResourceRequested` の仮想ホスト方式）は、手数が増えるため**今回は採用しない**（`<base>` と #リンク用スクリプトで足りる。相対パスやリンクで問題が出たら 16-2 の時点で見直す）

## 6. 決定事項（確認済み）

1. Docs を別プロジェクトにする（Core を AOT 互換に保つ）。
2. Phase は 5 つ（13〜17）。要件定義の見出しも書き換え済み。
3. gitignore 判定は自前実装（NuGet を使わない）。
4. 進める順は 13 → 14 → 15 → 16 → 17。

次の一歩：Phase 13-1（`GitIgnoreMatcher` のテスト先行）。
