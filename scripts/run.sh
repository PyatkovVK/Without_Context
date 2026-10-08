#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
exec dotnet run --project src/ContextGuard.App/ContextGuard.App.csproj -- "$@"
