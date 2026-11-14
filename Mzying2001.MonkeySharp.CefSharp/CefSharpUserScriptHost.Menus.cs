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
        private void BrowserJavascriptMessageReceived(object sender, JavascriptMessageReceivedEventArgs args)
        {
            if (args.Message is string proof && args.Frame != null && !args.Frame.IsDisposed)
                _isolated?.ConfirmNativeMenuFrame(proof, GetFrameIdentifier(args.Frame), args.Frame.IsMain);
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
            if (_isolated != null) entries.AddRange(_isolated.MenuAsync(GetFrameIdentifier(cefFrame), cefFrame.IsMain).GetAwaiter().GetResult());
            return entries;
        }

        internal void ExecuteContextMenu(IFrame cefFrame, CefSharpMenuTarget target)
        {
            if (GetFrameIdentifier(cefFrame) != target.NativeFrameId) return;
            if (target.Frame.ExecutionWorld == "isolated")
            {
                var executor = _isolated;
                if (executor != null) Observe(executor.ExecuteMenuAsync(target.Frame.DocumentId, target.NativeFrameId, target.Entry.ScriptKey), "MSC104_CONTEXT_MENU_FAILED", null);
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

            public void OnBeforeContextMenu(IWebBrowser browser, IBrowser cefBrowser, IFrame frame,
                IContextMenuParams parameters, IMenuModel model)
            {
                _inner?.OnBeforeContextMenu(browser, cefBrowser, frame, parameters, model);
                lock (_sync) _commands.Clear();
                var entries = _host.GetContextMenuEntries(frame);
                if (entries.Count == 0)
                    return;
                model.AddSeparator();
                for (var index = 0; index < entries.Count; index++)
                {
                    var command = (CefMenuCommand)((int)CefMenuCommand.UserFirst + index);
                    lock (_sync) _commands[command] = entries[index];
                    model.AddItem(command, entries[index].Entry.Title);
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
