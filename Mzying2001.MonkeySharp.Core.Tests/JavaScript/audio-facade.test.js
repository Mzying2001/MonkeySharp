const { waitFor } = require('./test-wait');
const completePlan = require('./test-plan');
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const template = require('./bootstrap-template');

test('audio preserves callback/Promise returns and removes the exact listener during registration', async () => {
    const calls = [];
    let pendingAdd;
    globalThis.__MonkeySharpBridge = { dispatch: async json => {
        const request = JSON.parse(json);
        if (request.type === 'hello') return JSON.stringify({type:'hello-result',protocol:2,ok:true,limits:{},apis:['GM.audio']});
        calls.push(request);
        if (request.params.operation === 'addListener') await new Promise(resolve => { pendingAdd = resolve; });
        return JSON.stringify({type:'response',protocol:2,requestId:request.requestId,ok:true,
            result: request.params.operation === 'getState' ? {isMuted:false} : request.params.operation === 'setMute' ? {isMuted:request.params.isMuted} : {$monkeySharpType:'undefined'}});
    }};
    const payload = completePlan({protocol:2,documentId:'audio-document',frameId:'main',runAt:'DocumentEnd',invocations:[{
        executionId:'audio-execution',scriptKey:'77777777-7777-7777-7777-777777777777',
        source:'globalThis.audio = GM.audio; globalThis.legacyAudio = GM_audio;',
        grants:['GM.audio'],declaredGrants:['GM_audio'],info:{},capability:'cap',deliveryToken:'delivery'
    }]});
    vm.runInThisContext(template.replace('__MONKEYSHARP_PAYLOAD_BASE64__', Buffer.from(JSON.stringify(payload)).toString('base64')));
    await waitFor(() => globalThis.audio, 'audio bootstrap');
    assert.deepEqual(await audio.getState(), {isMuted:false});
    assert.deepEqual(await audio.setMute({isMuted:true}), {isMuted:true});
    assert.equal(typeof legacyAudio, 'function');
    await assert.rejects(audio.setMute({isMuted:'true'}), TypeError);
    let callbackState;
    assert.equal(legacyAudio.getState(value => { callbackState = value; }), undefined);
    await waitFor(() => callbackState, 'audio callback');
    assert.deepEqual(callbackState, {isMuted:false});
    const events = [];
    const listener = event => events.push(event);
    const ready = audio.addStateChangeListener(listener);
    assert.equal(audio.addStateChangeListener(listener), ready);
    await waitFor(() => pendingAdd, 'audio listener registration');
    const id = calls.find(item => item.params.operation === 'addListener').params.listenerId;
    const receive = () => __MonkeySharpRuntime.receive({type:'notification',protocol:2,executionId:'audio-execution',deliveryToken:'delivery',event:'audio-state',data:{listenerId:id,change:{muted:'extension'}}});
    assert.equal(receive(), true);
    assert.deepEqual(events, [{muted:'extension'}]);
    const removed = audio.removeStateChangeListener(listener);
    assert.equal(receive(), false);
    pendingAdd();
    await ready;
    assert.equal(await removed, undefined);
    assert.equal(calls.filter(item => item.params.operation === 'addListener').length, 1);
    assert.equal(calls.filter(item => item.params.operation === 'removeListener').length, 1);
    assert.equal(await audio.removeStateChangeListener(listener), undefined);
});
