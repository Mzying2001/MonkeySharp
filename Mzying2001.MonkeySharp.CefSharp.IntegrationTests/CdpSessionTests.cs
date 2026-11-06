using CefSharp;
using CefSharp.Callback;
using Moq;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class CdpSessionTests
    {
        [Fact]
        public async Task CommandsUseNativeIdsAndOnlyDisposeTheirOwnObserver()
        {
            var host = new Mock<IBrowserHost>();
            var registration = new Mock<IRegistration>();
            host.Setup(value => value.AddDevToolsMessageObserver(It.IsAny<IDevToolsMessageObserver>())).Returns(registration.Object);
            host.Setup(value => value.ExecuteDevToolsMethod(0, It.IsAny<string>(), It.IsAny<string>())).Returns(712);
            using (var session = new CefSharpCdpSession(host.Object, action => { action(); return Task.CompletedTask; }))
            {
                await session.InitializeAsync();
                var command = session.ExecuteAsync("Network.getCookies");
                session.OnDevToolsMethodResult(null, 99, true, Json("{\"other\":true}"));
                Assert.False(command.IsCompleted);
                var stream = Json("{\"cookies\":[]}");
                session.OnDevToolsMethodResult(null, 712, true, stream);
                Assert.Equal(0, (await command).GetProperty("cookies").GetArrayLength());
                Assert.True(stream.CanRead);
                Assert.False(session.OnDevToolsMessage(null, Json("{}")));
            }
            registration.Verify(value => value.Dispose(), Times.Once);
            host.Verify(value => value.CloseDevTools(), Times.Never);
        }

        [Fact]
        public async Task NativeReentrantResponseIsMatchedAfterTheAllocatedIdReturns()
        {
            var host = new Mock<IBrowserHost>();
            using (var session = new CefSharpCdpSession(host.Object, action => { action(); return Task.CompletedTask; }))
            {
                host.Setup(value => value.ExecuteDevToolsMethod(0, It.IsAny<string>(), It.IsAny<string>())).Returns(() =>
                {
                    session.OnDevToolsMethodResult(null, 4, true, Json("{\"product\":\"Chrome/84\"}"));
                    return 4;
                });
                Assert.Equal("Chrome/84", (await session.ExecuteAsync("Browser.getVersion")).GetProperty("product").GetString());
            }
        }

        [Fact]
        public async Task CancellationTimeoutAndAgentDetachReleasePendingCommands()
        {
            var host = new Mock<IBrowserHost>();
            var next = 1;
            host.Setup(value => value.ExecuteDevToolsMethod(0, It.IsAny<string>(), It.IsAny<string>())).Returns(() => next++);
            using (var session = new CefSharpCdpSession(host.Object, action => { action(); return Task.CompletedTask; }))
            using (var cancellation = new CancellationTokenSource())
            {
                var canceled = session.ExecuteAsync("Runtime.evaluate", cancellationToken: cancellation.Token);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
                session.OnDevToolsMethodResult(null, 1, true, Json("{}"));
                await Assert.ThrowsAsync<TimeoutException>(() => session.ExecuteAsync("Runtime.evaluate", timeout: TimeSpan.FromMilliseconds(10)));
                var detached = session.ExecuteAsync("Runtime.evaluate");
                session.OnDevToolsAgentDetached(null);
                await Assert.ThrowsAsync<ObjectDisposedException>(() => detached);
                await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ExecuteAsync("Runtime.evaluate"));
            }
        }

        [Fact]
        public async Task ProtocolFailuresAndObserverExceptionsDoNotLoseOtherEvents()
        {
            var host = new Mock<IBrowserHost>();
            host.Setup(value => value.ExecuteDevToolsMethod(0, It.IsAny<string>(), It.IsAny<string>())).Returns(3);
            using (var session = new CefSharpCdpSession(host.Object, action => { action(); return Task.CompletedTask; }))
            {
                var command = session.ExecuteAsync("Unknown.command");
                session.OnDevToolsMethodResult(null, 3, false, Json("{\"code\":-32601}"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => command);
                var failures = 0;
                var deliveries = 0;
                session.HandlerFailed += (_, error) => failures++;
                session.EventReceived += (_, args) => throw new InvalidOperationException();
                session.EventReceived += (_, args) => { Assert.Equal("Runtime.bindingCalled", args.Method); deliveries++; };
                session.OnDevToolsEvent(null, "Runtime.bindingCalled", Json("{\"executionContextId\":7}"));
                Assert.Equal(1, failures);
                Assert.Equal(1, deliveries);
            }
        }

        private static MemoryStream Json(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));
    }
}
