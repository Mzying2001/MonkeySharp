using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS1591

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Maps MonkeySharp cookie operations to a CefSharp request context cookie manager.</summary>
    public sealed class CefSharpCookieService : ICookieService, IDisposable
    {
        private readonly Func<IRequestContext> _contextAccessor;
        private readonly object _sync = new object();
        private readonly Dictionary<Guid, Listener> _listeners = new Dictionary<Guid, Listener>();
        private readonly HashSet<IPendingOperation> _pending = new HashSet<IPendingOperation>();
        private long _sequence;
        private bool _disposed;

        /// <summary>Initializes a cookie service for an initialized browser request context.</summary>
        public CefSharpCookieService(IRequestContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _contextAccessor = () => context;
        }

        /// <summary>
        /// Initializes a cookie service that resolves the current browser context for
        /// each operation. This supports hosts that detach and attach to another browser.
        /// </summary>
        public CefSharpCookieService(Func<IRequestContext> contextAccessor)
        {
            _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        }

        public Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var manager = GetCookieManager();
            if (manager == null || manager.IsDisposed)
                throw new ObjectDisposedException(nameof(ICookieManager));
            var source = new CookieVisitor(query, cancellationToken);
            AddPending(source);
            var cancellation = cancellationToken.Register(source.Cancel);
            source.Completion.ContinueWith(_ =>
            {
                cancellation.Dispose();
                RemovePending(source);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try
            {
                if (!manager.VisitUrlCookies(query.Url.AbsoluteUri, true, source))
                {
                    source.Complete();
                    return source.Completion;
                }
            }
            catch
            {
                source.Cancel();
                throw;
            }
            return source.Completion;
        }

        public async Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var manager = GetCookieManager();
            if (manager == null || manager.IsDisposed)
                throw new ObjectDisposedException(nameof(ICookieManager));
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingCompletion<bool>(completion);
            AddPending(pending);
            var cookie = new Cookie
            {
                Name = mutation.Name,
                Value = mutation.Value,
                Domain = mutation.Domain,
                Path = mutation.Path,
                Secure = mutation.Secure,
                Expires = mutation.Expiration
            };
            try
            {
                if (!manager.SetCookie(mutation.Url.AbsoluteUri, cookie, new SetCallback(completion)))
                    throw new InvalidOperationException("CefSharp rejected the cookie mutation.");
                using (cancellationToken.Register(pending.Cancel))
                {
                    if (!await completion.Task.ConfigureAwait(false))
                        throw new InvalidOperationException("CefSharp rejected the cookie mutation.");
                }
            }
            finally
            {
                RemovePending(pending);
            }
            var result = new UserScriptCookie(mutation.Name, mutation.Value, mutation.Domain, mutation.Path,
                mutation.Expiration, mutation.Secure, false, mutation.SameSite);
            Publish(result, "explicit", false, mutation.OriginExecutionId);
            return result;
        }

        public async Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var manager = GetCookieManager();
            if (manager == null || manager.IsDisposed)
                throw new ObjectDisposedException(nameof(ICookieManager));
            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingCompletion<int>(completion);
            AddPending(pending);
            try
            {
                if (!manager.DeleteCookies(mutation.Url.AbsoluteUri, mutation.Name, new DeleteCallback(completion)))
                    return false;
                using (cancellationToken.Register(pending.Cancel))
                {
                    var deleted = await completion.Task.ConfigureAwait(false) > 0;
                    if (deleted)
                        Publish(new UserScriptCookie(mutation.Name, mutation.Value, mutation.Domain, mutation.Path,
                            mutation.Expiration, mutation.Secure, false, mutation.SameSite), "explicit", true,
                            mutation.OriginExecutionId);
                    return deleted;
                }
            }
            finally
            {
                RemovePending(pending);
            }
        }

        public ICookieListenerRegistration AddListener(UserScriptCookieQuery query,
            EventHandler<UserScriptCookieChangedEventArgs> changed)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (changed == null) throw new ArgumentNullException(nameof(changed));
            lock (_sync)
            {
                ThrowIfDisposed();
                var id = Guid.NewGuid();
                var listener = new Listener(id, query, changed, this);
                _listeners.Add(id, listener);
                return listener;
            }
        }

        private void Publish(UserScriptCookie cookie, string cause, bool removed, string originExecutionId)
        {
            Listener[] listeners;
            long sequence;
            lock (_sync)
            {
                if (_disposed) return;
                sequence = ++_sequence;
                listeners = _listeners.Values.Where(item => item.Query.Matches(cookie)).ToArray();
            }
            var change = new UserScriptCookieChangedEventArgs(cookie, cause, removed, originExecutionId, sequence);
            foreach (var listener in listeners)
                listener.Callback(this, change);
        }

        private void Remove(Guid id)
        {
            lock (_sync) _listeners.Remove(id);
        }

        private void AddPending(IPendingOperation operation)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _pending.Add(operation);
            }
        }

        private void RemovePending(IPendingOperation operation)
        {
            lock (_sync) _pending.Remove(operation);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CefSharpCookieService));
        }

        private ICookieManager GetCookieManager()
        {
            var context = _contextAccessor();
            if (context == null)
                throw new InvalidOperationException("No active CefSharp request context is attached.");
            return context.GetCookieManager(null);
        }

        public void Dispose()
        {
            IPendingOperation[] pending;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _listeners.Clear();
                pending = _pending.ToArray();
                _pending.Clear();
            }
            foreach (var operation in pending)
                operation.Cancel();
        }

        private interface IPendingOperation
        {
            void Cancel();
        }

        private sealed class PendingCompletion<T> : IPendingOperation
        {
            private readonly TaskCompletionSource<T> _completion;

            public PendingCompletion(TaskCompletionSource<T> completion)
            {
                _completion = completion;
            }

            public void Cancel()
            {
                _completion.TrySetCanceled();
            }

        }

        private sealed class CookieVisitor : ICookieVisitor, IPendingOperation
        {
            private readonly UserScriptCookieQuery _query;
            private readonly CancellationToken _cancellationToken;
            private readonly TaskCompletionSource<IReadOnlyList<UserScriptCookie>> _completion =
                new TaskCompletionSource<IReadOnlyList<UserScriptCookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<UserScriptCookie> _cookies = new List<UserScriptCookie>();

            public CookieVisitor(UserScriptCookieQuery query, CancellationToken cancellationToken)
            { _query = query; _cancellationToken = cancellationToken; }
            public Task<IReadOnlyList<UserScriptCookie>> Completion => _completion.Task;

            public void Cancel()
            {
                _completion.TrySetCanceled();
            }

            public void Complete()
            {
                _completion.TrySetResult(new ReadOnlyCollection<UserScriptCookie>(_cookies));
            }

            public bool Visit(Cookie cookie, int count, int total, ref bool deleteCookie)
            {
                if (_completion.Task.IsCompleted || _cancellationToken.IsCancellationRequested)
                {
                    Cancel();
                    return false;
                }
                var sameSite = cookie.GetType().GetProperty("SameSite")?.GetValue(cookie, null)?.ToString();
                var value = new UserScriptCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path,
                    cookie.Expires, cookie.Secure, cookie.HttpOnly, sameSite);
                if (_query.Matches(value)) _cookies.Add(value);
                if (total <= 0 || count + 1 >= total)
                    _completion.TrySetResult(new ReadOnlyCollection<UserScriptCookie>(_cookies));
                return total <= 0 || count + 1 < total;
            }

            public void Dispose()
            {
                Complete();
            }
        }

        private sealed class SetCallback : ISetCookieCallback
        {
            private readonly TaskCompletionSource<bool> _completion;
            public SetCallback(TaskCompletionSource<bool> completion) { _completion = completion; }
            public bool IsDisposed => false;
            public void OnComplete(bool success) { _completion.TrySetResult(success); }
            public void Dispose() { }
        }

        private sealed class DeleteCallback : IDeleteCookiesCallback
        {
            private readonly TaskCompletionSource<int> _completion;
            public DeleteCallback(TaskCompletionSource<int> completion) { _completion = completion; }
            public bool IsDisposed => false;
            public void OnComplete(int numDeleted) { _completion.TrySetResult(numDeleted); }
            public void Dispose() { }
        }

        private sealed class Listener : ICookieListenerRegistration
        {
            private readonly CefSharpCookieService _owner;
            private int _disposed;

            public Listener(Guid id, UserScriptCookieQuery query,
                EventHandler<UserScriptCookieChangedEventArgs> callback, CefSharpCookieService owner)
            {
                Id = id;
                Query = query;
                Callback = callback;
                _owner = owner;
            }

            public Guid Id { get; }
            public UserScriptCookieQuery Query { get; }
            public EventHandler<UserScriptCookieChangedEventArgs> Callback { get; }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    _owner.Remove(Id);
            }
        }
    }
}
