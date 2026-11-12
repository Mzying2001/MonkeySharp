using CefSharp;
using Moq;
using Mzying2001.MonkeySharp.CefSharp.SmokeHost;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class NativeFrameProbeTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RetiredFrameIsSkippedBeforeOrDuringEvaluation(bool duringEvaluation)
        {
            var frame = new Mock<IFrame>();
            var valid = duringEvaluation;
            frame.SetupGet(value => value.IsValid).Returns(() => valid);
            frame.Setup(value => value.EvaluateScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan?>()
#if !CEF_SHARP_LEGACY_EVALUATION
                , false
#endif
                )).Returns(() =>
                {
                    valid = false;
                    throw new Exception("The underlying frame is no longer valid");
                });
            Assert.Null(await NativeFrameProbe.EvaluateAsync(frame.Object, "location.pathname"));
            if (!duringEvaluation) frame.Verify(value => value.IsValid, Times.Once);
        }

        [Fact]
        public async Task LiveFrameEvaluationErrorsRemainFailures()
        {
            var frame = new Mock<IFrame>();
            frame.SetupGet(value => value.IsValid).Returns(true);
            var failure = new InvalidOperationException("evaluation failed");
            frame.Setup(value => value.EvaluateScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan?>()
#if !CEF_SHARP_LEGACY_EVALUATION
                , false
#endif
                )).Throws(failure);
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => NativeFrameProbe.EvaluateAsync(frame.Object, "location.pathname")));
        }
    }
}
