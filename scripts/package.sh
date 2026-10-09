#!/usr/bin/env sh
set -eu

app_script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
app_root=$(CDPATH= cd -- "$app_script_dir/.." && pwd)
app_host=$(uname -s)
app_arch=$(uname -m)
app_runtime=${1:-}
app_configuration=${2:-Release}

if [ "$#" -gt 2 ]; then
    printf '%s\n' 'Usage: sh scripts/package.sh [osx-arm64|osx-x64|linux-x64|linux-arm64] [Debug|Release]' >&2
    exit 2
fi
if [ -z "$app_runtime" ]; then
    case "$app_arch" in
        arm64|aarch64) app_arch=arm64 ;;
        x86_64|amd64) app_arch=x64 ;;
        *) printf 'Unsupported architecture: %s\n' "$app_arch" >&2; exit 2 ;;
    esac
    case "$app_host" in
        Darwin) app_runtime=osx-$app_arch ;;
        Linux) app_runtime=linux-$app_arch ;;
        *) printf '%s\n' 'Specify a macOS or Linux runtime when packaging on another OS.' >&2; exit 2 ;;
    esac
fi
case "$app_runtime" in
    osx-arm64|osx-x64|linux-x64|linux-arm64) ;;
    *) printf 'Unsupported runtime: %s\n' "$app_runtime" >&2; exit 2 ;;
esac
case "$app_configuration" in
    Debug|Release) ;;
    *) printf '%s\n' 'Configuration must be Debug or Release.' >&2; exit 2 ;;
esac

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
app_dotnet=dotnet
if [ -x "$app_root/.tools/dotnet/dotnet" ]; then
    app_dotnet=$app_root/.tools/dotnet/dotnet
elif [ -x "$app_root/.tools/dotnet/dotnet.exe" ]; then
    app_dotnet=$app_root/.tools/dotnet/dotnet.exe
fi
if [ "$app_dotnet" != dotnet ]; then
    export DOTNET_CLI_HOME="$app_root/.dotnet-home"
    export NUGET_PACKAGES="$app_root/.tools/packages"
fi
app_project=$app_root/src/ContextGuard.App/ContextGuard.App.csproj
app_assets=$app_root/src/ContextGuard.App/images
app_output=$app_root/artifacts/$app_runtime
case "$app_runtime" in
    osx-*) app_bundle=$app_output/Without\ Context.app; app_publish=$app_bundle/Contents/MacOS ;;
    linux-*) app_publish=$app_output ;;
esac

"$app_dotnet" publish "$app_project" -c "$app_configuration" -r "$app_runtime" \
    --self-contained false -p:UseAppHost=true -o "$app_publish" -m:1 -nodeReuse:false -v:minimal
chmod +x "$app_publish/WithoutContext"

case "$app_runtime" in
    osx-*)
        app_version=$("$app_dotnet" msbuild "$app_project" -getProperty:Version -nologo | tr -d '\r')
        app_version=${app_version%%-*}
        case "$app_version" in
            ''|*[!0-9.]*) printf '%s\n' 'Bundle version must contain only digits and dots.' >&2; exit 1 ;;
        esac
        mkdir -p "$app_bundle/Contents/Resources"
        cp "$app_assets/icon.icns" "$app_bundle/Contents/Resources/icon.icns"
        sed "s/@VERSION@/$app_version/g" "$app_root/src/ContextGuard.App/Platforms/macOS/Info.plist.in" \
            > "$app_bundle/Contents/Info.plist"
        if [ "$app_host" = Darwin ]; then
            /usr/bin/plutil -lint "$app_bundle/Contents/Info.plist"
            /usr/bin/codesign --force --deep --sign - "$app_bundle"
        fi
        printf 'Application bundle: %s\n' "$app_bundle"
        ;;
    linux-*)
        cp "$app_assets/icon-256.png" "$app_publish/icon.png"
        cp "$app_root/src/ContextGuard.App/Platforms/Linux/WithoutContext.desktop.in" "$app_publish/WithoutContext.desktop.in"
        cp "$app_script_dir/install-linux.sh" "$app_publish/install.sh"
        printf 'Published application: %s\n' "$app_publish"
        printf 'Add the Linux launcher with: sh scripts/install-linux.sh "%s"\n' "$app_publish"
        ;;
esac
