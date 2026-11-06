[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')][string]$Platform = 'x64',
    [string]$CefSharpVersion = '84.4.10',
    [switch]$NoBuild,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SmokeResults.ps1')
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "../artifacts/smoke/$CefSharpVersion-$Platform" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if (!$NoBuild) {
    foreach ($operation in @('restore', 'clean', 'build')) {
        $arguments = @($operation, (Join-Path $PSScriptRoot 'Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj'), "-p:Platform=$Platform", "-p:CefSharpVersion=$CefSharpVersion", '--nologo', '-v:q')
        if ($operation -ne 'restore') { $arguments += @('-c', 'Release') }
        if ($operation -eq 'build') { $arguments += '--no-restore' }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "CDP probe $operation failed." }
    }
}
$stdout = Join-Path $OutputDirectory 'cdp-probe.jsonl'
$stderr = Join-Path $OutputDirectory 'cdp-probe.stderr.log'
$process = Start-Process -FilePath (Join-Path $PSScriptRoot "bin/$Platform/Release/net462/MonkeySharp.CefSharp.SmokeHost.exe") `
    -ArgumentList '--cdp-probe' -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$null = $process.Handle
try {
    if (!$process.WaitForExit(90000)) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        throw 'CDP probe exceeded its watchdog.'
    }
    if ($process.ExitCode -ne 0) { throw "CDP probe failed. See $stdout and $stderr." }
    $records = @(Get-Content -LiteralPath $stdout | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-CdpProbeRecords -Records $records -Platform $Platform -CefSharpVersion $CefSharpVersion
    Write-Host "CDP probe passed ($CefSharpVersion/$Platform); full GM document-start remains best effort. Evidence: $stdout"
} finally { $process.Dispose() }
