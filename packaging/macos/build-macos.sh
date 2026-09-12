#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?version required}"
RID="${2:?runtime identifier required}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/artifacts/publish/$RID"
RELEASE="$ROOT/artifacts/release"
APP="$ROOT/artifacts/Termox-$RID.app"
TEMP_DMG="$ROOT/artifacts/temp-$RID.dmg"
DMG_STAGING="$ROOT/artifacts/dmg-staging-$RID"
MOUNT_DIR="$ROOT/artifacts/mount-$RID"

# Clean up all temporary intermediates and ensure mounts are unmounted on exit.
cleanup() {
  if [[ -d "${MOUNT_DIR:-}" ]]; then
    hdiutil detach "$MOUNT_DIR" -force 2>/dev/null || true
    rm -rf -- "$MOUNT_DIR"
  fi
  rm -rf -- "$PUBLISH" "${TEMP_DMG:-}" "${DMG_STAGING:-}"
}
trap cleanup EXIT

rm -rf "$PUBLISH" "$APP" "$TEMP_DMG" "$DMG_STAGING" "$MOUNT_DIR"
mkdir -p "$PUBLISH" "$RELEASE" "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/Termox.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true \
  -p:Version="$VERSION" -p:VersionPrefix="$VERSION" -o "$PUBLISH"

cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
rm -rf -- "$PUBLISH"

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

# Stage DMG contents (Termox.app and Applications symlink)
mkdir -p "$DMG_STAGING"
cp -R "$APP" "$DMG_STAGING/Termox.app"
ln -s /Applications "$DMG_STAGING/Applications"

# Calculate app size with a safe 60 MB buffer to prevent virtual disk overflow (ENOSPC).
# hdiutil's default -srcfolder estimation underestimates HFS+ metadata overhead for large
# self-contained .NET applications, causing 'hdiutil: create failed - No space left on device'.
APP_SIZE_MB=$(du -sm "$DMG_STAGING" | awk '{print $1}')
DMG_SIZE_MB=$(( APP_SIZE_MB + 60 ))

# Create a temporary read-write disk image of explicit size, copy files, and convert to UDZO.
hdiutil create -size "${DMG_SIZE_MB}m" -volname "Termox" -fs HFS+ -ov "$TEMP_DMG" >/dev/null
mkdir -p "$MOUNT_DIR"
hdiutil attach "$TEMP_DMG" -noautoopen -nobrowse -mountpoint "$MOUNT_DIR" >/dev/null
cp -R "$DMG_STAGING/." "$MOUNT_DIR/"
sync
hdiutil detach "$MOUNT_DIR" -force >/dev/null || (sleep 2 && hdiutil detach "$MOUNT_DIR" -force >/dev/null)
rm -rf "$MOUNT_DIR" "$DMG_STAGING"

rm -f "$RELEASE/Termox-$VERSION-$RID.dmg"
hdiutil convert "$TEMP_DMG" -format UDZO -imagekey zlib-level=9 -o "$RELEASE/Termox-$VERSION-$RID.dmg" -ov >/dev/null
rm -f "$TEMP_DMG"

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

