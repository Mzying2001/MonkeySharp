[CmdletBinding()]
param(
    [string]$CefSharpVersion = '156.0.150',
    [ValidateSet('all', 'navigation-menu')][string]$Case = 'all',
    [string]$OutputDirectory = '',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/testing/TestProcess.ps1')
. (Join-Path $PSScriptRoot 'SmokeResults.ps1')
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
$deploymentDirectory = Split-Path -Parent $executable
$cefSharpDirectory = Join-Path $deploymentDirectory 'CefSharp'
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
$managedAssemblyDirectory = if ($expectedVersion.Major -ge 87) { $deploymentDirectory } else { $cefSharpDirectory }
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
$runId = [Guid]::NewGuid().ToString('N')
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "../artifacts/demo-smoke/$CefSharpVersion-x64/$runId" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$report = Join-Path $OutputDirectory 'report.json'
if (Test-Path -LiteralPath $report) { throw "Refusing to overwrite existing smoke evidence: $report" }
$profile = Join-Path ([IO.Path]::GetTempPath()) ("MonkeySharpDemoSmoke/" + $runId)
try {
    $run = Invoke-TestProcess -FilePath $executable -ArgumentList @('--smoke', '--smoke-case', $Case, '--smoke-report', $report, '--smoke-profile', $profile) -WorkingDirectory $OutputDirectory -StdoutPath (Join-Path $OutputDirectory 'stdout.log') -StderrPath (Join-Path $OutputDirectory 'stderr.log') -TimeoutSeconds 180
    Assert-TestProcess $run 'WPF smoke'
    if (!(Test-Path -LiteralPath $report)) { throw "No smoke report was produced: $report" }
    $result = Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
    Get-Content -LiteralPath $report -Raw -Encoding UTF8
    Assert-DemoSmokeReport $result $CefSharpVersion $Case $run.ExitCode
} finally {
    $logs = Join-Path $profile 'Data/Logs'
    if (Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination (Join-Path $OutputDirectory 'logs') -Recurse -Force }
    if (Test-Path -LiteralPath $profile) { Get-ChildItem -LiteralPath $profile -Filter '*.png' -File | Copy-Item -Destination $OutputDirectory -Force }
    Write-Host "WPF evidence: $OutputDirectory"
}
Write-Host "WPF/Chromium smoke passed ($Case/en). Report: $report"
