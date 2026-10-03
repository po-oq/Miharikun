<#
.SYNOPSIS
  配布用の zip を作る（要件 5章）。App は自己完結の単一ファイル、Hook は NativeAOT。
.EXAMPLE
  ./scripts/publish.ps1 -Version 0.1.0
.NOTES
  Hook の NativeAOT 発行には C++ のビルドツール（Visual Studio の「C++ によるデスクトップ開発」＋ Windows SDK）が要る。
  -SkipHook は App だけを発行して動作確認するためのもので、zip は作らない。
#>
param(
    [string]$Version = "0.1.0",
    [switch]$SkipHook
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$name = "Miharikun-v$Version-win-x64"
$stage = Join-Path $dist $name
$appOut = Join-Path $dist '_app'
$hookOut = Join-Path $dist '_hook'

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') が失敗しました（終了コード $LASTEXITCODE）" }
}

# 前回の出力を消す（dist だけ。リポジトリの他の場所には触れない）
if (Test-Path -LiteralPath $dist) { [System.IO.Directory]::Delete($dist, $true) }
New-Item -ItemType Directory $dist, $stage | Out-Null

Write-Host "== App（単一ファイル・自己完結）" -ForegroundColor Cyan
Invoke-Dotnet @('publish', (Join-Path $root 'src/Miharikun'), '-c', 'Release', '-r', 'win-x64', '--self-contained',
    '-p:PublishSingleFile=true', "-p:Version=$Version", '-o', $appOut)
Copy-Item (Join-Path $appOut 'Miharikun.exe') $stage

if ($SkipHook) {
    Write-Warning "-SkipHook: Hook exe を含めていないので zip は作りません。出力: $stage"
    return
}

Write-Host "== Hook（NativeAOT）" -ForegroundColor Cyan
try {
    Invoke-Dotnet @('publish', (Join-Path $root 'src/Miharikun.Hook'), '-c', 'Release', '-r', 'win-x64',
        "-p:Version=$Version", '-o', $hookOut)
}
catch {
    throw ("Hook の NativeAOT 発行に失敗しました。Visual Studio の「C++ によるデスクトップ開発」ワークロード" +
        "（MSVC と Windows SDK）が入っているか確認してください。`n" + $_)
}
Copy-Item (Join-Path $hookOut 'Miharikun.Hook.exe') $stage

@"
Miharikun（みはりくん） v$Version

1. このフォルダを好きな場所に置きます（Miharikun.exe と Miharikun.Hook.exe は同じフォルダに）。
2. Miharikun.exe [プロジェクトのフォルダ] で起動します。省略すると、いまのフォルダが対象です。
3. 初回は「導入しますか？」と出るので「はい」を選びます。
   - Hook exe を %LOCALAPPDATA%\Miharikun\bin\ にコピーします。
   - %USERPROFILE%\.cursor\hooks.json に登録します（既存の設定は残し、変更前にバックアップを作ります）。
4. Cursor でチャットを始めると、ダッシュボードに表示されます。反映されないときは Cursor を再起動してください。

Hook を外すときは、画面右上の ⚙ → 「Hook を削除」。
"@ | Set-Content (Join-Path $stage 'README.txt') -Encoding UTF8

$zip = Join-Path $dist "$name.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "完成: $zip" -ForegroundColor Green
Get-ChildItem $stage | Select-Object Name, Length
