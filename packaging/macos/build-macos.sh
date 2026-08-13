#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?version required}"
RID="${2:?runtime identifier required}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/artifacts/publish/$RID"
RELEASE="$ROOT/artifacts/release"
APP="$ROOT/artifacts/Termox-$RID.app"

# The publish directory is copied into the app bundle and is no longer needed
# afterward. Remove it even when packaging fails.
cleanup() {
  rm -rf -- "$PUBLISH"
}
trap cleanup EXIT

rm -rf "$PUBLISH" "$APP"
mkdir -p "$PUBLISH" "$RELEASE" "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/Termox.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true \
  -p:Version="$VERSION" -p:VersionPrefix="$VERSION" -o "$PUBLISH"

cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
cp "$ROOT/Assets/Icons/termox-icon.icns" "$APP/Contents/Resources/termox-icon.icns"
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDisplayName</key><string>termox</string>
  <key>CFBundleExecutable</key><string>Termox</string>
  <key>CFBundleIdentifier</key><string>com.cosqnetwork.termox</string>
  <key>CFBundleIconFile</key><string>termox-icon</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>termox</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
</dict></plist>
EOF
chmod +x "$APP/Contents/MacOS/Termox"

if [[ -n "${APPLE_SIGNING_IDENTITY:-}" ]]; then
  echo "Signing macOS bundle with identity: $APPLE_SIGNING_IDENTITY"
  codesign --deep --force --options runtime --timestamp --sign "$APPLE_SIGNING_IDENTITY" "$APP"
  codesign --verify --deep --strict "$APP"
else
  echo "APPLE_SIGNING_IDENTITY not set — building unsigned bundle." >&2
fi

hdiutil create -volname Termox -srcfolder "$APP" -ov -format UDZO "$RELEASE/Termox-$VERSION-$RID.dmg" >/dev/null

if [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" ]]; then
  echo "Submitting DMG for notarization..."
  xcrun notarytool submit "$RELEASE/Termox-$VERSION-$RID.dmg" \
    --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait --timeout 10m
  xcrun stapler staple "$RELEASE/Termox-$VERSION-$RID.dmg"
  xcrun stapler staple "$APP"
  echo "Notarization and stapling complete."
else
  echo "Notarization credentials not set — skipping notarization and stapling." >&2
fi

rm -f "$RELEASE/Termox-$VERSION-$RID.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$RELEASE/Termox-$VERSION-$RID.zip"
