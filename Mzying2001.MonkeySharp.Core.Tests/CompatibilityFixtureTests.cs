using Mzying2001.MonkeySharp.Core.Parsing;
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
            Assert.False(preamble.CanEnable);
            Assert.Contains(preamble.Diagnostics, item => item.Code == "MSM000_HEADER_MISSING");
        }

        [Fact]
        public void StrictBaselineRejectsHeaderPreamble()
        {
            var result = new UserScriptMetadataParser().Parse(CompatibilityFixtures.LegacyHeaderPreamble);

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM000_HEADER_MISSING");
        }
    }
}
