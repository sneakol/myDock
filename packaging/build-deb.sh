#!/usr/bin/env bash
# Builds the .deb packages of myDock into dist/: packaging/build-deb.sh [version]
# The version defaults to <Version> in Directory.Build.props. ARCHES (default "amd64 arm64") picks the architectures:
# the app is self-contained, so either one can be built from any machine. MAINTAINER overrides the Maintainer field.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$root/Directory.Build.props")}"
maintainer="${MAINTAINER:-myDock <noreply@example.invalid>}"
arches="${ARCHES:-amd64 arm64}"

[[ "$version" =~ ^[0-9][A-Za-z0-9.+~-]*$ ]] || { echo "Invalid version: '$version'" >&2; exit 1; }
for tool in dotnet dpkg-deb; do
    command -v "$tool" >/dev/null || { echo "$tool is required." >&2; exit 1; }
done

build() {
    local arch="$1" rid pkg
    case "$arch" in
        amd64) rid=linux-x64 ;;
        arm64) rid=linux-arm64 ;;
        *) echo "Unsupported architecture: $arch" >&2; exit 1 ;;
    esac

    pkg="$work/mydock_${version}_${arch}"
    mkdir -p "$pkg/DEBIAN" "$pkg/opt/mydock" "$pkg/usr/bin" "$pkg/usr/share/applications" \
        "$pkg/usr/share/icons/hicolor/scalable/apps" "$pkg/usr/share/doc/mydock"

    dotnet publish "$root/src/MyDock.App" -c Release -r "$rid" --self-contained \
        -p:PublishSingleFile=true -p:DebugType=none -p:Version="$version" -o "$pkg/opt/mydock"

    find "$pkg" -type d -exec chmod 755 {} +
    find "$pkg/opt" -type f -exec chmod 644 {} +
    chmod 755 "$pkg/opt/mydock/mydock"
    ln -s /opt/mydock/mydock "$pkg/usr/bin/mydock"
    install -m 644 "$root/packaging/mydock.svg" "$pkg/usr/share/icons/hicolor/scalable/apps/mydock.svg"
    install -m 644 "$root/LICENSE" "$pkg/usr/share/doc/mydock/copyright"
    sed 's|@EXEC@|/usr/bin/mydock|' "$root/packaging/mydock.desktop" > "$pkg/usr/share/applications/mydock.desktop"
    chmod 644 "$pkg/usr/share/applications/mydock.desktop"

    # Self-contained .NET plus Skia/Avalonia: what they load at run time. ICU has a versioned package name per release.
    cat > "$pkg/DEBIAN/control" <<CONTROL
Package: mydock
Version: $version
Architecture: $arch
Maintainer: $maintainer
Installed-Size: $(du -sk "$pkg/opt" | cut -f1)
Section: utils
Priority: optional
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libfontconfig1, libfreetype6, libx11-6, libxcursor1, libxi6, libxrandr2, libice6, libsm6, libicu80 | libicu79 | libicu78 | libicu76 | libicu74 | libicu72 | libicu70
Recommends: docker.io | docker-ce-cli, docker-compose-v2 | docker-compose-plugin, libnotify-bin, gnome-shell-extension-appindicator
Description: Tray monitor for running Docker containers
 Shows the running containers and their CPU, memory, network and disk use from
 the system tray, with logs, a shell, compose controls and desktop
 notifications. The user must be able to read /var/run/docker.sock (docker group).
CONTROL

    mkdir -p "$root/dist"
    dpkg-deb --root-owner-group --build "$pkg" "$root/dist/mydock_${version}_${arch}.deb" >/dev/null
    echo "Built dist/mydock_${version}_${arch}.deb"
}

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
rm -rf "$root/dist"   # build output only: no stale packages of other versions
for arch in $arches; do
    build "$arch"
done
echo "Install one with: sudo apt install ./dist/mydock_${version}_<arch>.deb"
