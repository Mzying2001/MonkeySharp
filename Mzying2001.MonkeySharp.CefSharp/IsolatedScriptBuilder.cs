using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.CefSharp
{
    internal static class IsolatedScriptBuilder
    {
        private static readonly Lazy<string> Template = new Lazy<string>(() =>
        {
            using (var stream = typeof(IsolatedScriptBuilder).GetTypeInfo().Assembly.GetManifestResourceStream("Mzying2001.MonkeySharp.CefSharp.Js.Isolated.js"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd().Replace("/*__MONKEYSHARP_LIFECYCLE_EVENTS__*/", LifecycleScriptBuilder.EventsSource);
        });
        internal static string Build(string generation, string binding, string documentId, int maxPending) =>
            Template.Value.Replace("__MONKEYSHARP_ISOLATED_BASE64__", Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { generation, binding, documentId, maxPending }))));
    }
}
