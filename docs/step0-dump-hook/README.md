# Step 0 ダンプ用 hook

Cursor が hook に渡す JSON を**そのまま保存するだけ**の検証ツール（本体とは別物）。
`docs/miharikun-requirements.md` の 14章「Step 0：実機検証」の確認項目を、実際の Cursor で調べるために使う。

- 保存先：`%LOCALAPPDATA%\Miharikun\dump\{conversation_id}.jsonl`（1行 = 1イベント）
- 失敗したとき：`%LOCALAPPDATA%\Miharikun\dump-error.log`
- 1行の中身：`received_at` / `event` / `pid` / `cwd` / `env`（`CURSOR_PROJECT_DIR` など）/ `payload`（Cursor から受け取った JSON そのまま）

## 作り方

```powershell
./docs/step0-dump-hook/build.ps1
```

`docs/step0-dump-hook/out/step0-dump-hook.zip`（exe・hooks.json・この README）ができる。
NativeAOT ではなく自己完結の単一ファイル（約70MB、1回の実行は約60ms）なので、C++ のビルドツールも .NET のランタイムも要らない。

## 入れ方

1. zip を展開し、`MiharikunDump.exe` を `%USERPROFILE%\.cursor\hooks\` に置く（`hooks` フォルダは作る）。
2. `hooks.json` の `YOURNAME` を自分のユーザー名に直し、`%USERPROFILE%\.cursor\hooks.json` にする。
   すでに `hooks.json` があるときは、**先にバックアップ**してから、各イベントの配列にこのエントリを足す。
3. Cursor を再起動する。

> 確認項目の1つ目（「Windows で hooks.json の command が実行されるか」）は、この手順そのものの結果。
> 動かないときは、パスを `\\` 区切りにしたり、スペースを含む場所に置いたりして試し、どれが動いたかを記録する。

## 確認項目ごとのやること

普段どおり Cursor で作業しながら、次を意識して操作する。結果は `dump\*.jsonl` で確認する。

| 確認項目（14章） | やること | 見る場所 |
|---|---|---|
| command が実行されるか／`~/.cursor` の場所 | 上の「入れ方」のとおり。スペースを含むパスにも置いてみる | `dump` にファイルができるか。`pid` と `cwd` |
| `sessionEnd` の発火条件と reason | タブを閉じる／別チャットに切り替える／ウィンドウを閉じる、をそれぞれ試す | `event` が `sessionEnd` の行の `payload.reason` |
| 履歴から再開したとき `sessionStart` が再度来るか | 閉じたチャットを History から開いて話しかける | 同じ `conversation_id` で `sessionStart` が2回目に来るか |
| transcript の UUID = `conversation_id` か、slug の付け方 | `%USERPROFILE%\.cursor\projects\<slug>\agent-transcripts\` を見る | フォルダ名とファイル名の UUID を `conversation_id` と比べる。slug とプロジェクトのパスの対応 |
| `transcript_path` が null にならないか | 何度か会話する | `payload.transcript_path`、`env.CURSOR_TRANSCRIPT_PATH` |
| モデルが Auto のときの値 | モデルを Auto にして話す | `payload.model` / `model_id` / `model_params` |
| Ask モードで hook が発火するか | Ask モードで話す | `dump` にイベントが出るか |
| 承認系 hook を exit 1 で抜けたときの挙動 | ツール実行や subagent を使う。確認ダイアログが出る操作も行う | アクションが通常どおり進むか、確認ダイアログが維持されるか、Hooks 出力チャンネルのエラー表示が許容範囲か |
| Shell の `tool_output` に `exitCode` が入るか／`tool_use_id` が pre/post で一致するか | シェルでテストコマンドなどを実行させる（成功と失敗の両方） | `postToolUse` の `payload.tool_output`（文字列か、オブジェクトか）。失敗したとき `postToolUse` と `postToolUseFailure` のどちらが来るか。`preToolUse` と `postToolUse` の `tool_use_id` |

あわせて、本体の実装が前提にしている次の点も見ておくと、手戻りが減る。

- `payload.workspace_roots` のパスの形（`C:\work\proj` か `/c:/work/proj` か）
- `afterFileEdit` の `payload.file_path` が絶対パスか、どの形式か
- `beforeSubmitPrompt` の `payload.prompt` と、添付（`attachments` など）の有無

## 結果の見方（PowerShell）

```powershell
$dir = "$env:LOCALAPPDATA\Miharikun\dump"

# どんなイベントがどんな順で来たか
Get-Content "$dir\*.jsonl" -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json } |
  Select-Object received_at, event | Format-Table

# sessionEnd の reason
Get-Content "$dir\*.jsonl" -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json } |
  Where-Object event -eq 'sessionEnd' | ForEach-Object { $_.payload.reason }

# Shell の tool_output の形
Get-Content "$dir\*.jsonl" -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json } |
  Where-Object { $_.event -eq 'postToolUse' -and $_.payload.tool_name -eq 'Shell' } |
  ForEach-Object { $_.payload.tool_output | ConvertTo-Json -Depth 5 -Compress }
```

## 持ち帰るとき

`dump\*.jsonl` には**プロンプトや返事、ファイルの中身（社内情報）**がそのまま入っている。
自宅に持ち帰るのは、確認項目に必要な行だけにし、個人情報・社内情報を除去してから。
（テスト用フィクスチャも、16章のとおり除去したものから作る。）

## 片付け

1. `hooks.json` から、このツールのエントリを外す（バックアップから戻してもよい）。
2. `%USERPROFILE%\.cursor\hooks\MiharikunDump.exe` と、`%LOCALAPPDATA%\Miharikun\dump\` を消す。
