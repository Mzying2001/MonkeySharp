using CefSharp;
using Moq;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class HttpRequestContextTests
    {
        [Theory]
        [InlineData(302)]
        [InlineData(307)]
        public async Task RequestKeepsAcceptedContextAcrossSchedulingAndRedirects(int redirectStatus)
        {
            var first = Mock.Of<IRequestContext>();
            var second = Mock.Of<IRequestContext>();
            var current = first;
            var queue = new ConcurrentQueue<Action>();
            var sent = new List<IRequestContext>();
            IUrlRequestClient client = null;
            using (var service = new CefSharpHttpRequestService(() => current, queue.Enqueue,
                () => Mock.Of<IRequest>(), (request, observer, context) =>
                {
                    client = observer;
                    sent.Add(context);
                    return Mock.Of<IUrlRequest>();
                }))
            {
                var operation = service.SendAsync(new UserScriptHttpRequest("GET", new Uri("https://example.com/start"),
                    new Dictionary<string, string>(), null, null, null, (_, __) => Task.FromResult(true)),
                    new Observer(), CancellationToken.None);
                current = second;
                RunNext(queue);
                Assert.Same(first, Assert.Single(sent));

                var response = Mock.Of<IResponse>(value => value.StatusCode == redirectStatus &&
                    value.Headers == new NameValueCollection { { "Location", "/end" } });
                client.OnRequestComplete(Mock.Of<IUrlRequest>(value => value.Response == response));
                RunNext(queue);
                Assert.True(SpinWait.SpinUntil(() => !queue.IsEmpty, 5000), "Redirect was not scheduled.");
                RunNext(queue);
                Assert.Equal(2, sent.Count);
                Assert.All(sent, context => Assert.Same(first, context));

                response = Mock.Of<IResponse>(value => value.StatusCode == 200 && value.Headers == new NameValueCollection());
                client.OnRequestComplete(Mock.Of<IUrlRequest>(value => value.Response == response && value.RequestStatus == UrlRequestStatus.Success));
                RunNext(queue);
                Assert.Equal(200, (await operation.Completion).Status);
                Mock.Get(first).Verify(value => value.Dispose(), Times.Never);
                Mock.Get(second).Verify(value => value.Dispose(), Times.Never);
            }
        }

        private static void RunNext(ConcurrentQueue<Action> queue)
        {
            Assert.True(queue.TryDequeue(out var callback));
            callback();
        }

        private sealed class Observer : IUserScriptHttpObserver
        {
            public void OnResponseStarted(UserScriptHttpResponse response) { }
            public void OnUploadProgress(long loaded, long? total) { }
            public void OnDownloadProgress(long loaded, long? total) { }
            public void OnResponseData(byte[] buffer, int offset, int count) { }
        }
    }
}
