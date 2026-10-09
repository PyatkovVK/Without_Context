#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
if [ "$(uname -s)" = Darwin ]; then
    sh scripts/package.sh '' Debug
    case "$(uname -m)" in
        arm64|aarch64) app_runtime=osx-arm64 ;;
        x86_64|amd64) app_runtime=osx-x64 ;;
    esac
    exec /usr/bin/open -n -W "artifacts/$app_runtime/Without Context.app" --args "$@"
fi
exec dotnet run --project src/ContextGuard.App/ContextGuard.App.csproj -- "$@"
