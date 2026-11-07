$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SmokeResults.ps1')
$sample = @(
    @{ type = 'fixture-result'; phase = 'initial'; success = $true },
    @{ type = 'fixture-result'; phase = 'navigation'; success = $true },
    @{ type = 'smoke-summary'; success = $true; hostAttachedAfterDetach = $false;
        storageMirror = $true; xhr = $true; xhrExtended = $true; notification = $true; tab = $true;
        download = $true; cookie = $true; webRequest = $true; navigation = $true;
        mainFrameLoads = 2; childFrameLoads = 1; contextReleased = 1;
        compatibility = @{ audio = $true; pageHelpers = $true; binary = $true; permissions = $true; csp = $true; replay = $true; isolated = $true; conditions = $true; prepared = $true; frames = $true; spaUrl = $true; download = $true; cookie = $true; webRequest = $true } },
    @{ type = 'final-disposal'; disposed = $true; platform = 'x64'; cefSharpVersion = '121.3.70.0' }
)
foreach ($id in @('prepared', 'conditions', 'isolated', 'replay', 'csp', 'permissions', 'binary', 'pageHelpers', 'frames', 'spaUrl', 'download', 'cookie', 'webRequest', 'audio')) {
    $sample += @{ type = 'compatibility-case'; caseId = $id; success = $true; checks = @{ assertion = $true }; error = $null }
}
$sampleJson = ConvertTo-Json -InputObject $sample -Depth 20
Assert-SmokeRecords -Records ($sampleJson | ConvertFrom-Json) -Platform x64 -CefSharpVersion 121.3.70
$tests = [ordered]@{
    missingCase = { param($r) @($r | Where-Object { $_.caseId -ne 'frames' }) }
    duplicateCase = { param($r) $r + @($r | Where-Object { $_.caseId -eq 'frames' }) }
    failedCase = { param($r) ($r | Where-Object { $_.caseId -eq 'frames' }).success = $false; $r }
    falseAssertion = { param($r) ($r | Where-Object { $_.caseId -eq 'frames' }).checks.assertion = $false; $r }
    emptyAssertions = { param($r) ($r | Where-Object { $_.caseId -eq 'frames' }).checks = [pscustomobject]@{}; $r }
    nonBoolean = { param($r) ($r | Where-Object { $_.caseId -eq 'frames' }).success = 'true'; $r }
    wrongVersion = { param($r) ($r | Where-Object { $_.type -eq 'final-disposal' }).cefSharpVersion = '84.4.10.0'; $r }
    wrongPlatform = { param($r) ($r | Where-Object { $_.type -eq 'final-disposal' }).platform = 'x86'; $r }
    missingDisposal = { param($r) @($r | Where-Object { $_.type -ne 'final-disposal' }) }
    attachedHost = { param($r) ($r | Where-Object { $_.type -eq 'smoke-summary' }).hostAttachedAfterDetach = $true; $r }
    missingDetach = { param($r) ($r | Where-Object { $_.type -eq 'smoke-summary' }).PSObject.Properties.Remove('hostAttachedAfterDetach'); $r }
    missingLegacy = { param($r) @($r | Where-Object { $_.phase -ne 'initial' }) }
    inconsistentSummary = { param($r) ($r | Where-Object { $_.type -eq 'smoke-summary' }).compatibility.frames = $false; $r }
    timeout = { param($r) $r + [pscustomobject]@{ type = 'runner-timeout' } }
}
foreach ($test in $tests.GetEnumerator()) {
    $records = & $test.Value ($sampleJson | ConvertFrom-Json)
    $rejected = $false
    try { Assert-SmokeRecords -Records $records -Platform x64 -CefSharpVersion 121.3.70 } catch { $rejected = $true }
    if (!$rejected) { throw "Invalid result was accepted: $($test.Key)." }
}
$rejected = $false
try { Assert-SmokeRecords -Records ($sampleJson | ConvertFrom-Json) -Platform x64 -CefSharpVersion 121.3.70 -RunExitCode 1 } catch { $rejected = $true }
if (!$rejected) { throw 'Nonzero exit code was accepted.' }
Write-Host "Smoke result validation passed (1 positive, $($tests.Count + 1) negative cases)."
$cdp = @{ type = 'cdp-probe'; status = 'passed'; platform = 'x64'; cefSharpVersion = '84.4.10.0'; strictGm = 'blocked'; checks = @{} }
foreach ($name in @('preparedNavigation', 'pageBeforeFirstInline', 'isolatedBeforeFirstInline', 'realGmEventuallyReady', 'nativeFrameIdentity', 'perScriptWorld', 'contextIdBinding', 'cookieEngineAccepted', 'cookieReadback', 'cookieHttpEffect', 'cookieDeleted', 'observerCoexistence', 'commandTimeout', 'commandCancellation', 'contextRevocation')) { $cdp.checks[$name] = $true }
$cdpJson = ConvertTo-Json $cdp -Depth 10
Assert-CdpProbeRecords -Records @($cdpJson | ConvertFrom-Json) -Platform x64 -CefSharpVersion 84.4.10
foreach ($mutation in @('missingCheck', 'falseCheck', 'nonBoolean', 'wrongVersion', 'wrongPlatform', 'missingTiming', 'failed', 'duplicate')) {
    $record = $cdpJson | ConvertFrom-Json
    switch ($mutation) {
        missingCheck { $record.checks.PSObject.Properties.Remove('cookieHttpEffect') }
        falseCheck { $record.checks.cookieHttpEffect = $false }
        nonBoolean { $record.checks.cookieHttpEffect = 'true' }
        wrongVersion { $record.cefSharpVersion = '121.3.70.0' }
        wrongPlatform { $record.platform = 'x86' }
        missingTiming { $record.strictGm = $null }
        failed { $record.status = 'failed' }
    }
    $records = @($record)
    if ($mutation -eq 'duplicate') { $records += $record }
    $rejected = $false
    try { Assert-CdpProbeRecords -Records $records -Platform x64 -CefSharpVersion 84.4.10 } catch { $rejected = $true }
    if (!$rejected) { throw "Invalid CDP result was accepted: $mutation." }
}
Write-Host 'CDP result validation passed (1 positive, 8 negative cases).'
