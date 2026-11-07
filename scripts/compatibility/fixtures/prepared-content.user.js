// ==UserScript==
// @name Prepared content reference
// @namespace MonkeySharp compatibility
// @match http://127.0.0.1/*
// @run-at document-end
// @grant GM_getResourceURL
// @grant GM_getResourceText
// @require http://127.0.0.1:__PORT__/fixture-content/sha1.js#md5=00000000000000000000000000000000,sha1=COm419TsXPT1dE+2AdzbvBO34Vk=
// @require http://127.0.0.1:__PORT__/fixture-content/sha384.js#md5=00000000000000000000000000000000,sha384=AxlvZDsP1psUX9djuhmm5JPTGbWhuzfHqwQN7Kzxhvah8NCS0aJZ0iFyEmzCX3vb
// @require http://127.0.0.1:__PORT__/fixture-content/sha512.js#md5=00000000000000000000000000000000,sha512=Cd9VwPmeun9pTkcMP1SN3jAq/nYBgcfZHCzY80cBVbwHpcHCDBGxD1UAMWkDdT8mQgmq2aTVHiw9cLStLhfd2g==
// @require http://127.0.0.1:__PORT__/fixture-content/charset.js
// @resource charset http://127.0.0.1:__PORT__/fixture-content/charset.js
// @resource binary http://127.0.0.1:__PORT__/fixture-content/binary.bin
// ==/UserScript==
fetch('/evidence', {method:'POST',body:JSON.stringify({schemaVersion:1,caseId:'prepared-content',events:[],promise:{},immediateReturn:{source:'executed'},effects:{shared:shared,strict:(function(){return this;})()===undefined,accented:accented,resourceText:GM_getResourceText('charset'),binary:GM_getResourceURL('binary').split(',')[1],hashes:'sha1,sha384,sha512'},info:{handler:GM_info.scriptHandler,version:GM_info.version,userAgent:navigator.userAgent}})});
