#!/bin/bash
# mac 用の Miharikun.app を組み立てる（計画 7.19）。
#   scripts/publish-mac.sh [版] [出力フォルダ]
# 版の既定は 0.0.0-dev（開発用）、出力の既定は dist/mac/。アドホック署名まで（Apple の署名・公証はしない）。
# 配布用の zip（ditto・SHA-256・README）は 32-2 で足す。
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION="${1:-0.0.0-dev}"
OUT="${2:-dist/mac}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

APP="$OUT/Miharikun.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "== App（osx-arm64・自己完結・単一ファイルにしない）"
dotnet publish src/Miharikun -c Release -r osx-arm64 --self-contained -p:Version="$VERSION" -o "$STAGE/app" >/dev/null
echo "== Hook（NativeAOT）"
dotnet publish src/Miharikun.Hook -c Release -r osx-arm64 -p:Version="$VERSION" -o "$STAGE/hook" >/dev/null

cp -R "$STAGE/app/." "$APP/Contents/MacOS/"
cp "$STAGE/hook/Miharikun.Hook" "$APP/Contents/MacOS/Miharikun.Hook"
chmod +x "$APP/Contents/MacOS/Miharikun" "$APP/Contents/MacOS/Miharikun.Hook"
sed "s/{VERSION}/$VERSION/g" scripts/dist/Info.plist > "$APP/Contents/Info.plist"

echo "== アドホック署名（--options runtime は付けない：付けると .NET の JIT に権限が要る）"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"

echo "出力: $APP"
