#!/bin/bash
# mac 用の Miharikun.app を組み立てる（計画 7.19）。
#   scripts/publish-mac.sh [版] [出力フォルダ] [--zip]
# 版の既定は 0.0.0-dev（開発用）、出力の既定は dist/mac/。アドホック署名まで（Apple の署名・公証はしない）。
# --zip を付けると、配布用に Miharikun-v{版}-osx-arm64/（Miharikun.app と README.txt）を作り、
# ditto で Miharikun-v{版}-osx-arm64.zip にして、SHA-256（.zip.sha256）も作る。
set -euo pipefail

cd "$(dirname "$0")/.."
ZIP=false
ARGS=()
for a in "$@"; do
  if [ "$a" = "--zip" ]; then ZIP=true; else ARGS+=("$a"); fi
done
VERSION="${ARGS[0]:-0.0.0-dev}"
OUT="${ARGS[1]:-dist/mac}"
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

if $ZIP; then
  NAME="Miharikun-v$VERSION-osx-arm64"
  PKG="$OUT/$NAME"
  rm -rf "$PKG" "$OUT/$NAME.zip" "$OUT/$NAME.zip.sha256"
  mkdir -p "$PKG"
  ditto "$APP" "$PKG/Miharikun.app"
  sed "s/{VERSION}/$VERSION/g" scripts/dist/README-mac.txt > "$PKG/README.txt"
  codesign --verify --deep --strict "$PKG/Miharikun.app"

  echo "== zip（ditto。実行の権限と署名を保つ）・SHA-256"
  ditto -c -k --keepParent "$PKG" "$OUT/$NAME.zip"
  (cd "$OUT" && shasum -a 256 "$NAME.zip" > "$NAME.zip.sha256")
  echo "出力: $OUT/$NAME.zip"
  cat "$OUT/$NAME.zip.sha256"
else
  echo "出力: $APP"
fi
