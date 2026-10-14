#!/usr/bin/env bash
# Construit « Modpack Merger.app » et dist/ModpackMerger-<version>-macos-<arch>.zip. À lancer sur macOS.
#
#   build/macos/package.sh [arm64|x64]
#
# Signature : ad hoc par défaut (suffisant pour lancer l'app sur Apple Silicon, Gatekeeper demande une confirmation).
# Avec un certificat « Developer ID Application », définir MACOS_SIGN_IDENTITY ; pour notariser en plus,
# NOTARY_PROFILE (profil créé avec « xcrun notarytool store-credentials »).
set -euo pipefail

ARCH="${1:-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
OUT="$ROOT/dist"
WORK="$OUT/work-$ARCH"
APP="$WORK/Modpack Merger.app"
ZIP="$OUT/ModpackMerger-$VERSION-macos-$ARCH.zip"

echo "▸ Modpack Merger $VERSION pour macOS $ARCH"
rm -rf "$WORK" "$ZIP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/src/ModpackMerger.Mac" -c Release -r "osx-$ARCH" --self-contained \
    -p:UseAppHost=true -p:DebugType=none -o "$APP/Contents/MacOS"

sed "s/__VERSION__/$VERSION/g" "$ROOT/build/macos/Info.plist" > "$APP/Contents/Info.plist"

# Icône .icns générée depuis le logo de l'application.
ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"
LOGO="$ROOT/src/ModpackMerger.App/Assets/logo.png"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$LOGO" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    sips -z "$((size * 2))" "$((size * 2))" "$LOGO" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
    codesign --force --deep --timestamp --options runtime \
        --entitlements "$ROOT/build/macos/entitlements.plist" --sign "$MACOS_SIGN_IDENTITY" "$APP"
else
    codesign --force --deep --sign - "$APP"
fi

ditto -c -k --keepParent "$APP" "$ZIP"

if [[ -n "${MACOS_SIGN_IDENTITY:-}" && -n "${NOTARY_PROFILE:-}" ]]; then
    xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$APP"
    rm "$ZIP"
    ditto -c -k --keepParent "$APP" "$ZIP"
fi

echo "✓ $ZIP"
