#!/usr/bin/env sh
set -eu

if [ "$(uname -s)" != Linux ] || [ "$#" -gt 1 ]; then
    printf '%s\n' 'Run on Linux: sh install.sh [published-application-directory]' >&2
    exit 2
fi
app_script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
app_publish=$(CDPATH= cd -- "${1:-"$app_script_dir"}" && pwd)
app_executable=$app_publish/WithoutContext
if [ ! -f "$app_executable" ] || [ ! -f "$app_publish/icon.png" ] || [ ! -f "$app_publish/WithoutContext.desktop.in" ]; then
    printf '%s\n' 'The directory must contain the published executable, icon.png and WithoutContext.desktop.in.' >&2
    exit 1
fi
chmod +x "$app_executable"
if [ ! -x "$app_executable" ]; then
    printf '%s\n' 'The application executable needs execution permission on Linux.' >&2
    exit 1
fi
if printf '%s' "$app_executable" | LC_ALL=C grep -q '[[:cntrl:]]'; then
    printf '%s\n' 'The application path cannot contain control characters.' >&2
    exit 1
fi
app_data=${XDG_DATA_HOME:-"$HOME/.local/share"}
case "$app_data" in
    /*) ;;
    *) printf '%s\n' 'XDG_DATA_HOME must be an absolute path.' >&2; exit 1 ;;
esac
app_icon_dir=$app_data/icons/hicolor/256x256/apps
app_launcher_dir=$app_data/applications
mkdir -p "$app_icon_dir" "$app_launcher_dir"
cp "$app_publish/icon.png" "$app_icon_dir/WithoutContext.png"
# Escape both the desktop-entry string and its quoted Exec argument.
app_exec_escaped=$(printf '%s' "$app_executable" | sed \
    -e 's/\\/\\\\\\\\/g' -e 's/"/\\\\"/g' -e 's/`/\\\\`/g' -e 's/\$/\\\\$/g' -e 's/%/%%/g')
WITHOUT_CONTEXT_DESKTOP_EXEC="$app_exec_escaped" awk '
    $0 == "Exec=@EXEC@" { print "Exec=\"" ENVIRON["WITHOUT_CONTEXT_DESKTOP_EXEC"] "\""; next }
    { print }
' "$app_publish/WithoutContext.desktop.in" \
    > "$app_launcher_dir/WithoutContext.desktop"
chmod 644 "$app_launcher_dir/WithoutContext.desktop" "$app_icon_dir/WithoutContext.png"
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$app_launcher_dir/WithoutContext.desktop"
fi
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$app_launcher_dir"
fi
printf 'Installed launcher: %s\n' "$app_launcher_dir/WithoutContext.desktop"
