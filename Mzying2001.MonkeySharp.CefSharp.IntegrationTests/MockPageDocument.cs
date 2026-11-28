using CefSharp;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    // Host tests with no renderer can exercise lifecycle and bridge behavior
    // without simulating CDP. Native context routing has its own tests.
    internal sealed class MockPageDocument : CefSharpPageDocument
    {
        private readonly DocumentFrame _document;
        private readonly IFrame _frame;
        private readonly IWebBrowser _browser;

        internal MockPageDocument(DocumentFrame document, IFrame frame, IWebBrowser browser)
        {
            _document = document;
            _frame = frame;
            _browser = browser;
        }

        internal static void Use(CefSharpUserScriptHost host)
        {
            host.PageDocumentFactory = (document, frame, browser) => new MockPageDocument(document, frame, browser);
        }

        internal override Uri Url => _document.Url;
        internal override CancellationToken Revoked => CancellationToken.None;

        internal override Task ExecuteAsync(string source, string sourceUrl, CancellationToken token)
        {
            using (var refreshed = CefSharpUserScriptHost.GetFrameByIdentifier(_browser.GetBrowser(), _document.FrameId))
            {
                if (refreshed == null && sourceUrl == "monkeysharp://end-document") return Task.CompletedTask;
                var target = refreshed ?? _frame;
                try
                {
                    if (target.IsDisposed || !target.IsValid) throw new OperationCanceledException(token);
                    target.ExecuteJavaScriptAsync(source, sourceUrl, 1);
                }
                catch (ObjectDisposedException) when (target.IsDisposed) { throw new OperationCanceledException(token); }
            }
            return Task.CompletedTask;
        }
    }
}
