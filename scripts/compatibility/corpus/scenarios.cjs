const assetRoot = "document.querySelector('[data-sniffer-self]')?.shadowRoot";
const settingsRoot = "document.querySelector('[data-fsfts-host]')?.shadowRoot";
const open = url => ({ kind: 'open', url });
const assert = (name, expression) => ({ kind: 'assert', name, expression });
const evaluate = (expression, name) => ({ kind: 'evaluate', expression, ...(name ? { name } : {}) });
const wait = milliseconds => ({ kind: 'wait', milliseconds });
const menu = () => ({ kind: 'menu', caption: 'Settings' });
const panel = () => [assert('settingsPanel', `${settingsRoot}?.querySelector('input[type=checkbox]') != null`), wait(300)];
const toggle = () => evaluate(`(()=>{const e=${settingsRoot}.querySelector('input[data-key="keyword_github"]');e.checked=false;e.dispatchEvent(new Event('change',{bubbles:true}));return true})()`);
const assetReady = () => [assert('assetToolbar', `${assetRoot}?.querySelector('[data-fab]') != null`), wait(1700), evaluate(`(${assetRoot}.querySelector('[data-fab]').click(),true)`),
    assert('resourceCollected', `${assetRoot}.querySelector('.card')?.title.includes('/protected/')` )];

module.exports = base => [
    { id: 'dom-decoration', script: 'site-asset-dl', actions: [open(base + '/corpus'), ...assetReady(),
        assert('computedStyle', `getComputedStyle(${assetRoot}.querySelector('[data-fab]')).position === 'fixed'`),
        evaluate("(history.pushState({},'', '/route-dom'),true)"), wait(1500),
        assert('singleDecoration', "document.querySelectorAll('[data-sniffer-self]').length === 1")] },
    { id: 'dom-decoration-csp', category: 'dom-decoration', script: 'site-asset-dl', actions: [open(base + '/corpus?csp=1'),
        assert('nodeCreatedUnderCsp', `${assetRoot}?.querySelector('[data-fab]') != null`),
        evaluate(`getComputedStyle(${assetRoot}.querySelector('[data-fab]')).position === 'fixed'`, 'styleApplied'),
        assert('singleCspDecoration', "document.querySelectorAll('[data-sniffer-self]').length === 1")] },
    { id: 'page-hook', script: 'site-asset-dl', actions: [open(base + '/corpus'), ...assetReady(),
        evaluate('window.__firstInlineHook === true', 'hookBeforeFirstInline'),
        assert('historyHookInstalled', "history.pushState.__snifferPatched === true"),
        evaluate(`(()=>{document.getElementById('asset').src='/protected/second.png';history.pushState({},'', '/route-hook');return true})()`), wait(1800),
        assert('hookRefreshesResources', `Array.from(${assetRoot}.querySelectorAll('.card')).some(card => card.title.includes('second.png'))`)] },
    { id: 'authenticated-xhr', script: 'site-asset-dl', actions: [open(base + '/corpus'), ...assetReady(),
        evaluate(`(${assetRoot}.querySelector('[data-a="probe"]').click(),true)`), wait(1800),
        evaluate(`({status:${assetRoot}.querySelector('[data-r="status"]').textContent,cards:Array.from(${assetRoot}.querySelectorAll('.card')).map(card=>card.textContent)})`, 'probeResponseUi'),
        evaluate(`(${assetRoot}.querySelector('[data-a="zip"]').click(),true)`),
        { kind: 'files', name: 'authenticatedBytesPacked', minimum: 1 }] },
    { id: 'download', script: 'site-asset-dl', actions: [open(base + '/corpus'), ...assetReady(),
        evaluate(`(${assetRoot}.querySelector('[data-a="down"]').click(),true)`),
        { kind: 'files', name: 'fileSaved', minimum: 1 }] },
    { id: 'download-cancel', category: 'download', script: 'site-asset-dl', cancelDownloads: true,
        actions: [open(base + '/corpus'), ...assetReady(), evaluate(`(${assetRoot}.querySelector('[data-a="down"]').click(),true)`),
            wait(3500), evaluate(`({url:location.href,status:${assetRoot}?.querySelector('[data-r="status"]')?.textContent || null})`, 'cancelStatus'),
            { kind: 'files', name: 'cancellationLeavesNoFile', minimum: 0, maximum: 0 }] },
    { id: 'authenticated-xhr-denied', category: 'authenticated-xhr', script: 'site-asset-dl', managers: ['monkeysharp'], denyNetwork: true,
        actions: [open(base + '/corpus'), ...assetReady(), evaluate("navigator.sendBeacon('/phase/denial-start')"), wait(150),
            evaluate(`(${assetRoot}.querySelector('[data-a="zip"]').click(),true)`),
            assert('deniedRequestShowsFailure', `${assetRoot}.querySelector('[data-r="status"]').textContent.includes('全部下载失败')`),
            { kind: 'files', name: 'denialLeavesNoFile', minimum: 0, maximum: 0 }] },
    { id: 'storage-listener', script: 'find-scripts', actions: [open(base + '/settings-a'), menu(), ...panel(),
        open(base + '/settings-b'), menu(), ...panel(), { kind: 'switch', index: 0 }, toggle(), wait(500),
        { kind: 'assertMenu', name: 'localMenuRemoved', caption: 'keyword on GitHub', present: false },
        { kind: 'switch', index: 1 },
        { kind: 'assertMenu', name: 'remoteMenuRemoved', caption: 'keyword on GitHub', present: false },
        { kind: 'navigate', url: base + '/settings-reload' }, menu(), ...panel(),
        assert('persistedAfterNavigation', `${settingsRoot}.querySelector('input[data-key="keyword_github"]')?.checked === false`)] },
    { id: 'menu-configuration', script: 'find-scripts', actions: [open(base + '/settings-menu?frame=1'), wait(700),
        { kind: 'assertMenu', name: 'initialDomainMenu', caption: 'keyword on GitHub', present: true }, menu(), ...panel(), toggle(), wait(500),
        { kind: 'assertMenu', name: 'updatedDomainMenu', caption: 'keyword on GitHub', present: false },
        { kind: 'navigate', url: base + '/settings-menu-reload' }, menu(), ...panel(),
        assert('configurationReadback', `${settingsRoot}.querySelector('input[data-key="keyword_github"]')?.checked === false`)] },
    { id: 'iframe-spa', script: 'site-asset-dl', actions: [open(base + '/corpus?frame=1'), ...assetReady(),
        assert('sameProcessFrameDecoration', "document.querySelector('iframe').contentDocument?.querySelector('[data-sniffer-self]')?.shadowRoot?.querySelector('[data-fab]') != null"),
        evaluate("(history.replaceState({},'', '/route-frame'),true)"), wait(1500),
        assert('spaNoDuplicate', "document.querySelectorAll('[data-sniffer-self]').length === 1"),
        evaluate("({oopif:'not-proven-by-same-process-frame',bfcache:'not-proven-by-history-update'})", 'excludedScopes')] }
];
module.exports.assetRoot = assetRoot;
