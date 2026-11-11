using CefSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class ReadOnlyScriptEvaluatorTests
    {
        [Fact]
        public async Task CanceledEvaluationRetriesWithinTheRemainingBudget()
        {
            var attempts = 0;
            var expected = new JavascriptResponse { Success = true, Result = "ready" };
            var response = await ReadOnlyScriptEvaluator.EvaluateAsync(timeout =>
            {
                Assert.InRange(timeout, TimeSpan.FromTicks(1), TimeSpan.FromSeconds(2));
                return ++attempts == 1
                    ? Task.FromCanceled<JavascriptResponse>(new CancellationToken(true))
                    : Task.FromResult(expected);
            }, TimeSpan.FromSeconds(15), CancellationToken.None);

            Assert.Same(expected, response);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task RepeatedCancellationExpiresWithTheLastCancellation()
        {
            var attempts = 0;
            var timeout = await Assert.ThrowsAsync<TimeoutException>(() => ReadOnlyScriptEvaluator.EvaluateAsync(remaining =>
            {
                Assert.InRange(remaining, TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(150));
                attempts++;
                return Task.FromCanceled<JavascriptResponse>(new CancellationToken(true));
            }, TimeSpan.FromMilliseconds(150), CancellationToken.None));

            Assert.True(attempts > 0);
            Assert.IsType<TaskCanceledException>(timeout.InnerException);
        }

        [Fact]
        public async Task ExpiredBudgetDoesNotStartAnotherEvaluation()
        {
            var attempts = 0;
            await Assert.ThrowsAsync<TimeoutException>(() => ReadOnlyScriptEvaluator.EvaluateAsync(_ =>
            {
                attempts++;
                return Task.FromResult(new JavascriptResponse { Success = true });
            }, TimeSpan.Zero, CancellationToken.None));
            Assert.Equal(0, attempts);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CallerCancellationStopsEvaluationAndRetries(bool cancelDuringEvaluation)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var attempts = 0;
                if (!cancelDuringEvaluation) cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadOnlyScriptEvaluator.EvaluateAsync(_ =>
                {
                    attempts++;
                    cancellation.Cancel();
                    return Task.FromCanceled<JavascriptResponse>(new CancellationToken(true));
                }, TimeSpan.FromSeconds(15), cancellation.Token));
                Assert.Equal(cancelDuringEvaluation ? 1 : 0, attempts);
            }
        }

        [Fact]
        public async Task ScriptFailureDoesNotRetry()
        {
            var attempts = 0;
            var failure = new JavascriptResponse { Success = false, Message = "script failed" };
            var response = await ReadOnlyScriptEvaluator.EvaluateAsync(_ =>
            {
                attempts++;
                return Task.FromResult(failure);
            }, TimeSpan.FromSeconds(15), CancellationToken.None);
            Assert.Same(failure, response);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task EvaluationFaultDoesNotRetry()
        {
            var attempts = 0;
            var failure = new InvalidOperationException("native evaluation failed");
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadOnlyScriptEvaluator.EvaluateAsync(_ =>
            {
                attempts++;
                throw failure;
            }, TimeSpan.FromSeconds(15), CancellationToken.None));
            Assert.Same(failure, actual);
            Assert.Equal(1, attempts);
        }
    }
}
