// ==UserScript==
// @name Manual metadata condition
// @include https://does-not-match.invalid/*
// @exclude *
// @run-at context-menu
// @grant none
// ==/UserScript==
document.documentElement.dataset.manualCondition = 'ran';
