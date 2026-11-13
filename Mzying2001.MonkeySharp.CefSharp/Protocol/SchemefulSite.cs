using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;

namespace Mzying2001.MonkeySharp.CefSharp
{
    // Reuse the latest bundled snapshot not newer than the engine, including private suffixes.
    internal static class SchemefulSite
    {
        private const string ResourcePrefix = "Mzying2001.MonkeySharp.CefSharp.Protocol.psl-";

        private static readonly SortedDictionary<int, Lazy<Rules>> Snapshots = new SortedDictionary<int, Lazy<Rules>>(
            typeof(SchemefulSite).Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".dat.gz", StringComparison.Ordinal))
                .Select(name => int.Parse(name.Substring(ResourcePrefix.Length, name.Length - ResourcePrefix.Length - ".dat.gz".Length), CultureInfo.InvariantCulture))
                .ToDictionary(major => major, major => new Lazy<Rules>(() => Load(major))));

        private static int SnapshotMajor(int major) => Snapshots.Keys.TakeWhile(version => version <= major).DefaultIfEmpty(Snapshots.Keys.First()).Last();

        internal static string FromUrl(Uri url, int major)
        {
            if (url == null || !url.IsAbsoluteUri || url.Scheme != "http" && url.Scheme != "https") throw new ArgumentException("A schemeful site requires an HTTP(S) URL.");
            var originalHost = url.IdnHost.Trim('[', ']').ToLowerInvariant();
            var trailingDot = originalHost.EndsWith(".", StringComparison.Ordinal);
            var host = originalHost.TrimEnd('.');
            if (IPAddress.TryParse(host, out var address)) host = address.ToString();
            else
            {
                var rules = Snapshots[SnapshotMajor(major)].Value;
                var labels = host.Split('.');
                var publicLength = 1;
                for (var index = 0; index < labels.Length; index++)
                {
                    var suffix = string.Join(".", labels.Skip(index));
                    if (rules.Exceptions.Contains(suffix)) { publicLength = labels.Length - index - 1; break; }
                    if (rules.Exact.Contains(suffix)) publicLength = Math.Max(publicLength, labels.Length - index);
                    if (index > 0 && rules.Wildcards.Contains(suffix)) publicLength = Math.Max(publicLength, labels.Length - index + 1);
                }
                host = string.Join(".", labels.Skip(Math.Max(0, labels.Length - publicLength - 1)));
                if (trailingDot) host += ".";
            }
            return new UriBuilder(url.Scheme, host) { Port = -1 }.Uri.GetLeftPart(UriPartial.Authority);
        }
        private static Rules Load(int major)
        {
            var result = new Rules();
            using (var input = typeof(SchemefulSite).Assembly.GetManifestResourceStream("Mzying2001.MonkeySharp.CefSharp.Protocol.psl-" + major + ".dat.gz"))
            using (var gzip = new GZipStream(input ?? throw new InvalidOperationException("The suffix snapshot is missing."), CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
                    if (line.Length == 0) continue;
                    var target = result.Exact;
                    if (line[0] == '!') { target = result.Exceptions; line = line.Substring(1); }
                    else if (line.StartsWith("*.", StringComparison.Ordinal)) { target = result.Wildcards; line = line.Substring(2); }
                    line = line.Trim('.');
                    if (line != "*") target.Add(new IdnMapping().GetAscii(line).ToLowerInvariant());
                }
            }
            return result;
        }
        private sealed class Rules
        {
            public readonly HashSet<string> Exact = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Wildcards = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Exceptions = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
