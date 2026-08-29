using System;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    internal static class CompatibilityFixtures
    {
        public const string LegacyStorage =
            "/* fixture license */\n" +
            "// ==UserScript==\n" +
            "// @name legacy storage\n" +
            "// @namespace https://example.test/\n" +
            "// @match https://example.com/*\n" +
            "// @grant GM_getValue\n" +
            "// @grant GM_setValue\n" +
            "// @grant GM_listValues\n" +
            "// @run-at document-end\n" +
            "// ==/UserScript==\n" +
            "var fixtureCount = GM_getValue('count', 0);\n" +
            "GM_setValue('count', fixtureCount + 1);\n" +
            "this.fixtureCount = GM_getValue('count', 0);";

        public const string LegacyResourceAndCallbacks =
            "// ==UserScript==\n" +
            "// @name legacy callbacks\n" +
            "// @match https://example.com/*\n" +
            "// @grant GM_getResourceText\n" +
            "// @grant GM_getResourceURL\n" +
            "// @grant GM_xmlhttpRequest\n" +
            "// @grant GM_registerMenuCommand\n" +
            "// @resource template https://cdn.example/template.txt\n" +
            "// @connect api.example.com\n" +
            "// ==/UserScript==\n" +
            "GM_xmlhttpRequest({ url: 'https://api.example.com/data', onload: function () {} });";

        public const string LegacyHeaderPreamble =
            "// Copyright (c) MonkeySharp\n\n" +
            "// ==UserScript==\n" +
            "// @name preamble\n" +
            "// @match https://example.com/*\n" +
            "// @grant none\n" +
            "// ==/UserScript==\n" +
            "void 0;";

        public const string MissingDependencyProvider =
            "// ==UserScript==\n" +
            "// @name missing dependency\n" +
            "// @match https://example.com/*\n" +
            "// @grant none\n" +
            "// @require https://cdn.example/dependency.js\n" +
            "// ==/UserScript==\n" +
            "window.missingDependencyFixture = true;";
    }
}
