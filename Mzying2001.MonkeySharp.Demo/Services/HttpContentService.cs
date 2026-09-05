using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Demo.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class HttpContentService : IResourceProvider, IUserScriptDependencyProvider, IDisposable
    {
        private readonly HttpClient _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        private readonly string _directory;
        private readonly SemaphoreSlim _cacheGate = new SemaphoreSlim(1, 1);
        public HttpContentService(AppDataPaths paths) { _directory = paths.DependenciesDirectory; }

        public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken cancellationToken)
            => CachedAsync(installation, resource.Url, cancellationToken);
        public async Task<string> GetScriptAsync(UserScriptInstallation installation, string url, CancellationToken cancellationToken)
            => (await CachedAsync(installation, url, cancellationToken).ConfigureAwait(false)).Text;

        public async Task<ResourceContent> FetchAsync(string url, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (var response = await GetResponseAsync(_client, RequireHttp(url), timeout.Token).ConfigureAwait(false))
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new MemoryStream())
                {
                    if (response.Content.Headers.ContentLength > SqliteUserScriptRepositoryPersistence.MaximumSourceBytes)
                        throw new IOException("Content exceeds the 10 MiB limit.");
                    var buffer = new byte[65536];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        if (output.Length + count > SqliteUserScriptRepositoryPersistence.MaximumSourceBytes)
                            throw new IOException("Content exceeds the 10 MiB limit.");
                        output.Write(buffer, 0, count);
                    }
                    var bytes = output.ToArray();
                    var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
                    var encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
                    return new ResourceContent(bytes, response.Content.Headers.ContentType?.MediaType,
                        encoding.GetString(bytes).TrimStart('\uFEFF'));
                }
            }
        }

        private async Task<ResourceContent> CachedAsync(UserScriptInstallation installation, string url, CancellationToken cancellationToken)
        {
            RequireHttp(url);
            var cacheKey = installation.ScriptKey + ":" + installation.UpdatedAt.ToString("O") + ":" + url;
            var path = Path.Combine(_directory, SqliteUserScriptRepositoryPersistence.Hash(Encoding.UTF8.GetBytes(cacheKey)) + ".json");
            await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
                        {
                            var root = document.RootElement;
                            return new ResourceContent(Convert.FromBase64String(root.GetProperty("bytes").GetString()),
                                root.GetProperty("mime").GetString(), root.GetProperty("text").GetString());
                        }
                    }
                    catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is KeyNotFoundException)
                    { File.Delete(path); }
                }
                var content = await FetchAsync(url, cancellationToken).ConfigureAwait(false);
                var temporary = path + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(new
                    { bytes = Convert.ToBase64String(content.Bytes), mime = content.MediaType, text = content.Text }), new UTF8Encoding(false));
                    File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return content;
            }
            finally { _cacheGate.Release(); }
        }

        internal static Uri RequireHttp(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Only HTTP(S) URLs without embedded credentials are supported.");
            return uri;
        }

        internal static async Task<HttpResponseMessage> GetResponseAsync(HttpClient client, Uri uri, CancellationToken token)
        {
            for (var redirects = 0; redirects <= 10; redirects++)
            {
                var response = await client.GetAsync(RequireHttp(uri.AbsoluteUri), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
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
            throw new HttpRequestException("Too many redirects.");
        }
        public void Dispose() { _client.Dispose(); }
    }
}
