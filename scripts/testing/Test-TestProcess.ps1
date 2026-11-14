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
$pidFile = Join-Path $directory 'child.pid'
$result = Invoke-TestProcess powershell ($arguments + @('-Mode', 'hang', '-PidFile', $pidFile)) -StdoutPath (Join-Path $directory 'timeout.log') -TimeoutSeconds 3
if (!$result.TimedOut) { throw 'The hanging process was accepted.' }
$childId = [int](Get-Content -LiteralPath $pidFile)
if (Get-Process -Id $childId -ErrorAction SilentlyContinue) { throw 'The timed-out child process survived.' }
$result = Invoke-TestProcess (Join-Path $directory 'missing.exe') -StdoutPath (Join-Path $directory 'startup.log')
if (!$result.Error) { throw 'A missing executable was accepted.' }
Write-Host 'Process validation passed: quoted arguments, stdout/stderr, exit code, process-tree timeout, startup failure.'
