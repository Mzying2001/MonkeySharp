using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    internal abstract class CefSharpPageDocument
    {
        internal abstract Uri Url { get; }
        internal abstract CancellationToken Revoked { get; }
        internal abstract Task ExecuteAsync(string source, string sourceUrl, CancellationToken token);
        internal virtual Task ReleaseAsync() => Task.CompletedTask;
    }

    internal sealed class CefSharpPageContexts
    {
        private readonly CefSharpCdpSession _session;
        private readonly object _sync = new object();
        private readonly Dictionary<string, Context> _contexts = new Dictionary<string, Context>(StringComparer.Ordinal);
        private readonly Dictionary<string, CefSharpPageContexts> _children = new Dictionary<string, CefSharpPageContexts>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _discovery = new SemaphoreSlim(1, 1);

        internal CefSharpPageContexts(CefSharpCdpSession session) { _session = session; }

        internal async Task InitializeAsync(CancellationToken token)
        {
            _session.EventReceived += OnEvent;
            _session.Closed += OnClosed;
            try
            {
                await _session.ExecuteAsync("Page.enable", cancellationToken: token).ConfigureAwait(false);
                await _session.ExecuteAsync("Runtime.enable", cancellationToken: token).ConfigureAwait(false);
            }
            catch
            {
                _session.EventReceived -= OnEvent;
                _session.Closed -= OnClosed;
                throw;
            }
        }

        internal async Task<CefSharpPageDocument> BindAsync(string marker, string owner, bool main, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    while (true)
                    {
                        var binding = await FindAsync(marker, owner, main, deadline.Token).ConfigureAwait(false);
                        if (binding != null) return binding;
                        if (!main)
                        {
                            binding = await FindInChildTargetsAsync(marker, owner, deadline.Token).ConfigureAwait(false);
                            if (binding != null) return binding;
                        }
                        await Task.Delay(40, deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
                catch (ObjectDisposedException) when (_session.IsClosed) { return null; }
            }
        }

        private async Task<CefSharpPageDocument> FindInChildTargetsAsync(string marker, string owner, CancellationToken token)
        {
            await _discovery.WaitAsync(token).ConfigureAwait(false);
            try
            {
                CefSharpPageContexts[] known;
                lock (_sync) known = _children.Values.ToArray();
                foreach (var target in known)
                {
                    var document = await target.FindAsync(marker, owner, null, token).ConfigureAwait(false);
                    if (document != null) return document;
                }

                var targets = await _session.ExecuteAsync("Target.getTargets", cancellationToken: token).ConfigureAwait(false);
                var untracked = targets.GetProperty("targetInfos").EnumerateArray()
                    .Where(target => target.GetProperty("type").GetString() == "iframe")
                    .Select(target => target.GetProperty("targetId").GetString()).ToArray();
                lock (_sync) untracked = untracked.Where(id => !_children.ContainsKey(id)).ToArray();
                if (untracked.Length == 0) return null;

                var parents = new HashSet<string>(await ReadFrameIdsAsync(token).ConfigureAwait(false), StringComparer.Ordinal);
                foreach (var target in known)
                    if (!target._session.IsClosed) parents.UnionWith(await target.ReadFrameIdsAsync(token).ConfigureAwait(false));
                foreach (var id in untracked)
                {
                    var target = await AttachChildAsync(id, parents, token).ConfigureAwait(false);
                    if (target == null) continue;
                    var document = await target.FindAsync(marker, owner, null, token).ConfigureAwait(false);
                    if (document != null) return document;
                }
                return null;
            }
            finally { _discovery.Release(); }
        }

        private async Task<CefSharpPageContexts> AttachChildAsync(string id, HashSet<string> parents, CancellationToken token)
        {
            CefSharpCdpSession child = null;
            try
            {
                child = await _session.AttachTargetAsync(id, token).ConfigureAwait(false);
                var tree = await child.ExecuteAsync("Page.getFrameTree", cancellationToken: token).ConfigureAwait(false);
                var frame = tree.GetProperty("frameTree").GetProperty("frame");
                if (!frame.TryGetProperty("parentId", out var parent) || !parents.Contains(parent.GetString())) return null;
                var target = new CefSharpPageContexts(child);
                await target.InitializeAsync(token).ConfigureAwait(false);
                lock (_sync) _children.Add(id, target);
                child.Closed += (_, __) => { lock (_sync) _children.Remove(id); };
                if (child.IsClosed) lock (_sync) _children.Remove(id);
                child = null;
                return target;
            }
            catch (ObjectDisposedException) { return null; }
            catch (InvalidOperationException error) when (error.Message.StartsWith("CDP rejected", StringComparison.Ordinal)) { return null; }
            finally
            {
                if (child != null) await DetachChildAsync(child).ConfigureAwait(false);
            }
        }

        private async Task<IEnumerable<string>> ReadFrameIdsAsync(CancellationToken token) =>
            (await ReadLoadersAsync(null, token).ConfigureAwait(false)).Keys;

        internal async Task DetachChildrenAsync()
        {
            await _discovery.WaitAsync().ConfigureAwait(false);
            try
            {
                CefSharpPageContexts[] children;
                lock (_sync) children = _children.Values.ToArray();
                await Task.WhenAll(children.Select(child => DetachChildAsync(child._session))).ConfigureAwait(false);
            }
            finally { _discovery.Release(); }
        }

        private static async Task DetachChildAsync(CefSharpCdpSession child)
        {
            try { await child.DetachAsync().ConfigureAwait(false); }
            catch (InvalidOperationException) { }
            catch (TimeoutException) { }
        }

        private async Task<CefSharpPageDocument> FindAsync(string marker, string owner, bool? main, CancellationToken token)
        {
            Context[] contexts;
            lock (_sync) contexts = _contexts.Values.ToArray();
            if (contexts.Length == 0) return null;

            var before = await ReadLoadersAsync(main, token).ConfigureAwait(false);
            foreach (var context in contexts)
            {
                if (!before.TryGetValue(context.FrameId, out var loader)) continue;
                lock (_sync)
                    if (!IsCurrent(context) || context.Document?.Revoked.IsCancellationRequested == false) continue;

                var generation = Guid.NewGuid().ToString("N");
                var expression = "document[" + JsonSerializer.Serialize(marker) + "]===" + JsonSerializer.Serialize(owner) +
                    "?{document:document,url:location.href,generation:" + JsonSerializer.Serialize(generation) + "}:null";
                Document candidate = null;
                var published = false;
                try
                {
                    var evaluated = await _session.ExecuteAsync("Runtime.evaluate",
                        new { expression, contextId = context.Id, objectGroup = generation }, token).ConfigureAwait(false);
                    if (evaluated.TryGetProperty("exceptionDetails", out _) ||
                        !evaluated.TryGetProperty("result", out var result) || !result.TryGetProperty("objectId", out var id)) continue;
                    candidate = new Document(_session, id.GetString(), generation, loader);
                    var href = await candidate.CallAsync("return this.url;", "monkeysharp://document-identity", token).ConfigureAwait(false);
                    if (href.ValueKind != JsonValueKind.String || !Uri.TryCreate(href.GetString(), UriKind.Absolute, out var url)) continue;

                    var after = await ReadLoadersAsync(main, token).ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (!IsCurrent(context) || context.Document?.Revoked.IsCancellationRequested == false ||
                            !after.TryGetValue(context.FrameId, out var currentLoader) || loader != currentLoader ||
                            (context.Loader != null && context.Loader != loader)) continue;
                        candidate.Location = url;
                        context.Document = candidate;
                        published = true;
                        return candidate;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                catch (ObjectDisposedException) when (_session.IsClosed) { return null; }
                catch (InvalidOperationException error) when (ContextUnavailable(error)) { }
                finally
                {
                    if (candidate != null && !published) await candidate.ReleaseAsync().ConfigureAwait(false);
                }
            }
            return null;
        }

        private async Task<Dictionary<string, string>> ReadLoadersAsync(bool? main, CancellationToken token)
        {
            var loaders = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var result = await _session.ExecuteAsync("Page.getFrameTree", cancellationToken: token).ConfigureAwait(false);
                AddLoaders(result.GetProperty("frameTree"), main, true, loaders);
            }
            catch (ObjectDisposedException) when (_session.IsClosed)
            {
                // An iframe can migrate to another target while discovery is in flight.
            }
            return loaders;
        }

        private static void AddLoaders(JsonElement tree, bool? main, bool root, Dictionary<string, string> loaders)
        {
            var frame = tree.GetProperty("frame");
            if (main == null || main == root) loaders[frame.GetProperty("id").GetString()] = frame.GetProperty("loaderId").GetString();
            if (main != true && tree.TryGetProperty("childFrames", out var children))
                foreach (var child in children.EnumerateArray()) AddLoaders(child, false, false, loaders);
        }

        private bool IsCurrent(Context context) =>
            _contexts.TryGetValue(context.FrameId, out var current) && ReferenceEquals(current, context);

        private void OnEvent(object sender, CdpEventArgs args)
        {
            Document[] retired;
            Document[] check = null;
            var parameters = args.Parameters;
            lock (_sync)
            {
                switch (args.Method)
                {
                    case "Runtime.executionContextCreated":
                        var value = parameters.GetProperty("context");
                        if (!value.TryGetProperty("auxData", out var aux) ||
                            !aux.TryGetProperty("isDefault", out var isDefault) || isDefault.ValueKind != JsonValueKind.True ||
                            !aux.TryGetProperty("frameId", out var frameId)) return;
                        var context = new Context(frameId.GetString(), value.GetProperty("id").GetInt32(),
                            value.TryGetProperty("uniqueId", out var unique) ? unique.GetString() : null);
                        _contexts.TryGetValue(context.FrameId, out var previous);
                        if (previous != null && previous.Id == context.Id && previous.UniqueId == context.UniqueId) return;
                        _contexts[context.FrameId] = context;
                        retired = Documents(previous == null ? Array.Empty<Context>() : new[] { previous });
                        break;
                    case "Page.frameNavigated":
                        var frame = parameters.GetProperty("frame");
                        if (!_contexts.TryGetValue(frame.GetProperty("id").GetString(), out var navigated)) return;
                        navigated.Loader = frame.GetProperty("loaderId").GetString();
                        if (navigated.Document == null || navigated.Document.Loader == navigated.Loader) return;
                        _contexts.Remove(navigated.FrameId);
                        // Chromium 87 can announce the same default context ID before
                        // commit. Keep it as a probe candidate for the new loader.
                        if (navigated.UniqueId == null)
                            _contexts[navigated.FrameId] = new Context(navigated.FrameId, navigated.Id, null) { Loader = navigated.Loader };
                        retired = Documents(new[] { navigated });
                        break;
                    case "Runtime.executionContextDestroyed":
                        var id = parameters.TryGetProperty("executionContextId", out var numericId) ? numericId.GetInt32() : 0;
                        var uniqueId = parameters.TryGetProperty("executionContextUniqueId", out var destroyed) ? destroyed.GetString() : null;
                        if (uniqueId == null)
                        {
                            // A late numeric-only event can refer to the predecessor.
                            // Ask the bound object to prove it still belongs to its document.
                            check = Documents(_contexts.Values.Where(item => item.Id == id).ToArray());
                            retired = Array.Empty<Document>();
                        }
                        else retired = RemoveContexts(item => item.UniqueId == uniqueId);
                        break;
                    case "Runtime.executionContextsCleared":
                        retired = RemoveContexts(_ => true);
                        break;
                    case "Page.frameDetached":
                        var detached = parameters.GetProperty("frameId").GetString();
                        retired = RemoveContexts(item => item.FrameId == detached);
                        break;
                    default:
                        return;
                }
            }
            foreach (var document in retired) document.Revoke();
            if (check != null)
                foreach (var document in check) _ = document.CheckLivenessAsync();
        }

        private Document[] RemoveContexts(Func<Context, bool> predicate)
        {
            var removed = _contexts.Values.Where(predicate).ToArray();
            foreach (var context in removed) _contexts.Remove(context.FrameId);
            return Documents(removed);
        }

        private static Document[] Documents(Context[] contexts) =>
            contexts.Select(context => context.Document).Where(document => document != null).ToArray();

        private void OnClosed(object sender, EventArgs args)
        {
            Document[] retired;
            lock (_sync) retired = RemoveContexts(_ => true);
            foreach (var document in retired) document.Revoke();
        }

        private static bool ContextUnavailable(InvalidOperationException error) =>
            error.Message.StartsWith("CDP rejected", StringComparison.Ordinal) &&
            (error.Message.IndexOf("Cannot find context", StringComparison.OrdinalIgnoreCase) >= 0 ||
             error.Message.IndexOf("Cannot find object", StringComparison.OrdinalIgnoreCase) >= 0 ||
             error.Message.IndexOf("Could not find object", StringComparison.OrdinalIgnoreCase) >= 0 ||
             error.Message.IndexOf("Inspected target navigated", StringComparison.OrdinalIgnoreCase) >= 0);

        private sealed class Context
        {
            internal Context(string frameId, int id, string uniqueId) { FrameId = frameId; Id = id; UniqueId = uniqueId; }
            internal string FrameId { get; }
            internal int Id { get; }
            internal string UniqueId { get; }
            internal string Loader { get; set; }
            internal Document Document { get; set; }
        }

        private sealed class Document : CefSharpPageDocument
        {
            private readonly CefSharpCdpSession _session;
            private readonly string _objectId;
            private readonly string _generation;
            private readonly CancellationTokenSource _revoked = new CancellationTokenSource();
            internal Uri Location { get; set; }
            internal override Uri Url => Location;
            internal override CancellationToken Revoked => _revoked.Token;
            internal string Loader { get; }

            internal Document(CefSharpCdpSession session, string objectId, string generation, string loader)
            { _session = session; _objectId = objectId; _generation = generation; Loader = loader; }

            internal void Revoke() => _revoked.Cancel();

            internal async Task CheckLivenessAsync()
            {
                try { await CallAsync("return true;", "monkeysharp://document-identity", Revoked).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                catch (InvalidOperationException) { }
            }

            internal override async Task ReleaseAsync()
            {
                // A unique object group releases only this capture, even when a
                // replacement renderer has allocated the same remote object ID.
                try
                {
                    await _session.ExecuteAsync("Runtime.releaseObjectGroup", new { objectGroup = _generation },
                        timeout: TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                }
                catch (InvalidOperationException) { }
                catch (TimeoutException) { }
            }

            internal override async Task ExecuteAsync(string source, string sourceUrl, CancellationToken token)
            {
                await CallAsync("(function(){\n" + source + "\n}).call(globalThis);\nreturn true;", sourceUrl, token).ConfigureAwait(false);
            }

            internal async Task<JsonElement> CallAsync(string body, string sourceUrl, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Revoked.ThrowIfCancellationRequested();
                // Legacy Chromium can reuse both execution-context and remote-object IDs
                // after a cross-process navigation. Validate the object before running source.
                var function = "function(expected){if(this.generation!==expected||this.document!==document)return false;\n" +
                    body + "\n}\n//# sourceURL=" + sourceUrl + "\n";
                try
                {
                    var response = await _session.ExecuteAsync("Runtime.callFunctionOn", new
                    {
                        objectId = _objectId, functionDeclaration = function, returnByValue = true,
                        arguments = new[] { new { value = _generation } }
                    }, token).ConfigureAwait(false);
                    if (response.TryGetProperty("exceptionDetails", out var error))
                        throw new InvalidOperationException("Page-world JavaScript failed: " + error.GetRawText());
                    if (response.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value) &&
                        value.ValueKind != JsonValueKind.False) return value;
                }
                catch (InvalidOperationException error) when (ContextUnavailable(error)) { }
                catch (ObjectDisposedException) { }
                Revoke();
                throw new OperationCanceledException("The target CefSharp page context is no longer active.");
            }
        }
    }
}
