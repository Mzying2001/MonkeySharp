// ==UserScript==
// @name Compatibility audio
// @match http://127.0.0.1:__PORT__/compat/audio*
// @grant GM.audio
// @grant GM_audio
// @run-at document-end
// ==/UserScript==
(async function () {
    const result = globalThis.__audioCase = {checks:{}, events:[]};
    const listener = event => result.events.push(event);
    await GM.audio.addStateChangeListener(listener);
    if (location.pathname === '/compat/audio-child') { result.ready = true; return; }
    if (location.pathname === '/compat/audio-next') {
        const state = await GM.audio.getState();
        result.checks.navigation = typeof state.isMuted === 'boolean' && !('isAudible' in state);
        result.nativeNavigationMute = state.isMuted;
        result.done = true; return;
    }
    try {
        while (!document.querySelector('iframe').contentWindow.__audioCase?.ready) await new Promise(resolve => setTimeout(resolve,25));
        const initial = await GM.audio.getState();
        result.checks.initial = initial.isMuted === false;
        result.checks.partialInfo = GM.info.compatibility.capabilities['GM.audio'] === 'partial:mute-only;audible-unavailable';
        result.checks.noFakeAudible = !('isAudible' in initial);
        const context = new AudioContext();
        const oscillator = context.createOscillator();
        const gain = context.createGain(); gain.gain.value = 0;
        oscillator.connect(gain); gain.connect(context.destination); oscillator.start();
        result.checks.silentStream = !('isAudible' in await GM.audio.getState());
        gain.gain.value = 0.01;
        result.checks.webAudio = !('isAudible' in await GM.audio.getState());
        result.checks.muteReturn = (await GM.audio.setMute({isMuted:true})).isMuted === true;
        const muted = await GM.audio.getState();
        result.checks.muted = muted.isMuted === true && muted.muteReason === 'extension';
        result.checks.callbackReturn = GM_audio.getState(state => { result.legacy = state; }) === undefined;
        while (!result.legacy) await new Promise(resolve => setTimeout(resolve,25));
        result.checks.callbackState = result.legacy.isMuted === true;
        await new Promise((resolve,reject) => GM_audio.setMute({isMuted:false}, error => error ? reject(error) : resolve()));
        result.checks.unmuted = (await GM.audio.getState()).isMuted === false;
        while (result.events.length < 2 || document.querySelector('iframe').contentWindow.__audioCase.events.length < 2) await new Promise(resolve => setTimeout(resolve,25));
        result.checks.multipleFrames = true;
        await GM.audio.removeStateChangeListener(listener);
        const count = result.events.length;
        await GM.audio.setMute({isMuted:true}); await GM.audio.setMute({isMuted:false});
        result.checks.removed = result.events.length === count;
        oscillator.stop(); await context.close();
        result.external = [];
        await GM.audio.addStateChangeListener(event => result.external.push(event));
        result.done = true;
    } catch (error) { result.error = String(error); result.done = true; }
})();
