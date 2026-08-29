using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    public sealed class ResourceContent
    {
        public ResourceContent(byte[] bytes, string mediaType = "application/octet-stream", string text = null)
        {
            Bytes = bytes != null ? (byte[])bytes.Clone() : throw new ArgumentNullException(nameof(bytes));
            MediaType = string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType;
            if (MediaType.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("The media type cannot contain line breaks.", nameof(mediaType));
            Text = text;
        }

        public byte[] Bytes { get; }
        public string MediaType { get; }
        public string Text { get; }
    }

    public interface IResourceProvider
    {
        Task<ResourceContent> GetAsync(
            UserScriptInstallation installation,
            ResourceDeclaration resource,
            CancellationToken cancellationToken);
    }

    public interface IUserScriptDependencyProvider
    {
        Task<string> GetScriptAsync(
            UserScriptInstallation installation,
            string url,
            CancellationToken cancellationToken);
    }

    public sealed class UserScriptHttpRequest
    {
        public UserScriptHttpRequest(
            string method,
            Uri url,
            IDictionary<string, string> headers,
            string body,
            TimeSpan? timeout,
            int maxResponseBytes,
            Func<Uri, bool> redirectAllowed)
        {
            if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("The HTTP method is required.", nameof(method));
            Method = method;
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Headers = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(headers ?? throw new ArgumentNullException(nameof(headers)), StringComparer.OrdinalIgnoreCase));
            Body = body;
            Timeout = timeout;
            if (maxResponseBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
            MaxResponseBytes = maxResponseBytes;
            RedirectAllowed = redirectAllowed ?? throw new ArgumentNullException(nameof(redirectAllowed));
        }

        public string Method { get; }
        public Uri Url { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public string Body { get; }
        public TimeSpan? Timeout { get; }
        public int MaxResponseBytes { get; }
        public Func<Uri, bool> RedirectAllowed { get; }
    }

    public sealed class UserScriptHttpResponse
    {
        public UserScriptHttpResponse(
            int status,
            string statusText,
            Uri finalUrl,
            IDictionary<string, string> headers,
            byte[] body,
            string responseText,
            IEnumerable<Uri> redirectUrls = null)
        {
            Status = status;
            StatusText = statusText ?? string.Empty;
            FinalUrl = finalUrl ?? throw new ArgumentNullException(nameof(finalUrl));
            Headers = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(headers ?? throw new ArgumentNullException(nameof(headers)), StringComparer.OrdinalIgnoreCase));
            Body = body != null ? (byte[])body.Clone() : new byte[0];
            ResponseText = responseText;
            RedirectUrls = new ReadOnlyCollection<Uri>((redirectUrls ?? Enumerable.Empty<Uri>()).ToList());
        }

        public int Status { get; }
        public string StatusText { get; }
        public Uri FinalUrl { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public byte[] Body { get; }
        public string ResponseText { get; }
        public IReadOnlyList<Uri> RedirectUrls { get; }
    }

    public sealed class UserScriptHttpProgress
    {
        public UserScriptHttpProgress(long loaded, long? total)
        {
            Loaded = loaded;
            Total = total;
        }

        public long Loaded { get; }
        public long? Total { get; }
    }

    public interface IHttpRequestService
    {
        Task<UserScriptHttpResponse> SendAsync(
            UserScriptHttpRequest request,
            IProgress<UserScriptHttpProgress> progress,
            CancellationToken cancellationToken);
    }

    public sealed class MenuCommandRequest
    {
        public MenuCommandRequest(ScriptKey scriptKey, string name, string accessKey)
        {
            ScriptKey = scriptKey;
            Name = !string.IsNullOrWhiteSpace(name)
                ? name
                : throw new ArgumentException("The menu command name is required.", nameof(name));
            AccessKey = accessKey;
        }

        public ScriptKey ScriptKey { get; }
        public string Name { get; }
        public string AccessKey { get; }
    }

    public interface IMenuRegistration : IDisposable
    {
    }

    public interface IMenuService
    {
        Task<IMenuRegistration> RegisterAsync(
            MenuCommandRequest request,
            Action invoked,
            CancellationToken cancellationToken);
    }

    public sealed class UserScriptNotificationRequest
    {
        public UserScriptNotificationRequest(ScriptKey scriptKey, string title, string text, string imageUrl)
        {
            ScriptKey = scriptKey;
            Title = title;
            Text = text;
            ImageUrl = imageUrl;
        }

        public ScriptKey ScriptKey { get; }
        public string Title { get; }
        public string Text { get; }
        public string ImageUrl { get; }
    }

    public interface INotificationService
    {
        Task ShowAsync(UserScriptNotificationRequest request, CancellationToken cancellationToken);
    }

    public interface IClipboardService
    {
        Task SetTextAsync(string text, string mediaType, CancellationToken cancellationToken);
    }

    public sealed class OpenTabRequest
    {
        public OpenTabRequest(Uri url, bool active, bool insert, bool setParent)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Active = active;
            Insert = insert;
            SetParent = setParent;
        }

        public Uri Url { get; }
        public bool Active { get; }
        public bool Insert { get; }
        public bool SetParent { get; }
    }

    public sealed class OpenTabResult
    {
        public OpenTabResult(string tabId)
        {
            TabId = tabId;
        }

        public string TabId { get; }
    }

    public interface ITabService
    {
        Task<OpenTabResult> OpenAsync(OpenTabRequest request, CancellationToken cancellationToken);
    }

    public sealed class DownloadRequest
    {
        public DownloadRequest(ScriptKey scriptKey, Uri url, string name, bool saveAs)
        {
            ScriptKey = scriptKey;
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Name = name;
            SaveAs = saveAs;
        }

        public ScriptKey ScriptKey { get; }
        public Uri Url { get; }
        public string Name { get; }
        public bool SaveAs { get; }
    }

    public sealed class DownloadResult
    {
        public DownloadResult(string downloadId)
        {
            DownloadId = downloadId;
        }

        public string DownloadId { get; }
    }

    public interface IDownloadService
    {
        Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken);
    }

    public interface ITabStateService
    {
        Task<string> GetAsync(ScriptKey scriptKey, DocumentFrame frame, CancellationToken cancellationToken);
        Task SaveAsync(ScriptKey scriptKey, DocumentFrame frame, string jsonValue, CancellationToken cancellationToken);
        Task<IReadOnlyDictionary<string, string>> GetAllAsync(ScriptKey scriptKey, CancellationToken cancellationToken);
    }
}
