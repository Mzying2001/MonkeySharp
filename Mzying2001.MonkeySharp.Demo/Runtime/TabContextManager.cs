using CefSharp;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Storage;
using Mzying2001.MonkeySharp.Demo.Persistence;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    /// <summary>Owns live tab bindings and the private session; normal profile resources belong to DemoRuntime.</summary>
    internal sealed class TabContextManager : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly IRequestContext _normalRequestContext;
        private readonly IUserScriptValueStore _normalValues;
        private readonly SqliteTabStateService _normalTabStates;
        private readonly InteractiveUserScriptPermissionPolicy _normalPermissions;
        private readonly WpfPermissionPrompt _prompt;
        private readonly Dictionary<string, TabContext> _tabs = new Dictionary<string, TabContext>();
        private IRequestContext _privateRequestContext;
        private InMemoryUserScriptPermissionDecisionStore _privateDecisions;
        private InteractiveUserScriptPermissionPolicy _privatePermissions;
        private PrivateUserScriptValueStore _privateValues;
        private MemoryTabStateService _privateTabStates;

        public TabContextManager(Dispatcher dispatcher, IRequestContext requestContext, IUserScriptValueStore values,
            SqliteTabStateService tabStates, InteractiveUserScriptPermissionPolicy permissions, WpfPermissionPrompt prompt)
        {
            _dispatcher = dispatcher;
            _normalRequestContext = requestContext;
            _normalValues = values;
            _normalTabStates = tabStates;
            _normalPermissions = permissions;
            _prompt = prompt;
        }

        public bool HasPrivateContext => _privateRequestContext != null;

        public TabContext GetOrCreate(BrowserTabViewModel tab)
        {
            _dispatcher.VerifyAccess();
            if (_tabs.TryGetValue(tab.TabId, out var existing)) return existing;
            if (tab.IsIncognito && !HasPrivateContext) CreatePrivateContext();

            var context = tab.IsIncognito
                ? new TabContext(tab.TabId, _privateRequestContext, true, _privatePermissions, _privateTabStates.ForTab(tab.TabId), _privateValues)
                : new TabContext(tab.TabId, _normalRequestContext, false, _normalPermissions, _normalTabStates.ForTab(tab.TabId), _normalValues);
            _tabs.Add(tab.TabId, context);
            return context;
        }

        public void Release(BrowserTabViewModel tab)
        {
            _dispatcher.VerifyAccess();
            if (!_tabs.TryGetValue(tab.TabId, out var context)) return;
            _tabs.Remove(tab.TabId);
            if (!context.IsIncognito)
            {
                _normalTabStates.CloseTab(tab.TabId);
                return;
            }

            _privateTabStates.CloseTab(tab.TabId);
            // Called only after the tab's host, downloads and browser have drained.
            if (!_tabs.Values.Any(item => item.IsIncognito)) DisposePrivateContext();
        }

        public Task CancelPendingAndRevokePrivatePermissionsAsync(string scriptKey)
        {
            _dispatcher.VerifyAccess();
            _normalPermissions.CancelPending();
            _privatePermissions?.CancelPending();
            return _privateDecisions?.RevokeAsync(scriptKey, CancellationToken.None) ?? Task.CompletedTask;
        }

        private void CreatePrivateContext()
        {
            _privateRequestContext = CefSharpRequestContexts.CreatePrivate();
            _privateDecisions = new InMemoryUserScriptPermissionDecisionStore();
            _privatePermissions = new InteractiveUserScriptPermissionPolicy(
                new SensitiveUserScriptPermissionsPolicy(), _privateDecisions, _prompt.ForPrivateContext());
            _privateValues = new PrivateUserScriptValueStore(_normalValues);
            _privateTabStates = new MemoryTabStateService();
        }

        private void DisposePrivateContext()
        {
            _privatePermissions?.Dispose();
            _privatePermissions = null;
            _privateDecisions?.RevokeAsync(null, CancellationToken.None).GetAwaiter().GetResult();
            _privateDecisions = null;
            _privateValues?.Dispose();
            _privateValues = null;
            _privateTabStates?.Clear();
            _privateTabStates = null;
            _privateRequestContext?.Dispose();
            _privateRequestContext = null;
        }

        public void Dispose()
        {
            _dispatcher.VerifyAccess();
            DisposePrivateContext();
            _tabs.Clear();
        }
    }
}
