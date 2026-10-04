using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Security;
using System;
using System.Text;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class ResourceIntegrityTests
    {
        private const string HelloMd5 = "5d41402abc4b2a76b9719d911017c592";
        private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
        private const string HelloSha256Base64 = "LPJNul+wow4m6DsqxbninhsWHlwfp0JecwQzYpOLmCQ=";

        [Fact]
        public void ParserRemovesSRIFragmentAndRetainsHashDeclarations()
        {
            var result = Parse(
                "// @require https://cdn.example/lib.js#md5=" + HelloMd5 + ",sha256=" + HelloSha256Base64 + "\n" +
                "// @resource logo https://cdn.example/logo.png#sha256-" + HelloSha256Base64);

            Assert.True(result.CanEnable);
            var dependency = Assert.Single(result.Metadata.Requires);
            Assert.Equal("https://cdn.example/lib.js", dependency.Url);
            Assert.Equal(2, dependency.Integrity.Count);
            var resource = Assert.Single(result.Metadata.Resources);
            Assert.Equal("https://cdn.example/logo.png", resource.Url);
            Assert.Equal(HelloSha256Base64, Assert.Single(resource.Integrity).Digest);
        }

        [Fact]
        public void VerifierSupportsMd5Sha256HexAndBase64AndSelectsLastSupportedHash()
        {
            var bytes = Encoding.UTF8.GetBytes("hello");
            var md5 = Parse("// @require https://cdn.example/lib.js#md5=" + HelloMd5)
                .Metadata.Requires[0].Integrity;
            var sha256 = Parse("// @require https://cdn.example/lib.js#sha256=" + HelloSha256)
                .Metadata.Requires[0].Integrity;
            var base64 = Parse("// @require https://cdn.example/lib.js#sha256=" + HelloSha256Base64)
                .Metadata.Requires[0].Integrity;
            var unpaddedBase64 = Parse("// @require https://cdn.example/lib.js#sha256=" + HelloSha256Base64.TrimEnd('='))
                .Metadata.Requires[0].Integrity;
            var multiple = Parse("// @require https://cdn.example/lib.js#md5=" + HelloMd5 + ";sha256=" + HelloSha256Base64)
                .Metadata.Requires[0].Integrity;

            ResourceIntegrityVerifier.Verify(bytes, md5);
            ResourceIntegrityVerifier.Verify(bytes, sha256);
            ResourceIntegrityVerifier.Verify(bytes, base64);
            ResourceIntegrityVerifier.Verify(bytes, unpaddedBase64);
            ResourceIntegrityVerifier.Verify(bytes, multiple);
        }

        [Fact]
        public void UnsupportedAlgorithmWarnsButSupportedDeclarationCanVerify()
        {
            var result = Parse("// @require https://cdn.example/lib.js#sha384=unsupported,sha256=" + HelloSha256);

            Assert.True(result.CanEnable);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == "MSM071_UNSUPPORTED_SRI" && diagnostic.Severity == DiagnosticSeverity.Warning);
            ResourceIntegrityVerifier.Verify(Encoding.UTF8.GetBytes("hello"), result.Metadata.Requires[0].Integrity);
        }

        [Theory]
        [InlineData("sha1=unsupported")]
        [InlineData("sha256=not-a-digest")]
        [InlineData("sha256")]
        [InlineData("")]
        public void InvalidOrUnsupportedOnlyIntegrityCannotBeInstalled(string fragment)
        {
            var result = Parse("// @require https://cdn.example/lib.js#" + fragment);

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                (diagnostic.Code == "MSM070_INVALID_SRI" || diagnostic.Code == "MSM072_NO_SUPPORTED_SRI"));
        }

        [Fact]
        public void VerifierRejectsContentThatDoesNotMatchDeclaredDigest()
        {
            var integrity = Parse("// @resource logo https://cdn.example/logo#sha256=" + HelloSha256)
                .Metadata.Resources[0].Integrity;

            var exception = Assert.Throws<ResourceIntegrityException>(() =>
                ResourceIntegrityVerifier.Verify(Encoding.UTF8.GetBytes("tampered"), integrity, "https://cdn.example/logo"));
            Assert.Equal("MSR410_RESOURCE_INTEGRITY_FAILED", exception.Code);
        }

        private static MetadataParseResult Parse(string directives)
        {
            return new UserScriptMetadataParser().Parse(MetadataAndMatchingTests.Script(
                "// @name SRI test\n// @match https://example.com/*\n" + directives));
        }

    }
}
