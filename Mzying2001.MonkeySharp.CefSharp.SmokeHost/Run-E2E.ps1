[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')]
    [string]$Platform = 'x64',
    [string]$CefSharpVersion = '121.3.70'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj'

Write-Host "Building SmokeHost ($Platform, CefSharp $CefSharpVersion)..."
& dotnet restore $project "-p:Platform=$Platform" "-p:CefSharpVersion=$CefSharpVersion" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet clean $project -c Release "-p:Platform=$Platform" "-p:CefSharpVersion=$CefSharpVersion" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build $project -c Release "-p:Platform=$Platform" "-p:CefSharpVersion=$CefSharpVersion" --no-restore --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Running real Chromium smoke host...'
$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$output = @(& dotnet run --project $project -c Release "-p:Platform=$Platform" "-p:CefSharpVersion=$CefSharpVersion" --no-build --nologo 2>&1)
$runExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorAction
$output | ForEach-Object { Write-Output $_ }
if ($runExitCode -ne 0) { exit $runExitCode }

$records = @()
foreach ($line in $output) {
    try { $records += ($line | ConvertFrom-Json -ErrorAction Stop) } catch { }
}

$summary = $records | Where-Object { $_.type -eq 'smoke-summary' } | Select-Object -Last 1
$final = $records | Where-Object { $_.type -eq 'final-disposal' } | Select-Object -Last 1
if ($null -eq $summary -or !$summary.success) {
    Write-Error 'SmokeHost did not report a successful smoke-summary.'
    exit 1
}
if ($null -eq $final -or !$final.disposed) {
    Write-Error 'SmokeHost did not report final disposal.'
    exit 1
}
foreach ($name in @('storageMirror', 'xhr', 'xhrExtended', 'notification', 'tab', 'download', 'cookie', 'webRequest')) {
    if (!$summary.$name) {
        Write-Error "SmokeHost API check failed: $name"
        exit 1
    }
}

Write-Host "SmokeHost E2E passed ($Platform, CefSharp $CefSharpVersion)."
