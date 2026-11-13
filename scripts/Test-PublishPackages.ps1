#Requires -Version 5.1
<#
.SYNOPSIS
Tests publication boundaries using built packages and an in-process dotnet stub.
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackageDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$publisher = Join-Path $PSScriptRoot 'Publish-Packages.ps1'
$manifestText = Get-Content -LiteralPath (Join-Path $PackageDirectory 'packages.json') -Raw
$version = ($manifestText | ConvertFrom-Json).version
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ("MonkeySharp-PublishTests-" + [Guid]::NewGuid().ToString('N') + '/relocated packages')
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
foreach ($extension in @('*.nupkg', '*.snupkg', 'packages.json')) {
    Get-ChildItem -LiteralPath $PackageDirectory -Filter $extension | Copy-Item -Destination $testDirectory
}
$testManifest = Join-Path $testDirectory 'packages.json'
$state = [pscustomobject]@{ calls = (New-Object 'System.Collections.Generic.List[object]'); failAt = 0 }
$savedKey = $env:NUGET_API_KEY

# This function shadows the executable for every publisher invocation in this process.
$dotnetStub = {
    $parameters = @($args)
    if ($parameters.Count -lt 3 -or $parameters[0] -ne 'nuget' -or $parameters[1] -ne 'push') {
        throw 'Unexpected dotnet invocation in publication test.'
    }
    $state.calls.Add([pscustomobject]@{ parameters = $parameters; path = $parameters[2] })
    $global:LASTEXITCODE = if ($state.calls.Count -eq $state.failAt) { 37 } else { 0 }
}.GetNewClosure()
Set-Item -Path Function:dotnet -Value $dotnetStub

function Assert-Rejected([scriptblock]$Action, [string]$ExpectedMessage, [int]$ExpectedPushCount = 0) {
    $rejected = $false
    try { & $Action }
    catch {
        if (!$_.Exception.Message.Contains($ExpectedMessage)) { throw }
        $rejected = $true
    }
    if (!$rejected) { throw "Expected rejection containing: $ExpectedMessage" }
    if ($state.calls.Count -ne $ExpectedPushCount) { throw 'Unexpected number of upload attempts.' }
}

try {
    $env:NUGET_API_KEY = ''
    & $publisher -PackageDirectory $testDirectory -Version $version -ValidateOnly
    if ($state.calls.Count -ne 0) { throw 'Validation invoked NuGet.' }
    Write-Host 'Passed: downloaded artifacts validate without credentials or build-runner paths.'

    Assert-Rejected { & $publisher -PackageDirectory $testDirectory -Version $version } 'NUGET_API_KEY is required'
    Write-Host 'Passed: credentials are required before uploads.'

    $env:NUGET_API_KEY = 'test-only-not-a-real-key'
    Assert-Rejected { & $publisher -PackageDirectory $testDirectory -Version '0.0.0-wrong-version' } 'requested version'
    Write-Host 'Passed: release-version mismatches stop before uploads.'

    $changedManifest = $manifestText | ConvertFrom-Json
    $changedManifest.packages[-1].symbolsSha256 = '0' * 64
    $changedManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $testManifest -Encoding UTF8
    Assert-Rejected { & $publisher -PackageDirectory $testDirectory -Version $version } 'SHA-256 mismatch'
    Set-Content -LiteralPath $testManifest -Value $manifestText -Encoding UTF8
    Write-Host 'Passed: a corrupt final symbol package prevents every upload.'

    $changedManifest = $manifestText | ConvertFrom-Json
    $changedManifest.packages = @($changedManifest.packages | Select-Object -First 4)
    $changedManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $testManifest -Encoding UTF8
    Assert-Rejected { & $publisher -PackageDirectory $testDirectory -Version $version } 'all four distinct adapter packages'
    Set-Content -LiteralPath $testManifest -Value $manifestText -Encoding UTF8
    Write-Host 'Passed: incomplete releases stop before uploads.'

    & $publisher -PackageDirectory $testDirectory -Version $version
    if ($state.calls.Count -ne 10) { throw 'Expected five main packages and five symbol packages.' }
    if ([IO.Path]::GetFileName($state.calls[0].path) -cne "Mzying2001.MonkeySharp.Core.$version.nupkg") {
        throw 'Core was not uploaded first.'
    }
    for ($index = 0; $index -lt 10; $index++) {
        $call = $state.calls[$index]
        $extension = if ($index -lt 5) { '.nupkg' } else { '.snupkg' }
        if (!([string]$call.path).EndsWith($extension) -or $call.parameters -notcontains '--skip-duplicate') {
            throw 'Main packages and symbols must be independently retriable.'
        }
        if ($index -lt 5 -and $call.parameters -notcontains '--no-symbols') { throw 'Implicit symbol upload was enabled.' }
        if ($index -ge 5 -and $call.parameters -notcontains '--symbol-api-key') { throw 'Missing symbol credential.' }
        if ((Split-Path -Parent $call.path) -ne $testDirectory) { throw 'A build-runner path was used for upload.' }
    }
    $reportPath = Join-Path $testDirectory 'publish-results.json'
    if ((Get-Content -LiteralPath $reportPath -Raw).Contains($env:NUGET_API_KEY)) { throw 'A credential leaked into the report.' }
    Write-Host 'Passed: Core-first upload order, separate symbol retries and credential-free reports.'

    $state.calls.Clear()
    $state.failAt = 2
    Assert-Rejected { & $publisher -PackageDirectory $testDirectory -Version $version } 'NuGet push failed (37)' 2
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (@($report.results).Count -ne 2 -or $report.results[-1].status -ne 'failed') {
        throw 'A failed upload did not stop and preserve its partial report.'
    }
    Write-Host 'Passed: native failures stop publication and retain partial results.'
}
finally { $env:NUGET_API_KEY = $savedKey }
Write-Host 'All seven package-publication checks passed without network access.'
exit 0
