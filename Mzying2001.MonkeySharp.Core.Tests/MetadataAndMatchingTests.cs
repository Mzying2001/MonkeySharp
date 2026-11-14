using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Matching;
using Mzying2001.MonkeySharp.Core.Parsing;
using System;
using System.Linq;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class MetadataAndMatchingTests
    {
        private readonly UserScriptMetadataParser _parser = new UserScriptMetadataParser();

        [Fact]
        public void UnclosedHeaderIsAnError()
        {
            var result = _parser.Parse("// ==UserScript==\n// @name broken");

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM001_HEADER_UNCLOSED");
        }

        [Fact]
        public void DuplicateSingletonKeepsFirstValue()
        {
            var result = _parser.Parse(Script("// @name first\n// @name second\n// @match https://example.com/*"));

            Assert.True(result.CanEnable);
            Assert.Equal("first", result.Metadata.Name);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM010_DUPLICATE_SINGLETON");
        }

        [Fact]
        public void DuplicateAntifeaturesKeepFirstDescriptionForEachTypeAndNormalizedLocale()
        {
            var result = _parser.Parse(Script("// @name disclosures\n// @match https://example.com/*\n" +
                "// @antifeature tracking first\n// @antifeature Tracking second\n" +
                "// @antifeature:en-US tracking English\n// @antifeature:EN-us TRACKING ignored\n" +
                "// @antifeature:de tracking Deutsch\n// @antifeature ads advertising"));
            Assert.True(result.CanEnable);
            Assert.Equal(4, result.Metadata.Antifeatures.Count);
            Assert.Equal("first", result.Metadata.Antifeatures[0].Description);
            Assert.Equal("English", Assert.Single(result.Metadata.Antifeatures, item => item.Locale == "en-US").Description);
            Assert.Equal(2, result.Diagnostics.Count(item => item.Code == "MSM063_DUPLICATE_ANTIFEATURE" && item.Severity == DiagnosticSeverity.Warning));
        }

        [Fact]
        public void MissingGrantIsDistinctFromExplicitNone()
        {
            var result = _parser.Parse(Script("// @name none\n// @match https://example.com/*"));

            Assert.Empty(result.Metadata.DeclaredGrants);
            Assert.Empty(result.Metadata.Grants);
            Assert.Equal(GrantDeclarationState.Missing, result.Metadata.GrantDeclarationState);

            var explicitNone = _parser.Parse(Script("// @name explicit\n// @match https://example.com/*\n// @grant none"));
            Assert.Equal(new[] { "none" }, explicitNone.Metadata.DeclaredGrants);
            Assert.Equal(new[] { "none" }, explicitNone.Metadata.Grants);
            Assert.Equal(GrantDeclarationState.ExplicitNone, explicitNone.Metadata.GrantDeclarationState);
        }

        [Theory]
        [InlineData("GM_getValue", "GM.getValue")]
        [InlineData("GM_getValues", "GM.getValues")]
        [InlineData("GM_setValues", "GM.setValues")]
        [InlineData("GM_deleteValues", "GM.deleteValues")]
        [InlineData("GM_getResourceURL", "GM.getResourceUrl")]
        public void LegacyGrantAliasesNormalizeWhilePreservingDeclarations(string alias, string canonical)
        {
            var result = new UserScriptMetadataParser().Parse(
                Script("// @name legacy\n// @match https://example.com/*\n// @grant " + alias));

            Assert.True(result.CanEnable);
            Assert.Equal(new[] { alias }, result.Metadata.DeclaredGrants);
            Assert.Equal(new[] { canonical }, result.Metadata.Grants);
        }

        [Fact]
        public void CookieAndWebRequestLegacyGrantsNormalizeToCanonicalCapabilities()
        {
            var result = new UserScriptMetadataParser().Parse(
                Script("// @name advanced legacy\n// @match https://example.com/*\n" +
                    "// @grant GM_cookie\n// @grant GM_webRequest"));

            Assert.True(result.CanEnable);
            Assert.Equal(new[] { "GM_cookie", "GM_webRequest" }, result.Metadata.DeclaredGrants);
            Assert.Equal(new[] { "GM.cookie", "GM.webRequest" }, result.Metadata.Grants);
        }

        [Theory]
        [InlineData("GM.getResourceUrl")]
        [InlineData("GM_getResourceURL")]
        public void OfficialResourceUrlNamesShareOneCapability(string grant)
        {
            var result = _parser.Parse(Script("// @name resource\n// @match https://example.com/*\n// @grant " + grant));
            Assert.True(result.CanEnable);
            Assert.Equal(new[] { grant }, result.Metadata.DeclaredGrants);
            Assert.Equal(new[] { "GM.getResourceUrl" }, result.Metadata.Grants);
            Assert.DoesNotContain(result.Diagnostics, item => item.Code == "MSM031_UNKNOWN_GRANT");
        }

        [Theory]
        [InlineData("GM.getResourceURL")]
        [InlineData("GM.windowClose")]
        public void UnofficialNamesDoNotAuthorizeAnyCapability(string grant)
        {
            var result = _parser.Parse(Script("// @name unknown\n// @match https://example.com/*\n// @grant " + grant));
            Assert.Empty(result.Metadata.Grants);
            Assert.Equal(new[] { grant }, result.Metadata.DeclaredGrants);
            Assert.Equal(GrantDeclarationState.ExplicitList, result.Metadata.GrantDeclarationState);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM031_UNKNOWN_GRANT");
        }

        [Fact]
        public void UnknownMetadataValuesArePreservedInOrder()
        {
            var result = _parser.Parse(Script(
                "// @name custom\n// @match https://example.com/*\n// @custom first\n// @custom second"));

            Assert.Equal(new[] { "first", "second" }, result.Metadata.AdditionalEntries["custom"]);
        }

        [Fact]
        public void ExtendedTampermonkeyMetadataIsTypedAndValidated()
        {
            var result = _parser.Parse(Script(
                "// @name extended\n// @match https://example.com/*\n" +
                "// @copyright Example\n// @icon64URL https://example.com/icon64.png\n" +
                "// @homepage https://example.com/home\n// @website https://example.com/site\n" +
                "// @source https://example.com/source\n// @sandbox JavaScript\n// @unwrap\n" +
                "// @antifeature ads Shows ads\n// @antifeature:de tracking Verfolgt Nutzer\n" +
                "// @webRequest {\"selector\":\"https://example.com/*\",\"action\":\"cancel\"}"));

            Assert.True(result.CanEnable);
            Assert.Equal("Example", result.Metadata.Copyright);
            Assert.Equal("https://example.com/icon64.png", result.Metadata.Icon64Url);
            Assert.Equal("https://example.com/home", result.Metadata.HomepageUrl);
            Assert.Equal("https://example.com/site", result.Metadata.WebsiteUrl);
            Assert.Equal("https://example.com/source", result.Metadata.SourceUrl);
            Assert.Equal("JavaScript", result.Metadata.Sandbox);
            Assert.True(result.Metadata.Unwrap);
            Assert.Equal(2, result.Metadata.Antifeatures.Count);
            Assert.Equal("de", result.Metadata.Antifeatures[1].Locale);
            Assert.Single(result.Metadata.WebRequest);
            Assert.Equal("cancel", result.Metadata.WebRequest[0].Action.GetString());
        }

        [Fact]
        public void InvalidStaticWebRequestMetadataIsAnError()
        {
            var result = _parser.Parse(Script(
                "// @name invalid webrequest\n// @match https://example.com/*\n" +
                "// @webRequest {\"selector\":{},\"action\":\"cancel\"}"));

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM062_INVALID_WEBREQUEST");
        }

        [Fact]
        public void InvalidMatchCannotBeEnabled()
        {
            var result = _parser.Parse(Script("// @name invalid\n// @match https://foo.*.example/*"));

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM020_INVALID_MATCH");
        }

        [Fact]
        public void AboutBlankMatchIsExactAndIgnoresOnlyFragments()
        {
            var result = _parser.Parse(Script("// @name about blank\n// @match about:blank"));
            var pattern = MatchPatternCompiler.Compile("about:blank");

            Assert.True(result.CanEnable);
            Assert.True(pattern.IsMatch(new Uri("about:blank")));
            Assert.True(pattern.IsMatch(new Uri("about:blank#section")));
            Assert.False(pattern.IsMatch(new Uri("about:blank?query=1")));
            Assert.False(pattern.IsMatch(new Uri("about:srcdoc")));
            Assert.False(pattern.IsMatch(new Uri("about://blank")));
            Assert.False(MatchPatternCompiler.TryCompile("<all_urls>", out _, out _));
        }

        [Theory]
        [InlineData("*://*.example.com/*", "https://example.com/page", true)]
        [InlineData("*://*.example.com/*", "http://example.com/page", true)]
        [InlineData("*://*.example.com/*", "https://www.example.com/page", true)]
        [InlineData("*://*.example.com/*", "https://a.b.example.com/page", true)]
        [InlineData("*://*.EXAMPLE.COM/*", "https://EXAMPLE.com/page", true)]
        [InlineData("*://*.example.com/*", "https://badexample.com/page", false)]
        [InlineData("*://*.example.com/*", "https://example.com.evil.test/page", false)]
        [InlineData("*://*.example.com/*", "https://other.test/page", false)]
        [InlineData("https://example.com/*", "https://www.example.com/page", false)]
        [InlineData("https://*.example.com/*", "http://example.com/page", false)]
        [InlineData("https://*.example.com:8443/path*", "https://example.com:8443/path?q=1#fragment", true)]
        [InlineData("https://*.example.com:8443/path*", "https://www.example.com:8443/path", true)]
        [InlineData("https://*.example.com:8443/path*", "https://example.com/path", false)]
        [InlineData("https://*.example.com:8443/path*", "https://www.example.com/path", false)]
        [InlineData("https://*.example.com:8443/path*", "https://example.com:8443/other", false)]
        [InlineData("https://*.xn--fsqu00a.xn--0zwm56d/*", "https://xn--fsqu00a.xn--0zwm56d/page", true)]
        [InlineData("https://*.xn--fsqu00a.xn--0zwm56d/*", "https://www.xn--fsqu00a.xn--0zwm56d/page", true)]
        public void MatchPatternHostRulesHonorWildcardsAndConstraints(string rule, string url, bool expected)
        {
            var pattern = MatchPatternCompiler.Compile(rule);

            Assert.Equal(expected, pattern.IsMatch(new Uri(url)));
        }

        [Fact]
        public void MatchPatternHonorsExplicitPort()
        {
            var pattern = MatchPatternCompiler.Compile("https://example.com:8443/*");

            Assert.True(pattern.IsMatch(new Uri("https://example.com:8443/page")));
            Assert.False(pattern.IsMatch(new Uri("https://example.com/page")));
        }

        [Fact]
        public void MatchPatternNormalizesIdnAndIgnoresFragment()
        {
            var pattern = MatchPatternCompiler.Compile("https://xn--fsqu00a.xn--0zwm56d/path?*");

            Assert.True(pattern.IsMatch(new Uri("https://xn--fsqu00a.xn--0zwm56d/path?q=1#different")));
            Assert.True(pattern.IsMatch(new Uri("https://\u4f8b\u5b50.\u6d4b\u8bd5/path?q=1#different")));
            Assert.False(MatchPatternCompiler.Compile("https://\u4f8b\u5b50.\u6d4b\u8bd5/path?*").IsMatch(new Uri("https://xn--fsqu00a.xn--0zwm56d/path?q=1")));
        }

        [Fact]
        public void MatchPatternTreatsQuestionMarkAsQuerySeparator()
        {
            var pattern = MatchPatternCompiler.Compile("https://example.com/path?key=*");

            Assert.True(pattern.IsMatch(new Uri("https://example.com/path?key=value")));
            Assert.False(pattern.IsMatch(new Uri("https://example.com/pathXkey=value")));
        }

        [Fact]
        public void ExclusionWinsOverPositiveRule()
        {
            var metadata = _parser.Parse(Script(
                "// @name exclusion\n" +
                "// @include https://example.com/*\n" +
                "// @exclude https://example.com/private/*")).Metadata;

            Assert.False(new UserScriptMatcher().IsMatch(metadata, new Uri("https://example.com/private/a")));
        }

        [Theory]
        [InlineData("match", "example.com")]
        [InlineData("include", "example.com")]
        [InlineData("match", "www.example.com")]
        [InlineData("include", "www.example.com")]
        public void WildcardExcludeMatchWinsOverPositiveRule(string directive, string host)
        {
            var result = _parser.Parse(Script(
                "// @name wildcard exclusion\n" +
                "// @" + directive + " https://" + host + "/*\n" +
                "// @exclude-match https://*.example.com/private/*"));
            var matcher = new UserScriptMatcher();

            Assert.True(result.CanEnable);
            Assert.True(matcher.IsMatch(result.Metadata, new Uri("https://" + host + "/public/a")));
            Assert.False(matcher.IsMatch(result.Metadata, new Uri("https://" + host + "/private/a")));
        }

        [Fact]
        public void IncludeAndExcludeAcceptSlashDelimitedRegularExpressions()
        {
            var metadata = _parser.Parse(Script(
                "// @name regex rules\n" +
                "// @include /^https?:\\/\\/example\\.com\\/products\\/.*$/\n" +
                "// @exclude /\\/private\\//"));
            var matcher = new UserScriptMatcher();

            Assert.True(metadata.CanEnable);
            Assert.True(matcher.IsMatch(metadata.Metadata, new Uri("https://example.com/products/one")));
            Assert.False(matcher.IsMatch(metadata.Metadata, new Uri("https://example.com/products/private/one")));
            Assert.False(matcher.IsMatch(metadata.Metadata, new Uri("https://example.com/other")));
        }

        [Fact]
        public void InvalidIncludeRegularExpressionIsAMetadataError()
        {
            var result = _parser.Parse(Script("// @name invalid regex\n// @include /[abc/"));

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item =>
                item.Code == "MSM021_INVALID_URL_REGEX" && item.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void HttpWildcardMatchCoversBothHttpAndHttps()
        {
            var result = _parser.Parse(Script("// @name http wildcard\n// @match http*://example.com/*"));
            var matcher = new UserScriptMatcher();

            Assert.True(result.CanEnable);
            Assert.True(matcher.IsMatch(result.Metadata, new Uri("http://example.com/path")));
            Assert.True(matcher.IsMatch(result.Metadata, new Uri("https://example.com/path")));
            Assert.False(matcher.IsMatch(result.Metadata, new Uri("file:///example.com/path")));
        }

        [Fact]
        public void LegacyCompatibilityFixtureParsesItsMetadataAndUrlRules()
        {
            var result = _parser.Parse(LegacyUserscriptFixture.Read());

            Assert.True(result.CanEnable);
            Assert.Equal("Legacy userscript compatibility fixture", result.Metadata.Name);
            Assert.Contains("GM_cookie", result.Metadata.DeclaredGrants);
            Assert.Contains("GM.cookie", result.Metadata.Grants);
            Assert.Single(result.Metadata.Requires);
            Assert.Single(result.Metadata.Resources);
            Assert.Single(result.Metadata.WebRequest);
            var matcher = new UserScriptMatcher();
            Assert.True(matcher.IsMatch(result.Metadata, new Uri("http://example.com/legacy/install")));
            Assert.True(matcher.IsMatch(result.Metadata, new Uri("https://example.com/legacy/install")));
            Assert.False(matcher.IsMatch(result.Metadata, new Uri("https://example.com/blocked/legacy/install")));
        }

        [Fact]
        public void AboutBlankIncludeUsesTheCanonicalOpaqueUrl()
        {
            var matcher = new UserScriptMatcher();
            var include = _parser.Parse(Script("// @name about include\n// @include about:blank")).Metadata;
            var legacy = _parser.Parse(Script("// @name legacy about include\n// @include about://blank")).Metadata;
            var excluded = _parser.Parse(Script(
                "// @name about exclude\n// @include about:blank\n// @exclude about:blank")).Metadata;
            var excludeMatch = _parser.Parse(Script(
                "// @name about exclude match\n// @include about:blank\n// @exclude-match about:blank")).Metadata;

            Assert.True(matcher.IsMatch(include, new Uri("about:blank#section")));
            Assert.False(matcher.IsMatch(include, new Uri("about:blank?query=1")));
            Assert.False(matcher.IsMatch(legacy, new Uri("about:blank")));
            Assert.False(matcher.IsMatch(excluded, new Uri("about:blank")));
            Assert.False(matcher.IsMatch(excludeMatch, new Uri("about:blank")));
        }

        internal static string Script(string metadataLines, string body = "")
        {
            return "// ==UserScript==\n" + metadataLines + "\n// ==/UserScript==\n" + body;
        }
    }
}
