using System.IO;
using System.Reflection;
using System.Text;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    internal static class LegacyUserscriptFixture
    {
        public static string Read()
        {
            using (var stream = typeof(LegacyUserscriptFixture).GetTypeInfo().Assembly
                .GetManifestResourceStream("Mzying2001.MonkeySharp.Core.Tests.legacy-userscript.user.js"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
