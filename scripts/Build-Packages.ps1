#Requires -Version 5.1
<#
.SYNOPSIS
Builds Core and the legacy/current CefSharp NuGet packages without publishing.
.EXAMPLE
./scripts/Build-Packages.ps1 -Version 3.0.0
.EXAMPLE
./scripts/Build-Packages.ps1 -Platforms x64 -CurrentCefSharpVersion 156.0.150 -OutputDirectory artifacts/release
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [ValidateSet('x64', 'x86')][string[]]$Platforms = @('x64', 'x86'),
    [string]$CurrentCefSharpVersion = '',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$coreProject = Join-Path $root 'Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj'
$adapterProject = Join-Path $root 'Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj'
if (!$Version) {
    [xml]$coreXml = Get-Content -LiteralPath $coreProject -Raw
    $Version = $coreXml.SelectSingleNode('//Version').InnerText
}
if (!$CurrentCefSharpVersion) {
    [xml]$adapterXml = Get-Content -LiteralPath $adapterProject -Raw
    $CurrentCefSharpVersion = $adapterXml.SelectSingleNode('//CefSharpVersion').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw 'Version must be a three-part NuGet version, optionally with a prerelease suffix.'
}
if ($CurrentCefSharpVersion -notmatch '^\d+\.\d+\.\d+$' -or ([version]$CurrentCefSharpVersion).Major -le 84) {
    throw 'CurrentCefSharpVersion must be an explicit stable CefSharp version newer than 84.'
}
if (!$Platforms.Count) { throw 'Select at least one platform.' }
$Platforms = @($Platforms | Select-Object -Unique)
Get-Command dotnet -ErrorAction Stop | Out-Null

$runId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
if (!$OutputDirectory) { $OutputDirectory = "artifacts/packages/$Version-$runId" }
if (![IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $root $OutputDirectory }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count) {
    throw "OutputDirectory must be empty so a release cannot contain stale packages: $OutputDirectory"
}
$buildRoot = Join-Path $root "artifacts/package-build/$runId"
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Invoke-DotNet([string[]]$Arguments) {
    Write-Host ("dotnet " + ($Arguments -join ' '))
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')" }
}

function Confirm-Package([string]$Id, [string]$ExpectedCefSharpVersion = '', [string]$Platform = 'AnyCPU') {
    $package = Join-Path $OutputDirectory "$Id.$Version.nupkg"
    $symbols = Join-Path $OutputDirectory "$Id.$Version.snupkg"
    if (!(Test-Path -LiteralPath $symbols)) { throw "Missing symbol package: $symbols" }
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = @($zip.Entries | Where-Object FullName -Like '*.nuspec')
        if ($entry.Count -ne 1) { throw "Expected one nuspec in $package" }
        $reader = New-Object IO.StreamReader($entry[0].Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $actualId = $spec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='id']").InnerText
        $actualVersion = $spec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']").InnerText
        if ($actualId -ne $Id -or $actualVersion -ne $Version) { throw "Unexpected package identity in $package" }
        $dependencies = @($spec.SelectNodes("//*[local-name()='dependency']"))
        if ($ExpectedCefSharpVersion) {
            $cef = @($dependencies | Where-Object { $_.GetAttribute('id') -eq 'CefSharp.Common' })
            $exactVersions = @("[$ExpectedCefSharpVersion]", "[$ExpectedCefSharpVersion, $ExpectedCefSharpVersion]")
            if ($cef.Count -ne 1 -or $cef[0].GetAttribute('version') -notin $exactVersions) {
                throw "CefSharp.Common must be pinned to [$ExpectedCefSharpVersion] in $package"
            }
            $core = @($dependencies | Where-Object { $_.GetAttribute('id') -eq 'Mzying2001.MonkeySharp.Core' })
            if ($core.Count -ne 1 -or $core[0].GetAttribute('version') -notin @($Version, "[$Version, )", "[$Version,)")) {
                throw "Missing Core $Version dependency in $package"
            }
            if (!@($zip.Entries | Where-Object FullName -EQ 'lib/net462/Mzying2001.MonkeySharp.CefSharp.dll').Count) {
                throw "Missing adapter assembly in $package"
            }
            if (@($zip.Entries | Where-Object FullName -Like 'lib/*/Mzying2001.MonkeySharp.Core.dll').Count) {
                throw "Core must be a package dependency, not a bundled DLL: $package"
            }
        }
        else {
            if (@($dependencies | Where-Object { $_.GetAttribute('id') -like 'CefSharp*' }).Count) {
                throw "Core must not depend on CefSharp: $package"
            }
            foreach ($framework in @('net462', 'netstandard2.0', 'net8.0')) {
                if (!@($zip.Entries | Where-Object FullName -EQ "lib/$framework/Mzying2001.MonkeySharp.Core.dll").Count) {
                    throw "Missing Core target $framework in $package"
                }
            }
        }
    }
    finally { $zip.Dispose() }
    [pscustomobject]@{
        id = $Id
        version = $Version
        platform = $Platform
        cefSharpVersion = $ExpectedCefSharpVersion
        package = $package
        sha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
        symbols = $symbols
        symbolsSha256 = (Get-FileHash -LiteralPath $symbols -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

Push-Location $root
try {
    $common = @('-c', $Configuration, "-p:Version=$Version", "-p:PackageVersion=$Version", '-o', $OutputDirectory, '--nologo')
    # ArtifactsPath isolates both projects' restore/build outputs for each engine/platform.
    Invoke-DotNet (@('pack', $coreProject, '-p:Platform=AnyCPU', "-p:ArtifactsPath=$buildRoot/core") + $common)
    $packages = @(Confirm-Package 'Mzying2001.MonkeySharp.Core')
    foreach ($cefVersion in @('84.4.10', $CurrentCefSharpVersion)) {
        $suffix = if ($cefVersion -eq '84.4.10') { '84' } else { '' }
        foreach ($platform in $Platforms) {
            Invoke-DotNet (@('pack', $adapterProject, "-p:Platform=$platform", "-p:CefSharpVersion=$cefVersion",
                "-p:ArtifactsPath=$buildRoot/$cefVersion-$platform") + $common)
            $packages += Confirm-Package "Mzying2001.MonkeySharp.CefSharp$suffix.$platform" $cefVersion $platform
        }
    }
    $manifest = [ordered]@{
        createdUtc = [DateTime]::UtcNow.ToString('o')
        version = $Version
        configuration = $Configuration
        packages = $packages
    }
    $manifestPath = Join-Path $OutputDirectory 'packages.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Built and checked $($packages.Count) NuGet packages and their symbols: $OutputDirectory"
    Write-Host "Manifest: $manifestPath"
}
finally { Pop-Location }
