using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class NotificationLifecycleTests
    {
        [Fact]
        public async Task StartFailureReportsErrorInsteadOfSuccessfulDismissal()
        {
            var installation = await Install();
            var service = new Service { FailOnStart = true };
            using var provider = new HostInteractionApiProvider(notifications: service);
            var events = new List<string>();
            provider.Notification += (_, value) => events.Add(value.EventName);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Show(provider, installation));
            Assert.Equal(new[] { "notification-error" }, events);
            Assert.True(service.Handles[0].Disposed);
        }

        [Fact]
        public async Task SubscribesBeforeEarlyCloseAndReportsOneTerminal()
        {
            var installation = await Install();
            var service = new Service { CloseOnStart = true };
            using var provider = new HostInteractionApiProvider(notifications: service);
            var events = new List<string>();
            provider.Notification += (_, value) => events.Add(value.EventName);
            await Show(provider, installation);
            service.Handles[0].Dispose();
            Assert.Equal(new[] { "notification-done" }, events);
        }

        [Fact]
        public async Task TagsReplaceAndUnloadRetainsOnlyPersistentUiWithoutCallbacks()
        {
            var installation = await Install();
            var service = new Service();
            using var provider = new HostInteractionApiProvider(notifications: service);
            var events = new List<string>();
            provider.Notification += (_, value) => events.Add(value.EventName);
            await Show(provider, installation, "replace");
            await Show(provider, installation, "replace");
            Assert.True(service.Handles[0].Disposed);
            Assert.False(service.Handles[1].Disposed);
            await Show(provider, installation);
            provider.OnExecutionEnded("execution");
            Assert.True(service.Handles[2].Disposed);
            Assert.False(service.Handles[1].Disposed);
            events.Clear();
            service.Handles[1].Click();
            Assert.Equal(false, service.Handles[1].Prevented);
            Assert.Empty(events);
            provider.Dispose();
            Assert.True(service.Handles[1].Disposed);
        }

        [Fact]
        public async Task ClickDefaultWaitsForScriptAcknowledgmentAndDuplicateSignalsAreIgnored()
        {
            var installation = await Install();
            var service = new Service();
            using var provider = new HostInteractionApiProvider(notifications: service);
            var events = new List<string>();
            provider.Notification += (_, value) => events.Add(value.EventName);
            var id = await Show(provider, installation);
            service.Handles[0].Click(); service.Handles[0].Click();
            Assert.Null(service.Handles[0].Prevented);
            await provider.InvokeAsync(Context(installation, new { clientId = id, operation = "click-result", preventDefault = true }), CancellationToken.None);
            Assert.True(service.Handles[0].Prevented);
            Assert.Equal(new[] { "notification-click", "notification-done" }, events);
        }

        private static async Task<string> Show(HostInteractionApiProvider provider, UserScriptInstallation installation, string tag = null)
        {
            var clientId = Guid.NewGuid().ToString();
            await provider.InvokeAsync(Context(installation, new { clientId, text = "text", tag }), CancellationToken.None);
            return clientId;
        }
        private static ApiInvocationContext Context(UserScriptInstallation installation, object value)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
            return new ApiInvocationContext(installation, new DocumentFrame("browser", "document", "frame", new Uri("https://example.com"), true,
                TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified), "execution", Guid.NewGuid().ToString(), "GM.notification", json.RootElement.Clone());
        }
        private static Task<UserScriptInstallation> Install() => new InMemoryUserScriptRepository().InstallAsync(
            MetadataAndMatchingTests.Script("// @name notifications\n// @match https://example.com/*\n// @grant GM.notification"), "test", true, CancellationToken.None);
        private sealed class Service : INotificationService
        {
            public bool CloseOnStart, FailOnStart;
            public readonly List<Handle> Handles = new List<Handle>();
            public INotificationHandle Create(UserScriptNotificationRequest request, CancellationToken cancellationToken)
            { var handle = new Handle { CloseOnStart = CloseOnStart, FailOnStart = FailOnStart }; Handles.Add(handle); return handle; }
        }
        private sealed class Handle : INotificationHandle
        {
            private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>();
            public Task Completion => _completion.Task;
            public bool CloseOnStart, FailOnStart, Disposed;
            public bool? Prevented;
            public event EventHandler Clicked, Closed;
            public void Start() { if (FailOnStart) throw new InvalidOperationException("UI failed"); if (CloseOnStart) Dispose(); }
            public void Click() => Clicked?.Invoke(this, EventArgs.Empty);
            public void ResolveClick(bool preventDefault) { Prevented = preventDefault; Dispose(); }
            public void Dispose() { if (Disposed) return; Disposed = true; Closed?.Invoke(this, EventArgs.Empty); _completion.TrySetResult(null); }
        }
    }
}
