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
    public sealed class CefSharpCookieService : ICookieService
    {
        private readonly IRequestContext _context;

        /// <summary>Initializes a cookie service for an initialized browser request context.</summary>
        public CefSharpCookieService(IRequestContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manager = _context.GetCookieManager(null);
            if (manager == null || manager.IsDisposed)
                throw new ObjectDisposedException(nameof(ICookieManager));
            var source = new CookieVisitor(query, cancellationToken);
            if (!manager.VisitUrlCookies(query.Url.AbsoluteUri, true, source))
                return Task.FromResult<IReadOnlyList<UserScriptCookie>>(new ReadOnlyCollection<UserScriptCookie>(new List<UserScriptCookie>()));
            return source.Completion;
        }

        public async Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manager = _context.GetCookieManager(null);
            if (manager == null || manager.IsDisposed)
                throw new ObjectDisposedException(nameof(ICookieManager));
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cookie = new Cookie
            {
                Name = mutation.Name,
                Value = mutation.Value,
                Domain = mutation.Domain,
                Path = mutation.Path,
                Secure = mutation.Secure,
                Expires = mutation.Expiration
            };
            if (!manager.SetCookie(mutation.Url.AbsoluteUri, cookie, new SetCallback(completion)))
                throw new InvalidOperationException("CefSharp rejected the cookie mutation.");
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
                await completion.Task.ConfigureAwait(false);
            return new UserScriptCookie(mutation.Name, mutation.Value, mutation.Domain, mutation.Path,
                mutation.Expiration, mutation.Secure, false, mutation.SameSite);
        }

        public async Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manager = _context.GetCookieManager(null);
            if (manager == null || manager.IsDisposed)
                throw new ObjectDisposedException(nameof(ICookieManager));
            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!manager.DeleteCookies(mutation.Url.AbsoluteUri, mutation.Name, new DeleteCallback(completion)))
                return false;
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
                return await completion.Task.ConfigureAwait(false) > 0;
        }

        public ICookieListenerRegistration AddListener(UserScriptCookieQuery query,
            EventHandler<UserScriptCookieChangedEventArgs> changed)
        {
            // CEF does not expose a cookie-change observer on ICookieManager. Hosts that need
            // change notifications should forward their request-context events to this contract.
            return new NoopCookieListenerRegistration();
        }

        private sealed class CookieVisitor : ICookieVisitor
        {
            private readonly UserScriptCookieQuery _query;
            private readonly CancellationToken _cancellationToken;
            private readonly TaskCompletionSource<IReadOnlyList<UserScriptCookie>> _completion =
                new TaskCompletionSource<IReadOnlyList<UserScriptCookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<UserScriptCookie> _cookies = new List<UserScriptCookie>();

            public CookieVisitor(UserScriptCookieQuery query, CancellationToken cancellationToken)
            { _query = query; _cancellationToken = cancellationToken; }
            public Task<IReadOnlyList<UserScriptCookie>> Completion => _completion.Task;

            public bool Visit(Cookie cookie, int count, int total, ref bool deleteCookie)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    _completion.TrySetCanceled();
                    return false;
                }
                var value = new UserScriptCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path,
                    cookie.Expires, cookie.Secure, cookie.HttpOnly, cookie.SameSite.ToString());
                if (_query.Matches(value)) _cookies.Add(value);
                if (total <= 0 || count + 1 >= total)
                    _completion.TrySetResult(new ReadOnlyCollection<UserScriptCookie>(_cookies));
                return total <= 0 || count + 1 < total;
            }

            public void Dispose() { }
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

        private sealed class NoopCookieListenerRegistration : ICookieListenerRegistration
        {
            public void Dispose() { }
        }
    }
}
