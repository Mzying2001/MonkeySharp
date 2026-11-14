[CmdletBinding()]
param(
    [ValidateSet('bfcache','oopif','start-csp')][string]$Probe = 'bfcache',
    [ValidateSet('x64','x86')][string]$Platform = 'x64',
    [string]$CefSharpVersion = '84.4.10',
    [string]$OutputDirectory = '',
    [switch]$NoBuild,
    [switch]$EnableBackForwardCache
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../testing/TestProcess.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/native-probes/' + [Guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$resultFile = Join-Path $OutputDirectory 'results.json'
if (Test-Path -LiteralPath $resultFile) { throw 'Refusing to overwrite existing probe results.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$project = Join-Path $root 'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj'
if (!$NoBuild) {
    foreach ($operation in @('restore','clean','build')) {
        $arguments = @($operation, $project, "-p:Platform=$Platform", "-p:CefSharpVersion=$CefSharpVersion", '--nologo', '-v:q')
        if ($operation -ne 'restore') { $arguments += @('-c','Release') }
        if ($operation -eq 'build') { $arguments += '--no-restore' }
        & dotnet @arguments > (Join-Path $OutputDirectory "$operation.log") 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Native probe $operation failed." }
    }
}
$stdout = Join-Path $OutputDirectory 'stdout.jsonl'
$stderr = Join-Path $OutputDirectory 'stderr.log'
$executable = Join-Path $root "Mzying2001.MonkeySharp.CefSharp.SmokeHost/bin/$Platform/Release/net462/MonkeySharp.CefSharp.SmokeHost.exe"
$probeArguments = @("--$Probe-probe")
if ($EnableBackForwardCache) { $probeArguments += '--enable-bfcache' }
$run = Invoke-TestProcess -FilePath $executable -ArgumentList $probeArguments -WorkingDirectory $OutputDirectory -StdoutPath $stdout -StderrPath $stderr -TimeoutSeconds 110
$timedOut = $run.TimedOut
$exitCode = $run.ExitCode
$records = @()
Get-Content -LiteralPath $stdout | ForEach-Object {
    try { $item = $_ | ConvertFrom-Json -ErrorAction Stop; if ($item.type -eq "$Probe-probe") { $records += $item } } catch { }
}
$expectedRecords = 2
$valid = !$run.Error -and !$timedOut -and $exitCode -eq 0 -and $records.Count -eq $expectedRecords -and @($records | Where-Object { $_.status -ne 'passed' -or $_.cefSharpVersion -ne "$CefSharpVersion.0" -or $_.platform -ne $Platform }).Count -eq 0
$result = [ordered]@{ schemaVersion=1; probe=$Probe; assessmentCompleted=$valid; commit=(& git -C $root rev-parse HEAD).Trim(); version=$CefSharpVersion; platform=$Platform; processExitCode=$exitCode; timedOut=$timedOut; records=$records; runnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant() }
$result | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $resultFile -Encoding UTF8
if (!$valid) { throw "Native $Probe assessment failed. See $resultFile" }
Write-Host "Native $Probe assessment completed; capability results remain separate: $resultFile"
