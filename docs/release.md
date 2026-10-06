# リリース手順

配布用の zip（`Miharikun.exe` + `Miharikun.Hook.exe` + `README.txt`）は、GitHub Actions
（`.github/workflows/release.yml`）が作り、GitHub の Releases ページに載せる。
利用者は Releases ページから zip をダウンロードして使う。

## 先に知っておくこと

- **バージョンは `v` + 数字3つ**（例：`v0.1.5`）。タグ名がそのまま zip 名とバージョンになる。
- **一度出したバージョンは使い回さない**。直すときは次の番号（`v0.1.6`）で出す。
- Release は**公開される**（リポジトリが public なら誰でも見られる）。
- ビルドは `windows-latest` で動く（テスト → AOT 発行 → zip）。約 2〜3 分。

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
3. <https://github.com/po-oq/Miharikun/releases> に、zip 付きの Release ができている。

## 方法 2：GitHub の画面から作る

コマンドを使わずに、Release の説明文を画面で書きたいときに使う。
**公開した直後は zip が付いておらず、2〜3 分後にワークフローが自動で追加する。**

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
3. 実行が緑になったら、その実行を開き、下の **Artifacts** から zip をダウンロードできる。

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
- [ ] 試運転で zip を作り、中身を確認した（必要なときだけ）
- [ ] 本物の Cursor で、導入 → 会話 → ダッシュボード表示 を確認した（大きな変更のとき）
- [ ] Claude Code のセッションが、導入なしでダッシュボードに混ざって出る（`%USERPROFILE%\.claude\projects\` を読み取りだけ。バッジ・チップでの絞り込み・「停止」の表示・⚙ → 「設定…」で変えた時間がすぐ効く。大きな変更のとき）
- [ ] 試運転の zip の Miharikun.exe で、「ドキュメント」タブに md / html が表示される（WebView2 を使うので、単一ファイル発行で動くか。大きな変更のとき）
- [ ] 「メモ」タブで、書く → 保存（Ctrl+S）→ プレビューに出る → 再起動後も残る。キャンセル・未保存で閉じるときの確認が効く。メモ内の md リンクが「ドキュメント」タブで開く（大きな変更のとき）
- [ ] Hook の置き場所（Issue #17。大きな変更のとき。`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR` で一時フォルダに向けて確かめる）：
  - hooks.json の登録を別の場所（13 イベント全部）に書き換えて起動 → ダイアログが出ず、`settings.json` に `hookDir` が入り、`app.log` に 1 行出る（次の起動でも出ない）
  - ⚙ → 「設定…」で置き場所を変える（存在しないフォルダ・相対パスは保存できない）→ 導入し直す？ → はいで 13 件がその場所に変わり exe がコピーされる／いいえなら hooks.json は変わらない
- [ ] 「Hook なし」の警告（Issue #17。大きな変更のとき）：Hook の登録あり・events なしで新しい transcript がある → 一覧の上に黄色の帯が出て、カードは「Hook なし」（件数には入らない）。Hook の記録ができる、または ⚙ →「Hook を削除」で帯が消える。ライト/ダークの両方。Hook が動いている本物の Cursor で、新しいチャットや古いチャットを開くだけでは帯が出ない
