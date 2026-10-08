# リリース手順

配布用の zip は、GitHub Actions（`.github/workflows/release.yml`）が **Windows と macOS の 2 つ**作り、GitHub の Releases ページに載せる。
利用者は Releases ページから自分の OS の zip をダウンロードして使う。

| OS | zip | 中身 |
|---|---|---|
| Windows | `Miharikun-v<版>-win-x64.zip` | `Miharikun.exe` + `Miharikun.Hook.exe` + `README.txt` |
| macOS（Apple Silicon） | `Miharikun-v<版>-osx-arm64.zip` | `Miharikun.app`（中に Hook）+ `README.txt` |

どちらにも、SHA-256 のファイル（`<zip 名>.sha256`）が付く（Release に計 4 ファイル）。
README の文は `scripts/dist/README-win.txt`・`scripts/dist/README-mac.txt`（`{VERSION}` を版に置き換えて zip に入れる）。

## 先に知っておくこと

- **バージョンは `v` + 数字3つ**（例：`v0.1.5`）。タグ名がそのまま zip 名とバージョンになる。
- **一度出したバージョンは使い回さない**。直すときは次の番号（`v0.1.6`）で出す。
- Release は**公開される**（リポジトリが public なら誰でも見られる）。
- ビルドは `windows-latest` と `macos-latest`（Apple Silicon）の 2 つが並んで動く（テスト → AOT 発行 → zip）。それぞれ約 2〜3 分。
- macOS の zip は **Apple の署名・公証をしていない**（アドホック署名だけ）。利用者は初回に「隔離」の印を外す（zip の README に手順）。
- 両方の zip が揃ってから、Release に載る（片方が失敗したら Release は作られない）。

## 方法 1：タグを push する（基本）

zip が付いてから Release が公開されるので、いちばん安全。ビルドが失敗したら Release は作られない。

```bash
git checkout main
git pull
git tag v0.1.5
git push origin v0.1.5
```

1. 上の 4 行を実行する（`v0.1.5` は出したいバージョンに変える）。
2. <https://github.com/po-oq/Miharikun/actions> で「release」の実行が緑になるのを待つ（2〜3 分）。
3. <https://github.com/po-oq/Miharikun/releases> に、両 OS の zip と `.sha256` が付いた Release ができている。

## 方法 2：GitHub の画面から作る

コマンドを使わずに、Release の説明文を画面で書きたいときに使う。
**公開した直後は zip が付いておらず、2〜3 分後にワークフローが自動で追加する（両 OS の zip と `.sha256`）。**

1. <https://github.com/po-oq/Miharikun/releases/new> を開く。
2. **Select tag** を押し、`v0.1.5` のように入力して **Create new tag: v0.1.5 on publish** を選ぶ。
   （ターゲットは `main` のままでよい。）
3. タイトルを入れる（例：`Miharikun v0.1.5`）。説明文は **Generate release notes** で自動生成できる。
4. **Publish release** を押す。
5. 2〜3 分後、<https://github.com/po-oq/Miharikun/actions> の「release」が緑になり、Release に zip が付く。
   赤（失敗）の場合は、zip が無い Release だけが残る → 下の「失敗したとき」を見る。

## 公開せずに試す（試運転）

ビルドが通るか、zip が作れるかだけを確かめたいとき。Release は作られない。

1. <https://github.com/po-oq/Miharikun/actions> を開き、左の **release** を選ぶ。
2. **Run workflow** を押し、バージョン（例：`0.1.5`）を入れて、緑の **Run workflow** を押す。
3. 実行が緑になったら、その実行を開き、下の **Artifacts** から zip をダウンロードできる（`Miharikun-v<版>-win-x64` と `Miharikun-v<版>-osx-arm64` の 2 つ。
   ダウンロードしたものは二重の zip になっている。外側を展開すると、中に本物の zip と `.sha256` が出る）。

コマンドで行うなら次のとおり。

```bash
gh workflow run release.yml -f version=0.1.5
gh run list --workflow release.yml --limit 3
```

## 失敗したとき・やり直したいとき

- **ビルドが赤**：実行を開いて、赤い手順のログを読む。直してコミット・push してから、**同じバージョンで**やり直せる
  （まだ Release ができていなければ、タグを消してから打ち直す。下記）。
- **間違ったタグを打った・Release を取り消したい**：Releases ページで該当の Release を開き **Delete**。
  そのあとタグも消す。
  ```bash
  git tag -d v0.1.5
  git push origin :refs/tags/v0.1.5
  ```
- **公開済みのバージョンの中身を直したい**：同じ番号で作り直さず、次の番号で出す。

## 出す前のチェック

- [ ] `main` に最新の変更が入っている（`git status` がきれい、`git push` 済み）
- [ ] ローカルで `dotnet test tests/Miharikun.Tests` が通る
- [ ] 試運転で zip を作り、中身を確認した（必要なときだけ。Windows の zip に `README.txt` と 2 つの exe、mac の zip に `Miharikun.app` と `README.txt`）
- [ ] 本物の Cursor で、導入 → 会話 → ダッシュボード表示 を確認した（大きな変更のとき）
- [ ] Claude Code のセッションが、導入なしでダッシュボードに混ざって出る（Windows は `%USERPROFILE%\.claude\projects\`、mac は `~/.claude/projects/` を読み取りだけ。バッジ・チップでの絞り込み・「停止」の表示・⚙ → 「設定…」で変えた時間がすぐ効く。大きな変更のとき）
- [ ] 試運転の zip の Miharikun.exe で、「ドキュメント」タブに md / html が表示される（WebView2 を使うので、単一ファイル発行で動くか。大きな変更のとき）
- [ ] **mac（大きな変更のとき。試運転の mac の zip で）**：
  - zip を展開 → README どおり `xattr -dr com.apple.quarantine <Miharikun.app の場所>` → 起動できる（Finder から開くとフォルダ選択の画面が出る。キャンセルで終了する）
  - `open -n -a Miharikun --args "$(pwd -P)"` で、プロジェクトのフォルダを指定して起動できる（別のフォルダなら 2 つ目が開く）
  - ⚙ →「Hook を導入 / 再導入」→ 印の解除と試しの起動が通り、`~/Library/Application Support/Miharikun/bin/` に Hook ができて、`~/.cursor/hooks.json` に登録される → 本物の Cursor で会話して、ダッシュボードに出る
  - 版を上げた zip で入れ替えて再導入すると、Hook が更新される
  - `.app` を「アプリケーション」以外（ダウンロードフォルダなど）に置いたときの Hook の案内が出る
  - 書類フォルダなどの中のプロジェクトを初めて開くと、ファイルとフォルダの許可を聞かれ、「許可」で読める
  - 「ドキュメント」「メモ」のプレビュー、⌘S、⌘Q・Dock の「終了」・閉じるボタンでの未保存の確認
  - ライト/ダークの両方（ダークで、md の読み込み時に白く光らない）
- [ ] 「メモ」タブで、書く → 保存（Ctrl+S）→ プレビューに出る → 再起動後も残る。キャンセル・未保存で閉じるときの確認が効く。メモ内の md リンクが「ドキュメント」タブで開く（大きな変更のとき）
- [ ] ドキュメントの拡大モード（Issue #28。大きな変更のとき。Windows と mac の両方で、ライト/ダークの両方。`MIHARIKUN_DATA_DIR` などで一時フォルダに向け、見出し・`[x]`/`[ ]`・`###`・mermaid・別の md／html へのリンクを書いた md と、html を置いて確かめる）：
  - 「⤢ 拡大」で上段・ツリー・一覧・概要が隠れ、目次とプレビューが全幅になる。「⤡ 戻す」で元の幅・元の位置に戻る（区切り線を動かした幅も）。ファイルを選ぶまでボタンは押せない
  - 目次の項目を押す／Enter で見出しへ移る（同じ項目をもう一度も）。`[x]`/`[ ]` の見出しの ✅/⬜・節のタスク数「2/5」・上の進み具合が合う。見出しの無い md は「見出しがありません」
  - Esc で戻る（プレビューの中をクリックした後・目次を選んだ後・ボタンの後）。拡大していないときの Esc は何も起きない。ダッシュボードで拡大したままドキュメントタブで Esc を押しても、ダッシュボードの拡大は戻らない（逆も）
  - 保存すると目次が替わり、目次のスクロール位置が保たれる。リンクで別の md へ移っても拡大のまま。html では目次の列が出ない。ファイルを消すと通常に戻る
  - 拡大⇄戻す・区切り線のドラッグ・ウィンドウの大きさの変更で、本文の位置がずれない（Windows では位置の補正が Chromium の働きと二重にならないか）
- [ ] Hook の置き場所（Issue #17。大きな変更のとき。`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR` で一時フォルダに向けて確かめる。Windows と mac の両方で。保存先は Windows が `%LOCALAPPDATA%\Miharikun\`、mac が `~/Library/Application Support/Miharikun/`）：
  - hooks.json の登録を別の場所（13 イベント全部）に書き換えて起動 → ダイアログが出ず、`settings.json` に `hookDir` が入り、`app.log` に 1 行出る（次の起動でも出ない）
  - ⚙ → 「設定…」で置き場所を変える（存在しないフォルダ・相対パスは保存できない）→ 導入し直す？ → はいで 13 件がその場所に変わり exe がコピーされる／いいえなら hooks.json は変わらない
- [ ] 「Hook なし」の警告（Issue #17。大きな変更のとき）：Hook の登録あり・events なしで新しい transcript がある → 一覧の上に黄色の帯が出て、カードは「Hook なし」（件数には入らない）。Hook の記録ができる、または ⚙ →「Hook を削除」で帯が消える。ライト/ダークの両方。Hook が動いている本物の Cursor で、新しいチャットや古いチャットを開くだけでは帯が出ない。mac でも同じ（帯の文は「この Mac で Hook の実行が止められている可能性があります」）
