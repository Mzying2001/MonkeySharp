$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestProcess.ps1')
$directory = Join-Path ([IO.Path]::GetTempPath()) ('MonkeySharp-ProcessTests-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $PSScriptRoot 'fixtures/ProcessChild.ps1'
$payload = 'spaces, "quotes", and a trailing slash\'
$arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $fixture)
$result = Invoke-TestProcess powershell ($arguments + @('-Mode', 'success', '-Payload', $payload)) -StdoutPath (Join-Path $directory 'success.log')
Assert-TestProcess $result 'Success fixture'
if ([IO.File]::ReadAllText((Join-Path $directory 'success.log')).Trim() -ne $payload) { throw 'Native argument quoting changed the payload.' }
if ([IO.File]::ReadAllText((Join-Path $directory 'success.log.stderr')).Trim() -ne 'fixture stderr') { throw 'Stderr was lost.' }
$result = Invoke-TestProcess powershell ($arguments + @('-Mode', 'failure')) -StdoutPath (Join-Path $directory 'failure.log')
if ($result.ExitCode -ne 7 -or $result.TimedOut) { throw 'The child exit code was lost.' }
$parentModulePath = $env:PSModulePath
$expectedHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
$shells = @('powershell', (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'))
for ($index = 0; $index -lt $shells.Count; $index++) {
    $log = Join-Path $directory ("hash-$index.log")
    $result = Invoke-TestProcess $shells[$index] ($arguments + @('-Mode', 'hash')) -StdoutPath $log
    Assert-TestProcess $result 'Windows PowerShell module autoload fixture'
    if ([IO.File]::ReadAllText($log).Trim() -ne $expectedHash) { throw 'Windows PowerShell could not load its native hashing function.' }
    if ($env:PSModulePath -ne $parentModulePath) { throw 'Starting a child shell changed the parent module path.' }
}
$pidFile = Join-Path $directory 'child.pid'
$result = Invoke-TestProcess powershell ($arguments + @('-Mode', 'hang', '-PidFile', $pidFile)) -StdoutPath (Join-Path $directory 'timeout.log') -TimeoutSeconds 3
if (!$result.TimedOut) { throw 'The hanging process was accepted.' }
$childId = [int](Get-Content -LiteralPath $pidFile)
if (Get-Process -Id $childId -ErrorAction SilentlyContinue) { throw 'The timed-out child process survived.' }
$result = Invoke-TestProcess (Join-Path $directory 'missing.exe') -StdoutPath (Join-Path $directory 'startup.log')
if (!$result.Error) { throw 'A missing executable was accepted.' }
Write-Host 'Process validation passed: quoted arguments, stdout/stderr, exit code, shell module isolation, process-tree timeout, startup failure.'
