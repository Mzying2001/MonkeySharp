using System.IO;
using System.Text;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    internal static class UserScriptSourceFile
    {
        internal const int MaximumBytes = 10 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static string ReadText(string path) => Decode(ReadBytes(path));

        internal static string Decode(byte[] bytes)
        {
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return Utf8.GetString(bytes, offset, bytes.Length - offset);
        }

        internal static byte[] Encode(string source)
        {
            var bytes = Utf8.GetBytes(source);
            if (source.Length == 0 || source[0] != '\uFEFF') return bytes;
            var output = new byte[bytes.Length + 3];
            output[0] = 0xef; output[1] = 0xbb; output[2] = 0xbf;
            System.Buffer.BlockCopy(bytes, 0, output, 3, bytes.Length);
            return output;
        }

        internal static byte[] ReadBytes(string path)
        {
            using (var input = File.OpenRead(path))
            {
                if (input.Length > MaximumBytes) throw new IOException("Script exceeds the 10 MiB limit.");
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (output.Length + count > MaximumBytes) throw new IOException("Content exceeds the 10 MiB limit.");
                        output.Write(buffer, 0, count);
                    }
                    return output.ToArray();
                }
            }
        }
    }
}
