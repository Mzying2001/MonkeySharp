using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Text;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class ResourceContentTests
    {
        [Fact]
        public void StrictAndReplacementDecodingAreIndependent()
        {
            var content = new ResourceContent(new byte[] { 0xff });
            Assert.Contains("\ufffd", content.DecodeText());
            Assert.Throws<DecoderFallbackException>(() => content.DecodeText(strict: true));
            Assert.Contains("\ufffd", content.DecodeText());
        }

        [Fact]
        public void DecodingRemovesExactlyOneBomAndPreservesNewlines()
        {
            var content = new ResourceContent(Encoding.UTF8.GetBytes("\ufeff\ufeffhello\r\n"));
            Assert.Equal("\ufeffhello\r\n", content.DecodeText(strict: true));
            Assert.Equal("\ufeffhello\r\n", content.DecodeText());
        }

        [Fact]
        public void CharsetAndDefensiveBytesArePreserved()
        {
            var bytes = new byte[] { 0xe9 };
            var content = new ResourceContent(bytes, "text/plain", "windows-1252");
            bytes[0] = 0;
            content.Bytes[0] = 0;
            Assert.Equal("é", content.DecodeText(strict: true));
            Assert.Equal(0xe9, content.Bytes[0]);
            Assert.Throws<ArgumentException>(() => new ResourceContent(bytes, charset: "unknown-monkeysharp").DecodeText());
        }
    }
}
