using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class CompatibilityFixtureTests
    {
        [Fact]
        public void LegacyFixturesHaveClosedMetadataBlocks()
        {
            var parser = new UserScriptMetadataParser();

            foreach (var source in new[]
            {
                CompatibilityFixtures.LegacyResourceAndCallbacks,
                CompatibilityFixtures.MissingDependencyProvider
            })
            {
                var result = parser.Parse(source);
                Assert.NotNull(result.Metadata);
                Assert.DoesNotContain(result.Diagnostics, item => item.Code == "MSM001_HEADER_UNCLOSED");
            }

            var preamble = parser.Parse(CompatibilityFixtures.LegacyStorage);
            Assert.True(preamble.CanEnable);
            Assert.NotNull(preamble.Metadata);
        }

        [Fact]
        public void StrictBaselineRejectsHeaderPreamble()
        {
            var result = new UserScriptMetadataParser(new UserScriptMetadataParserOptions
            {
                Profile = UserScriptCompatibilityProfile.ModernStrict
            }).Parse(CompatibilityFixtures.LegacyHeaderPreamble);

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM000_HEADER_MISSING");
        }
    }
}
