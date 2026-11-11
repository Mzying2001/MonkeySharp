using Mzying2001.MonkeySharp.CefSharp.SmokeHost;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class SmokeLifecycleAssertionsTests
    {
        [Theory]
        [InlineData(DocumentLifecycleKind.ContextReleased)]
        [InlineData(DocumentLifecycleKind.NavigationSuperseded)]
        public void ChildNavigationAcceptsEitherOldDocumentTermination(DocumentLifecycleKind terminal)
        {
            // Chromium 84 can report supersession before the native release callback.
            var old = Frame("old", "same");
            var next = Frame("next", "same-next");
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

        private static DocumentFrame Frame(string documentId, string path)
        {
            return new DocumentFrame("browser", documentId, "child-frame", new Uri("http://127.0.0.1/compat/frames/" + path),
                false, TimingGuarantee.BestEffortDocumentStart);
        }
    }
}
