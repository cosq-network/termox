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
DEBIANDIR="$APPDIR-debian"

# Keep only final archives after packaging. The publish and staging folders
# can each contain a full self-contained application.
cleanup() {
  rm -rf -- "$PUBLISH" "$APPDIR" "$DEBIANDIR" "$ROOT/artifacts/rpmbuild-$RID"
}
trap cleanup EXIT

rm -rf "$PUBLISH" "$APPDIR" "$DEBIANDIR"
mkdir -p "$PUBLISH" "$RELEASE" "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/1024x1024/apps"

dotnet publish "$ROOT/Termox.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true \
  -p:Version="$VERSION" -p:VersionPrefix="$VERSION" -o "$PUBLISH"

cp -R "$PUBLISH/." "$APPDIR/usr/bin/"
cp "$ROOT/packaging/linux/termox.desktop" "$APPDIR/usr/share/applications/termox.desktop"
cp "$ROOT/Assets/Icons/termox-icon.png" "$APPDIR/usr/share/icons/hicolor/1024x1024/apps/termox-icon.png"
sed -i 's#Exec=Termox#Exec=/usr/bin/Termox#' "$APPDIR/usr/share/applications/termox.desktop"

tar -C "$APPDIR" -czf "$RELEASE/Termox-$VERSION-$RID.tar.gz" .

if command -v dpkg-deb >/dev/null 2>&1; then
  mkdir -p "$DEBIANDIR/DEBIAN"
  cat > "$DEBIANDIR/DEBIAN/control" <<EOF
Package: termox
Version: $VERSION
Section: net
Priority: optional
Architecture: $ARCH
Maintainer: Termox Project <contact@cosqnetwork.com>
Description: Cross-platform SSH and SFTP workspace
 Termox provides SSH terminal sessions, SFTP file management, and network utilities.
EOF
  cp -a "$APPDIR/usr" "$DEBIANDIR/usr"
  dpkg-deb --build "$DEBIANDIR" "$RELEASE/Termox-$VERSION-$RID.deb" >/dev/null
fi

if command -v rpmbuild >/dev/null 2>&1; then
  case "$ARCH" in
    amd64) RPM_ARCH="x86_64" ;;
    arm64) RPM_ARCH="aarch64" ;;
    armhf) RPM_ARCH="armv7hl" ;;
    *) echo "Unsupported Linux arch for RPM: $ARCH" >&2; exit 1 ;;
  esac
  RPMROOT="$ROOT/artifacts/rpmbuild-$RID"
  rm -rf "$RPMROOT"
  mkdir -p "$RPMROOT"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}
  cp "$PUBLISH/Termox" "$RPMROOT/SOURCES/termox"
  cat > "$RPMROOT/SPECS/termox.spec" <<EOF
Name: termox
Version: $VERSION
Release: 1
Summary: Cross-platform SSH and SFTP workspace
License: MIT
URL: https://github.com/cosqnetwork/termox
BuildArch: $RPM_ARCH
%description
Termox provides SSH terminal sessions, SFTP file management, and network utilities.

%prep
%setup -q -T -c -n %{name}-%{version}

%build

%install
mkdir -p "\$RPM_BUILD_ROOT/%{_bindir}"
install -D -m 0755 "$RPMROOT/SOURCES/termox" "\$RPM_BUILD_ROOT/%{_bindir}/Termox"
mkdir -p "\$RPM_BUILD_ROOT/%{_datadir}/applications"
install -D -m 0644 "$ROOT/packaging/linux/termox.desktop" "\$RPM_BUILD_ROOT/%{_datadir}/applications/termox.desktop"
mkdir -p "\$RPM_BUILD_ROOT/%{_datadir}/icons/hicolor/1024x1024/apps"
install -D -m 0644 "$ROOT/Assets/Icons/termox-icon.png" "\$RPM_BUILD_ROOT/%{_datadir}/icons/hicolor/1024x1024/apps/termox-icon.png"
mkdir -p "\$RPM_BUILD_ROOT/%{_datadir}/doc/termox"
install -D -m 0644 "$ROOT/LICENSE" "\$RPM_BUILD_ROOT/%{_datadir}/doc/termox/COPYING"

%files
%{_bindir}/Termox
%{_datadir}/applications/termox.desktop
%{_datadir}/icons/hicolor/1024x1024/apps/termox-icon.png
%{_datadir}/doc/termox/COPYING
EOF
  rpmbuild -bb --define "_topdir $RPMROOT" --define "__strip /usr/bin/true" --define "debug_package %{nil}" "$RPMROOT/SPECS/termox.spec"
  cp -f "$RPMROOT/RPMS/$RPM_ARCH/termox-$VERSION-1.$RPM_ARCH.rpm" "$RELEASE/Termox-$VERSION-$RID.rpm"
fi
