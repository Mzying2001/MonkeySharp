# Shared by native validation runners; works in Windows PowerShell 5.1 and PowerShell 7.
if (!('MonkeySharp.Testing.TestProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'TestProcess.cs')
}

function Invoke-TestProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = (Get-Location).Path,
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [string]$StderrPath = '',
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 120
    )
    $StdoutPath = [IO.Path]::GetFullPath($StdoutPath)
    if (!$StderrPath) { $StderrPath = $StdoutPath + '.stderr' }
    $StderrPath = [IO.Path]::GetFullPath($StderrPath)
    foreach ($file in @($StdoutPath, $StderrPath)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
    }
    $started = [DateTime]::UtcNow.ToString('o')
    $result = [MonkeySharp.Testing.TestProcess]::Run($FilePath, $ArgumentList, $WorkingDirectory, $StdoutPath, $StderrPath, $TimeoutSeconds)
    [ordered]@{
        schemaVersion = 1; executable = $FilePath; startedUtc = $started
        processId = $result.ProcessId; processExitCode = $result.ExitCode; timedOut = $result.TimedOut
        elapsedMs = $result.ElapsedMs; error = $result.Error
        githubRunId = $env:GITHUB_RUN_ID; githubRunAttempt = $env:GITHUB_RUN_ATTEMPT
    } | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($StdoutPath, '.runner.json')) -Encoding UTF8
    return $result
}

function Assert-TestProcess($Result, [string]$Stage) {
    if ($Result.TimedOut) { throw "$Stage exceeded its process deadline. See the runner report and redirected logs." }
    if ($Result.Error) { throw "$Stage failed: $($Result.Error)" }
    if ($Result.ExitCode -ne 0) { throw "$Stage exited with code $($Result.ExitCode)." }
}
