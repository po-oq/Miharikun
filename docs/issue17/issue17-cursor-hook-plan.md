# Cursor の Hook の置き場所・Hook なしの警告・transcript のリアルタイム取り込み 実装計画（Issue #17）

> **連動ルール**：この md と `issue17-cursor-hook-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `docs/miharikun-requirements.md`（6 章 settings.json・**8.2**・9 章のファイル監視・10 章の状態の表・**11 章の「Issue #17 で変更」**・12.9・**12.11**・Phase 34〜36）。設計の資料は `docs/issue17/issue17-cursor-realtime-review.html`（原因の切り分けと Q1〜Q5 の回答）。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（7 章）**。
> 進め方：Core（テスト先行）→ 画面 → 仕上げ。各 Phase の終わりに隔離環境（`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`）で確かめ、要件定義の `[x]` と「状況」を更新して、ビルドの有無と出力先を報告する。コミットは頼まれたときだけ（ここは区切りの目安）。途中のコミットは `Refs #17`、最後だけ `Fixes #17`。

## 1. 全体像

```
Phase 34 Core ──▶ 35 画面 ──▶ 36 仕上げ
  4 コミット        2 コミット     1 コミット
```

- 原因（会社の PC で `%LOCALAPPDATA%` 配下の exe が動かない）への対策は **Hook の置き場所の設定＋既存の登録の受け入れ**（34-1・35-1）。保険が **transcript のリアルタイム取り込み**（34-2）と **Hook なしの判定・警告**（34-3・35-2）。
- 新しい依存パッケージは無い。フォルダ選択は .NET 標準の `Microsoft.Win32.OpenFolderDialog`。
- Core は AOT 互換のまま（リフレクションを使わない。JSON は `TryGetValue`）。**Hook exe（`src/Miharikun.Hook`）は変えない**。Hook の記録先は exe の場所に関係なく `AppPaths.Default()`（`%LOCALAPPDATA%\Miharikun`）のまま（`Program.cs`）。
- Cursor のフォルダ（`~\.cursor\projects\…\agent-transcripts\`）は**読むだけ**。監視のためにフォルダを作らない（`CreateIfMissing: false`）。
- 共通化（挙動を変えないリファクタ）のフェーズは無い。ただし 34-2 は既存の取り込みの経路を変えるので、**既存の取り込みのテストを全部通すこと**を完了条件に入れる（例外は 1 件だけ：`CursorSessionSourceFailureTests.A_failing_import_scan_is_tried_again_on_the_next_read` は「成功したらもう Scan を呼ばない」を確かめているので、意図を変えて書き直す。34-2）。
- レビュー（`docs/issue17/issue17-cursor-hook-review.html`）の指摘 #1〜#13 と、その Q1〜Q3（すべて A）を反映済み。
- いまのテスト：**842 件合格 + スキップ 5**（`PerfFact`。worktree で実行して確認）。**件数は減らさない**。
- ブランチ：`feature/issue17-cursor-hook`（main から。作業フォルダは worktree `C:\zDev\repo\Miharikun-issue17`）。Issue #22（macOS 対応・Avalonia へ移す）が別ブランチで進んでいる。**#22 の Phase 28（ViewModel の切り出し）より先に、この Issue を main に入れる**のが手戻りが少ない（8 章）。

## 2. Phase 34：Core（テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 34-1 | Hook の置き場所 | `AppSettingsStore`：`LoadHookDir() → string?`（欠けている・空・文字列でない・完全なパスでない（`Path.IsPathFullyQualified`）・`Path.GetFullPath` が例外 → null。null にしたときは `app.log` に 1 行。返す値は正規化済み：`GetFullPath`＋`Path.TrimEndingDirectorySeparator`）、`SaveHookDir(string? dir) → bool`（null → キーを消す。ファイルもキーも無ければ何も書かない。他のキーは残す。読めない・書けないときは false。7.5）。`HookInstaller`：`CreateDefault(paths, appDirectory, hookDir)`（null → 既定の `paths.Root\bin`。7.5 のフォルダを作るかの区別も渡す）、`DefaultHookDir(paths)`、`ToSettingValue(dir, paths) → string?`（正規化して、既定と同じなら null。受け入れと設定画面の保存で共通。7.5）、`TryParseExePath(command) → string?`（7.2）、`SameRegistration(command, exePath) → bool`（7.2）、`FindRegisteredElsewhere() → string?`（7.1。採用できる別の場所の exe のフルパス。無ければ null）。**`GetState` の「一致」を `SameRegistration` に替える**（文字列の完全一致をやめる。レビュー Q1） | `LoadHookDir`：無い・空・数値・文字列・相対パス → null・不正な文字（NUL）→ null・末尾の `\` あり → 無しと同じ値。`SaveHookDir`：書く・null で消す・他のキーが残る・ファイルが無く null → ファイルを作らない・読めないときは書かず false。`ToSettingValue`：既定と同じ（大文字小文字・末尾の `\` 違い）→ null。`TryParseExePath`：`C:/dev/Miharikun.Hook.exe --agent cursor`・引用符つきの空白入り・引数なし・Miharikun 以外・相対パス → null・`--agent` の前に空白 2 つ。`SameRegistration`：`\` 区切り・空白なしの引用符・大文字小文字違い → true／引数なし・`--agent` だけ・別の場所 → false。`GetState`：書き方だけ違う 13 件 → `Installed`・引数なしの 13 件 → `Partial`。`FindRegisteredElsewhere`：7.1 の表の行ごと。`CreateDefault` に dir を渡すと `InstalledExePath` がそこになり、`GetState` が `Installed`・dir のフォルダが無い → `Install` は失敗し、hooks.json もフォルダも作らない（既定の `bin` は今まで通り作る。7.5）（既存のテストは全部そのまま通る） |
| 34-2 | transcript の変わった分だけの取り込み | `WatchTarget` に `IncludeSubdirectories`（既定 false）を足し、`SessionMonitor` が `FileSystemWatcher.IncludeSubdirectories` に渡す。`CursorTranscriptImporter`：`TranscriptDirs(projectFolder)`（監視先用。いまの `FindTranscriptDirs` を公開）、`Scan` は**前回から長さか更新日時が変わったファイルだけ**返す（初回は全部。7.3）。読むときは `FileShare.ReadWrite \| Delete`。最後の行は「改行で終わる、または JSON として最後まで読める」なら使い、それ以外（書きかけ）は飛ばす（レビュー Q3）。覚える値は開く前に取る（7.3）。`skip` が true の ID は覚えた値も消す。`CursorSessionSource`：`_importScanned` をやめて毎回 `Scan` を呼ぶ。Scan の失敗のログは、同じ文なら 1 回だけ（成功したら忘れる）。`WatchTargets` に transcript のフォルダ（`*.jsonl`・サブフォルダ込み・`CreateIfMissing: false`）を足す | 一時フォルダ：初回は全部・変わらなければ 2 回目は空（既存の `A_transcript_is_imported_once_and_not_reported_again` はそのまま通る）・追記したら同じキーで `Replace`・新しい transcript が増えたら出る・hook の記録がある ID は出ない・途中で切れた最後の行は飛ばし、続きが書かれたら出る・**末尾に改行の無い完全な行は使う**・読んでいる途中で追記された分は次の Scan で出る（テスト用の差し込み口で、読み終わる前にファイルを伸ばす）・他のプロセスが書き込みで開いていても読める・`subagents\` は見ない・transcript が消えてもセッションは消えない・取り込み → hook のファイルができて切り替え → hook のファイルを消す → 次の Scan で取り込みに戻る。`CursorSessionSource`：Scan が同じ例外を 3 回投げてもログは 1 行。`SessionMonitor`：サブフォルダ内のファイルの変更で通知が来る（`IncludeSubdirectories`）。既存の取り込みのテスト（`ProjectEventStoreImportTests`・`CursorSessionSourceFailureTests`・`ProjectEventStoreCharacterizationTests`）が全部通る。ただし `A_failing_import_scan_is_tried_again_on_the_next_read` は書き直す：失敗 → 次の回に再試行（`Calls == 2`）・成功の後も毎回呼ぶ（`Calls == 3`）が差分は空。件数は変えない |
| 34-3 | Hook なしの判定 | `HookRegistration`（新規。7.4）：`new HookRegistration(hooksJsonPath, eventsDir)`、`Stamp() → DateTimeOffset?`（hooks.json の更新日時。無ければ null。stat だけ）、`Since() → DateTimeOffset?`。`AgentEvent` に `bool HookMissing = false`。`CursorTranscriptImporter` のコンストラクタに `HookRegistration?`（null → すべて Imported）を渡す。transcript の更新日時が `Since` より後なら、その回のイベントを全部 `HookMissing: true` にする。`Scan` のたびに `Stamp()` を見て、前回と違えば `Since` を取り直し、**覚えた値を全部消して全 transcript を読み直す**（レビュー Q2。7.4）。`SessionState.NoHook` を足し、`SessionAnalyzer` で「全部 Imported かつ 1 つでも HookMissing → NoHook」。`SessionText.StateName(NoHook)` = 「Hook なし」、ラベルは「⚪ Hook なし」。`SessionText.IsTranscriptOnly(state)`（Imported か NoHook。7.6）を足し、`RecentActivity` の除外をそれに替える | `HookRegistration.Since`：登録なし → null・登録あり・events 無し → hooks.json の更新日時・events あり → 早いほう・hooks.json が壊れている → null。`Stamp`：無い → null・書き換えると変わる。importer：since より後 → HookMissing・前 → そうでない・registration が null → そうでない・`Stamp` が変わらなければ変わっていない transcript は読まない・hooks.json から登録を外す → 次の Scan で NoHook だったものが Imported で出直す・登録を足す（更新日時は transcript より後）→ 読み直すが Imported のまま。`SessionAnalyzer`：NoHook・Imported のまま・hook のイベントが混ざれば今まで通り。`SessionText`：名前とラベル。`RecentActivity`：NoHook の入力を出さない。`StalledRule`：NoHook はそのまま |
| 34-4 | 組み立て（App の起動） | `App.xaml.cs`：`settings.LoadHookDir()` を `HookInstaller.CreateDefault` に渡す。`CursorTranscriptImporter` に `new HookRegistration(…\hooks.json, paths.EventsDir("cursor"))` を渡す。画面はまだ変えない（NoHook は灰色の丸・「Hook なし」で出るだけ） | ビルドが通る。隔離環境で：hooks.json に登録あり＋events 無し＋新しい transcript → 起動中に「Hook なし」のカードが数秒で出る・transcript に追記 → タイムラインが増える・hooks.json から登録を外す → 数秒で「閉じた（導入前）」に戻る。本物の transcript で、**末尾が改行で終わるか**を数える（先に利用者に了承を取る。読むだけ・コピーしない。各ファイルの最後の 1 バイトだけ見て、件数だけ報告する。中身は見ない・引用しない） |

完了条件：テストが通る（842 件以上）。`dotnet build src/Miharikun.Core --no-incremental` が警告 0（AOT 互換）。

## 3. Phase 35：画面

| # | コミット | 内容 | 確認（隔離環境） |
|---|---|---|---|
| 35-1 | 置き場所：起動時の受け入れと設定画面 | `HookSetup` は `Func<string?, HookInstaller>`（置き場所を受け取って作る）と `AppSettingsStore` を持つ。`CheckAtStartup` の順番は 7.1 の図（`Unreadable` → 受け入れ → `CanInstall` の確認 → 今までの提案）。受け入れたら `SaveHookDir(ToSettingValue(フォルダ))` して `app.log` に 1 行（「hook の置き場所を hooks.json の登録から採用: <フォルダ>」。保存に失敗したら「…（保存できなかったので今回だけ使う）」）、**受け入れたフォルダを直接渡して**作り直した installer で状態を見直す（設定を読み直さない。7.1）。`AppSettingsDialog`：「Cursor の Hook exe の置き場所」の入力欄＋「参照…」（`OpenFolderDialog`）＋「既定に戻す」。存在しないフォルダ・完全なパスでない値は保存できない（エラーの文を出す）。「保存」の有効・無効は、分と置き場所の両方の検査をまとめた `Revalidate()` で決め、両方の `TextChanged` から呼ぶ。`MainWindow.OnSettingsClick`：場所が変わったら `SaveHookDir(ToSettingValue(…))` → 「この場所に Hook を導入し直しますか？」→ はいで `HookSetup.InstallFromMenu`（7.5） | hooks.json を手で `C:/…/別の場所/Miharikun.Hook.exe` に（13 イベント全部）書き換えて起動 → ダイアログが出ず、settings.json に `hookDir` が入り、`app.log` に 1 行・次の起動でもダイアログが出ない・`\` 区切り（`C:\…\Miharikun.Hook.exe --agent cursor`）で書き換えても同じ（Q1）・`--agent cursor` を消して書き換え → 「登録し直します」・settings.json を別のプロセスが排他で開いている間に起動 → 受け入れは今回だけ使われ、ダイアログが出ず、hooks.json は変わらない・別の場所の exe の中身が同梱と違う → 「更新」の提案 → はいで**その場所に**コピー（`%LOCALAPPDATA%` 側は変わらない）・別の場所が一部のイベントだけ → 今まで通り「登録し直します」・exe が無い → 同じ。設定画面：参照で選ぶ・無いフォルダ・相対パス → 保存できない（分を打ち直しても保存できないまま）・既定に戻す → `hookDir` が消える・場所を変えて保存 → 導入し直す？→ はい → hooks.json の 13 件がその場所になり exe がコピーされる／いいえ → hooks.json は変わらない（次の起動の確認で「登録し直します」は出ない：受け入れで元の場所に戻る。7.5） |
| 35-2 | Hook なしの表示と警告 | `SessionDetailViewModel` の `imported` 判定を `SessionText.IsTranscriptOnly` に替える（チェック欄の文は 7.6）。`MainViewModel`：`NoHookCount`（カードの中の NoHook の数）と `HookWarningText`、`OpenHookErrorLogCommand`（`ShellOpen.Open`。ファイルが無ければ押せない）。`UpdateCounts` の件数は今のまま（NoHook はどの件数にも入れない）。`MainWindow.xaml`：左ペインの最上段（`Counts` の上）に黄色の帯（`NoHookCount > 0` のときだけ）。帯の色はテーマのリソース（ライト/ダーク）を足す | events 無し＋transcript 2 件（Hook の登録より後）→ 帯「…（Hook なし 2 件）…」・カードは灰色の丸「Hook なし」・詳細は「—」・「最近の入力」に出ない。hook-error.log がある → ボタンで開く／無い → 押せない。events の記録を後から置く（Hook が直った）→ そのセッションが Hook の記録に切り替わり、帯の件数が減り、0 で消える。Hook の登録より前の transcript は「閉じた（導入前）」のまま。⚙ →「hook を削除」→ 数秒で帯が消え、カードは「閉じた（導入前）」に（Q2）。ライト/ダーク。本物の Cursor を使う確認は 10.2（利用者の作業） |

完了条件：8.2・12.9・12.11 の各動作を隔離環境で確かめる。ライト/ダークの両方。既存の起動時の導入ダイアログ（未導入・一部・更新）が今まで通り出る。

## 4. Phase 36：仕上げ

| # | コミット | 内容 |
|---|---|---|
| 36-1 | 仕上げ | 配布 zip の `README.txt`（`scripts/publish.ps1`）に「会社の PC などで Hook が動かないとき（⚙ → 設定… → Hook exe の置き場所）」と「Hook なし」の意味（導入・置き場所の変更の直後に出たら、まず Cursor を再起動する）を足す。`docs/release.md` の出す前のチェックに 35-1・35-2 の確認を足す。要件定義の `[x]` と「状況」。`CLAUDE.md` は変えない（守ることに変化が無い）。`Fixes #17` |

## 5. （欠番：共通化のフェーズは無い）

## 6. ファイル構成（追加・変更）

```
src/Miharikun.Core/
├─ Settings/AppSettings.cs              34-1 変更：LoadHookDir（検査・正規化）/ SaveHookDir（キーを消す処理・bool）
├─ Install/HookInstaller.cs             34-1 変更：CreateDefault(…, hookDir)・DefaultHookDir・ToSettingValue・TryParseExePath・
│                                                  SameRegistration（GetState の一致）・FindRegisteredElsewhere・設定の場所は作らない
├─ Install/HookRegistration.cs          34-3 新規：Stamp()・Since()
├─ Sessions/ISessionSource.cs           34-2 変更：WatchTarget に IncludeSubdirectories
├─ Sessions/SessionMonitor.cs           34-2 変更：IncludeSubdirectories を Watcher に渡す
├─ Agents/CursorTranscriptImporter.cs   34-2 変更：TranscriptDirs・変わった分だけ・共有して読む・最後の行
│                                       34-3 変更：HookRegistration → HookMissing・登録が変わったら全部読み直す
├─ Sessions/CursorSessionSource.cs      34-2 変更：毎回 Scan・失敗のログは 1 回・WatchTargets に transcript
├─ Agents/AgentEvent.cs                 34-3 変更：HookMissing
├─ Sessions/SessionSummary.cs           34-3 変更：SessionState.NoHook
├─ Sessions/SessionAnalyzer.cs          34-3 変更：NoHook の判定
├─ Sessions/SessionText.cs              34-3 変更：「Hook なし」・IsTranscriptOnly
└─ Sessions/RecentActivity.cs           34-3 変更：IsTranscriptOnly で除外

src/Miharikun/
├─ App.xaml.cs                          34-4 変更：hookDir・HookRegistration の組み立て
├─ HookSetup.cs                         35-1 変更：installer の作り直し（受け入れたフォルダを直接渡す）・受け入れ
├─ Views/AppSettingsDialog.xaml(.cs)    35-1 変更：置き場所の欄・参照・既定に戻す・存在チェック・Revalidate
├─ MainWindow.xaml.cs                   35-1 変更：OnSettingsClick で置き場所の保存と導入し直し
├─ ViewModels/MainViewModel.cs          35-2 変更：NoHookCount・HookWarningText・OpenHookErrorLogCommand
├─ ViewModels/SessionDetailViewModel.cs 35-2 変更：IsTranscriptOnly
├─ MainWindow.xaml                      35-2 変更：左ペイン最上段の警告の帯
└─ Themes/Colors.Light.xaml / Colors.Dark.xaml  35-2 変更：警告の帯の色

scripts/publish.ps1・docs/release.md    36-1 変更

tests/Miharikun.Tests/Core/
├─ AppSettingsTests.cs                  34-1 追加：hookDir
├─ HookInstallerTests.cs                34-1 追加：TryParseExePath・SameRegistration・GetState（書き方違い）・FindRegisteredElsewhere・CreateDefault(dir)
├─ CursorTranscriptRealtimeTests.cs     34-2 新規：変わった分だけ・共有・最後の行・読む途中の追記・skip で忘れる・subagents
├─ CursorSessionSourceFailureTests.cs   34-2 変更：成功の後も毎回 Scan（1 件を書き直し）・失敗のログは 1 回
├─ SessionMonitorWatchTests.cs          34-2 追加：サブフォルダの通知
├─ HookRegistrationTests.cs             34-3 新規：Stamp・Since
├─ CursorTranscriptRealtimeTests.cs     34-3 追加：HookMissing・登録が変わったら読み直す
└─ SessionAnalyzerTests.cs・SessionTextAndSearchTests.cs・RecentActivityTests.cs  34-3 追加：NoHook
```

## 7. 実装の決めごと（迷いやすい所）

### 7.1 起動時の確認と「別の場所の登録」の受け入れ
`HookSetup.CheckAtStartup` の順番：
1. `GetState()` が `Unreadable` → 今まで通り警告して終わり。
2. `Installed` → 終わり。
3. それ以外で `FindRegisteredElsewhere()` が exe のパスを返す → `SaveHookDir(ToSettingValue(そのフォルダ))`（既定と同じならキーを消す）・`app.log` に 1 行 → **受け入れたフォルダを直接渡して** installer を作り直し、1. に戻る（1 回だけ。2 回目は受け入れをしない）。設定を読み直さない：保存に失敗した（`SaveHookDir` が false）ときに既定の場所で作り直すと、「登録し直します」→ はいで、動いている登録を止められている場所に書き換えてしまうため。保存に失敗したら `app.log` に「今回だけ使う」と書き、次の起動でもう一度受け入れる。作り直した後は `Installed` か `ExeOutdated` になる（`GetState` の一致は `SameRegistration` なので、書き方だけの違いでは `Partial` にならない。受け入れの条件に `--agent cursor` を含めているので、引数の無い登録は受け入れない）。
4. `CanInstall` が false（同梱の exe が無い・開発時）→ 今まで通りログだけ。**受け入れは 3. で済んでいる**（同梱が無くても受け入れはする）。
5. `NotInstalled` / `Partial` / `ExeOutdated` → 今まで通りの提案。`ExeOutdated` の更新は、作り直した installer の場所（＝受け入れた場所）にコピーする。

`FindRegisteredElsewhere()` の条件（Core。表の行がそのままテストになる）：

| hooks.json の Miharikun の登録 | 結果 |
|---|---|
| 13 イベント全部が設定の場所（`SameRegistration`。書き方だけの違いも含む） | null（受け入れ不要。`GetState` は `Installed` か `ExeOutdated`） |
| 13 イベント全部が同じ別の場所で、その exe がある（`\` 区切り・引用符の有無・大文字小文字の違いは問わない） | その exe のフルパス |
| 同じ別の場所だが、どれかに `--agent cursor` が無い | null（Hook は引数が無いと何も記録しない：`HookRunner.cs`。`Partial` で登録し直す） |
| どれかのコマンドの exe のパスを読めない（相対パス・不正な文字） | null（ばらばらと同じ） |
| 同じ別の場所だが exe が無い | null |
| 一部のイベントに登録が無い | null |
| 場所がばらばら（2 か所以上） | null |
| 1 つのイベントに Miharikun の登録が 2 つ（同じ場所） | その場所として扱う（Install の重複除去と同じ考え） |
| 1 つのイベントに Miharikun の登録が 2 つ（違う場所） | null（ばらばら） |
| 別アプリの登録が同じイベントに並んでいる | 無視する（`IsOurs` で Miharikun のものだけ見る） |
| 登録が無い・hooks.json が無い・壊れている | null |

- 場所の比較は `Path.GetFullPath` で正規化し、大文字小文字を無視する。引用符・`/` 区切りの違いは `TryParseExePath` で吸収する。
- 受け入れるのは「フォルダ」（`Path.GetDirectoryName(exe)`）。exe の名前は `Miharikun.Hook.exe` 固定（`IsOurs` は名前で見ている）。

### 7.2 `TryParseExePath(command)` と `SameRegistration(command, exePath)`
- `TryParseExePath`：`BuildCommand` の逆。前後の空白を落とし、先頭が `"` なら次の `"` まで、そうでなければ最初の ` --agent` の手前まで（空白を含むパスを引用符なしで書いた手書きも拾うため、「最初の空白まで」にしない）。`--agent` が無ければ全体。取り出したパスも前後の空白を落とす。
- `/` を `\` にし、**`Path.IsPathFullyQualified` でなければ null**（相対の登録を Miharikun のカレントディレクトリで解決しない。Cursor は別の基準で解決する）。`Path.GetFullPath`。失敗（不正な文字など）→ null。
- 末尾が `Miharikun.Hook.exe`（大文字小文字無視）でなければ null。
- `SameRegistration(command, exePath)`：`TryParseExePath(command)` が `exePath` と（`GetFullPath` して大文字小文字を無視して）同じで、**パスの後ろの引数に `--agent` `cursor` が続けて並ぶ**とき true。空白の数は問わない。`GetState` の `exact` の数え方をこれに替える（`HookInstaller.cs:118`）。`Install` は今まで通り、`BuildCommand` と文字列が違えば書き直す（メニューから導入したときに書き方を揃える）。

### 7.3 transcript の「変わった分だけ」
- importer はファイルごとに `(Length, LastWriteTimeUtc)` を覚える。`Scan` のたびにフォルダを列挙して、**覚えている値と違うものだけ**読む（`skip` が true の ID は読まず、**覚えた値も消す**＝Hook の記録ができた ID。消しておくと、Hook のファイルが後で消えたときに取り込みに戻れる）。
- 値は**開く前に、1 ファイルずつ `new FileInfo(path)` で**取り、その値を覚える（列挙の結果の `FileInfo` は、別のプロセスが開いたまま追記していると古いことがある）。読み終わった後にもう一度取り、開く前と違えば覚えずに次の回にまた読む（読んでいる間の追記を取りこぼさない）。
- 返すのは「変わったセッション」だけ。`CursorSessionSource` は今まで通り `Replace` で出す（同じキーの丸ごと置き換え）。
- 読むのは `new FileStream(path, Open, Read, ReadWrite | Delete)` ＋ `StreamReader(UTF8)`。Cursor が書いている途中でも開ける。`File.ReadLines` では最後の改行の有無が分からないので、行の区切りを自分で見る。**最後の行は、改行で終わる、または JSON として最後まで読めるなら使う**。どちらでもない（書きかけ）なら使わない（次に長さが変わったときに読み直す。レビュー Q3）。Cursor が最後の行に改行を付けない書き方でも、最後の返事が抜けない。
- 読めなかった（`IOException`）ときは、覚えている値を**更新しない**（次のポーリングでもう一度読む）。
- `CursorSessionSource` は毎回 `Scan` を呼ぶので、`Scan` が例外を投げ続けると 3 秒ごとにログが出る。前回のログの文を覚え、**同じ文なら出さない**（成功したら忘れる）。
- 時刻は今まで通りファイルの更新日時（全イベント同じ）。読み直すたびに「最後の動き」が新しくなる。
- 消えた transcript は何もしない（取り込んだセッションは残す。今まで通り）。
- `subagents\` は今まで通り見ない（`<id>\<id>.jsonl` と旧形式 `<id>.jsonl` だけ）。ただし Watcher はサブフォルダ込みなので、`subagents\` の変更でも再読み込みが走る（デバウンスで 1 回にまとまる。中身が変わらなければ何も出ない）。
- 監視先のフォルダはポーリングのたびに `WatchTargets` で取り直される（`SessionMonitor.EnsureWatchers`）。プロジェクトのフォルダが後からできても、次のポーリングで張られる。`CreateIfMissing: false` なので作らない。
- Watcher のキーは今の `(Directory, Filter)` のまま（同じフォルダをサブフォルダの有無だけ違えて張ることは無い）。

### 7.4 Hook の登録の時刻（`HookRegistration`）
`Since()`：
1. hooks.json を読み（壊れている・無い → null）、Miharikun の登録（`HookInstaller.IsOurs`）が 1 つも無ければ null。
2. 候補 A：hooks.json の更新日時。候補 B：`events\cursor\*.jsonl` の一番古い作成日時（無ければ候補なし。`_app.jsonl` も Hook の記録なので含める）。
3. 早いほうを返す。

`Stamp()`：hooks.json の更新日時（無ければ null）。読まずに stat だけ。
- importer は `Scan` のたびに `Stamp()` を見る（stat 1 回）。前回と違えば `Since()` を取り直し、**覚えた値を全部消して、全 transcript を読み直す**（レビュー Q2）。⚙ →「hook を削除」・導入・手での書き換えがすぐ反映され、削除の後に帯が残らない。hooks.json が変わるのはまれなので、全部読み直しても重くない。
- `Stamp()` が変わらない回は `Since()` を呼ばない（3 秒ごとに hooks.json を読まない）。`Since()` は最初の `Scan` と、`Stamp()` が変わった回だけ。
- 判定は transcript ごとに `更新日時 > Since` なら `HookMissing`。Hook が正しく動いていれば、その ID には events のファイルがあり、取り込み自体がされないので、`HookMissing` になるのは「Hook が記録していない」ものだけ。
- 推測で設計：hooks.json を別のツールが後から書き換えると候補 A が新しくなるが、Hook が一度でも記録していれば候補 B が古いので影響しない。一度も記録していない環境（会社の PC）では候補 A＝手で書き換えた時刻で、それより後の会話が「Hook なし」になる（ねらいどおり）。
- 候補 A・B はどちらも本当の登録の時刻より後なので、登録より前の会話を「Hook なし」にすることは無い。誤検知になりうるのは、(a) 導入した直後、Cursor が hooks.json を読み直すまでに続けた会話（Cursor が自動で読み直すかは要確認）、(b) Cursor が古い transcript の更新日時だけを変える操作（開いただけ・形式の移行など。要確認）。Phase 35 の後の確認（10.2。利用者の作業）で、Hook が動いている環境で帯が出ないことを確かめる。README に「導入・置き場所の変更の直後に出たら、まず Cursor を再起動」と書く（帯の文は要件定義 12.11 のまま）。

### 7.5 置き場所の保存と導入し直し
- 設定画面の欄の初期値は `LoadHookDir() ?? DefaultHookDir`。保存は `SaveHookDir(ToSettingValue(欄の値))`：正規化（`GetFullPath`＋末尾の `\` を落とす）して、既定と同じ（大文字小文字を無視）ならキーを消す。7.1 の受け入れの保存も同じ関数を通す。
- 存在しないフォルダ・完全なパスでない値は保存できない（「フォルダが見つかりません」「C:\ から始まるフォルダを指定してください」）。検査は `LoadHookDir` と同じ規則。アプリはフォルダを作らない。
- 導入（`Install` の `CopyExe`）も、**設定した場所（`hookDir` あり）は作らない**。保存の後にフォルダが消えていたら「Hook の置き場所 <フォルダ> が見つかりません（⚙ → 設定… で直してください）」で失敗させ、hooks.json は変えない。既定の `bin` は今まで通り作る（`CreateDefault` が「作ってよいか」を渡す）。
- 場所が変わって保存 → 「この場所に Hook を導入し直しますか？（hooks.json の Miharikun の登録をこの場所に書き換え、Hook exe をコピーします）」→ はいで `InstallFromMenu`（作り直した installer で。バックアップ・既存の登録を壊さない、は今まで通り）。
- いいえ → hooks.json は変えない。この場合、次の起動の確認では、hooks.json の登録（元の場所）が「全部同じ別の場所」なら 7.1 の受け入れで**元の場所に戻る**。利用者が選んだ場所が上書きされて見えるが、「hooks.json の登録が正」とする（動いている場所を優先）。設定画面の説明に「導入し直さないと、次の起動で hooks.json の登録の場所に戻ります」と書く。
- 古い場所の exe は消さない。

### 7.6 「Hook なし」の扱い（画面）
| 場所 | Imported（導入前） | NoHook（Hook なし） |
|---|---|---|
| 状態の名前 | 閉じた（導入前） | Hook なし |
| 丸の色 | 灰（`StateIdleBrush`） | 灰（同じ。警告は帯で出す） |
| 件数（`Counts`） | 「閉じた」に入る | **どこにも入らない** |
| 詳細（時刻・ターン・ツール・git） | 「—」 | 「—」 |
| チェック欄の文 | 導入前のセッションなので不明 | Hook の記録が無いセッションなので不明 |
| 最近の入力 | 出さない | 出さない |
| 警告の帯 | 数えない | 数える |

- 画面側は `state == Imported` を直接見ずに `SessionText.IsTranscriptOnly(state)` を使う（`SessionDetailViewModel.cs:134,202`、`RecentActivity.cs:20`）。`MainViewModel.UpdateCounts` の「閉じた」は `Closed, Imported` のまま。
- 帯の件数は `Cards` から数える（フィルタ・検索に関係なく全体）。`UpdateCounts` と同じ所（`Apply` の後）で数え直す。
- `SessionState.NoHook` は列挙の**最後**に足す。`SessionAnalyzer` の `imported`（`SessionAnalyzer.cs:150-159`）は NoHook でも true のまま使う（開いたターンを閉じる・`TurnInProgress` を false にするため）。状態だけを `imported && 1 つでも HookMissing ? NoHook : Imported` にする。
- レビューで `src` 全体を grep 済み：状態の `Imported` を直接見ているのは上の 3 か所と `MainViewModel.cs:387`・`SessionText.cs:26` だけ。`Timeline.cs`・`SessionAnalyzer.cs:150` はイベントの `Imported` なので変えない。丸の色は `MainWindow.xaml` の既定（`StateIdleBrush`）で灰色になる。

## 8. リスクと対策

| リスク | 対策 |
|---|---|
| 置き場所の変更で、Hook の記録先まで変わったと誤解する | 記録先は変えない（Hook exe は `AppPaths.Default()`）。設定画面の説明に「記録先は変わりません」 |
| 受け入れで、利用者が意図しない場所を採用する | 13 イベント全部が同じ場所・`--agent cursor` あり・完全なパス・exe が実在、のときだけ。`app.log` に残す。設定画面で見える |
| 受け入れの保存に失敗して、動いている登録を既定の場所に書き換える | 作り直す installer には受け入れたフォルダを直接渡す（設定を読み直さない）。保存の失敗は `app.log` に残し、次の起動でもう一度受け入れる（7.1） |
| settings.json の `hookDir` が手で壊される（相対パス・不正な文字） | `LoadHookDir` で null にして既定を使う（起動時に例外で落ちない）。ログに 1 行 |
| transcript の監視で、Cursor の書き込みのたびに読み直して重い | 変わったファイルだけ読む。デバウンス 300ms。大きい transcript（数 MB）の読み直しの時間を `PerfFact` で測る（目安：1 回 100ms 以下）。読み直しは監視のロックの中なので、その間は詳細のタイムラインの表示も待つ。目安を超えたら、ロックの外で読んで差分だけ渡す形を検討する |
| Cursor の書き込み中に開けない／書きかけの行／読む途中の追記 | 共有モードで開く。最後の行は改行か JSON として読めるときだけ使う。値は開く前に取り、読み終わりと違えば次の回に読み直す。読めなければ次のポーリングで読み直す |
| Hook が正しく動いているのに「Hook なし」と出る | events のファイルがある ID は取り込まない。導入直後（Cursor が読み直す前）と、古い会話の transcript だけが更新された場合は「Hook なし」になりうる（その会話で依頼を送れば Hook の記録に切り替わる）。10.2 の確認で、Hook が動いている環境で帯が出ないことを確かめる。README に「Hook なし」の意味と「まず Cursor を再起動」を書く |
| Hook を削除しても「Hook なし」と帯が残る | hooks.json の更新日時が変わったら、全 transcript を判定し直す（7.4） |
| Issue #22（Avalonia へ移す）と画面の変更がぶつかる | この Issue を先に main に入れる。#22 側の Phase 28 の前に、利用者からそのセッションに伝えてもらう（35 の変更点：設定画面の欄・警告の帯・HookSetup） |
| 要件定義の衝突（#22 も同じファイルの 15 章・8 章を変えている） | Phase 番号は 34〜36、節は 8.2（#22 は 8.1 を使う）にして、番号の衝突は避けた。マージ時の文の衝突は手で直す |

## 9. 決定事項（確認済み。設計の質問 Q1〜Q5）
1. Q1：まず切り分け → 原因を直す → 保険（#1・#2）。→ 切り分け済み（会社の PC で `%LOCALAPPDATA%` 配下の exe が動かない。`C:\dev` なら動く）。
2. Q2：transcript をリアルタイムに取り込む＋警告。「Hook なし」と分かる表示にする。
3. Q3：会社の PC の `C:\dev` の登録は、hooks.json を手で書き換えた。→ 7.1 の受け入れの対象。
4. Q4：A（設定画面で置き場所を選べる＋既存の登録を自動で受け入れる）。
5. Q5：導入ダイアログに「今後表示しない」は足さない。

計画で決めたこと（仕様にない細部）：
- Phase 番号は 34〜36（27〜33 は Issue #22）。要件定義の節は 8.2・12.11。
- `settings.json` のキー名は `hookDir`。既定と同じなら保存しない（キーを消す）。
- 受け入れの条件（7.1 の表）と、起動時の確認の順番（受け入れは `CanInstall` の確認より前）。
- 導入し直さずに保存したら、次の起動で hooks.json の登録の場所に戻る（hooks.json が正）。
- Hook の登録の時刻 ＝ hooks.json の更新日時と events の一番古いファイルの早いほう（7.4）。
- 「Hook なし」は灰色の丸で、状態の件数には入れない。警告の帯は閉じられない。
- transcript が消えてもセッションは消さない。書きかけの最後の行は使わない。

計画のレビュー（`docs/issue17/issue17-cursor-hook-review.html`）で決めたこと（すべて推奨の A）：
- レビュー Q1：`GetState` の「一致」は、exe のパスの正規化した一致＋`--agent cursor` あり（`SameRegistration`）。手で書いた動く登録は書き換えず、ダイアログも出さない。引数の無い登録は今まで通り `Partial`。
- レビュー Q2：hooks.json の更新日時が変わったら、読み済みの transcript も全部判定し直す。
- レビュー Q3：最後の行は「改行で終わる、または JSON として読める」なら使う。
- 要件定義との食い違いは無い（8.2 の「同じ 1 つの別の場所」、11 章の「書きかけなら飛ばす」・登録の時刻の定義のまま）。要件定義は変えていない。

## 10. 実装するセッションへの注意（必ず読む）

### 10.1 始める前に
- **この計画・設計の資料・要件定義の変更が見えることを確かめる**。作業フォルダは worktree `C:\zDev\repo\Miharikun-issue17`（ブランチ `feature/issue17-cursor-hook`）。元のフォルダ `C:\zDev\repo\Miharikun` は Issue #22 の作業中なので、**そちらで作業しない**。計画・要件定義の変更が未コミットなら、この worktree で開くこと。見つからなければ、推測で進めずに利用者に聞く。
- 計画と仕様に食い違いが無いか確かめる。あれば仕様が正。判断がつかなければ聞く。
- `CLAUDE.md` の決まり：`git add` はパスを指定、コミットは頼まれたときだけ、本物の `~\.cursor\hooks.json` と `%LOCALAPPDATA%\Miharikun\` を書き換えない（確認は `MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR` で一時フォルダに向ける。UI の確認では、本物の hooks.json をコピーした一時フォルダを使う）、`dotnet build`／`test` の前にアプリを止める。
- テストのフィクスチャは手書き（実際の transcript をコピーしない）。

### 10.2 各フェーズの終わり
- 隔離環境で確かめ、要件定義の `[x]` と「状況」を更新する（その Phase のコミットに入れる）。
- 報告には、変えたこと、テスト結果（件数）、ビルドしたかと出力先、できなかった確認を書く。**次のフェーズに進む前に止まって報告する**。
- Phase 35 の後、会社の PC での確認（利用者の作業）：設定画面で `C:\dev` 側を選ぶ／起動時の受け入れでダイアログが出ないこと、Cursor の新しいチャットが起動中に出ること。
- Phase 35 の後、Hook が動いている PC での確認（利用者の作業。本物の Cursor を使う）：新しいチャットを始める・古いチャットを開くだけ → 帯が一瞬も出ない（7.4 の誤検知の確認。出たら報告）。Cursor に長い返事を書かせ、止まった後に最後の返事まで出る（7.3）。

**状態：レビュー反映済み**（指摘 #1〜#13・Q1〜Q3 を反映。実装に進める）。
