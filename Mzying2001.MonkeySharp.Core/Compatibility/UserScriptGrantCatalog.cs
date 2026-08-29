using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core.Compatibility
{
    /// <summary>Provides the single mapping between declared grants and runtime capabilities.</summary>
    public static class UserScriptGrantCatalog
    {
        private static readonly IReadOnlyDictionary<string, string> Aliases =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GM_info"] = "GM.info",
                ["GM_log"] = "GM.log",
                ["GM_getValue"] = "GM.getValue",
                ["GM_setValue"] = "GM.setValue",
                ["GM_deleteValue"] = "GM.deleteValue",
                ["GM_listValues"] = "GM.listValues",
                ["GM_addValueChangeListener"] = "GM.addValueChangeListener",
                ["GM_removeValueChangeListener"] = "GM.removeValueChangeListener",
                ["GM_addStyle"] = "GM.addStyle",
                ["GM_addElement"] = "GM.addElement",
                ["GM_getResourceText"] = "GM.getResourceText",
                ["GM_getResourceURL"] = "GM.getResourceURL",
                ["GM_xmlhttpRequest"] = "GM.xmlHttpRequest",
                ["GM_registerMenuCommand"] = "GM.registerMenuCommand",
                ["GM_unregisterMenuCommand"] = "GM.unregisterMenuCommand",
                ["GM_notification"] = "GM.notification",
                ["GM_setClipboard"] = "GM.setClipboard",
                ["GM_openInTab"] = "GM.openInTab",
                ["GM_download"] = "GM.download",
                ["GM_getTab"] = "GM.getTab",
                ["GM_saveTab"] = "GM.saveTab",
                ["GM_getTabs"] = "GM.getTabs"
            });

        private static readonly IReadOnlyCollection<string> CanonicalGrants =
            new ReadOnlyCollection<string>(new[]
            {
                "none", "unsafeWindow", "GM.info", "GM.log", "GM.getValue", "GM.setValue",
                "GM.deleteValue", "GM.listValues", "GM.addValueChangeListener",
                "GM.removeValueChangeListener", "GM.addStyle", "GM.addElement", "GM.getResourceText",
                "GM.getResourceURL", "GM.xmlHttpRequest", "GM.registerMenuCommand",
                "GM.unregisterMenuCommand", "GM.notification", "GM.setClipboard", "GM.openInTab",
                "GM.download", "GM.getTab", "GM.saveTab", "GM.getTabs"
            });

        /// <summary>Gets all canonical grant names understood by MonkeySharp.</summary>
        public static IReadOnlyCollection<string> KnownCanonicalGrants => CanonicalGrants;

        /// <summary>Gets all legacy grant aliases understood by MonkeySharp.</summary>
        public static IReadOnlyCollection<string> LegacyAliases => Aliases.Keys.ToList().AsReadOnly();

        /// <summary>Attempts to normalize a declared grant to a canonical capability.</summary>
        /// <param name="declaredGrant">The exact metadata grant value.</param>
        /// <param name="acceptLegacyAliases">Whether legacy aliases are accepted.</param>
        /// <param name="canonicalGrant">Receives the canonical capability.</param>
        /// <returns><see langword="true"/> when the grant is known.</returns>
        public static bool TryNormalize(
            string declaredGrant,
            bool acceptLegacyAliases,
            out string canonicalGrant)
        {
            if (declaredGrant == null)
                throw new ArgumentNullException(nameof(declaredGrant));
            if (declaredGrant == "none" || declaredGrant == "unsafeWindow" ||
                CanonicalGrants.Contains(declaredGrant))
            {
                canonicalGrant = declaredGrant;
                return true;
            }
            if (acceptLegacyAliases && Aliases.TryGetValue(declaredGrant, out canonicalGrant))
                return true;
            canonicalGrant = null;
            return false;
        }

        /// <summary>Gets the legacy alias for a canonical capability, if one exists.</summary>
        /// <param name="canonicalGrant">The canonical capability name.</param>
        /// <returns>The exact legacy alias or <see langword="null"/>.</returns>
        public static string GetLegacyAlias(string canonicalGrant)
        {
            if (canonicalGrant == null)
                throw new ArgumentNullException(nameof(canonicalGrant));
            return Aliases.FirstOrDefault(item => item.Value == canonicalGrant).Key;
        }
    }
}
