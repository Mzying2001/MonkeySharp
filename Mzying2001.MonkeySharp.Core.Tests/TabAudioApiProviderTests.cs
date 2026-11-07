using Mzying2001.MonkeySharp.Core.Apis;
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
    public sealed class TabAudioApiProviderTests
    {
        [Fact]
        public async Task UnknownAudibleAndReasonAreOmittedAndMuteUsesNativeReadback()
        {
            var service = new AudioService();
            using var provider = new TabAudioApiProvider(service);
            var state = await provider.InvokeAsync(await Context(new { operation = "getState" }), CancellationToken.None);
            using var json = JsonDocument.Parse(state.Json);
            Assert.False(json.RootElement.GetProperty("isMuted").GetBoolean());
            Assert.False(json.RootElement.TryGetProperty("isAudible", out _));
            Assert.False(json.RootElement.TryGetProperty("muteReason", out _));
            var changed = await provider.InvokeAsync(await Context(new { operation = "setMute", isMuted = true }), CancellationToken.None);
            Assert.Equal("{\"isMuted\":true}", changed.Json);
            Assert.True(service.Muted);
        }

        [Fact]
        public async Task FunctionRegistrationsAreExecutionOwnedAndRemovedOnEnd()
        {
            var service = new AudioService();
            using var provider = new TabAudioApiProvider(service);
            var events = new List<ApiNotificationEventArgs>();
            provider.Notification += (_, value) => events.Add(value);
            await provider.InvokeAsync(await Context(new { operation = "addListener", listenerId = 1 }), CancellationToken.None);
            await provider.InvokeAsync(await Context(new { operation = "addListener", listenerId = 1 }), CancellationToken.None);
            await service.SetMuteAsync(true, CancellationToken.None);
            Assert.Single(events);
            using (var json = JsonDocument.Parse(events[0].DataJson))
            {
                Assert.Equal("extension", json.RootElement.GetProperty("change").GetProperty("muted").GetString());
                Assert.False(json.RootElement.GetProperty("change").TryGetProperty("audible", out _));
            }
            await provider.InvokeAsync(await Context(new { operation = "removeListener", listenerId = 1 }), CancellationToken.None);
            await service.SetMuteAsync(false, CancellationToken.None);
            Assert.Single(events);
            await provider.InvokeAsync(await Context(new { operation = "addListener", listenerId = 2 }), CancellationToken.None);
            provider.OnExecutionEnded("audio-execution");
            await service.SetMuteAsync(true, CancellationToken.None);
            Assert.Single(events);
        }

        [Fact]
        public async Task InvalidMuteCannotReachServiceAndProviderDoesNotOwnService()
        {
            var service = new AudioService();
            var provider = new TabAudioApiProvider(service);
            await Assert.ThrowsAsync<Domain.BridgeProtocolException>(() => InvokeInvalid());
            Assert.False(service.Muted);
            provider.Dispose();
            Assert.Equal(0, service.Subscribers);
            async Task InvokeInvalid() => await provider.InvokeAsync(await Context(new { operation = "setMute", isMuted = "true" }), CancellationToken.None);
        }

        private static async Task<ApiInvocationContext> Context(object parameters)
        {
            var script = await new InMemoryUserScriptRepository().InstallAsync(MetadataAndMatchingTests.Script("// @name audio\n// @match https://example.com/*\n// @grant GM_audio"), "test", true, CancellationToken.None);
            return new ApiInvocationContext(script, new DocumentFrame("tab", "doc", "frame", new Uri("https://example.com/"), true,
                TimingGuarantee.BestEffortDocumentStart), "audio-execution", Guid.NewGuid().ToString(), "GM.audio", JsonSerializer.SerializeToElement(parameters));
        }
        private sealed class AudioService : ITabAudioService
        {
            private EventHandler<TabAudioState> _changed;
            public bool SupportsAudible => false;
            public bool Muted;
            public int Subscribers;
            public event EventHandler<TabAudioState> StateChanged { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
            public Task<TabAudioState> GetStateAsync(CancellationToken token) => Task.FromResult(new TabAudioState(Muted));
            public Task<TabAudioState> SetMuteAsync(bool muted, CancellationToken token) { Muted = muted; var state = new TabAudioState(muted, muted ? "extension" : null); _changed?.Invoke(this, state); return Task.FromResult(state); }
        }
    }
}
