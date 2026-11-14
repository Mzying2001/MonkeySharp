$script:DemoRequiredChecks = @(
    'localizedResources', 'localizedPermissionPrompt', 'faviconDomProbe', 'permissionPrompt',
    'permissionRemembered', 'storageResourcesXhrCookieWebRequest', 'audioMuteOnly', 'cookiePartitionCapability',
    'tabStateScriptIsolation', 'domAndInfo', 'iframe', 'menuScriptNames',
    'menuGroupHeaders', 'menuGroupTextAlignment', 'menuCommandLabels', 'menuCommandBindings',
    'menuKeyboardNavigation', 'menuRowsMatchCommands', 'menu', 'menuKeyboardFields',
    'menuOptions', 'menuStaysOpenOnClick', 'menuUpdate', 'menuAutoCloseOnClick',
    'multiFrameMenu', 'backgroundTab', 'tabSwitchPreservesDocument', 'crossTabValues',
    'undefinedSnapshot', 'windowFocus', 'tabStateNavigation', 'undefinedNavigation',
    'sharedCookies', 'openInTab', 'tabRelativeUrlAndPosition', 'tabCloseOnce',
    'download', 'downloadSaveCanceled', 'notificationWaits', 'notification',
    'notificationTag', 'notificationPreventDefault', 'notificationUrl', 'notificationHighlight',
    'clipboardHtml', 'clipboardText', 'tabState', 'localizedBrowser',
    'tabFavicon', 'selectedTabColors', 'inactiveSelectedTabColors', 'localizedScriptManager',
    'scriptManagerDetails', 'scriptManagerInlineToggle', 'scriptManagerCtrlS', 'navigationCleanup',
    'disableAndReload', 'enableAndReload', 'emptyScriptMenuHidden', 'registeredScriptMenuVisible',
    'crossSiteMenuCleanup', 'crossSiteMenuCallback', 'historyMenuRefresh', 'reloadMenuRefresh',
    'sameDocumentMenuPreserved', 'unmatchedPageMenuEmpty', 'navigationHidesEmptyScriptMenu', 'navigationMenuTabIsolation',
    'scriptMenuVisibilityFollowsTab', 'lastCommandHidesScriptMenu', 'privateNativeContext', 'privateInfoRunIn',
    'normalInfoRunIn', 'privateCookieNetworkIsolation', 'privatePermissionIsolation', 'privateCacheIsolation',
    'privateSessionShared', 'privateCacheShared', 'privateTabDataIsolation', 'privateWritesNotPersistent',
    'privateLivesUntilLastTab', 'privateContextReleased', 'privateFreshSession', 'privatePermissionCleared',
    'privateCacheCleared', 'privateLogsNotPersisted', 'windowClose', 'closeTab',
    'closeTabSelectsPrevious', 'lastTabPolicy', 'closeTabsDuringRequests', 'finalDisposal'
)
$script:NavigationRequiredChecks = @(
    'emptyScriptMenuHidden', 'registeredScriptMenuVisible', 'crossSiteMenuCleanup', 'crossSiteMenuCallback',
    'historyMenuRefresh', 'reloadMenuRefresh', 'sameDocumentMenuPreserved', 'unmatchedPageMenuEmpty',
    'navigationHidesEmptyScriptMenu', 'navigationMenuTabIsolation', 'scriptMenuVisibilityFollowsTab', 'lastCommandHidesScriptMenu',
    'finalDisposal'
)

function Assert-DemoSmokeReport {
    param($Result, [string]$CefSharpVersion, [string]$Case = 'all', [int]$RunExitCode = 0)
    if ($RunExitCode -ne 0 -or $Result.success -isnot [bool] -or !$Result.success) { throw 'Demo smoke did not complete successfully.' }
    if ($Result.schemaVersion -ne 1 -or $Result.smokeCase -ne $Case) { throw 'Demo report schema/case does not match this invocation.' }
    if ($Result.environment.platform -ne 'x64' -or $Result.environment.uiLanguage -ne 'en') { throw 'Demo requires actual x64/default English evidence.' }
    $actual = [version]$Result.environment.cefSharpVersion
    $expected = [version]$CefSharpVersion
    if ($actual.Major -ne $expected.Major -or $actual.Minor -ne $expected.Minor -or $actual.Build -ne $expected.Build) { throw 'Demo report has the wrong CefSharp version.' }
    $required = if ($Case -eq 'all') { $script:DemoRequiredChecks } else { $script:NavigationRequiredChecks }
    foreach ($name in $required) {
        if ($Result.checks.$name -isnot [bool] -or !$Result.checks.$name) { throw "Missing or unsuccessful Demo assertion: $name" }
    }
    if (@($Result.checks.PSObject.Properties | Where-Object { $_.Value -isnot [bool] -or !$_.Value }).Count) { throw 'Demo report contains unsuccessful assertions.' }
}
