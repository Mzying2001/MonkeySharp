using Mzying2001.MonkeySharp.Demo.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class SmokePollingTests
    {
        [Fact]
        public async Task NavigationCanceledEvaluationIsRetriedUntilTheConditionIsTrue()
        {
            var attempts = 0;
            await DemoSmokeTest.WaitAsync(() =>
            {
                attempts++;
                if (attempts == 1) return Task.FromCanceled<bool>(new CancellationToken(true));
                return Task.FromResult(attempts == 3);
            });
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task PollingDoesNotSuppressAssertionOrProgrammingErrors()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DemoSmokeTest.WaitAsync(() =>
                throw new InvalidOperationException("Assertion failed")));
        }

        [Fact]
        public async Task RepeatedCancellationStillFailsTheBoundedWait()
        {
            await Assert.ThrowsAsync<TimeoutException>(() => DemoSmokeTest.WaitAsync(() =>
                Task.FromCanceled<bool>(new CancellationToken(true)), TimeSpan.FromMilliseconds(20)));
        }
    }
}
