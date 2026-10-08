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
    [switch]$SkipHook,
    # PoC 用：Hook を NativeAOT ではなく通常の自己完結・単一ファイルで作る（C++ ビルドツール不要。1回の実行は約60ms）
    [switch]$NoAot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$name = "Miharikun-v$Version-win-x64"
if ($NoAot) { $name += "-poc" }
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

if ($NoAot) {
    Write-Warning "-NoAot: Hook は NativeAOT ではなく通常ビルドです（PoC 用）。配布用には使わないでください。"
    Invoke-Dotnet @('publish', (Join-Path $root 'src/Miharikun.Hook'), '-c', 'Release', '-r', 'win-x64', '--self-contained',
        '-p:PublishAot=false', '-p:PublishSingleFile=true', '-p:DebugType=none', "-p:Version=$Version", '-o', $hookOut)
    Copy-Item (Join-Path $hookOut 'Miharikun.Hook.exe') $stage
}
else {
Write-Host "== Hook（NativeAOT）" -ForegroundColor Cyan
# ILCompiler がリンカーを探すときに vswhere.exe を PATH から呼ぶ。VS Installer の場所は既定では PATH に無いので足す。
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $installer)) {
    $env:PATH += ";$installer"
}
try {
    Invoke-Dotnet @('publish', (Join-Path $root 'src/Miharikun.Hook'), '-c', 'Release', '-r', 'win-x64',
        "-p:Version=$Version", '-o', $hookOut)
}
catch {
    throw ("Hook の NativeAOT 発行に失敗しました。Visual Studio の「C++ によるデスクトップ開発」ワークロード" +
        "（MSVC と Windows SDK）が入っているか確認してください。`n" + $_)
}
Copy-Item (Join-Path $hookOut 'Miharikun.Hook.exe') $stage
}

# 配布 zip の README（文は scripts/dist/README-win.txt。{VERSION} を置き換える）
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dist/README-win.txt') -Raw -Encoding UTF8).Replace('{VERSION}', $Version) |
    Set-Content (Join-Path $stage 'README.txt') -Encoding UTF8

$zip = Join-Path $dist "$name.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "完成: $zip" -ForegroundColor Green
Get-ChildItem $stage | Select-Object Name, Length
