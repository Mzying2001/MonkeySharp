using Mzying2001.MonkeySharp.CefSharp.SmokeHost;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class SmokeLifecycleAssertionsTests
    {
        [Theory]
        [InlineData(DocumentLifecycleKind.ContextReleased, false)]
        [InlineData(DocumentLifecycleKind.NavigationSuperseded, false)]
        [InlineData(DocumentLifecycleKind.ContextReleased, true)]
        [InlineData(DocumentLifecycleKind.NavigationSuperseded, true)]
        public void NavigationAcceptsEitherOldDocumentTermination(DocumentLifecycleKind terminal, bool mainFrame)
        {
            // Chromium 84 can report supersession before the native release callback.
            var old = Frame("old", "same", mainFrame);
            var next = Frame("next", "same-next", mainFrame);
            var events = new[]
            {
                new DocumentLifecycleEventArgs(DocumentLifecycleKind.ContextCreated, old),
                new DocumentLifecycleEventArgs(terminal, old),
                new DocumentLifecycleEventArgs(DocumentLifecycleKind.ContextCreated, next),
                new DocumentLifecycleEventArgs(DocumentLifecycleKind.Load, next)
            };

            Assert.True(SmokeLifecycleAssertions.HasDocumentEnded(events, old.DocumentId));
            Assert.False(SmokeLifecycleAssertions.HasDocumentEnded(events, next.DocumentId));
        }

        [Fact]
        public void AnotherDocumentEndingOnTheSameNativeFrameDoesNotRetireTheTarget()
        {
            var events = new[]
            {
                new DocumentLifecycleEventArgs(DocumentLifecycleKind.ContextCreated, Frame("target", "same")),
                new DocumentLifecycleEventArgs(DocumentLifecycleKind.ContextReleased, Frame("other", "same")),
                new DocumentLifecycleEventArgs(DocumentLifecycleKind.NavigationSuperseded, Frame("other", "same"))
            };
            Assert.False(SmokeLifecycleAssertions.HasDocumentEnded(events, "target"));
        }

        private static DocumentFrame Frame(string documentId, string path, bool mainFrame = false)
        {
            return new DocumentFrame("browser", documentId, "child-frame", new Uri("http://127.0.0.1/compat/frames/" + path),
                mainFrame, TimingGuarantee.BestEffortDocumentStart);
        }
    }
}
