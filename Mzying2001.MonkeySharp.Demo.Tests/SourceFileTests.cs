using Mzying2001.MonkeySharp.Demo.Services;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class SourceFileTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SourceFilesEnforceTheExistingByteLimit(bool oversized)
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var path = Path.Combine(fixture.Paths.BaseDirectory, "source.user.js");
                using (var output = File.Create(path))
                    output.SetLength(UserScriptSourceFile.MaximumBytes + (oversized ? 1 : 0));

                if (oversized) Assert.Throws<IOException>(() => UserScriptSourceFile.ReadBytes(path));
                else Assert.Equal(UserScriptSourceFile.MaximumBytes, UserScriptSourceFile.ReadBytes(path).Length);
            }
        }

        [Fact]
        public async Task SourceTextPreservesUnicodeAndRejectsInvalidUtf8()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var path = Path.Combine(fixture.Paths.BaseDirectory, "source.user.js");
                File.WriteAllText(path, "\uFEFF// 你好 👋", new UTF8Encoding(false));
                Assert.Equal("// 你好 👋", UserScriptSourceFile.ReadText(path));

                File.WriteAllBytes(path, new byte[] { 0xC3, 0x28 });
                Assert.Throws<DecoderFallbackException>(() => UserScriptSourceFile.ReadText(path));
            }
        }
    }
}
