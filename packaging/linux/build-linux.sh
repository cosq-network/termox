#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?version required}"
RID="${2:-linux-x64}"
case "$RID" in
  linux-x64) ARCH="amd64" ;;
  linux-arm64) ARCH="arm64" ;;
  linux-arm) ARCH="armhf" ;;
  *) echo "Unsupported Linux RID: $RID" >&2; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/artifacts/publish/$RID"
RELEASE="$ROOT/artifacts/release"
APPDIR="$ROOT/artifacts/linux-appdir-$RID"

rm -rf "$APPDIR"
mkdir -p "$PUBLISH" "$RELEASE" "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/1024x1024/apps"

dotnet publish "$ROOT/Termox.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version="$VERSION" -p:VersionPrefix="$VERSION" -o "$PUBLISH"

cp -R "$PUBLISH/." "$APPDIR/usr/bin/"
cp "$ROOT/packaging/linux/termox.desktop" "$APPDIR/usr/share/applications/termox.desktop"
cp "$ROOT/Assets/Icons/termox-icon.png" "$APPDIR/usr/share/icons/hicolor/1024x1024/apps/termox-icon.png"
sed -i.bak 's#Exec=Termox#Exec=/usr/bin/Termox#' "$APPDIR/usr/share/applications/termox.desktop"
rm -f "$APPDIR/usr/share/applications/termox.desktop.bak"

tar -C "$APPDIR" -czf "$RELEASE/Termox-$VERSION-$RID.tar.gz" .

if command -v dpkg-deb >/dev/null 2>&1; then
  mkdir -p "$APPDIR/DEBIAN"
  cat > "$APPDIR/DEBIAN/control" <<EOF
Package: termox
Version: $VERSION
Section: net
Priority: optional
Architecture: $ARCH
Maintainer: Termox Project <contact@cosqnetwork.com>
Description: Cross-platform SSH and SFTP workspace
 Termox provides SSH terminal sessions, SFTP file management, and network utilities.
EOF
  dpkg-deb --build "$APPDIR" "$RELEASE/Termox-$VERSION-$RID.deb" >/dev/null
  rm -rf "$APPDIR/DEBIAN"
fi
