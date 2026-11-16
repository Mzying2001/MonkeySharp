using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Updates;
using Mzying2001.MonkeySharp.Demo.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class HttpContentService : IResourceProvider, IUserScriptDependencyProvider, IUserScriptUpdateFetcher, IDisposable
    {
        private readonly HttpClient _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate })
        { Timeout = Timeout.InfiniteTimeSpan };
        public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken cancellationToken)
            => FetchAsync(resource.Url, cancellationToken);
        public Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration dependency, CancellationToken cancellationToken)
            => FetchAsync(dependency.Url, cancellationToken);

        public async Task<ResourceContent> FetchAsync(string url, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (var response = await GetResponseAsync(_client, RequireHttp(url), timeout.Token).ConfigureAwait(false))
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new MemoryStream())
                {
                    if (response.Content.Headers.ContentLength > UserScriptSourceFile.MaximumBytes)
                        throw new IOException("Content exceeds the 10 MiB limit.");
                    var buffer = new byte[65536];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        if (output.Length + count > UserScriptSourceFile.MaximumBytes)
                            throw new IOException("Content exceeds the 10 MiB limit.");
                        output.Write(buffer, 0, count);
                    }
                    var bytes = output.ToArray();
                    var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
                    return new ResourceContent(bytes, response.Content.Headers.ContentType?.MediaType,
                        charset);
                }
            }
        }

        public async Task<string> FetchSourceAsync(
            UserScriptInstallation installation,
            string url,
            CancellationToken cancellationToken)
        {
            var content = await FetchAsync(url, cancellationToken).ConfigureAwait(false);
            return new ResourceContent(content.Bytes, charset: "utf-8").DecodeText(strict: true);
        }

        internal static Uri RequireHttp(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Only HTTP(S) URLs without embedded credentials are supported.");
            return uri;
        }

        internal static Task<HttpResponseMessage> GetResponseAsync(HttpClient client, Uri uri, CancellationToken token)
            => GetResponseAsync(client, uri, null, token);

        internal static async Task<HttpResponseMessage> GetResponseAsync(
            HttpClient client,
            Uri uri,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken token)
        {
            for (var redirects = 0; redirects <= 10; redirects++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, RequireHttp(uri.AbsoluteUri)))
                {
                    if (headers != null)
                        foreach (var header in headers)
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        using (response)
                        {
                            if (response.Headers.Location == null) throw new HttpRequestException("Redirect has no Location header.");
                            uri = new Uri(uri, response.Headers.Location);
                        }
                        continue;
                    }
                    try { response.EnsureSuccessStatusCode(); return response; }
                    catch { response.Dispose(); throw; }
                }
            }
            throw new HttpRequestException("Too many redirects.");
        }

        public void Dispose() { _client.Dispose(); }
    }
}
