using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Domain;
using System.Linq;
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

        [Fact]
        public void LegacyHeaderScanHonorsConfiguredBounds()
        {
            var result = new UserScriptMetadataParser(new UserScriptMetadataParserOptions
            {
                MaxHeaderScanLines = 1
            }).Parse(CompatibilityFixtures.LegacyHeaderPreamble);

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM000_HEADER_MISSING");
        }

        [Fact]
        public void TampermonkeyFixtureCatalogHasStableMetadataAndExpectations()
        {
            var parser = new UserScriptMetadataParser();

            Assert.Equal(10, CompatibilityFixtures.All.Count);
            foreach (var fixture in CompatibilityFixtures.All)
            {
                var result = parser.Parse(fixture.Source);
                Assert.NotNull(result.Metadata);
                Assert.True(result.CanEnable, fixture.Name + " metadata should be installable.");
                Assert.NotEmpty(fixture.SourceOrigin);
                Assert.NotEmpty(fixture.ExpectedSequence);
                Assert.NotNull(fixture.ExpectedApis);
                Assert.NotNull(fixture.ExpectedReturnTypes);
                Assert.NotNull(fixture.ExpectedCallbackShapes);
                Assert.NotNull(fixture.ExpectedErrorCodes);
            }

            Assert.Equal(GrantDeclarationState.Missing,
                parser.Parse(CompatibilityFixtures.MissingGrant).Metadata.GrantDeclarationState);
            Assert.Equal(GrantDeclarationState.ExplicitNone,
                parser.Parse(CompatibilityFixtures.ExplicitNone).Metadata.GrantDeclarationState);
            Assert.Equal(GrantDeclarationState.ExplicitList,
                parser.Parse(CompatibilityFixtures.LegacyStorage).Metadata.GrantDeclarationState);
            Assert.Contains("MSP006_NOT_SUPPORTED", CompatibilityFixtures.All
                .Single(item => item.Name == "unsupported-xhr-inputs").ExpectedErrorCodes);
        }
    }
}
