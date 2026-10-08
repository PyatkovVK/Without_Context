$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
if (Test-Path -LiteralPath $localDotnet) {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
}
Push-Location $projectRoot
try {
    & $dotnetCommand run --project src/ContextGuard.App/ContextGuard.App.csproj -- @args
    $appExitCode = $LASTEXITCODE
} finally { Pop-Location }
exit $appExitCode
