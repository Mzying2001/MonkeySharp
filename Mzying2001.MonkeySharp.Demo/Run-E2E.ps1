[CmdletBinding()]
param([string]$CefSharpVersion = '121.3.70', [switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Mzying2001.MonkeySharp.Demo.csproj'
if (!$NoBuild) {
    & dotnet restore $project -p:Platform=x64 "-p:CefSharpVersion=$CefSharpVersion" --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet clean $project -c Release -p:Platform=x64 "-p:CefSharpVersion=$CefSharpVersion" --nologo -v:quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet build $project -c Release -p:Platform=x64 "-p:CefSharpVersion=$CefSharpVersion" --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$executable = Join-Path $PSScriptRoot 'bin/x64/Release/net462/Mzying2001.MonkeySharp.Demo.exe'
$reports = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/demo-smoke'))
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$report = Join-Path $reports ('smoke-' + [Guid]::NewGuid().ToString('N') + '.json')
$process = Start-Process -FilePath $executable -ArgumentList @('--smoke', '--smoke-report', ('"' + $report + '"')) -PassThru -WindowStyle Hidden
if (!$process.WaitForExit(180000)) {
    Stop-Process -Id $process.Id -Force
    throw 'Demo smoke test exceeded 180 seconds.'
}
if (!(Test-Path -LiteralPath $report)) { throw "No smoke report was produced: $report" }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
Get-Content -LiteralPath $report -Raw
if (!$result.success -or $process.ExitCode -ne 0) { throw "Demo smoke failed. See $report and $($result.profile)/Data/Logs/demo.log" }
$expectedVersion = [version]$CefSharpVersion
if (!$result.environment -or $result.environment.platform -ne 'x64') { throw 'Demo smoke requires actual x64 process evidence.' }
$actualVersion = [version]$result.environment.cefSharpVersion
if ($actualVersion.Major -ne $expectedVersion.Major -or $actualVersion.Minor -ne $expectedVersion.Minor -or $actualVersion.Build -ne $expectedVersion.Build) {
    throw "Demo ran CefSharp $actualVersion, requested $expectedVersion."
}
if (@($result.checks.PSObject.Properties).Count -eq 0 -or @($result.checks.PSObject.Properties | Where-Object { $_.Value -isnot [bool] -or !$_.Value }).Count -gt 0) {
    throw 'Demo smoke has missing or unsuccessful assertions.'
}
Write-Host "WPF/Chromium smoke passed. Report: $report"
