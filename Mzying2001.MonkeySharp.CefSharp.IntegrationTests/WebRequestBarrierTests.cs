using Mzying2001.MonkeySharp.CefSharp.SmokeHost;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class WebRequestBarrierTests
    {
        [Fact]
        public async Task BarrierAcknowledgesOnlyCommittedDynamicFixtureRules()
        {
            using (var service = new ObservedWebRequestService { InstallationId = "fixture" })
            {
                var pending = service.WaitAsync(1, CancellationToken.None);
                using (service.SetRules("other", Array.Empty<WebRequestRule>(), _ => true, installationId: "other")) { }
                using (service.SetRules("static", Array.Empty<WebRequestRule>(), _ => true, installationId: "fixture", isStatic: true)) { }
                Assert.False(pending.IsCompleted);
                using (service.SetRules("dynamic", Array.Empty<WebRequestRule>(), _ => true, installationId: "browser:fixture"))
                {
                    await pending;
                    await service.WaitAsync(1, CancellationToken.None);
                    var replacement = service.WaitAsync(2, CancellationToken.None);
                    Assert.False(replacement.IsCompleted);
                    using (service.SetRules("dynamic", Array.Empty<WebRequestRule>(), _ => true, installationId: "fixture")) await replacement;
                }
            }
        }

        [Fact]
        public async Task CanceledBarrierDoesNotAcknowledgeOrBlockFutureRegistrations()
        {
            using (var service = new ObservedWebRequestService { InstallationId = "fixture" })
            using (var cancellation = new CancellationTokenSource())
            {
                var pending = service.WaitAsync(1, cancellation.Token);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                using (service.SetRules("dynamic", Array.Empty<WebRequestRule>(), _ => true, installationId: "fixture"))
                    await service.WaitAsync(1, CancellationToken.None);
            }
        }
    }
}
