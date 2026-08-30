using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Matching;
using Mzying2001.MonkeySharp.Core.Parsing;
using System;
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
        public void MissingGrantMeansGrantNone()
        {
            var result = _parser.Parse(Script("// @name none\n// @match https://example.com/*"));

            Assert.Equal(new[] { "none" }, result.Metadata.Grants);
        }

        [Fact]
        public void LegacyGrantAliasesNormalizeWhilePreservingDeclarations()
        {
            var result = new UserScriptMetadataParser().Parse(
                Script("// @name legacy\n// @match https://example.com/*\n// @grant GM_getValue"));

            Assert.True(result.CanEnable);
            Assert.Equal(new[] { "GM_getValue" }, result.Metadata.DeclaredGrants);
            Assert.Equal(new[] { "GM.getValue" }, result.Metadata.Grants);
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

        [Fact]
        public void StrictParserCanRejectLegacyAliases()
        {
            var result = new UserScriptMetadataParser(new UserScriptMetadataParserOptions
            {
                Profile = UserScriptCompatibilityProfile.ModernStrict,
                AcceptLegacyGrantAliases = false
            }).Parse(Script("// @name strict\n// @match https://example.com/*\n// @grant GM_getValue"));

            Assert.Equal(new[] { "none" }, result.Metadata.Grants);
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
        public void InvalidMatchCannotBeEnabled()
        {
            var result = _parser.Parse(Script("// @name invalid\n// @match https://foo.*.example/*"));

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM020_INVALID_MATCH");
        }

        [Fact]
        public void MatchPatternRequiresAnActualSubdomain()
        {
            var pattern = MatchPatternCompiler.Compile("*://*.example.com/*");

            Assert.True(pattern.IsMatch(new Uri("https://www.example.com/page")));
            Assert.False(pattern.IsMatch(new Uri("https://example.com/page")));
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
            var pattern = MatchPatternCompiler.Compile("https://\u4f8b\u5b50.\u6d4b\u8bd5/path?*");

            Assert.True(pattern.IsMatch(new Uri("https://xn--fsqu00a.xn--0zwm56d/path?q=1#different")));
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

        internal static string Script(string metadataLines, string body = "")
        {
            return "// ==UserScript==\n" + metadataLines + "\n// ==/UserScript==\n" + body;
        }
    }
}
