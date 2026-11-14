[CmdletBinding()]
param(
    [ValidateSet('x64','x86')][string]$Platform = 'x64',
    [ValidateSet('84.4.10','156.0.150')][string]$CefSharpVersion = '156.0.150',
    [ValidateSet('webRequest','navigation-menu')][string]$Case = 'webRequest',
    [ValidateRange(1,1000)][int]$Repeat = 30,
    [string]$OutputDirectory = '',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if ($Case -eq 'navigation-menu' -and $Platform -ne 'x64') { throw 'WPF navigation requires x64.' }
. (Join-Path $PSScriptRoot 'TestProcess.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ("artifacts/repeated-tests/$CefSharpVersion-$Platform/" + [Guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'results.json')) { throw 'Refusing to overwrite repeat evidence.' }
$commit = (& git -C $root rev-parse HEAD).Trim()
$patch = Join-Path $OutputDirectory 'source.patch'
& git -c core.safecrlf=false -C $root diff --binary HEAD "--output=$patch"
if ($LASTEXITCODE -ne 0) { throw 'Cannot snapshot source changes.' }
$hash = (Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash.ToLowerInvariant()
$attempts = @()
for ($attempt = 1; $attempt -le $Repeat; $attempt++) {
    $destination = Join-Path $OutputDirectory ("attempt-" + $attempt)
    $script = if ($Case -eq 'webRequest') { 'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1' } else { 'Mzying2001.MonkeySharp.Demo/Run-E2E.ps1' }
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root $script),'-Case',$Case,'-CefSharpVersion',$CefSharpVersion,'-OutputDirectory',$destination)
    if ($Case -eq 'webRequest') { $arguments += @('-Platform',$Platform) }
    if ($NoBuild -or $attempt -gt 1) { $arguments += '-NoBuild' }
    $run = Invoke-TestProcess powershell $arguments -WorkingDirectory $root -StdoutPath (Join-Path $OutputDirectory ("attempt-$attempt.log")) -TimeoutSeconds 600
    $passed = !$run.TimedOut -and !$run.Error -and $run.ExitCode -eq 0
    $attempts += [pscustomobject]@{ attempt=$attempt; passed=$passed; exitCode=$run.ExitCode; timedOut=$run.TimedOut; error=$run.Error; elapsedMs=$run.ElapsedMs }
    $failed = @($attempts | Where-Object { !$_.passed }).Count
    [ordered]@{ schemaVersion=1; commit=$commit; sourcePatchSha256=$hash; version=$CefSharpVersion; platform=$Platform; case=$Case; requested=$Repeat; completed=$attempt; failed=$failed; firstAttemptPassed=$attempts[0].passed; attempts=$attempts } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json') -Encoding UTF8
    Write-Host "Repeat $attempt/$Repeat ($CefSharpVersion/$Platform/$Case): passed=$passed; failures=$failed"
}
if ($failed) { throw "$failed of $Repeat attempts failed. Evidence: $OutputDirectory" }
Write-Host "All $Repeat attempts passed. Evidence: $OutputDirectory"
