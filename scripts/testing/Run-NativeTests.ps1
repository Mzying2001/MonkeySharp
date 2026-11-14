[CmdletBinding()]
param(
    [ValidateSet('x64','x86')][string]$Platform = 'x64',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$CefSharpVersion = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestProcess.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$CefSharpVersion) {
    [xml]$adapterXml = Get-Content -LiteralPath (Join-Path $root 'Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj') -Raw
    $CefSharpVersion = $adapterXml.SelectSingleNode('//CefSharpVersion').InnerText
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ("artifacts/native-tests/$CefSharpVersion-$Platform/" + [Guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Invoke-Stage([string]$Name, [string]$Script, [string[]]$Arguments) {
    Write-Host "Native stage: $Name"
    $log = Join-Path $OutputDirectory ($Name + '.log')
    $run = Invoke-TestProcess powershell (@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root $Script)) + $Arguments) -WorkingDirectory $root -StdoutPath $log -TimeoutSeconds 600
    Get-Content -LiteralPath $log -Tail 4 | Write-Host
    Assert-TestProcess $run $Name
}
$native = @('-Platform',$Platform,'-CefSharpVersion',$CefSharpVersion)
Invoke-Stage 'smoke' 'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1' ($native + @('-OutputDirectory',(Join-Path $OutputDirectory 'smoke')))
Invoke-Stage 'oopif' 'scripts/compatibility/Run-NativeProbe.ps1' ($native + @('-Probe','oopif','-NoBuild','-OutputDirectory',(Join-Path $OutputDirectory 'oopif')))
Invoke-Stage 'corpus' 'scripts/compatibility/Run-Corpus.ps1' ($native + @('-NoBuild','-OutputDirectory',(Join-Path $OutputDirectory 'corpus')))
if ($Platform -eq 'x64') {
    Invoke-Stage 'demo' 'Mzying2001.MonkeySharp.Demo/Run-E2E.ps1' @('-CefSharpVersion',$CefSharpVersion,'-OutputDirectory',(Join-Path $OutputDirectory 'demo'))
}
Write-Host "Native checks passed. Evidence: $OutputDirectory"
