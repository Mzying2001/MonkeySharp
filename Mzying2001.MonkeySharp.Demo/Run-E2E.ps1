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
Write-Host "WPF/Chromium smoke passed. Report: $report"
