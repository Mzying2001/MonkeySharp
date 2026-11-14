param([string]$Mode, [string]$Payload, [string]$PidFile)
[Console]::Out.WriteLine($Payload)
[Console]::Error.WriteLine('fixture stderr')
if ($Mode -eq 'failure') { exit 7 }
if ($Mode -eq 'hang') {
    $child = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 120')
    Set-Content -LiteralPath $PidFile -Value $child.Id
    Start-Sleep -Seconds 120
}
