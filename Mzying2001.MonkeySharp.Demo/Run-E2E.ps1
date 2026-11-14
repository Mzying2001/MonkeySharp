[CmdletBinding()]
param(
    [string]$CefSharpVersion = '156.0.150',
    [ValidateSet('all', 'navigation-menu')][string]$Case = 'all',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Mzying2001.MonkeySharp.Demo.csproj'
if (!$NoBuild) {
    & dotnet restore $project -p:Platform=x64 "-p:CefSharpVersion=$CefSharpVersion" --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet clean $project -c Release -p:Platform=x64 "-p:CefSharpVersion=$CefSharpVersion" --nologo -v:quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet build $project -c Release -p:Platform=x64 "-p:CefSharpVersion=$CefSharpVersion" --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$executable = Join-Path $PSScriptRoot 'bin/x64/Release/net462/MonkeySharpDemo.exe'
$outputDirectory = Split-Path -Parent $executable
$cefSharpDirectory = Join-Path $outputDirectory 'CefSharp'
$expectedVersion = [version]$CefSharpVersion
$requiredFiles = @(
    'CefSharp.dll',
    'CefSharp.BrowserSubprocess.exe', 'CefSharp.BrowserSubprocess.Core.dll',
    'libcef.dll', 'icudtl.dat', 'locales/en-US.pak', 'locales/zh-CN.pak'
)
if ($expectedVersion.Major -ge 87) { $requiredFiles += 'CefSharp.Core.Runtime.dll' }
foreach ($relativePath in $requiredFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $cefSharpDirectory $relativePath) -PathType Leaf)) {
        throw "Missing CefSharp dependency in the CefSharp output directory: $relativePath"
    }
}
$managedAssemblyNames = @('CefSharp', 'CefSharp.Core', 'CefSharp.Wpf')
# Version 87 introduced ordinary managed references; keep the package's default layout.
$managedAssemblyDirectory = if ($expectedVersion.Major -ge 87) { $outputDirectory } else { $cefSharpDirectory }
foreach ($name in $managedAssemblyNames) {
    if (!(Test-Path -LiteralPath (Join-Path $managedAssemblyDirectory ($name + '.dll')) -PathType Leaf)) {
        throw "Missing CefSharp managed assembly in its default output directory: $name.dll"
    }
}
if (@(Get-ChildItem -LiteralPath $cefSharpDirectory -Filter '*.pak' -File).Count -eq 0) {
    throw 'CefSharp resource packs are missing from the CefSharp output directory.'
}
[xml]$configuration = Get-Content -LiteralPath ($executable + '.config') -Raw
$namespaces = New-Object System.Xml.XmlNamespaceManager($configuration.NameTable)
$namespaces.AddNamespace('asm', 'urn:schemas-microsoft-com:asm.v1')
$probing = $configuration.SelectSingleNode('/configuration/runtime/asm:assemblyBinding/asm:probing', $namespaces)
if (!$probing -or 'CefSharp' -notin @($probing.privatePath -split ';')) {
    throw 'The Demo executable configuration must probe the CefSharp output directory.'
}
$reports = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/demo-smoke'))
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$report = Join-Path $reports ('smoke-en-' + [Guid]::NewGuid().ToString('N') + '.json')
# The isolated smoke profile has no saved preference; omit --lang to verify default English startup.
$process = Start-Process -FilePath $executable -ArgumentList @('--smoke', '--smoke-case', $Case, '--smoke-report', ('"' + $report + '"')) -PassThru -WindowStyle Hidden
if (!$process.WaitForExit(180000)) {
    Stop-Process -Id $process.Id -Force
    throw 'Demo smoke test exceeded 180 seconds.'
}
if (!(Test-Path -LiteralPath $report)) { throw "No smoke report was produced: $report" }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
Get-Content -LiteralPath $report -Raw
if (!$result.success -or $process.ExitCode -ne 0) { throw "Demo smoke failed. See $report and $($result.profile)/Data/Logs/demo.log" }
if (!$result.environment -or $result.environment.platform -ne 'x64') { throw 'Demo smoke requires actual x64 process evidence.' }
if ($result.environment.uiLanguage -ne 'en') { throw "Demo used UI language '$($result.environment.uiLanguage)', expected default English (en)." }
$requiredChecks = if ($Case -eq 'all') { @('localizedResources', 'localizedBrowser', 'localizedScriptManager', 'localizedPermissionPrompt') } else { @('crossSiteMenuCleanup', 'crossSiteMenuCallback', 'historyMenuRefresh', 'reloadMenuRefresh', 'navigationMenuTabIsolation', 'finalDisposal') }
foreach ($check in $requiredChecks) {
    if ($result.checks.$check -ne $true) { throw "Demo smoke is missing successful English UI assertion: $check" }
}
$actualVersion = [version]$result.environment.cefSharpVersion
if ($actualVersion.Major -ne $expectedVersion.Major -or $actualVersion.Minor -ne $expectedVersion.Minor -or $actualVersion.Build -ne $expectedVersion.Build) {
    throw "Demo ran CefSharp $actualVersion, requested $expectedVersion."
}
if (@($result.checks.PSObject.Properties).Count -eq 0 -or @($result.checks.PSObject.Properties | Where-Object { $_.Value -isnot [bool] -or !$_.Value }).Count -gt 0) {
    throw 'Demo smoke has missing or unsuccessful assertions.'
}
Write-Host "WPF/Chromium smoke passed (en). Report: $report"
