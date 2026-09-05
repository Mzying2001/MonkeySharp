using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal sealed class LocalHttpFixture : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly ConcurrentBag<TcpClient> _clients = new ConcurrentBag<TcpClient>();
        private int _slowRequests;
        public LocalHttpFixture()
        {
            _listener.Start();
            BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }
        public string BaseUrl { get; }
        public int SlowRequests => Volatile.Read(ref _slowRequests);
        private async Task AcceptAsync()
        {
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _clients.Add(client); _ = ServeAsync(client);
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) when (_cancellation.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
                {
                    var request = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (request == null) return;
                    var path = request.Split(' ')[1].Split('?')[0];
                    var headers = new StringBuilder();
                    string line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false))) headers.AppendLine(line);
                    string body;
                    var status = "200 OK";
                    var extra = string.Empty;
                    var mime = "text/plain; charset=utf-8";
                    switch (path)
                    {
                        case "/page":
                            body = "<!doctype html><html><head><title>MonkeySharp E2E</title></head><body><h1>Local userscript fixture</h1><iframe src='/frame'></iframe></body></html>";
                            mime = "text/html; charset=utf-8"; break;
                        case "/frame": body = "<!doctype html><html><body>Child frame</body></html>"; mime = "text/html"; break;
                        case "/dependency.js": body = "window.__demoDependency = true;"; mime = "application/javascript"; break;
                        case "/resource": body = "demo-resource"; break;
                        case "/headers": case "/cookies": body = headers.ToString(); break;
                        case "/redirect": body = "redirect"; status = "302 Found"; extra = "Location: /resource\r\n"; break;
                        case "/bad-redirect": body = "redirect"; status = "302 Found"; extra = "Location: file:///C:/Windows/win.ini\r\n"; break;
                        case "/oversize": body = string.Empty; break;
                        case "/download": case "/slow": body = new string('d', 256 * 1024); break;
                        default: body = "fixture"; break;
                    }
                    if (path == "/slow") Interlocked.Increment(ref _slowRequests);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var length = path == "/oversize" ? 12 * 1024 * 1024 : bytes.Length;
                    var responseHeaders = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + mime + "\r\nContent-Length: " + length + "\r\n" + extra + "Connection: close\r\n\r\n");
                    await stream.WriteAsync(responseHeaders, 0, responseHeaders.Length, _cancellation.Token).ConfigureAwait(false);
                    for (var offset = 0; offset < bytes.Length; offset += 8192)
                    {
                        if (path == "/slow") await Task.Delay(80, _cancellation.Token).ConfigureAwait(false);
                        if (path == "/download") await Task.Delay(10, _cancellation.Token).ConfigureAwait(false);
                        await stream.WriteAsync(bytes, offset, Math.Min(8192, bytes.Length - offset), _cancellation.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is OperationCanceledException || exception is ObjectDisposedException || exception is SocketException) { }
        }
        public void Dispose()
        {
            _cancellation.Cancel(); _listener.Stop();
            foreach (var client in _clients) client.Dispose();
        }
    }
}
