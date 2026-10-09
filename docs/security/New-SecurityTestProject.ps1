<#
  Issue #31 の Windows 実機の確認用に、害のない試験用のプロジェクトを作る（手順は docs/security/windows-check.md）。
  - .bat は中身が echo の 1 行だけ（起動されたら "MIHARIKUN-TEST-STARTED" が出るだけ）。.exe は中身がただの文字（拡張子だけ）。
  - 本物のプロジェクト・本物のデータには触れない。作る場所は -Root（既定は %TEMP%\miharikun-sec-test）。作り直すときは先に消す。
  使い方:  powershell -ExecutionPolicy Bypass -File .\New-SecurityTestProject.ps1
#>
param([string]$Root = (Join-Path $env:TEMP 'miharikun-sec-test'))

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Root) { throw "$Root が既にあります。確認してから消して、もう一度実行してください。" }

$utf8 = New-Object System.Text.UTF8Encoding($false)
function Put([string]$path, [string]$text) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($path, $text, $utf8)
}

$proj = Join-Path $Root 'proj'
$docs = Join-Path $proj 'docs'
foreach ($d in 'data', 'cursor', 'claude', 'proj\docs\folder') { New-Item -ItemType Directory -Path (Join-Path $Root $d) -Force | Out-Null }

# --- 害のない試験用のファイル ---
Put (Join-Path $docs 'run.bat')    "@echo MIHARIKUN-TEST-STARTED`r`n"
Put (Join-Path $docs 'a, b.bat')   "@echo MIHARIKUN-TEST-STARTED`r`n"
Put (Join-Path $docs 'x.exe')      'これは実行形式ではない、ただの文字です'
Put (Join-Path $docs 'note.txt')   'text'
Put (Join-Path $docs 'folder\inside.txt') 'inside'
[System.IO.File]::WriteAllBytes((Join-Path $docs 'a.png'), [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='))
Put (Join-Path $docs 'a.pdf') "%PDF-1.1`n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj`n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj`n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj`ntrailer<</Root 1 0 R>>`n%%EOF`n"
# シンボリックリンク（権限が無ければ飛ばす）：note2.txt → run.bat（名前は .txt、先は .bat）
$linkMade = $false
try { New-Item -ItemType SymbolicLink -Path (Join-Path $docs 'note2.txt') -Target (Join-Path $docs 'run.bat') | Out-Null; $linkMade = $true } catch { Write-Warning 'シンボリックリンクを作れないので、リンクの試験（links.md の最後の項目）は飛ばします（開発者モード、または管理者で実行すると作れます）' }

# --- md ---
Put (Join-Path $docs 'links.md') @'
# リンクの試験

- [txt](note.txt) → 既定のアプリで開く
- [png](a.png) → 既定のアプリで開く
- [pdf](a.pdf) → 既定のアプリで開く
- [bat](run.bat) → 何も起動せず、エクスプローラーで選ばれる
- [exe](x.exe) → 何も起動せず、エクスプローラーで選ばれる
- [コンマと空白の名前](a,%20b.bat) → 何も起動せず、エクスプローラーで「a, b.bat」が選ばれる
- [フォルダ](folder) → エクスプローラーで選ばれる
- [ネットワーク](//server/share/x.txt) → 何も起きない
- [ネットワーク（円記号）](\\server\share\x.txt) → 何も起きない
- [名前は txt のリンク（先は bat）](note2.txt) → 何も起動せず、エクスプローラーで選ばれる（リンクを作れたときだけ）
'@

Put (Join-Path $docs 'scripts.md') @'
# スクリプトの試験

下の 2 つは、本文の文字を書き換えるだけのスクリプトです。動いたら「SCRIPT-RAN」「ONERROR-RAN」が出ます。動かないのが正しいです。

<p id="out">まだ何も動いていません</p>

<script>document.getElementById('out').textContent = 'SCRIPT-RAN';</script>

<img src="x" onerror="document.getElementById('out').textContent = 'ONERROR-RAN'">

[javascript リンク](javascript:document.getElementById('out').textContent='JSLINK-RAN')
'@

Put (Join-Path $docs 'refresh-https.md') @'
# meta refresh（https）

開いても、ブラウザが開いてはいけません。この md はそのまま表示されるのが正しいです。

<meta http-equiv="refresh" content="0;url=https://example.com/">
'@

Put (Join-Path $docs 'refresh-bat.md') @'
# meta refresh（.bat）

開いても、何も起動してはいけません（MIHARIKUN-TEST-STARTED の窓が出たら失敗）。

<meta http-equiv="refresh" content="0;url=run.bat">
'@

Put (Join-Path $docs 'network-image.md') @'
# ネットワークのパスの画像

開いても、待たされたり、資格情報の画面が出たりしてはいけません。

![](//no-such-host-miharikun-test/x.png)
'@

Put (Join-Path $docs 'tasks.md') @'
# チェックボックス

- [ ] 未完了
- [x] 完了

空行を挟んだリスト：

- [ ] 一つ目

- [x] 二つ目

1. [ ] 番号付き
2. [x] 番号付き 2

- [ ] 入れ子
  - [x] 子
'@

Put (Join-Path $docs 'mermaid.md') @'
# 図と色付け

```mermaid
graph TD
  A[はじめ] --> B[おわり]
```

```csharp
var x = 1; // 色付け
```

[見出しへ](#あとの見出し)

<div style="height:1500px"></div>

## あとの見出し

おわり
'@

Put (Join-Path $docs 'page.html') @'
<!doctype html><meta charset="utf-8"><title>html</title><p id="t">html のプレビュー：下の JS が動いて「JS-OK」と出るのが正しいです</p>
<script>document.getElementById('t').textContent = 'JS-OK';</script>
'@

# --- 起動のしかた ---
Write-Host ''
Write-Host "作りました: $Root" -ForegroundColor Green
Write-Host '次の 3 行を PowerShell で実行して、Miharikun を起動してください（Miharikun.exe の場所に置き換える）:'
Write-Host "  `$env:MIHARIKUN_DATA_DIR = '$Root\data'; `$env:MIHARIKUN_CURSOR_DIR = '$Root\cursor'; `$env:MIHARIKUN_CLAUDE_DIR = '$Root\claude'"
Write-Host "  Set-Location '$proj'"
Write-Host '  & "<Miharikun.exe の場所>\Miharikun.exe"'
Write-Host "app.log は $Root\data\logs\app.log です。"
