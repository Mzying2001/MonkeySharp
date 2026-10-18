using System;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    internal sealed class CompatibilityFixtureDescriptor
    {
        public CompatibilityFixtureDescriptor(
            string name,
            string source,
            string sourceOrigin,
            IEnumerable<string> declaredGrants,
            IEnumerable<string> expectedApis,
            IEnumerable<string> expectedReturnTypes,
            IEnumerable<string> expectedSequence,
            IEnumerable<string> expectedCallbackShapes,
            IEnumerable<string> expectedErrorCodes)
        {
            Name = name;
            Source = source;
            SourceOrigin = sourceOrigin;
            DeclaredGrants = (declaredGrants ?? Enumerable.Empty<string>()).ToArray();
            ExpectedApis = (expectedApis ?? Enumerable.Empty<string>()).ToArray();
            ExpectedReturnTypes = (expectedReturnTypes ?? Enumerable.Empty<string>()).ToArray();
            ExpectedSequence = (expectedSequence ?? Enumerable.Empty<string>()).ToArray();
            ExpectedCallbackShapes = (expectedCallbackShapes ?? Enumerable.Empty<string>()).ToArray();
            ExpectedErrorCodes = (expectedErrorCodes ?? Enumerable.Empty<string>()).ToArray();
        }

        public string Name { get; }
        public string Source { get; }
        public string SourceOrigin { get; }
        public IReadOnlyList<string> DeclaredGrants { get; }
        public IReadOnlyList<string> ExpectedApis { get; }
        public IReadOnlyList<string> ExpectedReturnTypes { get; }
        public IReadOnlyList<string> ExpectedSequence { get; }
        public IReadOnlyList<string> ExpectedCallbackShapes { get; }
        public IReadOnlyList<string> ExpectedErrorCodes { get; }
    }

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
            "// @grant GM_addValueChangeListener\n" +
            "// @run-at document-end\n" +
            "// ==/UserScript==\n" +
            "var fixtureCount = GM_getValue('count', 0);\n" +
            "GM_setValue('count', fixtureCount + 1);\n" +
            "GM_addValueChangeListener('count', function () {});\n" +
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

        public const string ExplicitNone =
            "// ==UserScript==\n" +
            "// @name explicit none\n" +
            "// @match https://example.com/*\n" +
            "// @grant none\n" +
            "// ==/UserScript==\n" +
            "void 0;";

        public const string MissingGrant =
            "// ==UserScript==\n" +
            "// @name missing grant\n" +
            "// @match https://example.com/*\n" +
            "// ==/UserScript==\n" +
            "void 0;";

        public const string UnsupportedXhrInputs =
            "// ==UserScript==\n" +
            "// @name unsupported xhr inputs\n" +
            "// @match https://example.com/*\n" +
            "// @grant GM_xmlhttpRequest\n" +
            "// @connect api.example.com\n" +
            "// ==/UserScript==\n" +
            "GM_xmlhttpRequest({ url: 'https://api.example.com/', proxy: {}, cookiePartition: {}, binary: true, responseXML: true, synchronous: true });";

        public const string LegacyXhr =
            "// ==UserScript==\n" +
            "// @name legacy xhr\n" +
            "// @match https://example.com/*\n" +
            "// @grant GM_xmlhttpRequest\n" +
            "// @connect api.example.com\n" +
            "// ==/UserScript==\n" +
            "GM_xmlhttpRequest({ url: 'https://api.example.com/data', onload: function () {}, onerror: function () {} });";

        public const string WindowFacade =
            "// ==UserScript==\n" +
            "// @name window facade\n" +
            "// @match https://example.com/*\n" +
            "// @grant window.close\n" +
            "// @grant window.focus\n" +
            "// @grant window.onurlchange\n" +
            "// ==/UserScript==\n" +
            "window.onurlchange = function () {}; window.close(); window.focus();";

        public const string GrantList =
            "// ==UserScript==\n" +
            "// @name grant list\n" +
            "// @match https://example.com/*\n" +
            "// @grant GM_getValue\n" +
            "// @grant GM_setValue\n" +
            "// ==/UserScript==\n" +
            "GM_setValue('key', GM_getValue('key', 0));";

        public static IReadOnlyList<CompatibilityFixtureDescriptor> All { get; } =
            new CompatibilityFixtureDescriptor[]
            {
                new CompatibilityFixtureDescriptor(
                    "storage",
                    LegacyStorage,
                    "fixture://legacy-storage.user.js",
                    new[] { "GM_getValue", "GM_setValue", "GM_listValues" },
                    new[] { "GM.getValue", "GM.setValue", "GM.listValues", "GM.addValueChangeListener" },
                    new[] { "legacy-sync-value", "promise-value" },
                    new[] { "sync-read", "async-commit", "value-change" },
                    new[] { "(key, oldValue, newValue, remote)" },
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "legacy-xhr",
                    LegacyXhr,
                    "fixture://legacy-xhr.user.js",
                    new[] { "GM_xmlhttpRequest" },
                    new[] { "GM.xmlHttpRequest", "GM_xmlhttpRequest" },
                    new[] { "abort-handle", "promise" },
                    new[] { "create", "execute", "onload/onerror", "onabort", "ontimeout" },
                    new[] { "(response)", "(error)" },
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "download",
                    "// ==UserScript==\n// @name download\n// @match https://example.com/*\n// @grant GM_download\n// ==/UserScript==\nGM_download('https://example.com/file');",
                    "fixture://download.user.js",
                    new[] { "GM_download" },
                    new[] { "GM.download", "GM_download" },
                    new[] { "download-handle", "promise" },
                    new[] { "start", "progress", "complete", "error", "abort" },
                    new[] { "(progress)", "(error)" },
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "cookie",
                    "// ==UserScript==\n// @name cookie\n// @match https://example.com/*\n// @grant GM_cookie\n// ==/UserScript==\nGM_cookie.list(function () {});",
                    "fixture://cookie.user.js",
                    new[] { "GM_cookie" },
                    new[] { "GM.cookie", "GM_cookie" },
                    new[] { "promise", "legacy-callback" },
                    new[] { "default-url", "explicit-url", "callback" },
                    new[] { "(cookies, error)", "(error)" },
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "webRequest",
                    "// ==UserScript==\n// @name web request\n// @match https://example.com/*\n// @grant GM_webRequest\n// ==/UserScript==\nGM_webRequest([{selector:'https://example.com/*', action:'cancel'}], function () {});",
                    "fixture://webrequest.user.js",
                    new[] { "GM_webRequest" },
                    new[] { "GM.webRequest", "GM_webRequest" },
                    new[] { "registration-handle", "promise" },
                    new[] { "register", "callback", "remove" },
                    new[] { "(info, message, details)" },
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "window",
                    WindowFacade,
                    "fixture://window.user.js",
                    new[] { "window.close", "window.focus", "window.onurlchange" },
                    new[] { "window.close", "window.focus", "window.onurlchange" },
                    new[] { "function", "undefined" },
                    new[] { "grant-gating", "url-change" },
                    new[] { "(event)" },
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "grant-missing",
                    MissingGrant,
                    "fixture://grant-missing.user.js",
                    Array.Empty<string>(),
                    new[] { "GM", "GM_info" },
                    new[] { "undefined" },
                    new[] { "facade-absent" },
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "grant-none",
                    ExplicitNone,
                    "fixture://grant-none.user.js",
                    new[] { "none" },
                    new[] { "GM.info", "GM_info" },
                    new[] { "object", "undefined" },
                    new[] { "info-only" },
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "grant-list",
                    GrantList,
                    "fixture://grant-list.user.js",
                    new[] { "GM_getValue", "GM_setValue" },
                    new[] { "GM.getValue", "GM.setValue" },
                    new[] { "function", "promise" },
                    new[] { "facade-present" },
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                new CompatibilityFixtureDescriptor(
                    "unsupported-xhr-inputs",
                    UnsupportedXhrInputs,
                    "fixture://unsupported-xhr-inputs.user.js",
                    new[] { "GM_xmlhttpRequest" },
                    new[] { "GM.xmlHttpRequest", "GM_xmlhttpRequest" },
                    new[] { "throw" },
                    new[] { "input-validation" },
                    new[] { "error.code" },
                    new[] { "MSP006_NOT_SUPPORTED" })
            };
    }
}
