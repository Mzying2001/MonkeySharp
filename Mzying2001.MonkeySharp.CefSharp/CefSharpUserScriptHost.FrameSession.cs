using CefSharp;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    public sealed partial class CefSharpUserScriptHost
    {
        private sealed class FrameSession : IDisposable
        {
            private bool _disposed;
            public FrameSession(IFrame cefFrame, DocumentFrame frame, string lifecycleToken)
            {
                CefFrame = cefFrame;
                Frame = frame;
                LifecycleToken = lifecycleToken;
                Token = Cancellation.Token;
            }

            public long AttachmentGeneration { get; set; }
            public long ContextGeneration { get; set; }
            public bool HasDocumentMarker { get; set; }
            public IFrame CefFrame { get; }
            public DocumentFrame Frame { get; private set; }
            public string LifecycleToken { get; }
            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();
            public CancellationToken Token { get; }
            public object LifetimeSync { get; } = new object();
            public Task ReleaseTask { get; set; }
            public Task PredecessorsReleased { get; set; } = Task.CompletedTask;
            public Task Initialization { get; set; } = Task.CompletedTask;
            public SemaphoreSlim OperationGate { get; } = new SemaphoreSlim(1, 1);
            public int CompletedRank { get; set; }
            public bool Initialized { get; set; }

            public void UpdateUrl(Uri url)
            {
                Frame = new DocumentFrame(
                    Frame.BrowserSessionId,
                    Frame.DocumentId,
                    Frame.FrameId,
                    url,
                    Frame.IsMainFrame,
                    Frame.TimingGuarantee,
                    Frame.BridgeIntegrity);
            }

            public void Cancel()
            {
                lock (LifetimeSync)
                    if (!_disposed) Cancellation.Cancel();
            }

            public void Dispose()
            {
                lock (LifetimeSync)
                {
                    if (_disposed) return;
                    _disposed = true;
                    Cancellation.Dispose();
                    OperationGate.Dispose();
                }
            }
        }
    }
}
