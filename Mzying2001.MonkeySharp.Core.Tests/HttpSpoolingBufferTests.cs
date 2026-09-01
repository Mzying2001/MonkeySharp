using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class HttpSpoolingBufferTests
    {
        [Fact]
        public void SpillsAfterThresholdReadsInBridgeChunksAndDeletesTemporaryFile()
        {
            var bytes = Enumerable.Range(0, HttpSpoolingBuffer.MemoryThreshold + 17)
                .Select(index => (byte)(index % 251)).ToArray();
            string path;
            using (var buffer = new HttpSpoolingBuffer())
            {
                buffer.Append(bytes, 0, HttpSpoolingBuffer.MemoryThreshold);
                Assert.False(buffer.IsFileBacked);
                buffer.Append(bytes, HttpSpoolingBuffer.MemoryThreshold, 17);
                Assert.True(buffer.IsFileBacked);
                path = buffer.TemporaryPath;
                Assert.True(File.Exists(path));
                Assert.Equal(bytes.Length, buffer.Length);

                var first = buffer.Read(0, HttpSpoolingBuffer.BridgeChunkSize);
                var last = buffer.Read(HttpSpoolingBuffer.MemoryThreshold, HttpSpoolingBuffer.BridgeChunkSize);
                Assert.Equal(HttpSpoolingBuffer.BridgeChunkSize, first.Length);
                Assert.Equal(bytes.Take(first.Length), first);
                Assert.Equal(bytes.Skip(HttpSpoolingBuffer.MemoryThreshold), last);
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    buffer.Read(0, HttpSpoolingBuffer.BridgeChunkSize + 1));

                using (var stream = buffer.OpenRead())
                {
                    Assert.Equal(bytes.Length, stream.Length);
                    Assert.Equal(bytes[bytes.Length - 1], ReadLastByte(stream));
                }
            }
            Assert.False(File.Exists(path));
        }

        private static int ReadLastByte(Stream stream)
        {
            stream.Position = stream.Length - 1;
            return stream.ReadByte();
        }
    }
}
