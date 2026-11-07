// ==UserScript==
// @name Matching conditions reference
// @namespace MonkeySharp compatibility
// @run-at document-end
// @grant none
// @include *://127.0.0.1:__PORT__/allowed*
// @include *://*.example.test/host*
// @include /^http:\/\/127\.0\.0\.1:[0-9]+\/regex\/Mixed$/
// @include http://127.0.0.1:__PORT__/literal?query=*
// @include http://127.0.0.1:__PORT__/encoded/%41?%61=*
// @exclude *://127.0.0.1:__PORT__/allowed/excluded*
// @match http://127.0.0.1:__PORT__/match?value=*
// @match http*://127.0.0.1:__PORT__/Scheme*
// @match http://[::1]:__IPV6_PORT__/ipv6*
// @match http://例子.测试:__PORT__/idn*
// @match http://xn--fsqu00a.xn--0zwm56d:__PORT__/puny*
// @match http://127.0.0.1:*/wild-port
// ==/UserScript==
document.documentElement.dataset.matchProbe = "ran";
fetch("/evidence", { method: "POST", body: JSON.stringify({ caseId: "matching-url", effects: { url: location.href } }) });
