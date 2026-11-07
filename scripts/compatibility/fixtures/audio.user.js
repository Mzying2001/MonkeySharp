// ==UserScript==
// @name Audio contract reference
// @match http://127.0.0.1/*
// @grant GM.audio
// @grant GM_audio
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = {schemaVersion:1,caseId:'audio',immediateReturn:{},promise:{},events:[],effects:{}};
    try {
        r.immediateReturn.modern = typeof GM.audio;
        r.immediateReturn.legacy = typeof GM_audio;
        r.immediateReturn.legacyGetMethod = typeof GM_audio.getState;
        r.immediateReturn.modernGetMethod = typeof GM.audio?.getState;
        if (typeof GM.audio?.getState !== 'function' || typeof GM_audio.getState !== 'function') throw new Error('Audio API unavailable in this reference');
        r.effects.initial = await GM.audio.getState();
        const listener = event => r.events.push(event);
        const registration = GM.audio.addStateChangeListener(listener);
        r.immediateReturn.add = typeof registration?.then;
        r.promise.addValue = await registration;
        const muted = GM.audio.setMute({isMuted:true});
        r.immediateReturn.set = typeof muted?.then;
        const setValue = await muted;
        r.promise.set = typeof setValue;
        r.promise.setValue = setValue;
        r.effects.muted = await GM.audio.getState();
        await new Promise(resolve => {
            r.immediateReturn.legacyGet = typeof GM_audio.getState(state => {r.effects.legacyState = state; resolve();});
        });
        await new Promise((resolve,reject) => {
            r.immediateReturn.legacySet = typeof GM_audio.setMute({isMuted:false}, error => error ? reject(error) : resolve());
        });
        await new Promise(resolve => setTimeout(resolve,150));
        r.effects.unmuted = await GM.audio.getState();
        const removal = GM.audio.removeStateChangeListener(listener);
        r.immediateReturn.remove = typeof removal?.then;
        r.promise.remove = typeof await removal;
    } catch (error) { r.effects.error = String(error); }
    fetch('/evidence',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(r)});
})();
