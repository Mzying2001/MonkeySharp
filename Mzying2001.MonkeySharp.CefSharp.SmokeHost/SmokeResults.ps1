function Assert-CdpProbeRecords {
    param([object[]]$Records, [string]$Platform, [string]$CefSharpVersion)
    $probes = @($Records | Where-Object { $_.type -eq 'cdp-probe' })
    if ($probes.Count -ne 1 -or $probes[0].status -ne 'passed') { throw 'Exactly one successful CDP probe is required.' }
    $probe = $probes[0]
    if ($probe.platform -ne $Platform) { throw 'CDP probe process architecture mismatch.' }
    $actual = [version]$probe.cefSharpVersion
    $expected = [version]$CefSharpVersion
    if ($actual.Major -ne $expected.Major -or $actual.Minor -ne $expected.Minor -or $actual.Build -ne $expected.Build) { throw 'CDP probe CefSharp version mismatch.' }
    foreach ($name in @('preparedNavigation', 'pageBeforeFirstInline', 'isolatedBeforeFirstInline', 'realGmEventuallyReady', 'nativeFrameIdentity', 'perScriptWorld', 'contextIdBinding', 'cookieEngineAccepted', 'cookieReadback', 'cookieHttpEffect', 'cookieDeleted', 'observerCoexistence', 'commandTimeout', 'commandCancellation', 'contextRevocation')) {
        if ($probe.checks.$name -isnot [bool] -or !$probe.checks.$name) { throw "CDP assertion missing or failed: $name." }
    }
    if ($probe.strictGm -notin @('blocked', 'observed-in-this-run-only')) { throw 'GM timing limitation must be explicit.' }
}

function Assert-SmokeRecords {
    param([object[]]$Records, [string]$Platform, [string]$CefSharpVersion, [int]$RunExitCode = 0, [ValidateSet('all', 'webRequest')][string]$Case = 'all')

    if ($RunExitCode -ne 0) { throw "SmokeHost exited with code $RunExitCode." }
    if (@($Records | Where-Object { $_.type -in @('smoke-timeout', 'runner-timeout') }).Count -gt 0) { throw 'SmokeHost timed out.' }
    $summaries = @($Records | Where-Object { $_.type -eq 'smoke-summary' })
    $finals = @($Records | Where-Object { $_.type -eq 'final-disposal' })
    if ($summaries.Count -ne 1 -or $summaries[0].success -isnot [bool] -or !$summaries[0].success) { throw 'Exactly one successful smoke-summary is required.' }
    $summary = $summaries[0]
    if (($Case -ne 'all' -or $summary.compatibilityCase) -and $summary.compatibilityCase -ne $Case) { throw 'Smoke case does not match this invocation.' }
    if ($summary.hostAttachedAfterDetach -isnot [bool] -or $summary.hostAttachedAfterDetach) { throw 'Host was not detached.' }
    if ($finals.Count -ne 1 -or $finals[0].disposed -isnot [bool] -or !$finals[0].disposed) { throw 'Final disposal is required.' }
    $final = $finals[0]
    if ($final.platform -ne $Platform) { throw "Unexpected process platform: $($final.platform)." }
    $actual = [version]$final.cefSharpVersion
    $expected = [version]$CefSharpVersion
    if ($actual.Major -ne $expected.Major -or $actual.Minor -ne $expected.Minor -or $actual.Build -ne $expected.Build) { throw "Unexpected CefSharp assembly version: $actual (requested $expected)." }
    foreach ($name in @('storageMirror', 'xhr', 'xhrExtended', 'notification', 'tab', 'download', 'cookie', 'webRequest', 'navigation')) {
        if ($summary.$name -isnot [bool] -or !$summary.$name) { throw "SmokeHost check failed or missing: $name." }
    }
    if ($summary.mainFrameLoads -lt 2 -or $summary.childFrameLoads -lt 1 -or $summary.contextReleased -lt 1) { throw 'Browser lifecycle evidence is missing.' }
    foreach ($phase in @('initial', 'navigation')) {
        $fixtures = @($Records | Where-Object { $_.type -eq 'fixture-result' -and $_.phase -eq $phase })
        if ($fixtures.Count -ne 1 -or $fixtures[0].success -isnot [bool] -or !$fixtures[0].success) { throw "Legacy fixture failed or missing: $phase." }
    }
    $cases = @($Records | Where-Object { $_.type -eq 'compatibility-case' })
    $requiredCases = if ($Case -eq 'all') { @('prepared', 'conditions', 'isolated', 'replay', 'csp', 'permissions', 'binary', 'pageHelpers', 'frames', 'spaUrl', 'download', 'cookie', 'webRequest', 'audio', 'partitions') } else { @('webRequest') }
    if ($cases.Count -ne $requiredCases.Count) { throw 'Compatibility case count does not match the selected scope.' }
    foreach ($id in $requiredCases) {
        $matches = @($cases | Where-Object { $_.caseId -eq $id })
        if ($matches.Count -ne 1 -or $matches[0].success -isnot [bool] -or !$matches[0].success -or $matches[0].error) { throw "Compatibility case failed or missing: $id." }
        $checks = @($matches[0].checks.PSObject.Properties)
        if ($checks.Count -eq 0 -or @($checks | Where-Object { $_.Value -isnot [bool] -or !$_.Value }).Count -gt 0) { throw "Compatibility assertions failed or missing: $id." }
        if ($summary.compatibility.$id -isnot [bool] -or !$summary.compatibility.$id) { throw "Compatibility summary failed: $id." }
        if ($id -eq 'partitions') {
            $required = if ($expected.Major -lt 114) { @('listRejected','setRejected','deleteRejected') }
                else { @('partitionA','partitionB','siteNormalization','partitionDeleteIsExact','cleaned','partitionCrudExecuted') }
            if ($expected.Major -ge 128) { $required += @('ancestorIdentities','defaultDeleteAncestor','ancestorCrudExecuted') }
            foreach ($name in $required) {
                if ($matches[0].checks.$name -isnot [bool] -or !$matches[0].checks.$name) { throw "Partition assertion missing or failed: $name." }
            }
        }
    }
}
