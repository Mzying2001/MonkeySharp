using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class WpfClipboardService : IClipboardService
    {
        private readonly Dispatcher _dispatcher;

        public WpfClipboardService(Dispatcher dispatcher) { _dispatcher = dispatcher; }

        public async Task SetTextAsync(string text, string mediaType, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var data = new DataObject();
                        data.SetText(text ?? string.Empty, TextDataFormat.UnicodeText);
                        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                            data.SetData(DataFormats.Html, HtmlClipboard(text ?? string.Empty));
                        Clipboard.SetDataObject(data, true);
                    });
                    return;
                }
                catch (ExternalException) when (attempt < 4) { await Task.Delay(60, cancellationToken).ConfigureAwait(false); }
            }
        }

        private static string HtmlClipboard(string text)
        {
            const string header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string prefix = "<html><body><!--StartFragment-->";
            const string suffix = "<!--EndFragment--></body></html>";
            var startHtml = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
            var startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
            var endFragment = startFragment + Encoding.UTF8.GetByteCount(text);
            return string.Format(header, startHtml, endFragment + Encoding.UTF8.GetByteCount(suffix), startFragment, endFragment) + prefix + text + suffix;
        }
    }
}
