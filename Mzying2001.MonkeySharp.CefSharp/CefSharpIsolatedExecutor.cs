using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    // CDP frame/context identities authenticate isolated execution. CEF frame IDs
    // enter only through the one-shot native menu proof; they are never cast to CDP IDs.
    internal sealed class CefSharpIsolatedExecutor
    {
        private readonly CefSharpCdpSession _session;
        private readonly IUserScriptRepository _repository;
        private readonly UserScriptEngine _engine;
        private readonly UserScriptBridgeGateway _gateway;
        private readonly string _browserId;
        private readonly Func<bool?> _privacy;
        private readonly Action<UserScriptDiagnostic> _diagnostic;
        private readonly Action<DocumentLifecycleEventArgs> _lifecycle;
        private readonly BridgeOptions _options;
        private readonly CancellationTokenSource _lifetime;
        private readonly object _sync = new object();
        private readonly Dictionary<string, NativeFrame> _frames = new Dictionary<string, NativeFrame>();
        private readonly Dictionary<string, World> _requested = new Dictionary<string, World>();
        private readonly Dictionary<int, World> _contexts = new Dictionary<int, World>();
        private readonly Dictionary<string, World> _documents = new Dictionary<string, World>();
        private readonly Dictionary<string, World> _menuProofs = new Dictionary<string, World>();
        private readonly HashSet<Task> _tasks = new HashSet<Task>();
        private readonly Dictionary<string, CefSharpIsolatedExecutor> _children = new Dictionary<string, CefSharpIsolatedExecutor>();
        private readonly HashSet<string> _attachingTargets = new HashSet<string>();
        private readonly HashSet<string> _ignoredTargets = new HashSet<string>();
        private bool _discovering, _discoverAgain;
        private readonly string _binding = "__MonkeySharpIsolated_" + Guid.NewGuid().ToString("N");
        private volatile bool _disposed;
        private volatile bool _stopping;
        private int _repositoryEpoch;

        internal CefSharpIsolatedExecutor(CefSharpCdpSession session, IUserScriptRepository repository, UserScriptEngine engine,
            UserScriptBridgeGateway gateway, string browserId, Func<bool?> privacy, BridgeOptions options,
            Action<UserScriptDiagnostic> diagnostic, Action<DocumentLifecycleEventArgs> lifecycle, CancellationToken attachment)
        {
            _session = session; _repository = repository; _engine = engine; _gateway = gateway;
            _browserId = browserId; _privacy = privacy; _options = options ?? new BridgeOptions(); _diagnostic = diagnostic;
            _lifecycle = lifecycle;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(attachment);
        }

        internal async Task InitializeAsync()
        {
            _session.EventReceived += EventReceived;
            _session.Closed += SessionClosed;
            _repository.Changed += RepositoryChanged;
            await _session.ExecuteAsync("Page.enable", cancellationToken: _lifetime.Token).ConfigureAwait(false);
            await _session.ExecuteAsync("Runtime.enable", cancellationToken: _lifetime.Token).ConfigureAwait(false);
            var tree = await _session.ExecuteAsync("Page.getFrameTree", cancellationToken: _lifetime.Token).ConfigureAwait(false);
            ReadTree(tree.GetProperty("frameTree"));
            await RefreshAsync().ConfigureAwait(false);
            if (!_session.IsChild) await _session.ExecuteAsync("Target.setDiscoverTargets", new { discover = true }, _lifetime.Token).ConfigureAwait(false);
            await DiscoverChildrenAsync().ConfigureAwait(false);
        }

        private void SessionClosed(object sender, EventArgs args) => Track(DisposeAsync());

        private async Task DiscoverChildrenAsync()
        {
            lock (_sync)
            {
                if (_disposed || _stopping) return;
                if (_discovering) { _discoverAgain = true; return; }
                _discovering = true;
            }
            try
            {
                bool again;
                do
                {
                    lock (_sync) _discoverAgain = false;
                    var targets = await _session.ExecuteRootAsync("Target.getTargets", token: _lifetime.Token).ConfigureAwait(false);
                    foreach (var target in targets.GetProperty("targetInfos").EnumerateArray())
                    {
                        if (target.GetProperty("type").GetString() != "iframe") continue;
                        var id = target.GetProperty("targetId").GetString();
                        lock (_sync)
                        {
                            if (_disposed || _stopping) return;
                            if (_frames.ContainsKey(id) || _children.ContainsKey(id) || _ignoredTargets.Contains(id) || !_attachingTargets.Add(id)) continue;
                        }
                        CefSharpCdpSession childSession = null;
                        CefSharpIsolatedExecutor startedChild = null;
                        try
                        {
                            childSession = await _session.AttachTargetAsync(id, _lifetime.Token).ConfigureAwait(false);
                            var tree = await childSession.ExecuteAsync("Page.getFrameTree", cancellationToken: _lifetime.Token).ConfigureAwait(false);
                            var native = tree.GetProperty("frameTree").GetProperty("frame");
                            var owned = false;
                            lock (_sync) owned = native.TryGetProperty("parentId", out var parent) && _frames.ContainsKey(parent.GetString()) && !_stopping && !_disposed;
                            if (!owned) { lock (_sync) _ignoredTargets.Add(id); continue; } // Validate native ancestry before enabling bindings or running any source.
                            var child = new CefSharpIsolatedExecutor(childSession, _repository, _engine, _gateway, _browserId, _privacy, _options, _diagnostic, _lifecycle, _lifetime.Token);
                            startedChild = child;
                            lock (_sync) _children.Add(id, child);
                            var ownedSession = childSession;
                            ownedSession.Closed += (_, __) => { lock (_sync) _children.Remove(id); };
                            childSession = null;
                            await child.InitializeAsync().ConfigureAwait(false);
                            startedChild = null;
                        }
                        catch (OperationCanceledException) { }
                        catch (ObjectDisposedException) { } // Target detach can win the attachment/initialization race.
                        catch (Exception error)
                        {
                            var stillPresent = false;
                            try
                            {
                                var current = await _session.ExecuteRootAsync("Target.getTargets", token: _lifetime.Token).ConfigureAwait(false);
                                stillPresent = current.GetProperty("targetInfos").EnumerateArray().Any(item => item.GetProperty("targetId").GetString() == id);
                            }
                            catch (OperationCanceledException) { }
                            catch (ObjectDisposedException) { }
                            if (stillPresent && !_disposed && !_stopping) _diagnostic(new UserScriptDiagnostic("MSC236_OOPIF_ROUTE_UNAVAILABLE", DiagnosticSeverity.Warning,
                                "A child target could not be prepared; no page-world fallback was used.", error));
                        }
                        finally
                        {
                            lock (_sync) _attachingTargets.Remove(id);
                            if (startedChild != null)
                            {
                                lock (_sync) _children.Remove(id);
                                await startedChild.DisposeAsync().ConfigureAwait(false);
                            }
                            if (childSession != null) try { await childSession.DetachAsync().ConfigureAwait(false); } catch (Exception) { }
                        }
                    }
                    CefSharpIsolatedExecutor[] children;
                    lock (_sync) children = _children.Values.ToArray();
                    foreach (var child in children) await child.DiscoverChildrenAsync().ConfigureAwait(false);
                    lock (_sync) again = _discoverAgain && !_stopping && !_disposed;
                } while (again);
            }
            finally { lock (_sync) _discovering = false; }
        }

        private void ReadTree(JsonElement tree)
        {
            var frame = tree.GetProperty("frame");
            lock (_sync) if (!_frames.ContainsKey(frame.GetProperty("id").GetString())) FrameNavigated(frame);
            if (tree.TryGetProperty("childFrames", out var children)) foreach (var child in children.EnumerateArray()) ReadTree(child);
        }

        private void EventReceived(object sender, CdpEventArgs args)
        {
            if (_disposed || _stopping) return;
            var data = args.Parameters;
            switch (args.Method)
            {
                case "Page.frameAttached":
                    lock (_sync) _ignoredTargets.Clear();
                    Track(RefreshTreeAsync());
                    Track(DiscoverChildrenAsync());
                    break;
                case "Target.targetCreated":
                case "Target.targetInfoChanged":
                    if (data.GetProperty("targetInfo").GetProperty("type").GetString() == "iframe") Track(DiscoverChildrenAsync());
                    break;
                case "Target.targetDestroyed":
                    lock (_sync) _ignoredTargets.Remove(data.GetProperty("targetId").GetString());
                    break;
                case "Page.frameNavigated":
                    FrameNavigated(data.GetProperty("frame"));
                    Track(RefreshAsync());
                    break;
                case "Page.frameDetached":
                    RemoveFrame(data.GetProperty("frameId").GetString());
                    break;
                case "Page.navigatedWithinDocument":
                    NativeFrame changed;
                    lock (_sync) _frames.TryGetValue(data.GetProperty("frameId").GetString(), out changed);
                    if (changed != null && Uri.TryCreate(data.GetProperty("url").GetString(), UriKind.Absolute, out var url))
                    {
                        var old = changed.Url;
                        changed.Url = url;
                        World[] worlds;
                        lock (_sync) worlds = changed.Worlds.Values.ToArray();
                        foreach (var world in worlds) Track(UrlChangedAsync(world, old));
                    }
                    break;
                case "Runtime.executionContextCreated":
                    var context = data.GetProperty("context");
                    var name = context.GetProperty("name").GetString();
                    World requested;
                    lock (_sync)
                    {
                        if (!context.TryGetProperty("auxData", out var auxiliary) || !auxiliary.TryGetProperty("frameId", out var nativeFrame) ||
                            !_requested.TryGetValue(name + "\n" + nativeFrame.GetString(), out requested) || !Alive(requested)) return;
                        var id = context.GetProperty("id").GetInt32();
                        if (_contexts.TryGetValue(id, out var previous) && !ReferenceEquals(previous, requested)) Revoke(previous);
                        requested.ContextId = id;
                        _contexts[id] = requested;
                        requested.Created.TrySetResult(id);
                    }
                    break;
                case "Runtime.executionContextDestroyed":
                    lock (_sync) if (_contexts.TryGetValue(data.GetProperty("executionContextId").GetInt32(), out var ended)) Revoke(ended);
                    break;
                case "Runtime.executionContextsCleared":
                    lock (_sync) foreach (var world in _contexts.Values.ToArray()) Revoke(world);
                    break;
                case "Runtime.bindingCalled":
                    if (data.GetProperty("name").GetString() != _binding) return;
                    World owner;
                    lock (_sync) _contexts.TryGetValue(data.GetProperty("executionContextId").GetInt32(), out owner);
                    if (owner != null && Alive(owner)) Track(ReceiveAsync(owner, data.GetProperty("payload").GetString()));
                    break;
            }
        }

        private void FrameNavigated(JsonElement frame)
        {
            var id = frame.GetProperty("id").GetString();
            var loader = frame.TryGetProperty("loaderId", out var value) ? value.GetString() : string.Empty;
            if (!Uri.TryCreate(frame.GetProperty("url").GetString(), UriKind.Absolute, out var url)) return;
            lock (_sync)
            {
                RemoveFrame(id);
                _frames[id] = new NativeFrame(id, loader, url, !frame.TryGetProperty("parentId", out _), _lifetime.Token);
                _ignoredTargets.Clear();
            }
        }

        private async Task RefreshTreeAsync()
        {
            var tree = await _session.ExecuteAsync("Page.getFrameTree", cancellationToken: _lifetime.Token).ConfigureAwait(false);
            ReadTree(tree.GetProperty("frameTree"));
            await RefreshAsync().ConfigureAwait(false);
        }

        private void RemoveFrame(string id)
        {
            lock (_sync)
            {
                if (!_frames.TryGetValue(id, out var frame)) return;
                frame.Cancellation.Cancel();
                foreach (var world in frame.Worlds.Values.ToArray()) Revoke(world);
                frame.Cancellation.Dispose();
                _frames.Remove(id);
            }
        }

        private void RepositoryChanged(object sender, UserScriptRepositoryChangedEventArgs args)
        { lock (_sync) _repositoryEpoch++; Track(RefreshAsync()); }

        private async Task RefreshAsync()
        {
            int epoch;
            lock (_sync) epoch = _repositoryEpoch;
            var scripts = await _repository.GetSnapshotAsync(_lifetime.Token).ConfigureAwait(false);
            var eligible = scripts.Where(script => script.IsEnabled && UserScriptEngine.RequestedWorld(script.Definition.Metadata) == "isolated").ToArray();
            NativeFrame[] frames;
            lock (_sync)
            {
                if (epoch != _repositoryEpoch || _disposed || _stopping) return;
                foreach (var world in _documents.Values.ToArray())
                    if (!eligible.Any(script => script.ScriptKey == world.Script.ScriptKey && script.RevisionId == world.Script.RevisionId)) Track(EndWorldAsync(world));
                frames = _frames.Values.ToArray();
            }
            foreach (var frame in frames)
                foreach (var script in eligible)
                {
                    if (!frame.Main && script.Definition.Metadata.NoFrames) continue;
                    bool matches;
                    try { matches = script.PreparedRevision.UrlRules.IsMatch(frame.Url, script.Definition.Metadata.RunAt == UserScriptRunAt.ContextMenu); }
                    catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { matches = false; }
                    if (!matches) continue;
                    if (!script.PreparedRevision.UrlRules.AllowsEnvironment(_privacy(), out var reason))
                    { Report("MSR211_RUN_IN_UNAVAILABLE", reason, script, frame); continue; }
                    World world;
                    lock (_sync)
                    {
                        if (_disposed || _stopping || epoch != _repositoryEpoch || frame.Cancellation.IsCancellationRequested || frame.Worlds.ContainsKey(script.RevisionId)) continue;
                        world = new World(frame, script, _binding + "_" + script.ScriptKey + "_" + script.RevisionId.ToString("N"), _browserId, _session.Scope);
                        frame.Worlds.Add(script.RevisionId, world);
                        _requested[world.Name + "\n" + frame.Id] = world;
                        _documents.Add(world.Frame.DocumentId, world);
                    }
                    Track(StartAsync(world));
                }
        }

        private async Task StartAsync(World world)
        {
            try
            {
                var created = await _session.ExecuteAsync("Page.createIsolatedWorld", new { frameId = world.Native.Id, worldName = world.Name }, world.Token).ConfigureAwait(false);
                var id = created.GetProperty("executionContextId").GetInt32();
                var deadline = Task.Delay(TimeSpan.FromSeconds(5), world.Token);
                if (await Task.WhenAny(world.Created.Task, deadline).ConfigureAwait(false) != world.Created.Task || await world.Created.Task.ConfigureAwait(false) != id)
                {
                    world.Token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("The isolated context did not receive its native frame identity.");
                }
                await ConfirmDocumentAsync(world).ConfigureAwait(false);
                var global = await _session.ExecuteAsync("Runtime.evaluate", new { expression = "globalThis", contextId = id }, world.Token).ConfigureAwait(false);
                world.ObjectId = global.GetProperty("result").GetProperty("objectId").GetString();
                await ConfirmDocumentAsync(world).ConfigureAwait(false);
                await CallAsync(world, IsolatedScriptBuilder.Build(world.Generation, _binding, world.Frame.DocumentId,
                    _options.MaxPendingRequestsPerDocument, _options.RequestTimeout), false).ConfigureAwait(false);
                await _session.ExecuteAsync("Runtime.addBinding", new { name = _binding, executionContextId = id }, world.Token).ConfigureAwait(false);
                // The one-shot nonce is sent before user source, solely through this private context.
                // JavascriptMessageReceived supplies the CEF frame; the payload supplies no frame identity.
                if (typeof(global::CefSharp.Cef).Assembly.GetName().Version.Major == 84)
                {
                    lock (_sync) _menuProofs[world.MenuProof] = world;
                    await CallAsync(world, "if(typeof CefSharp==='object'&&typeof CefSharp.PostMessage==='function')CefSharp.PostMessage(" +
                        JsonSerializer.Serialize(world.MenuProof) + ");").ConfigureAwait(false);
                }
                await LifecycleAsync(world, DocumentLifecycleKind.ContextCreated).ConfigureAwait(false);
                await LifecycleAsync(world, DocumentLifecycleKind.DocumentStart).ConfigureAwait(false);
                await CallAsync(world, "globalThis.__MonkeySharpIsolatedTransport.ready();").ConfigureAwait(false);
                world.Ready = true;
            }
            catch (OperationCanceledException) { Revoke(world); }
            catch (Exception error)
            {
                if (Alive(world) && !_session.IsClosed) Report("MSC231_ISOLATED_EXECUTION_UNAVAILABLE", "The isolated context could not be prepared; no page-world fallback was used. " + error.Message, world.Script, world.Native, error);
                Revoke(world);
            }
        }

        private async Task ConfirmDocumentAsync(World world)
        {
            lock (_sync)
                if (!Alive(world) || !_contexts.TryGetValue(world.ContextId, out var current) || !ReferenceEquals(current, world)) throw new OperationCanceledException();
            var tree = await _session.ExecuteAsync("Page.getFrameTree", cancellationToken: world.Token).ConfigureAwait(false);
            if (!FindFrame(tree.GetProperty("frameTree"), world.Native.Id, world.Native.Loader)) throw new OperationCanceledException("The native document changed during isolated preparation.");
        }

        private static bool FindFrame(JsonElement tree, string id, string loader)
        {
            var frame = tree.GetProperty("frame");
            if (frame.GetProperty("id").GetString() == id)
                return (frame.TryGetProperty("loaderId", out var value) ? value.GetString() : string.Empty) == loader;
            return tree.TryGetProperty("childFrames", out var children) && children.EnumerateArray().Any(child => FindFrame(child, id, loader));
        }

        private async Task ReceiveAsync(World world, string payload)
        {
            if (payload == null || payload.Length > _options.MaxRequestBytes * 2L) return;
            using (var document = JsonDocument.Parse(payload))
            {
                var message = document.RootElement;
                if (message.GetProperty("kind").GetString() == "stage")
                {
                    var stage = message.GetProperty("stage").GetString();
                    if (stage == "bfcache")
                    {
                        Report("MSC232_BFCACHE_EXECUTION_REVOKED", "Restored isolated documents require a fresh navigation; the cached execution was revoked.", world.Script, world.Native);
                        await CallAsync(world, "globalThis.__MonkeySharpIsolatedTransport.dispose();").ConfigureAwait(false);
                        Revoke(world);
                        return;
                    }
                    var kind = stage == "body" ? DocumentLifecycleKind.BodyAvailable : stage == "end" ? DocumentLifecycleKind.DomContentLoaded : stage == "idle" ? DocumentLifecycleKind.DocumentIdle : DocumentLifecycleKind.Load;
                    if (stage != "body" && stage != "end" && stage != "idle" && stage != "load") return;
                    await LifecycleAsync(world, kind).ConfigureAwait(false);
                    return;
                }
                var request = message.GetProperty("request").GetString();
                string response;
                using (var envelope = JsonDocument.Parse(request))
                {
                    var root = envelope.RootElement;
                    if (!root.TryGetProperty("documentId", out var documentId) || documentId.ValueKind != JsonValueKind.String || documentId.GetString() != world.Frame.DocumentId ||
                        !root.TryGetProperty("scriptKey", out var scriptKey) || scriptKey.ValueKind != JsonValueKind.String || scriptKey.GetString() != world.Script.ScriptKey.ToString())
                        response = JsonSerializer.Serialize(new { protocol = InjectionPlan.CurrentProtocolVersion,
                            type = root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "hello" ? "hello-result" : "response",
                            requestId = root.TryGetProperty("requestId", out var requestId) && requestId.ValueKind == JsonValueKind.String ? requestId.GetString() : string.Empty,
                            ok = false, error = new { code = BridgeErrorCodes.SessionExpired, message = "The proof does not belong to this native context." } });
                    else
                    {
                        world.UpdateUrl();
                        await _engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.UrlChanged, world.Frame), world.Token).ConfigureAwait(false);
                        response = await _gateway.DispatchAsync(request, _lifetime.Token).ConfigureAwait(false);
                    }
                }
                await CallAsync(world, "globalThis.__MonkeySharpIsolatedTransport.complete(" +
                    JsonSerializer.Serialize(message.GetProperty("id").GetString()) + "," + JsonSerializer.Serialize(response) + ");").ConfigureAwait(false);
            }
        }

        private async Task LifecycleAsync(World world, DocumentLifecycleKind kind)
        {
            await world.Gate.WaitAsync(world.Token).ConfigureAwait(false);
            try
            {
                if (!Alive(world)) return;
                world.UpdateUrl();
                _lifecycle(new DocumentLifecycleEventArgs(kind, world.Frame));
                var plan = await _engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(kind, world.Frame), world.Token).ConfigureAwait(false);
                if (plan.Invocations.Count != 0) await CallAsync(world, BridgeScriptBuilder.BuildInjection(plan)).ConfigureAwait(false);
            }
            finally { world.Gate.Release(); }
        }

        private async Task UrlChangedAsync(World world, Uri old)
        {
            await LifecycleAsync(world, DocumentLifecycleKind.UrlChanged).ConfigureAwait(false);
            await CallAsync(world, "if(globalThis.__MonkeySharpRuntime)globalThis.__MonkeySharpRuntime.urlChanged(" +
                JsonSerializer.Serialize(new { url = world.Native.Url.AbsoluteUri, oldURL = old.AbsoluteUri }) + ");").ConfigureAwait(false);
        }

        internal bool Owns(string documentId)
        {
            CefSharpIsolatedExecutor[] children;
            lock (_sync) { if (_documents.ContainsKey(documentId)) return true; children = _children.Values.ToArray(); }
            return children.Any(child => child.Owns(documentId));
        }

        internal bool ConfirmNativeMenuFrame(string proof, string nativeFrameId, bool main)
        {
            lock (_sync)
            {
                if (!_menuProofs.TryGetValue(proof, out var world) || !Alive(world)) return false;
                if (world.Native.Main != main) { Report("MSC235_MENU_FRAME_MISMATCH", "Native menu frame kind disagreed with the CDP frame.", world.Script, world.Native); return false; }
                _menuProofs.Remove(proof);
                if (world.Native.CefFrameId != null && world.Native.CefFrameId != nativeFrameId) return false;
                world.Native.CefFrameId = nativeFrameId;
                return true;
            }
        }

        internal bool OwnsNativeMenuFrame(DocumentFrame frame, string nativeFrameId, bool main)
        {
            lock (_sync) return _documents.TryGetValue(frame.DocumentId, out var world) && ReferenceEquals(world.Frame, frame) &&
                (main && world.Native.Main || !main && world.Native.CefFrameId == nativeFrameId) && world.Ready && Alive(world);
        }

        internal async Task<IReadOnlyList<CefSharpMenuTarget>> MenuAsync(string nativeFrameId, bool main)
        {
            World[] worlds;
            lock (_sync) worlds = _documents.Values.Where(world => (main && world.Native.Main || !main && world.Native.CefFrameId == nativeFrameId) && world.Ready && Alive(world)).ToArray();
            var entries = new List<CefSharpMenuTarget>();
            foreach (var world in worlds)
                entries.AddRange((await _engine.GetContextMenuEntriesAsync(world.Frame, world.Token).ConfigureAwait(false))
                    .Select(entry => new CefSharpMenuTarget(entry, world.Frame, nativeFrameId)));
            return entries;
        }

        internal async Task<bool> ExecuteMenuAsync(string documentId, string nativeFrameId, ScriptKey key)
        {
            World world;
            lock (_sync) _documents.TryGetValue(documentId, out world);
            if (world == null || !world.Native.Main && world.Native.CefFrameId != nativeFrameId || world.Script.ScriptKey != key || !world.Ready || !Alive(world)) return false;
            var plan = await _engine.ProcessContextMenuAsync(world.Frame, key, world.Token).ConfigureAwait(false);
            if (plan.Invocations.Count != 0) await CallAsync(world, BridgeScriptBuilder.BuildInjection(plan)).ConfigureAwait(false);
            return true;
        }

        internal Task ExecuteAsync(DocumentFrame frame, string source)
        {
            World world;
            lock (_sync) _documents.TryGetValue(frame.DocumentId, out world);
            if (world == null)
            {
                CefSharpIsolatedExecutor[] children;
                lock (_sync) children = _children.Values.ToArray();
                var owner = children.FirstOrDefault(child => child.Owns(frame.DocumentId));
                if (owner != null) return owner.ExecuteAsync(frame, source);
            }
            return world == null ? Task.FromException(new InvalidOperationException("The isolated document is no longer active.")) : CallAsync(world, source);
        }

        private async Task CallAsync(World world, string source, bool guard = true)
        {
            if (!Alive(world) || world.ObjectId == null) throw new OperationCanceledException();
            // Remote object IDs pin a particular context generation; numeric context IDs
            // can be reused after navigation and are not used for source or delivery.
            var function = "function(){" + (guard ? "if(!globalThis.__MonkeySharpIsolatedTransport||globalThis.__MonkeySharpIsolatedTransport.generation!==" + JsonSerializer.Serialize(world.Generation) + ")return false;" : string.Empty) +
                source + "\nreturn true;}";
            var result = await _session.ExecuteAsync("Runtime.callFunctionOn", new { objectId = world.ObjectId, functionDeclaration = function, returnByValue = true }, world.Token).ConfigureAwait(false);
            if (result.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException("Isolated JavaScript failed: " + error.GetRawText());
            if (result.TryGetProperty("result", out var returned) && returned.TryGetProperty("value", out var accepted) && accepted.ValueKind == JsonValueKind.False)
                throw new OperationCanceledException("The native context generation changed.");
        }

        private bool Alive(World world) => !_disposed && !_stopping && !world.Token.IsCancellationRequested && !world.Native.Cancellation.IsCancellationRequested;

        private void Revoke(World world)
        {
            lock (_sync)
            {
                if (world.Revoked) return;
                world.Revoked = true;
                world.Cancellation.Cancel();
                world.Cancellation.Dispose();
                var key = world.Name + "\n" + world.Native.Id;
                if (_requested.TryGetValue(key, out var request) && ReferenceEquals(request, world)) _requested.Remove(key);
                if (_contexts.TryGetValue(world.ContextId, out var current) && ReferenceEquals(current, world)) _contexts.Remove(world.ContextId);
                _documents.Remove(world.Frame.DocumentId);
                _menuProofs.Remove(world.MenuProof);
            }
            _lifecycle(new DocumentLifecycleEventArgs(DocumentLifecycleKind.ContextReleased, world.Frame));
            Track(_engine.InvalidateDocumentAsync(world.Frame.DocumentId, CancellationToken.None));
        }

        private void Track(Task task)
        {
            lock (_sync) _tasks.Add(task);
            task.ContinueWith(completed =>
            {
                lock (_sync) _tasks.Remove(completed);
                if (completed.IsFaulted && !_disposed && !_stopping && !(completed.Exception.GetBaseException() is OperationCanceledException))
                    _diagnostic(new UserScriptDiagnostic("MSC233_ISOLATED_DISPATCH_FAILED", DiagnosticSeverity.Error, "Isolated dispatch failed.", completed.Exception.GetBaseException()));
            }, TaskScheduler.Default);
        }

        private void Report(string code, string message, UserScriptInstallation script, NativeFrame frame, Exception error = null) =>
            _diagnostic(new UserScriptDiagnostic(code, DiagnosticSeverity.Warning, message, error, script.ScriptKey, frameId: "cdp:" + frame.Id));

        private async Task EndWorldAsync(World world)
        {
            if (world.ObjectId != null)
                try
                {
                    await _session.ExecuteAsync("Runtime.callFunctionOn", new { objectId = world.ObjectId,
                        functionDeclaration = "function(){if(globalThis.__MonkeySharpIsolatedTransport)globalThis.__MonkeySharpIsolatedTransport.dispose();}" },
                        timeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    await _session.ExecuteAsync("Runtime.releaseObject", new { objectId = world.ObjectId }, timeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (Exception) { }
            Revoke(world);
        }

        internal async Task DisposeAsync()
        {
            World[] worlds;
            CefSharpIsolatedExecutor[] children;
            lock (_sync) { if (_disposed || _stopping) return; _stopping = true; worlds = _documents.Values.ToArray(); children = _children.Values.ToArray(); _children.Clear(); }
            _repository.Changed -= RepositoryChanged;
            _session.EventReceived -= EventReceived;
            _session.Closed -= SessionClosed;
            foreach (var child in children) await child.DisposeAsync().ConfigureAwait(false);
            foreach (var world in worlds)
            {
                await EndWorldAsync(world).ConfigureAwait(false);
            }
            try { await _session.ExecuteAsync("Runtime.removeBinding", new { name = _binding }).ConfigureAwait(false); } catch (Exception) { }
            lock (_sync)
            {
                _disposed = true;
                foreach (var frame in _frames.Values) { frame.Cancellation.Cancel(); frame.Cancellation.Dispose(); }
                _frames.Clear();
            }
            _lifetime.Cancel();
            Task[] pending;
            lock (_sync) pending = _tasks.ToArray();
            var all = Task.WhenAll(pending);
            Track(all);
            await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            _lifetime.Dispose();
            if (_session.IsChild) try { await _session.DetachAsync().ConfigureAwait(false); } catch (Exception) { }
            // Shared Runtime/Page domains remain enabled for other observers.
        }

        private sealed class NativeFrame
        {
            internal NativeFrame(string id, string loader, Uri url, bool main, CancellationToken token)
            { Id = id; Loader = loader; Url = url; Main = main; Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); }
            internal readonly string Id, Loader;
            internal string CefFrameId;
            internal Uri Url;
            internal readonly bool Main;
            internal readonly CancellationTokenSource Cancellation;
            internal readonly Dictionary<Guid, World> Worlds = new Dictionary<Guid, World>();
        }

        private sealed class World
        {
            internal World(NativeFrame native, UserScriptInstallation script, string name, string browserId, string sessionScope)
            {
                Native = native; Script = script; Name = name;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(native.Cancellation.Token);
                Token = Cancellation.Token;
                Frame = new DocumentFrame(browserId, Guid.NewGuid().ToString("D"), "cdp:" + sessionScope + ":" + native.Id, native.Url, native.Main,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified, "isolated", script.ScriptKey, script.RevisionId);
            }
            internal readonly NativeFrame Native;
            internal readonly UserScriptInstallation Script;
            internal readonly string Name;
            internal readonly string Generation = Guid.NewGuid().ToString("N");
            internal readonly string MenuProof = "monkey-menu:" + Guid.NewGuid().ToString("N");
            internal readonly CancellationTokenSource Cancellation;
            internal readonly CancellationToken Token;
            internal readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
            internal readonly TaskCompletionSource<int> Created = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal DocumentFrame Frame;
            internal int ContextId;
            internal string ObjectId;
            internal volatile bool Ready;
            internal bool Revoked;
            internal void UpdateUrl() => Frame = new DocumentFrame(Frame.BrowserSessionId, Frame.DocumentId, Frame.FrameId, Native.Url,
                Frame.IsMainFrame, Frame.TimingGuarantee, Frame.BridgeIntegrity, "isolated", Script.ScriptKey, Script.RevisionId);
        }
    }
}
