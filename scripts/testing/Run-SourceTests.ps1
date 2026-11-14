[CmdletBinding()]
param(
    [ValidateSet('x64','x86')][string]$Platform = 'x64',
    [ValidateSet('84.4.10','156.0.150')][string]$CefSharpVersion = '156.0.150',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestProcess.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ("artifacts/source-tests/$CefSharpVersion-$Platform/" + [Guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Invoke-Stage([string]$Name, [string]$Executable, [string[]]$Arguments) {
    Write-Host "Source stage: $Name"
    $log = Join-Path $OutputDirectory ($Name + '.log')
    $run = Invoke-TestProcess -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory $root -StdoutPath $log -TimeoutSeconds 600
    Get-Content -LiteralPath $log -Tail 12 | Write-Host
    Assert-TestProcess $run $Name
}
foreach ($operation in @('clean','restore','build')) {
    $arguments = @($operation, 'MonkeySharp.slnx', "-p:Platform=$Platform", "-p:CefSharpVersion=$CefSharpVersion", '--nologo', '-v:minimal')
    if ($operation -ne 'restore') { $arguments += @('-c','Release') }
    if ($operation -eq 'build') { $arguments += '--no-restore' }
    Invoke-Stage $operation dotnet $arguments
}
# Separate result directories prevent simultaneous test hosts from overwriting TRX evidence.
[xml]$solution = Get-Content -LiteralPath (Join-Path $root 'MonkeySharp.slnx') -Raw
foreach ($item in $solution.Solution.Project) {
    $project = Join-Path $root $item.Path
    [xml]$definition = Get-Content -LiteralPath $project -Raw
    $isTest = $definition.SelectSingleNode('//IsTestProject')
    if (!$isTest -or $isTest.InnerText -ne 'true') { continue }
    $platforms = $definition.SelectSingleNode('//Platforms')
    if ($platforms -and $Platform -notin @($platforms.InnerText -split ';')) { continue }
    $testPlatform = if ($platforms) { $Platform } else { 'AnyCPU' }
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    Invoke-Stage $name dotnet @('test', $project, '-c','Release', "-p:Platform=$testPlatform", "-p:CefSharpVersion=$CefSharpVersion", '--no-build','--nologo', '--logger','trx', '--results-directory',(Join-Path $OutputDirectory "trx/$name"), '--blame-hang','--blame-hang-timeout','2m')
}
Invoke-Stage 'javascript' node @('--test','--test-timeout=30000','--test-reporter=tap','Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js')
foreach ($test in @('scripts/testing/Test-TestProcess.ps1','Mzying2001.MonkeySharp.Demo/Test-SmokeResults.ps1','Mzying2001.MonkeySharp.CefSharp.SmokeHost/Test-SmokeResults.ps1')) {
    $name = [IO.Path]::GetFileName((Split-Path -Parent $test)) + '-' + [IO.Path]::GetFileNameWithoutExtension($test)
    Invoke-Stage $name powershell @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root $test))
}
Write-Host "Source checks passed. Evidence: $OutputDirectory"
