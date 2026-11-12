[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')]
    [string]$Platform = 'x64',
    [string]$CefSharpVersion = '151.3.240',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SmokeResults.ps1')
$project = Join-Path $PSScriptRoot 'Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "../artifacts/smoke/$CefSharpVersion-$Platform" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Host "Building SmokeHost ($Platform, CefSharp $CefSharpVersion)..."
foreach ($operation in @('restore', 'clean', 'build')) {
    $arguments = @($operation, $project, "-p:Platform=$Platform", "-p:CefSharpVersion=$CefSharpVersion", '--nologo', '-v:q')
    if ($operation -ne 'restore') { $arguments += @('-c', 'Release') }
    if ($operation -eq 'build') { $arguments += '--no-restore' }
    & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory "$operation.log")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host 'Running real Chromium compatibility package...'
$executable = Join-Path $PSScriptRoot "bin/$Platform/Release/net462/MonkeySharp.CefSharp.SmokeHost.exe"
$stdoutPath = Join-Path $OutputDirectory 'stdout.log'
$stderrPath = Join-Path $OutputDirectory 'stderr.log'
$process = Start-Process -FilePath $executable -WorkingDirectory $OutputDirectory -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
$null = $process.Handle
$records = @()
$runExitCode = 1
try {
    if (!$process.WaitForExit(120000)) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        $process.WaitForExit()
        $records += [pscustomobject]@{ type = 'runner-timeout' }
    } else { $runExitCode = $process.ExitCode }
    $stdout = [IO.File]::ReadAllText($stdoutPath)
    $stderr = [IO.File]::ReadAllText($stderrPath)
    foreach ($line in ($stdout -split '\r?\n')) {
        try {
            $record = $line | ConvertFrom-Json -ErrorAction Stop
            if ($record.type) { $records += $record }
        } catch { }
    }
    $records | ForEach-Object { $_ | ConvertTo-Json -Depth 50 -Compress } |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.jsonl') -Encoding UTF8
    $stdout | Write-Output
    if ($stderr) { $stderr | Write-Output }
    Assert-SmokeRecords -Records $records -Platform $Platform -CefSharpVersion $CefSharpVersion -RunExitCode $runExitCode
} finally { $process.Dispose() }

Write-Host "SmokeHost E2E passed ($Platform, CefSharp $CefSharpVersion). Results: $OutputDirectory"
& (Join-Path $PSScriptRoot 'Run-CdpProbe.ps1') -Platform $Platform -CefSharpVersion $CefSharpVersion -NoBuild -OutputDirectory $OutputDirectory
