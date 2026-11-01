// ==UserScript==
// @name Compatibility noframes
// @match http://127.0.0.1:__PORT__/compat/frames*
// @match http://127.0.0.1:__OTHER_PORT__/compat/frames*
// @grant none
// @noframes
// @run-at document-end
// ==/UserScript==
globalThis.__compatNoFrames = (globalThis.__compatNoFrames || 0) + 1;
