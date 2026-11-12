using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Storage;
using Mzying2001.MonkeySharp.Demo.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class PrivateContextTests
    {
        [Fact]
        public async Task PrivateValuesCopyTypedBaselineOnceAndNeverWriteBack()
        {
            using (var normal = new InMemoryUserScriptValueStore())
            {
                await normal.SetAsync("script", "undefined", UserScriptValue.Undefined, CancellationToken.None);
                await normal.SetAsync("script", "setting", UserScriptValue.FromEncoded("snormal"), CancellationToken.None);
                var normalEvents = 0;
                normal.ValueChanged += (_, __) => normalEvents++;
                using (var privateValues = new PrivateUserScriptValueStore(normal))
                {
                    var privateEvents = 0;
                    privateValues.ValueChanged += (_, __) => privateEvents++;
                    Assert.Equal(new[] { "undefined", "setting" }, await privateValues.ListKeysAsync("script", CancellationToken.None));
                    Assert.Equal(0, privateEvents);
                    Assert.Equal("u", (await privateValues.GetAsync("script", "undefined", CancellationToken.None)).Value.EncodedValue);
                    await normal.SetAsync("script", "setting", UserScriptValue.FromEncoded("slater"), CancellationToken.None);
                    Assert.Equal("snormal", (await privateValues.GetAsync("script", "setting", CancellationToken.None)).Value.EncodedValue);
                    Assert.Equal(0, privateEvents);
                    await privateValues.SetAsync("script", "private-key", UserScriptValue.FromEncoded("sprivate-only-proof"), CancellationToken.None);
                    await privateValues.DeleteAsync("script", "setting", CancellationToken.None);
                    Assert.Equal(1, normalEvents);
                    Assert.Equal(2, privateEvents);
                    Assert.False((await normal.GetAsync("script", "private-key", CancellationToken.None)).Exists);
                    Assert.Equal("slater", (await normal.GetAsync("script", "setting", CancellationToken.None)).Value.EncodedValue);
                }
                using (var nextSession = new PrivateUserScriptValueStore(normal))
                {
                    Assert.False((await nextSession.GetAsync("script", "private-key", CancellationToken.None)).Exists);
                    Assert.Equal("slater", (await nextSession.GetAsync("script", "setting", CancellationToken.None)).Value.EncodedValue);
                }
            }
        }

        [Fact]
        public async Task PrivateTabDataIsScriptScopedAndGoneAfterClose()
        {
            var states = new MemoryTabStateService();
            var one = states.ForTab("one");
            var two = states.ForTab("two");
            var key = ScriptKey.Parse(Guid.NewGuid().ToString());
            await one.SaveAsync(key, null, "{\"private\":true}", CancellationToken.None);
            Assert.Single(await two.GetAllAsync(key, CancellationToken.None));
            Assert.Empty(await two.GetAllAsync(ScriptKey.Parse(Guid.NewGuid().ToString()), CancellationToken.None));
            states.CloseTab("one");
            Assert.Empty(await two.GetAllAsync(key, CancellationToken.None));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => one.SaveAsync(key, null, "{}", CancellationToken.None));
            states.Clear();
            Assert.Empty(await two.GetAllAsync(key, CancellationToken.None));
        }
    }
}
