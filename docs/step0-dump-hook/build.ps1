<#
.SYNOPSIS
  Step 0 用のダンプ hook を「通常ビルド」（NativeAOT なし・自己完結の単一ファイル）で作り、配布用 zip にまとめる。
.NOTES
  NativeAOT には C++ のビルドツールが要るが、ダンプ用はそこまで速さを求めないので不要。
  .NET のランタイムが入っていない PC でも動くよう、自己完結にしている（1回の実行は約60ms）。
  出力: docs/step0-dump-hook/out/  と  docs/step0-dump-hook/out/step0-dump-hook.zip
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$out = Join-Path $here 'out'

if (Test-Path -LiteralPath $out) { [System.IO.Directory]::Delete($out, $true) }

& dotnet publish (Join-Path $here 'MiharikunDump.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishAot=false -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'pkg')
if ($LASTEXITCODE -ne 0) { throw "dotnet publish が失敗しました（終了コード $LASTEXITCODE）" }

Copy-Item (Join-Path $here 'hooks.json') (Join-Path $out 'pkg')
Copy-Item (Join-Path $here 'README.md') (Join-Path $out 'pkg')

$zip = Join-Path $out 'step0-dump-hook.zip'
Compress-Archive -Path (Join-Path $out 'pkg\*') -DestinationPath $zip
Write-Host "完成: $zip" -ForegroundColor Green
Get-ChildItem (Join-Path $out 'pkg') | ForEach-Object { "{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
