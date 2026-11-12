// ==UserScript==
// @name Isolated about documents
// @include about:blank
// @include about:srcdoc
// @sandbox DOM
// @grant GM.info
// @run-at document-end
// ==/UserScript==
document.documentElement.setAttribute('data-about-isolated', location.href + '|' + GM_info.compatibility.executionWorld);
