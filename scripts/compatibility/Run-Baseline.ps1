[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')][string]$Platform = 'x64',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$CefSharpVersion = '84.4.10',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ("artifacts/baseline/$CefSharpVersion-$Platform/" + [Guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $root 'scripts/testing/Run-SourceTests.ps1') -Platform $Platform -CefSharpVersion $CefSharpVersion -OutputDirectory (Join-Path $OutputDirectory 'source')
& (Join-Path $root 'scripts/testing/Run-NativeTests.ps1') -Platform $Platform -CefSharpVersion $CefSharpVersion -OutputDirectory (Join-Path $OutputDirectory 'native')
Write-Host "Baseline passed. Evidence: $OutputDirectory"
