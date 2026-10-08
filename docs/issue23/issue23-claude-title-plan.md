# Claude Code のセッションタイトル 実装計画（Issue #23）

> **連動ルール**：この md と `issue23-claude-title-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `docs/miharikun-requirements.md`（5.2 章 Claude Code の対応表の `custom-title` の行、10 章 派生値の「タイトル」、15 章 Phase 37）。設計の資料は `docs/issue23/issue23-claude-title-design.html`（確定）。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（7 章）**。
> 進め方：Core（テスト先行）→ 利用側（画面は変えない・確認だけ）→ 仕上げ。各フェーズの終わりに `dotnet test` と隔離環境で確かめ、要件定義 15 章の Phase 37 の「状況」を更新して報告する。コミットは頼まれたときだけ（ここは区切りの目安）。途中のコミットは `Refs #23`、最後だけ `Fixes #23`（PR 本文に `Closes #23`）。
> 作業ブランチ：`feature/issue23-claude-session-title`（`main` から切った）。

## 1. 全体像

```
Phase 37-A Core ──▶ Phase 37-B 表示の確認 ──▶ Phase 37-C 仕上げ
  2 コミット          1 コミット（コードは原則変えない）   1 コミット
```

- 新しい依存パッケージ：無い。
- 守る制約：`~/.claude/` には書かない（読み取りだけ）。`app.log` にタイトルの文字を書かない（件数・種類だけ）。App は共通イベント `AgentEvent` だけを扱う。Core はリフレクションを使わない。フィクスチャは実ログのコピーでなく手書き。
- **共通化のフェーズは無い**（挙動を変えない部品の切り出しは要らない）。代わりに「`TitleChanged` が状態・件数・最終活動・タイムラインを変えない」ことをテストで固定する（品質ゲート）。
- いまのテスト：Tests 1106 合格・スキップ 29、UiTests 52 合格・スキップ 3（HANDOFF.md の値。着手前に `dotnet test` で取り直す）。**件数は減らさない**。

### 品質ゲート（各フェーズの終わり）
1. テストが全部通る（件数を減らさない。スキップの数が増えない）。
2. `dotnet build` が警告を増やさない。Core が AOT 互換のまま（リフレクション・動的な型を使わない）。
3. Cursor のセッションの要約・タイムライン・検索文字列が変わらない（`TitleChanged` を持たないので、テストで確認。7.4）。
4. Claude の既存の要約（状態・依頼数・ターン数・最終活動・開始時刻）が、`custom-title` の行を足しても変わらない（7.4 のテスト）。
5. 確かめ方：隔離環境（`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CLAUDE_DIR` を一時フォルダにし、手書きのダミー会話ログを置く）。本物のデータは使わない。任意で `GoldenSummaryDump` による before/after の比較（本物のコピーが要るので、使うなら先に利用者の了承を取る）。

## 2. Phase 37-A：Core（テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 37-1 | `AgentEventKind.TitleChanged` と `ClaudeTranscriptNormalizer` | 種別を足す（`AgentEvent.cs`。Text = タイトル）。`custom-title` を `TitleChanged` に変換（7.1）。`_lastAt`（直前に見た timestamp）と `_lastTitle`（最後に出したタイトル）を持つ | `ClaudeNormalizerTests` に追加（7.1 の表の行ごと）。先にテストを書いて落ちるのを確かめてから実装 |
| 37-2 | `SessionAnalyzer` | 最後の `TitleChanged` を持ち、`SessionSummary.AutoTitle` を「Claude のタイトル → 最初の依頼の先頭 40 文字」にする（7.2）。`switch` に `TitleChanged` の case は足すが、他の値は変えない | `SessionAnalyzerTests` に追加（7.2 の表の行ごと）。`TimelineTests` に「`TitleChanged` が項目を作らない」。`ClaudeSessionSourceTests` に「追記読みでタイトルが後から付く・変わる」 |

完了条件：テストが通る。品質ゲート 1〜4。

## 3. Phase 37-B：表示の確認（画面は変えない）

| # | コミット | 内容 | 確認（隔離環境） |
|---|---|---|---|
| 37-3 | 表示の確認（コードは原則変えない） | `SessionMeta.DisplayTitle(autoTitle)` と画面は変えない。カードの `TextTrimming="CharacterEllipsis"`（`SessionCardView.axaml:30`）、詳細ヘッダー（`DetailHeaderView.axaml:26`）は既に省略表示。足りなければ最小の修正（7.5） | ダミー会話ログ（`custom-title` あり／なし／途中で変更／空白だけ）で、カード・詳細・最近の一覧・拡大時の見出し・検索に出ること。✏️の手動タイトルが優先され、空にして戻すと Claude のタイトルに戻ること。Cursor のカードが変わらないこと。ダーク・ライトで読めること |

完了条件：要件定義 12.2・12.3 のタイトルの表示が、Claude のカードでも崩れない。**ここで止まって報告する。**

## 4. Phase 37-C：仕上げ

| # | コミット | 内容 |
|---|---|---|
| 37-4 | 仕上げ | 要件定義 15 章の `## [ ] Phase 37` を `## [x]` に、「状況」を書く（コミットは Phase のコミットに入れる）。設計の html を「確定」のまま、計画の状態を「確定」に。`Fixes #23` |

## 5. ファイル構成（追加・変更）

```
src/Miharikun.Core/
├─ Agents/AgentEvent.cs                   37-1 変更：AgentEventKind に TitleChanged
├─ Agents/ClaudeTranscriptNormalizer.cs   37-1 変更：custom-title の変換、_lastAt・_lastTitle
├─ Agents/ClaudeFormatLog.cs              （変更なし。custom-title は KnownSkipped のまま。変換が先に処理する）
└─ Sessions/SessionAnalyzer.cs            37-2 変更：agentTitle、AutoTitle の決め方

tests/Miharikun.Tests/Core/
├─ ClaudeNormalizerTests.cs               37-1 変更：custom-title の行ごと
├─ SessionAnalyzerTests.cs                37-2 変更：AutoTitle の優先、他の値が変わらない
├─ TimelineTests.cs                       37-2 変更：TitleChanged が項目を作らない
└─ ClaudeSessionSourceTests.cs            37-2 変更：追記読み
（ClaudeLogBuilder.cs に custom-title の行を作る補助を足してよい）

docs/miharikun-requirements.md            37-4 変更：Phase 37 の [x]・状況
```

## 7. 実装の決めごと（迷いやすい所）

### 7.1 `custom-title` の変換（`ClaudeTranscriptNormalizer`）

前提（実ログで確認済み。構造のみ）：行の項目は `type`・`customTitle`・`sessionId` だけで **`timestamp` が無い**。手元 21 本すべてで、最初の `timestamp` の行より後に入る。同じ値が 1 本に数十回繰り返される。

| 入力（手書きフィクスチャ） | 結果 |
|---|---|
| `timestamp` のある行（`user` 等）→ `custom-title`（"A"） | `TitleChanged`（Text = "A"、At = 直前の timestamp、Seq = その行の行番号） |
| 同じ "A" が続けて何度も来る | 最初の 1 回だけ出す（`_lastTitle` と同じなら出さない。イベントが何百も増えないように） |
| "A" の後に "B" | "B" を出す（変わったら追従） |
| "B" の後にまた "A" | "A" を出す（直前と違うので） |
| `timestamp` のある行より前に来た | 出さない。`_lastTitle` も更新しない（後の繰り返しで拾える） |
| `customTitle` が空・空白だけ・文字でない・無い | 出さない。エラーにも数えない（`LogFormatException` にしない） |
| 前後の空白・改行を含む | `ReplaceLineEndings(" ")` して `Trim()` したものを Text にする |
| `sessionId` がファイル名のセッションと違う | 無視しない（今は照合しない。セッションはファイル名で決まる）。要確認：実ログでは一致していた（21/21） |
| 壊れた JSON の行 | 今まで通り（`ReportError`）。他の行に影響しない |

- `_lastAt` は `Convert` の先頭で `Timestamp(root)` が取れたら更新する（`user`/`assistant` 以外の `timestamp` 付きの行でも更新）。
- `TitleChanged` を出す行でも、`AddStarted`・`AttachBranch` は今まで通り動く（この行は `timestamp` も `gitBranch` も無いので何も起きないはず。`events.Count == 0` の経路を壊さない）。
- `_started |= events.Count > 0 && events[0].Kind == SessionStarted` の判定は、`TitleChanged` が `events[0]` になっても `_started` を誤って立てない（`Kind` を見ているので大丈夫。テストで固定）。
- 会話ログの非公開仕様なので、`app.log` に書くのは「`custom-title` の項目が想定と違う件数」程度に留め、**タイトルの文字は書かない**。

### 7.2 `SessionAnalyzer` のタイトル

| 入力（イベント列） | `AutoTitle` |
|---|---|
| 依頼だけ（Cursor・Claude 共通） | 最初の依頼の先頭 40 文字（今のまま） |
| `TitleChanged("A")` と依頼 | "A"（切らない。40 文字を超えても） |
| `TitleChanged("A")` → `TitleChanged("B")` | "B"（最後） |
| `TitleChanged` だけ（依頼がまだ無い） | そのタイトル（`SessionStarted` は先にある） |
| `TitleChanged` が空・空白（来ないはずだが） | 無視して依頼のタイトルに |
| どちらも無い | null（今の画面の「（依頼なし）」） |

- `SessionAnalyzer` の中で `agentTitle` と `promptTitle` を別々に持ち、最後に `agentTitle ?? promptTitle` を `AutoTitle` に入れる（`SessionSummary.AutoTitle` の名前・型は変えない。画面・検索・`SessionMeta.DisplayTitle` が使っているため）。
- `SessionAnalyzer` の `switch (e.Kind)` に `case AgentEventKind.TitleChanged:` を足す（タイトル更新だけ。`prompts`・`stops`・`tools` などは増やさない）。
- `last = events[^1]` が `TitleChanged` でも、`last.At` は直前の timestamp なので、最終活動・継続時間は変わらない。Claude には `SessionEnded` が無いので閉じ判定も変わらない。**ただし将来、timestamp の無い種別を `events[^1]` にしないよう、テストで固定する**（7.4）。

### 7.3 検索
`SessionSearch.BuildSearchText` は `summary.AutoTitle` を検索文字列に入れるので、コードの変更は要らない。Claude のタイトルで検索に当たることをテストで確かめる（`SessionSnapshot` のテスト）。

### 7.4 `TitleChanged` が他の値を変えないことの固定（品質ゲート 3・4）
同じ会話の「`custom-title` あり／なし」の 2 つのログから作った要約が、`AutoTitle` 以外で一致することをテストする（`State`・`PromptCount`・`TurnCount`・`StartedAt`・`LastActivityAt`・`Duration`・`Branch`・`ChangedFiles`・サブエージェント・`Turns`）。`Timeline.Build` の結果の項目数が同じ。Cursor のイベント列（`TitleChanged` を持たない）の要約が変わらない。

### 7.5 画面
- カード・詳細ヘッダーは既に `TextTrimming` がある。**長い Claude のタイトルで崩れないか**だけ、隔離環境のスクリーンショットかテストで見る（UiTests の `Scene.cs` に長いタイトルのカードがあれば流用）。
- ✏️で開いた入力欄の初期値は、現在の表示タイトル（`Title`）。Claude のタイトルが入った状態で、手動タイトルを付ける・空にして戻す、が今まで通り動く（`RenameState`）。

### 7.6 追記読みと作り直し
`ClaudeSessionSource` の `FileState.Reset` はノーマライザを作り直す（`_lastAt`・`_lastTitle` が空になる）。作り直しのあとは先頭から読み直すので、同じ結果になる。追記読みでは同じインスタンスが続くので、`_lastTitle` が効いて同じタイトルの繰り返しは出ない。cwd が見つかる前の行は `Pending` に溜めてまとめて変換する（順序は変わらない）。

## 8. リスクと対策

| リスク | 対策 |
|---|---|
| 会話ログは非公開仕様。`custom-title` の形が変わる・消える | 無い・壊れたものは出さず、最初の依頼のタイトルに戻る。`ai-title` は実物が出たときに足す |
| `TitleChanged` が状態・件数・最終活動・タイムラインに紛れ込む | 7.4 のテストで固定。`switch` に case を足し、他の値を触らない |
| 同じタイトルの繰り返し（1 本に数十〜百件）でイベントが膨らむ | `_lastTitle` で、変わったときだけ出す（7.1） |
| 手動タイトルが消える・Claude のタイトルで上書きされる | `SessionMeta.DisplayTitle` を変えない。手動が最優先のテスト（既存）を壊さない |
| 実ログをテストに写してしまう | フィクスチャは手書き（構造を真似る）。`customTitle` の文字は架空 |
| Cursor に影響 | Cursor は `TitleChanged` を出さない。品質ゲート 3 |

## 9. 決定事項（確認済み。設計の質問）
1. 手動タイトルが常に優先（Claude のタイトルより上）。
2. Claude 側でタイトルが変わったら、手動が無い限り自動で追従する（最後の値を採用）。
3. Cursor の `state.vscdb` の読み取りは対象外のまま（Cursor は最初の依頼の先頭 40 文字のまま）。
4. Q1：新しいイベント種別 `TitleChanged`。Q2：`At` は直前の timestamp。Q3：切らずに出す（カードで省略表示）。

計画で決めたこと（仕様にない細部。違えば言ってください）：
- 同じタイトルの繰り返しは出さない（変わったときだけ `TitleChanged`）。
- 空白だけ・文字でない `customTitle` は、エラーにせず出さない。
- 改行は空白にし、前後の空白を取る。
- `SessionSummary.AutoTitle` の名前は変えない（中身が「Claude のタイトル or 最初の依頼」になる）。
- `ClaudeFormatLog` の `KnownSkipped` は変えない。
- Phase 番号は 37（Issue #17 の 34〜36 の次）。コミット番号は 37-1〜37-4。

## 10. 実装するセッションへの注意（必ず読む）

### 10.1 始める前に
- 作業ブランチ `feature/issue23-claude-session-title` にいること。このブランチには、設計の html・この計画・要件定義の変更が**まだコミットされていない**ことがある（未追跡の `docs/issue23/`）。別の作業ツリーでは見えない。見つからなければ利用者に聞く。
- 計画と要件定義（5.2・10・15 章の Phase 37）に食い違いが無いか確かめる。あれば要件定義が正。判断がつかなければ聞く。
- `CLAUDE.md` の決まり：返答は日本語、コミット・push・PR・マージは頼まれたときだけ、`git add` はパス指定（`-A` は使わない）、`~/.claude/` に書かない、会話ログの確認は構造だけ（本文・コマンド・パスを引用しない）、`dotnet build`/`test` の前にアプリを止める、完了報告に「ビルドしたかと出力先・テスト件数・できなかった確認」。
- 着手前に `dotnet test` を回して、いまの件数を控える。

### 10.2 各フェーズの終わり
- 37-1 → 37-2 は、テストを先に書いて落ちるのを確かめてから実装する。
- フェーズの終わりに、テストの件数・ビルドの有無と出力先・できなかった確認を報告する。**37-3（表示の確認）で止まって報告する**。
- 37-2 の後に、利用者が望めばレビューを挟む（差分と品質ゲートの結果を渡す）。

**状態：確定**（レビューは挟まず実装。37-1〜37-4 完了、2026-10-08）。
