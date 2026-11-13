#Requires -Version 5.1
<#
.SYNOPSIS
Validates a release artifact and publishes its packages using NUGET_API_KEY.
.EXAMPLE
./scripts/Publish-Packages.ps1 -PackageDirectory artifacts/packages -Version 3.0.1 -ValidateOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'packages.json') -Raw | ConvertFrom-Json
if ($manifest.version -cne $Version -or $manifest.configuration -ne 'Release') {
    throw 'The manifest must describe a Release build of the requested version.'
}
$expected = @(
    'Mzying2001.MonkeySharp.Core',
    'Mzying2001.MonkeySharp.CefSharp84.x64',
    'Mzying2001.MonkeySharp.CefSharp84.x86',
    'Mzying2001.MonkeySharp.CefSharp.x64',
    'Mzying2001.MonkeySharp.CefSharp.x86'
)
$entries = @($manifest.packages)
if ($entries.Count -ne $expected.Count -or @($entries.id | Select-Object -Unique).Count -ne $expected.Count) {
    throw 'The release must contain Core and all four distinct adapter packages.'
}
$files = @()
foreach ($id in $expected) {
    $matchingEntries = @($entries | Where-Object { $_.id -ceq $id })
    if ($matchingEntries.Count -ne 1 -or $matchingEntries[0].version -cne $Version) {
        throw "Missing package or incorrect version: $id"
    }
    $entry = $matchingEntries[0]
    foreach ($kind in @('package', 'symbols')) {
        $extension = if ($kind -eq 'package') { 'nupkg' } else { 'snupkg' }
        $name = "$id.$Version.$extension"
        # Manifests contain build-runner paths. Only their verified filenames are portable.
        if ([IO.Path]::GetFileName($entry.$kind) -cne $name) { throw "Unexpected manifest filename: $id/$kind" }
        $path = Join-Path $PackageDirectory $name
        $hashProperty = if ($kind -eq 'package') { 'sha256' } else { 'symbolsSha256' }
        $hash = $entry.$hashProperty
        if ($hash -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $hash) {
            throw "SHA-256 mismatch: $name"
        }
        $files += [pscustomobject]@{ id = $id; kind = $kind; path = $path }
    }
}
Write-Host "Validated all five packages and their symbols for $Version."
if ($ValidateOnly) { return }
if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) { throw 'NUGET_API_KEY is required for publishing.' }
Get-Command dotnet -ErrorAction Stop | Out-Null
$results = New-Object 'System.Collections.Generic.List[object]'
$reportPath = Join-Path $PackageDirectory 'publish-results.json'
try {
    # Upload Core first, then every adapter; retry symbols independently of main-package conflicts.
    foreach ($kind in @('package', 'symbols')) {
        foreach ($file in @($files | Where-Object { $_.kind -eq $kind })) {
            $arguments = @('nuget', 'push', $file.path, '--source', 'https://api.nuget.org/v3/index.json',
                '--api-key', $env:NUGET_API_KEY, '--skip-duplicate', '--timeout', '120')
            if ($kind -eq 'package') { $arguments += '--no-symbols' }
            else { $arguments += @('--symbol-api-key', $env:NUGET_API_KEY) }
            Write-Host "Publishing $([IO.Path]::GetFileName($file.path))"
            # Never log the command arguments, which contain the short-lived credential.
            & dotnet @arguments
            $code = $LASTEXITCODE
            $results.Add([pscustomobject]@{
                id = $file.id
                kind = $kind
                exitCode = $code
                status = $(if ($code -eq 0) { 'accepted-or-existing' } else { 'failed' })
            })
            if ($code -ne 0) { throw "NuGet push failed ($code): $($file.id)/$kind" }
        }
    }
}
finally {
    [ordered]@{
        version = $Version
        updatedUtc = [DateTime]::UtcNow.ToString('o')
        results = @($results.ToArray())
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
