using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    public sealed partial class CefSharpUserScriptHost
    {
        private void UpdateNativeMenuNavigation(object sender, IBrowser nativeBrowser, IFrame frame, bool loading)
        {
            CefSharpIsolatedExecutor isolated;
            lock (_sync) isolated = ReferenceEquals(sender, _browser) && nativeBrowser?.IsPopup == false ? _isolated : null;
            if (isolated != null && frame != null && !frame.IsDisposed)
                isolated.NativeMenuNavigation(GetFrameIdentifier(frame), frame.IsMain, loading);
        }

        /// <summary>Gets the installation key for a command this host added for <c>@run-at context-menu</c>.</summary>
        /// <param name="commandId">The command ID from the current context menu model.</param>
        /// <param name="scriptKey">Receives the installation key on success or the default value on failure.</param>
        /// <returns><see langword="true"/> if the attached host has a mapping for the command, otherwise <see langword="false"/>.</returns>
        /// <remarks>Query after the adapter's <c>OnBeforeContextMenu</c> has completed and use the result for that menu only. Command IDs can be reused on the next build.</remarks>
        public bool TryGetContextMenuScriptKey(CefMenuCommand commandId, out ScriptKey scriptKey)
        {
            lock (_sync)
            {
                if (!_disposed && _browser != null && _contextMenuHandler != null)
                    return _contextMenuHandler.TryGetScriptKey(commandId, out scriptKey);
            }

            scriptKey = default;
            return false;
        }

        /// <summary>Resolves a menu subscriber against a native menu event frame, rejecting stale documents and unknown mappings.</summary>
        /// <param name="nativeFrame">The native CEF event frame; never construct it from page-supplied IDs.</param>
        /// <param name="request">The authenticated registration received by the menu service.</param>
        /// <returns>The current subscribing frame to place in MenuCommandInvocation.Frame, or null if it cannot be attested.</returns>
        public DocumentFrame ResolveMenuFrame(IFrame nativeFrame, MenuCommandRequest request)
        {
            var frame = request?.OriginFrame;
            CefSharpIsolatedExecutor isolated;
            string id;
            bool main;
            lock (_sync)
            {
                if (_browser == null || nativeFrame == null || nativeFrame.IsDisposed || frame == null ||
                    frame.BrowserSessionId != _browserSessionId || nativeFrame.Browser.Identifier != _browser.GetBrowser().Identifier) return null;
                id = GetFrameIdentifier(nativeFrame);
                if (frame.ExecutionWorld != "isolated") return _sessions.TryGetValue(id, out var session) && session.Initialized &&
                    !session.Token.IsCancellationRequested && CefSharpDocumentIdentity.Matches(session.Frame, frame) ? session.Frame : null;
                if (!nativeFrame.IsValid) return null;
                isolated = _isolated;
                main = nativeFrame.IsMain;
            }
            return isolated?.OwnsNativeMenuFrame(frame, id, main) == true ? frame : null;
        }

        internal IReadOnlyList<CefSharpMenuTarget> GetContextMenuEntries(IFrame cefFrame)
        {
            FrameSession session;
            lock (_sync)
                _sessions.TryGetValue(GetFrameIdentifier(cefFrame), out session);
            var entries = new List<CefSharpMenuTarget>();
            if (session != null && session.Initialized)
                entries.AddRange(_engine.GetContextMenuEntriesAsync(session.Frame, CancellationToken.None).GetAwaiter().GetResult()
                    .Select(entry => new CefSharpMenuTarget(entry, session.Frame, GetFrameIdentifier(cefFrame))));
            var isolated = _isolated;
            if (isolated != null && !cefFrame.IsDisposed && cefFrame.IsValid)
                entries.AddRange(isolated.MenuAsync(GetFrameIdentifier(cefFrame), cefFrame.IsMain).GetAwaiter().GetResult());
            return entries;
        }

        internal void ExecuteContextMenu(IFrame cefFrame, CefSharpMenuTarget target)
        {
            if (GetFrameIdentifier(cefFrame) != target.NativeFrameId) return;
            if (target.Frame.ExecutionWorld == "isolated")
            {
                var executor = _isolated;
                if (executor != null && !cefFrame.IsDisposed && cefFrame.IsValid)
                    Observe(executor.ExecuteMenuAsync(target, cefFrame.IsMain), "MSC104_CONTEXT_MENU_FAILED", null);
                return;
            }
            FrameSession session;
            lock (_sync)
                _sessions.TryGetValue(GetFrameIdentifier(cefFrame), out session);
            if (session == null || session.Frame.DocumentId != target.Frame.DocumentId)
                return;
            Observe(ExecuteContextMenuAsync(session, target.Entry.ScriptKey), "MSC104_CONTEXT_MENU_FAILED", session);
        }

        private async Task ExecuteContextMenuAsync(FrameSession session, ScriptKey scriptKey)
        {
            session.Token.ThrowIfCancellationRequested();
            await session.OperationGate.WaitAsync(session.Token).ConfigureAwait(false);
            try
            {
                var plan = await _engine.ProcessContextMenuAsync(
                    session.Frame, scriptKey, session.Token).ConfigureAwait(false);
                if (plan.Invocations.Count != 0)
                    await ExecuteAsync(session.Frame, BridgeScriptBuilder.BuildInjection(plan), session.Token)
                        .ConfigureAwait(false);
            }
            finally
            {
                session.OperationGate.Release();
            }
        }

        private sealed class ContextMenuHandler : IContextMenuHandler
        {
            private readonly CefSharpUserScriptHost _host;
            private readonly IContextMenuHandler _inner;
            private readonly object _sync = new object();
            private readonly Dictionary<CefMenuCommand, CefSharpMenuTarget> _commands =
                new Dictionary<CefMenuCommand, CefSharpMenuTarget>();

            public ContextMenuHandler(CefSharpUserScriptHost host, IContextMenuHandler inner)
            {
                _host = host;
                _inner = inner;
            }

            public IContextMenuHandler Previous => _inner;

            public bool TryGetScriptKey(CefMenuCommand commandId, out ScriptKey scriptKey)
            {
                lock (_sync)
                {
                    if (_commands.TryGetValue(commandId, out var target))
                    {
                        scriptKey = target.Entry.ScriptKey;
                        return true;
                    }
                }

                scriptKey = default;
                return false;
            }

            public void OnBeforeContextMenu(IWebBrowser browser, IBrowser cefBrowser, IFrame frame,
                IContextMenuParams parameters, IMenuModel model)
            {
                lock (_sync) _commands.Clear();
                _inner?.OnBeforeContextMenu(browser, cefBrowser, frame, parameters, model);
                var entries = _host.GetContextMenuEntries(frame);
                if (entries.Count == 0)
                    return;
                model.AddSeparator();
                for (var index = 0; index < entries.Count; index++)
                {
                    var command = (CefMenuCommand)((int)CefMenuCommand.UserFirst + index);
                    var target = entries[index];
                    if (model.AddItem(command, target.Entry.Title))
                        lock (_sync) _commands[command] = target;
                }
            }

            public bool OnContextMenuCommand(IWebBrowser browser, IBrowser cefBrowser, IFrame frame,
                IContextMenuParams parameters, CefMenuCommand commandId, CefEventFlags eventFlags)
            {
                if (_inner != null && _inner.OnContextMenuCommand(browser, cefBrowser, frame, parameters, commandId, eventFlags))
                    return true;
                CefSharpMenuTarget target;
                lock (_sync)
                {
                    if (!_commands.TryGetValue(commandId, out target))
                        return false;
                }
                _host.ExecuteContextMenu(frame, target);
                return true;
            }

            public void OnContextMenuDismissed(IWebBrowser browser, IBrowser cefBrowser, IFrame frame)
            {
                lock (_sync) _commands.Clear();
                _inner?.OnContextMenuDismissed(browser, cefBrowser, frame);
            }

            public bool RunContextMenu(IWebBrowser browser, IBrowser cefBrowser, IFrame frame,
                IContextMenuParams parameters, IMenuModel model, IRunContextMenuCallback callback)
            {
                return _inner != null && _inner.RunContextMenu(browser, cefBrowser, frame, parameters, model, callback);
            }
        }

    }
}
