[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')][string]$Platform = 'x64',
    [ValidateSet('84.4.10', '121.3.70', '151.3.240')][string]$CefSharpVersion = '84.4.10'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try {
    foreach ($operation in @('clean', 'restore', 'build', 'test')) {
        $arguments = @($operation, 'MonkeySharp.slnx', "-p:Platform=$Platform", "-p:CefSharpVersion=$CefSharpVersion", '--nologo')
        if ($operation -ne 'restore') { $arguments += @('-c', 'Release') }
        if ($operation -eq 'build') { $arguments += '--no-restore' }
        if ($operation -eq 'test') { $arguments += '--no-build' }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "$operation failed ($LASTEXITCODE)." }
    }
    & node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
    if ($LASTEXITCODE -ne 0) { throw 'JavaScript tests failed.' }
    & (Join-Path $root 'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1') -Platform $Platform -CefSharpVersion $CefSharpVersion
    if ($LASTEXITCODE -ne 0) { throw 'SmokeHost failed.' }
    & (Join-Path $root 'scripts/compatibility/Run-NativeProbe.ps1') -Probe oopif -Platform $Platform -CefSharpVersion $CefSharpVersion -NoBuild
    if ($LASTEXITCODE -ne 0) { throw 'OOPIF runtime gate failed.' }
    & (Join-Path $root 'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Test-SmokeResults.ps1')
    $corpusDirectory = Join-Path $root ('artifacts/corpus/baseline-' + [Guid]::NewGuid().ToString('N'))
    & (Join-Path $root 'scripts/compatibility/Run-Corpus.ps1') -Platform $Platform -CefSharpVersion $CefSharpVersion -NoBuild -OutputDirectory $corpusDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Pinned corpus failed.' }
    if ($Platform -eq 'x64') {
        & (Join-Path $root 'Mzying2001.MonkeySharp.Demo/Run-E2E.ps1') -CefSharpVersion $CefSharpVersion
        if ($LASTEXITCODE -ne 0) { throw 'Demo failed.' }
    }
} finally { Pop-Location }
