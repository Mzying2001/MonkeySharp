using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Storage;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    /// <summary>The actual browser, privacy and permission context selected for one stable tab.</summary>
    public sealed class TabContext
    {
        internal TabContext(string tabId, IRequestContext requestContext, bool isIncognito,
            IUserScriptPermissionPolicy permissions, ITabStateService tabState, IUserScriptValueStore values)
        {
            TabId = tabId;
            RequestContext = requestContext;
            IsIncognito = isIncognito;
            Permissions = permissions;
            TabState = tabState;
            Values = values;
        }

        public string TabId { get; }
        public IRequestContext RequestContext { get; }
        public bool IsIncognito { get; }
        internal IUserScriptPermissionPolicy Permissions { get; }
        internal ITabStateService TabState { get; }
        internal IUserScriptValueStore Values { get; }
    }
}
