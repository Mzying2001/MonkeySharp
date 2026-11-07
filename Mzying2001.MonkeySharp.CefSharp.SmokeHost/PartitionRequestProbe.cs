using CefSharp;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    // Test the public native request field directly, without synthesizing Cookie headers.
    internal sealed class PartitionRequestProbe : IUrlRequestClient
    {
        private readonly TaskCompletionSource<string> _completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MemoryStream _body = new MemoryStream();
        private IRequest _request;
        private IUrlRequest _operation;
        private bool _selectorAvailable;
        internal sealed class Result
        {
            public string Text;
            public bool SelectorAvailable;
        }
        internal static async Task<Result> SendAsync(IRequestContext context, Uri url, string topLevelSite)
        {
            var probe = new PartitionRequestProbe();
            await Cef.UIThreadTaskFactory.StartNew(() =>
            {
#if CEF_SHARP_LEGACY_REQUEST_FACTORY
                probe._request = new Request();
#else
                probe._request = Request.Create();
#endif
                probe._request.Url = url.AbsoluteUri;
                probe._request.Method = "GET";
                probe._request.Flags = UrlRequestFlags.AllowStoredCredentials;
                var selector = probe._request.GetType().GetProperty("FirstPartyForCookies") ?? probe._request.GetType().GetProperty("SiteForCookies");
                probe._selectorAvailable = selector != null && selector.CanWrite && selector.PropertyType == typeof(string);
                if (probe._selectorAvailable) selector.SetValue(probe._request, topLevelSite, null);
                probe._operation = new UrlRequest(probe._request, probe, context);
            });
            try
            {
                if (await Task.WhenAny(probe._completion.Task, Task.Delay(8000)) != probe._completion.Task) throw new TimeoutException("Native partition probe timed out.");
                return new Result { Text = await probe._completion.Task, SelectorAvailable = probe._selectorAvailable };
            }
            finally
            {
                await Cef.UIThreadTaskFactory.StartNew(() => { probe._operation?.Dispose(); probe._request?.Dispose(); });
                probe._body.Dispose();
            }
        }
        public bool GetAuthCredentials(bool isProxy, string host, int port, string realm, string scheme, IAuthCallback callback) => false;
        public void OnUploadProgress(IUrlRequest request, long current, long total) { }
        public void OnDownloadProgress(IUrlRequest request, long current, long total) { }
        public void OnDownloadData(IUrlRequest request, Stream data) => data.CopyTo(_body);
        public void OnRequestComplete(IUrlRequest request)
        {
            if (request.RequestStatus == UrlRequestStatus.Success) _completion.TrySetResult(Encoding.UTF8.GetString(_body.ToArray()));
            else _completion.TrySetException(new IOException("Native partition probe failed: " + request.RequestStatus));
        }
    }
}
