[CmdletBinding()]
param(
    [ValidateSet('x64','x86')][string]$Platform = 'x64',
    [ValidateSet('84.4.10','156.0.150')][string]$CefSharpVersion = '156.0.150',
    [string]$OutputDirectory = '',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $root 'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj'
if (!$NoBuild) {
    foreach ($operation in @('restore','clean','build')) {
        $buildArgs = @($operation,$project,"-p:Platform=$Platform","-p:CefSharpVersion=$CefSharpVersion",'--nologo','-v:q')
        if ($operation -ne 'restore') { $buildArgs += @('-c','Release') }
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "Corpus host $operation failed." }
    }
}
$hostPath = Join-Path $root "Mzying2001.MonkeySharp.CefSharp.SmokeHost/bin/$Platform/Release/net462/MonkeySharp.CefSharp.SmokeHost.exe"
$arguments = @((Join-Path $PSScriptRoot 'Run-Corpus.cjs'),'--manager','monkeysharp','--host',$hostPath,'--expected-cef-version',$CefSharpVersion,'--expected-platform',$Platform)
if ($OutputDirectory) { $arguments += @('--output',[IO.Path]::GetFullPath($OutputDirectory)) }
& node @arguments
if ($LASTEXITCODE -ne 0) { throw 'Corpus functional assertions failed; preserve the report and raw logs.' }
