using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.CefSharp
{
    internal static class LifecycleScriptBuilder
    {
        private const string Placeholder = "__MONKEYSHARP_LIFECYCLE_BASE64__";
        private static readonly Lazy<string> Template = new Lazy<string>(LoadTemplate);

        public static string Build(string documentId, string frameId, string token)
        {
            var json = JsonSerializer.Serialize(new
            {
                protocol = 1,
                documentId,
                frameId,
                token
            });
            return Template.Value.Replace(Placeholder, Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
        }

        private static string LoadTemplate()
        {
            var assembly = typeof(LifecycleScriptBuilder).GetTypeInfo().Assembly;
            using (var stream = assembly.GetManifestResourceStream(
                "Mzying2001.MonkeySharp.CefSharp.Js.Lifecycle.js"))
            {
                if (stream == null)
                    throw new InvalidOperationException("The lifecycle JavaScript resource was not found.");
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
    }
}
