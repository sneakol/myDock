#!/usr/bin/env bash
# Builds myDock and installs it for the current user. Run with --uninstall to remove it again.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
config="${XDG_CONFIG_HOME:-$HOME/.config}"
app_dir="$data/mydock"
bin_link="$HOME/.local/bin/mydock"
desktop_entry="$data/applications/mydock.desktop"
icon="$data/icons/hicolor/scalable/apps/mydock.svg"

stop_running_instance() {
    if [ -x "$app_dir/mydock" ]; then
        "$app_dir/mydock" --quit || true
        sleep 1
    fi
}

if [ "${1:-}" = "--uninstall" ]; then
    stop_running_instance
    rm -rf "$app_dir"
    rm -f "$bin_link" "$desktop_entry" "$icon" "$config/autostart/mydock.desktop"
    echo "myDock rimosso. Le impostazioni restano in $config/mydock."
    exit 0
fi

case "$(uname -m)" in
    x86_64) rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "Architettura non supportata: $(uname -m)" >&2; exit 1 ;;
esac

if ! command -v dotnet >/dev/null; then
    echo "Serve l'SDK .NET 10: sudo apt install dotnet-sdk-10.0" >&2
    exit 1
fi

staging="$app_dir.new"
rm -rf "$staging"
dotnet publish "$root/src/MyDock.App" -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:DebugType=none -o "$staging"

stop_running_instance
rm -rf "$app_dir"
mv "$staging" "$app_dir"

mkdir -p "$(dirname "$bin_link")" "$(dirname "$desktop_entry")" "$(dirname "$icon")"
ln -sf "$app_dir/mydock" "$bin_link"
install -m 644 "$root/packaging/mydock.svg" "$icon"
sed "s|@EXEC@|\"$app_dir/mydock\"|" "$root/packaging/mydock.desktop" > "$desktop_entry"
update-desktop-database "$(dirname "$desktop_entry")" 2>/dev/null || true

echo "myDock installato in $app_dir."
echo "Avvialo dal menu delle applicazioni oppure con: mydock"
