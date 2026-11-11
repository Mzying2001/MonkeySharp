using CefSharp;
using CefSharp.Callback;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    // One browser attachment owns one observer. CEF allocates command IDs across
    // observers; never disable shared domains or detach the browser's agent here.
    internal sealed class CefSharpCdpSession : IDevToolsMessageObserver, IDisposable
    {
        private readonly IBrowserHost _host;
        private readonly Func<Action, Task> _schedule;
        private readonly object _sync = new object();
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending =
            new Dictionary<int, TaskCompletionSource<JsonElement>>();
        private IRegistration _registration;
        private Dictionary<int, Action<TaskCompletionSource<JsonElement>>> _submissionResults;
        private int _submissionDepth;
        private bool _disposed;
        private readonly CefSharpCdpSession _root;
        private readonly string _sessionId;
        private readonly Dictionary<string, CefSharpCdpSession> _children = new Dictionary<string, CefSharpCdpSession>();
        internal string Scope => _sessionId ?? "root";
        internal bool IsChild => _sessionId != null;
        internal bool IsClosed { get { lock (_sync) return _disposed; } }
        internal event EventHandler Closed;

        internal CefSharpCdpSession(IBrowserHost host, Func<Action, Task> schedule = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _schedule = schedule ?? (action => Cef.UIThreadTaskFactory.StartNew(action));
        }

        internal event EventHandler<CdpEventArgs> EventReceived;
        internal event EventHandler<Exception> HandlerFailed;

        private CefSharpCdpSession(CefSharpCdpSession root, string sessionId)
        { _root = root; _sessionId = sessionId; _host = root._host; _schedule = root._schedule; }

        internal async Task<CefSharpCdpSession> AttachTargetAsync(string targetId, CancellationToken token)
        {
            var root = _root ?? this;
            var result = await root.ExecuteAsync("Target.attachToTarget", new { targetId, flatten = true }, token).ConfigureAwait(false);
            var id = result.GetProperty("sessionId").GetString();
            lock (root._sync)
            {
                root.ThrowIfDisposed();
                var child = new CefSharpCdpSession(root, id);
                root._children.Add(id, child);
                return child;
            }
        }

        internal Task<JsonElement> ExecuteRootAsync(string method, object parameters = null, CancellationToken token = default(CancellationToken)) =>
            (_root ?? this).ExecuteAsync(method, parameters, token);

        internal async Task DetachAsync()
        {
            if (_root == null) return;
            try { await _root.ExecuteAsync("Target.detachFromTarget", new { sessionId = _sessionId }).ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
            finally { Dispose(); }
        }

        internal async Task InitializeAsync()
        {
            await _schedule(() =>
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (_registration == null)
                        _registration = _host.AddDevToolsMessageObserver(this) ??
                            throw new InvalidOperationException("CEF did not register the CDP observer.");
                }
            }).ConfigureAwait(false);
        }

        internal async Task<JsonElement> ExecuteAsync(string method, object parameters = null,
            CancellationToken cancellationToken = default(CancellationToken), TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("A CDP method is required.", nameof(method));
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            var json = JsonSerializer.Serialize(parameters ?? new object());
            var id = 0;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
                try
                {
                    await _schedule(() =>
                    {
                        lock (_sync)
                        {
                            ThrowIfDisposed();
                            deadline.Token.ThrowIfCancellationRequested();
                            if (_root != null)
                            {
                                // Reserve a CEF-allocated ID before sending its child-session command.
                                // No other observer can subsequently allocate this ID for a root request.
                                id = _host.ExecuteDevToolsMethod(0, "Browser.getVersion", "{}");
                                if (id <= 0) throw new InvalidOperationException("CEF did not allocate a child command ID.");
                                _pending.Add(id, completion);
                                if (!_host.SendDevToolsMessage(JsonSerializer.Serialize(new { id, sessionId = _sessionId, method, @params = parameters ?? new object() })))
                                    throw new InvalidOperationException("CEF rejected the child session message.");
                                return;
                            }
                            // CEF 84 can deliver the response reentrantly before returning
                            // its allocated ID. Keep only responses received during submission.
                            if (_submissionDepth++ == 0) _submissionResults = new Dictionary<int, Action<TaskCompletionSource<JsonElement>>>();
                            try
                            {
                                id = _host.ExecuteDevToolsMethod(0, method, json);
                                ThrowIfDisposed();
                                if (id <= 0) throw new InvalidOperationException("CEF rejected CDP command " + method + ".");
                                if (_submissionResults.TryGetValue(id, out var deliver)) deliver(completion);
                                else _pending.Add(id, completion);
                            }
                            finally { if (--_submissionDepth == 0) _submissionResults = null; }
                        }
                    }).ConfigureAwait(false);
                    using (deadline.Token.Register(() =>
                    {
                        lock (_sync) _pending.Remove(id);
                        if (cancellationToken.IsCancellationRequested) completion.TrySetCanceled();
                        else completion.TrySetException(new TimeoutException("CDP command timed out: " + method));
                    }))
                        return await completion.Task.ConfigureAwait(false);
                }
                finally { lock (_sync) _pending.Remove(id); }
            }
        }

        public bool OnDevToolsMessage(IBrowser browser, Stream message)
        {
            var packet = Read(message);
            if (!packet.TryGetProperty("sessionId", out var id)) return false;
            CefSharpCdpSession child;
            lock (_sync) _children.TryGetValue(id.GetString(), out child);
            child?.Receive(packet);
            // Session envelopes must never enter this observer's unscoped callbacks.
            // Returning true affects this observer only; other native observers remain registered.
            return true;
        }

        private void Receive(JsonElement packet)
        {
            if (packet.TryGetProperty("id", out var id))
            {
                TaskCompletionSource<JsonElement> pending;
                lock (_sync)
                {
                    if (!_pending.TryGetValue(id.GetInt32(), out pending)) return;
                    _pending.Remove(id.GetInt32());
                }
                if (packet.TryGetProperty("error", out var error)) pending.TrySetException(new InvalidOperationException("CDP rejected child command: " + error.GetRawText()));
                else pending.TrySetResult(packet.GetProperty("result").Clone());
            }
            else if (packet.TryGetProperty("method", out var method))
            {
                var parameters = packet.TryGetProperty("params", out var data) ? data.Clone() : JsonSerializer.SerializeToElement(new object());
                DispatchEvent(new CdpEventArgs(method.GetString(), parameters));
            }
        }

        public void OnDevToolsMethodResult(IBrowser browser, int messageId, bool success, Stream result)
        {
            Action<TaskCompletionSource<JsonElement>> deliver;
            try
            {
                var value = Read(result);
                deliver = success
                    ? (Action<TaskCompletionSource<JsonElement>>)(target => target.TrySetResult(value))
                    : target => target.TrySetException(new InvalidOperationException("CDP rejected command: " + value.GetRawText()));
            }
            catch (Exception error) { deliver = target => target.TrySetException(error); }
            lock (_sync)
            {
                if (_pending.TryGetValue(messageId, out var completion))
                {
                    _pending.Remove(messageId);
                    deliver(completion);
                }
                else if (_submissionResults != null) _submissionResults[messageId] = deliver;
            }
        }

        public void OnDevToolsEvent(IBrowser browser, string method, Stream parameters)
        {
            lock (_sync) if (_disposed) return;
            try
            {
                var args = new CdpEventArgs(method, Read(parameters));
                if (method == "Target.detachedFromTarget" && args.Parameters.TryGetProperty("sessionId", out var sessionId))
                {
                    CefSharpCdpSession child;
                    lock (_sync) _children.TryGetValue(sessionId.GetString(), out child);
                    child?.Dispose();
                }
                DispatchEvent(args);
            }
            catch (Exception error) { HandlerFailed?.Invoke(this, error); }
        }

        private void DispatchEvent(CdpEventArgs args)
        {
            try
            {
                var handlers = EventReceived;
                if (handlers == null) return;
                foreach (EventHandler<CdpEventArgs> handler in handlers.GetInvocationList())
                {
                    try { handler(this, args); }
                    catch (Exception error) { HandlerFailed?.Invoke(this, error); }
                }
            }
            catch (Exception error) { HandlerFailed?.Invoke(this, error); }
        }

        public void OnDevToolsAgentAttached(IBrowser browser) { }
        public void OnDevToolsAgentDetached(IBrowser browser) => Dispose();

        private static JsonElement Read(Stream stream)
        {
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
            {
                var json = reader.ReadToEnd();
                using (var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json))
                    return document.RootElement.Clone();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CefSharpCdpSession));
        }

        public void Dispose()
        {
            TaskCompletionSource<JsonElement>[] pending;
            IRegistration registration;
            CefSharpCdpSession[] children;
            EventHandler closed;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                pending = new List<TaskCompletionSource<JsonElement>>(_pending.Values).ToArray();
                _pending.Clear();
                registration = _registration;
                _registration = null;
                children = _children.Values.ToArray();
                _children.Clear();
                closed = Closed;
                Closed = null;
                EventReceived = null;
                HandlerFailed = null;
            }
            foreach (var completion in pending)
                completion.TrySetException(new ObjectDisposedException(nameof(CefSharpCdpSession)));
            registration?.Dispose();
            foreach (var child in children) child.Dispose();
            if (_root != null) lock (_root._sync) _root._children.Remove(_sessionId);
            closed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class CdpEventArgs : EventArgs
    {
        internal CdpEventArgs(string method, JsonElement parameters) { Method = method; Parameters = parameters; }
        internal string Method { get; }
        internal JsonElement Parameters { get; }
    }
}
