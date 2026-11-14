$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SmokeResults.ps1')
$checks = [ordered]@{}
foreach ($name in $script:DemoRequiredChecks) { $checks[$name] = $true }
$sample = @{ schemaVersion = 1; success = $true; smokeCase = 'all'; checks = $checks; environment = @{ platform = 'x64'; uiLanguage = 'en'; cefSharpVersion = '156.0.150.0' } } | ConvertTo-Json -Depth 5
Assert-DemoSmokeReport ($sample | ConvertFrom-Json) '156.0.150'
$mutations = @(
    { param($r) $r.checks.PSObject.Properties.Remove('crossSiteMenuCallback') },
    { param($r) $r.checks.PSObject.Properties.Remove('storageResourcesXhrCookieWebRequest') },
    { param($r) $r.checks.faviconDomProbe = $false },
    { param($r) $r.checks.finalDisposal = 'true' },
    { param($r) $r.success = 'true' },
    { param($r) $r.environment.platform = 'x86' },
    { param($r) $r.environment.cefSharpVersion = '84.4.10.0' },
    { param($r) $r.environment.uiLanguage = 'zh-CN' },
    { param($r) $r.smokeCase = 'navigation-menu' },
    { param($r) $r.schemaVersion = 0 }
)
foreach ($mutate in $mutations) {
    $result = $sample | ConvertFrom-Json
    & $mutate $result
    $rejected = $false
    try { Assert-DemoSmokeReport $result '156.0.150' } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid Demo evidence was accepted.' }
}
$rejected = $false
try { Assert-DemoSmokeReport ($sample | ConvertFrom-Json) '156.0.150' 'all' 1 } catch { $rejected = $true }
if (!$rejected) { throw 'Nonzero process exit was accepted.' }
$navigation = $sample | ConvertFrom-Json
$navigation.smokeCase = 'navigation-menu'
$navigation.checks = [pscustomobject]@{}
foreach ($name in $script:NavigationRequiredChecks) { $navigation.checks | Add-Member NoteProperty $name $true }
Assert-DemoSmokeReport $navigation '156.0.150' 'navigation-menu'
Write-Host 'Demo result validation passed (2 positive, 11 negative cases).'
