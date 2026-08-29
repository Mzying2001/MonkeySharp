/* Licensed compatibility fixture. */
// ==UserScript==
// @name Legacy userscript fixture
// @match https://example.com/*
// @grant GM_getValue
// @grant GM_setValue
// @grant GM_xmlhttpRequest
// @grant GM_registerMenuCommand
// @run-at document-end
// ==/UserScript==
var legacyFixtureValue = GM_getValue("value", 0);
GM_setValue("value", legacyFixtureValue + 1);
