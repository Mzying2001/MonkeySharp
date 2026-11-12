using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.Core.Compatibility
{
    /// <summary>Maps declared names to operations and reference-tested modern families.</summary>
    public static class UserScriptGrantCatalog
    {
        private static readonly IReadOnlyList<UserScriptApiDescriptor> Descriptors = Load();
        private static readonly IReadOnlyDictionary<string, string> Aliases =
            new ReadOnlyDictionary<string, string>(Descriptors.Where(item => item.LegacyName != null)
                .ToDictionary(item => item.LegacyName, item => item.Capability, StringComparer.Ordinal));
        private static readonly IReadOnlyCollection<string> CanonicalGrants =
            new ReadOnlyCollection<string>(new[] { "none", "unsafeWindow" }.Concat(Descriptors.Select(item => item.Capability)).ToArray());

        /// <summary>Gets the shared API description table embedded in the renderer bootstrap.</summary>
        public static IReadOnlyList<UserScriptApiDescriptor> Apis => Descriptors;
        /// <summary>Gets recognized canonical grants.</summary>
        public static IReadOnlyCollection<string> KnownCanonicalGrants => CanonicalGrants;
        /// <summary>Gets recognized official legacy names.</summary>
        public static IReadOnlyCollection<string> LegacyAliases => Aliases.Keys.ToList().AsReadOnly();

        /// <summary>Normalizes a declaration without changing its preserved spelling.</summary>
        public static bool TryNormalize(string declaredGrant, out string canonicalGrant)
        {
            if (declaredGrant == null) throw new ArgumentNullException(nameof(declaredGrant));
            if (CanonicalGrants.Contains(declaredGrant)) { canonicalGrant = declaredGrant; return true; }
            if (Aliases.TryGetValue(declaredGrant, out canonicalGrant)) return true;
            canonicalGrant = null;
            return false;
        }

        /// <summary>Gets the official legacy spelling for an operation, if one exists.</summary>
        public static string GetLegacyAlias(string canonicalGrant)
        {
            if (canonicalGrant == null) throw new ArgumentNullException(nameof(canonicalGrant));
            return Descriptors.FirstOrDefault(item => item.Capability == canonicalGrant)?.LegacyName;
        }

        /// <summary>Expands the modern families observed in the pinned reference runtime.</summary>
        public static IReadOnlyList<string> GetCapabilities(IEnumerable<string> grants)
        {
            if (grants == null) throw new ArgumentNullException(nameof(grants));
            var result = grants.Distinct(StringComparer.Ordinal).ToList();
            foreach (var family in Descriptors.GroupBy(item => item.Family))
                if (family.Any(item => result.Contains(item.Capability)))
                    foreach (var descriptor in family)
                        if (!result.Contains(descriptor.Capability)) result.Add(descriptor.Capability);
            return new ReadOnlyCollection<string>(result);
        }

        private static IReadOnlyList<UserScriptApiDescriptor> Load()
        {
            using (var stream = typeof(UserScriptGrantCatalog).Assembly.GetManifestResourceStream("Mzying2001.MonkeySharp.Core.Js.ApiCatalog.json"))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("API catalog resource is missing.")))
            using (var json = JsonDocument.Parse(reader.ReadToEnd()))
                return new ReadOnlyCollection<UserScriptApiDescriptor>(json.RootElement.EnumerateArray().Select(item =>
                    new UserScriptApiDescriptor(item.GetProperty("capability").GetString(), item.GetProperty("legacy").GetString(),
                        item.GetProperty("modernReturn").GetString(), item.GetProperty("legacyReturn").GetString(), item.GetProperty("family").GetString())).ToArray());
        }
    }
}
