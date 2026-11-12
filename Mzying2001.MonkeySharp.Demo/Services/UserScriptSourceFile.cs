using System.IO;
using System.Text;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    internal static class UserScriptSourceFile
    {
        internal const int MaximumBytes = 10 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static string ReadText(string path) => Utf8.GetString(ReadBytes(path)).TrimStart('\uFEFF');

        internal static byte[] ReadBytes(string path)
        {
            using (var input = File.OpenRead(path))
            {
                if (input.Length > MaximumBytes) throw new IOException("Script exceeds the 10 MiB limit.");
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return output.ToArray();
                }
            }
        }
    }
}
