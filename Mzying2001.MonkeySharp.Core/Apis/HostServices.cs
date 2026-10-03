using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Controls how a userscript HTTP request handles redirects.</summary>
    public enum UserScriptHttpRedirectMode
    {
        /// <summary>Follows redirects after authorization.</summary>
        Follow,

        /// <summary>Fails when a redirect response is received.</summary>
        Error,

        /// <summary>Returns the redirect response without following it.</summary>
        Manual
    }

    /// <summary>Identifies the JavaScript representation requested for an HTTP response body.</summary>
    public enum UserScriptHttpResponseType
    {
        /// <summary>Decodes the response as text.</summary>
        Text,

        /// <summary>Decodes and parses the response as JSON.</summary>
        Json,

        /// <summary>Returns an ArrayBuffer.</summary>
        ArrayBuffer,

        /// <summary>Returns a Blob.</summary>
        Blob,

        /// <summary>Returns a ReadableStream backed by host response chunks.</summary>
        Stream
    }

    /// <summary>
    /// Contains the binary and optional decoded text returned for a declared userscript resource.
    /// </summary>
    public sealed class ResourceContent
    {
        /// <summary>Initializes resource content.</summary>
        /// <param name="bytes">The resource bytes.</param>
        /// <param name="mediaType">The media type used for generated data URLs.</param>
        /// <param name="text">An optional host-decoded text representation.</param>
        public ResourceContent(byte[] bytes, string mediaType = "application/octet-stream", string text = null)
        {
            Bytes = bytes != null ? (byte[])bytes.Clone() : throw new ArgumentNullException(nameof(bytes));
            MediaType = string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType;
            if (MediaType.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("The media type cannot contain line breaks.", nameof(mediaType));
            Text = text;
        }

        /// <summary>Gets a copy of the resource bytes supplied at construction.</summary>
        public byte[] Bytes { get; }

        /// <summary>Gets the resource media type.</summary>
        public string MediaType { get; }

        /// <summary>Gets the host-decoded text representation, if available.</summary>
        public string Text { get; }
    }

    /// <summary>
    /// Loads named resources declared in userscript metadata.
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>Loads a declared userscript resource.</summary>
        /// <param name="installation">The userscript requesting the resource.</param>
        /// <param name="resource">The declared resource to load.</param>
        /// <param name="cancellationToken">A token that cancels resource loading.</param>
        /// <returns>The resource content.</returns>
        Task<ResourceContent> GetAsync(
            UserScriptInstallation installation,
            ResourceDeclaration resource,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Loads JavaScript dependencies declared with <c>@require</c>.
    /// </summary>
    public interface IUserScriptDependencyProvider
    {
        /// <summary>Loads one dependency as JavaScript source text.</summary>
        /// <param name="installation">The userscript requesting the dependency.</param>
        /// <param name="url">The dependency URL from metadata.</param>
        /// <param name="cancellationToken">A token that cancels dependency loading.</param>
        /// <returns>The dependency source text.</returns>
        Task<string> GetScriptAsync(
            UserScriptInstallation installation,
            string url,
            CancellationToken cancellationToken);
    }

    /// <summary>Provides repeatable access to a userscript HTTP request body.</summary>
    public interface IUserScriptHttpBody
    {
        /// <summary>Gets the body length in bytes.</summary>
        long Length { get; }

        /// <summary>Opens the body for reading from its beginning.</summary>
        Stream OpenRead();
    }

    /// <summary>Contains optional Tampermonkey-compatible HTTP request controls.</summary>
    public sealed class UserScriptHttpRequestOptions
    {
        /// <summary>Gets or sets cookies appended to the outgoing Cookie header.</summary>
        public string Cookie { get; set; }

        /// <summary>Gets or sets the user name supplied to an HTTP authentication challenge.</summary>
        public string Username { get; set; }

        /// <summary>Gets or sets the password supplied to an HTTP authentication challenge.</summary>
        public string Password { get; set; }

        /// <summary>Gets or sets whether stored cookies and credentials are disabled.</summary>
        public bool Anonymous { get; set; }

        /// <summary>Gets or sets the MIME type used to interpret the response.</summary>
        public string OverrideMimeType { get; set; }

        /// <summary>Gets or sets redirect handling.</summary>
        public UserScriptHttpRedirectMode Redirect { get; set; }

        /// <summary>Gets or sets whether the request bypasses cached content.</summary>
        public bool NoCache { get; set; }

        /// <summary>Gets or sets whether cached content must be revalidated.</summary>
        public bool Revalidate { get; set; }

        /// <summary>Gets or sets whether Tampermonkey fetch restrictions apply.</summary>
        public bool Fetch { get; set; }

        /// <summary>Gets or sets the requested response representation.</summary>
        public UserScriptHttpResponseType ResponseType { get; set; }
    }

    /// <summary>Describes a validated HTTP request initiated by a userscript.</summary>
    public sealed class UserScriptHttpRequest
    {
        /// <summary>Initializes a userscript HTTP request.</summary>
        public UserScriptHttpRequest(
            string method,
            Uri url,
            IDictionary<string, string> headers,
            IUserScriptHttpBody body,
            TimeSpan? timeout,
            long? maxResponseBytes,
            Func<Uri, CancellationToken, Task<bool>> redirectAllowed,
            UserScriptHttpRequestOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("The HTTP method is required.", nameof(method));
            if (maxResponseBytes.HasValue && maxResponseBytes.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
            Method = method;
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Headers = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(headers ?? throw new ArgumentNullException(nameof(headers)), StringComparer.OrdinalIgnoreCase));
            Body = body;
            Timeout = timeout;
            MaxResponseBytes = maxResponseBytes;
            RedirectAllowed = redirectAllowed ?? throw new ArgumentNullException(nameof(redirectAllowed));
            Options = options ?? new UserScriptHttpRequestOptions();
        }

        /// <summary>Gets the HTTP method.</summary>
        public string Method { get; }

        /// <summary>Gets the absolute request URL.</summary>
        public Uri Url { get; }

        /// <summary>Gets the case-insensitive request headers.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Gets the optional binary request body.</summary>
        public IUserScriptHttpBody Body { get; }

        /// <summary>Gets the optional host request timeout.</summary>
        public TimeSpan? Timeout { get; }

        /// <summary>Gets the optional response body limit. A null value means unlimited.</summary>
        public long? MaxResponseBytes { get; }

        /// <summary>Gets the callback that must authorize every redirect target before it is followed.</summary>
        public Func<Uri, CancellationToken, Task<bool>> RedirectAllowed { get; }

        /// <summary>Gets optional request controls.</summary>
        public UserScriptHttpRequestOptions Options { get; }
    }

    /// <summary>Contains HTTP response metadata returned by a userscript host service.</summary>
    public sealed class UserScriptHttpResponse
    {
        /// <summary>Initializes userscript HTTP response metadata.</summary>
        public UserScriptHttpResponse(
            int status,
            string statusText,
            Uri finalUrl,
            IDictionary<string, string> headers,
            string rawHeaders,
            string mimeType,
            string charset,
            IEnumerable<Uri> redirectUrls = null)
        {
            Status = status;
            StatusText = statusText ?? string.Empty;
            FinalUrl = finalUrl ?? throw new ArgumentNullException(nameof(finalUrl));
            Headers = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(headers ?? throw new ArgumentNullException(nameof(headers)), StringComparer.OrdinalIgnoreCase));
            RawHeaders = rawHeaders ?? string.Empty;
            MimeType = mimeType;
            Charset = charset;
            RedirectUrls = new ReadOnlyCollection<Uri>((redirectUrls ?? Enumerable.Empty<Uri>()).ToList());
        }

        /// <summary>Gets the numeric HTTP status code.</summary>
        public int Status { get; }

        /// <summary>Gets the HTTP reason phrase.</summary>
        public string StatusText { get; }

        /// <summary>Gets the final response URL after redirects.</summary>
        public Uri FinalUrl { get; }

        /// <summary>Gets the case-insensitive response headers.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Gets the raw CRLF-separated response headers.</summary>
        public string RawHeaders { get; }

        /// <summary>Gets the response MIME type, if reported.</summary>
        public string MimeType { get; }

        /// <summary>Gets the response character set, if reported.</summary>
        public string Charset { get; }

        /// <summary>Gets the redirect targets followed in order.</summary>
        public IReadOnlyList<Uri> RedirectUrls { get; }
    }

    /// <summary>Receives HTTP lifecycle events before request execution starts.</summary>
    public interface IUserScriptHttpObserver
    {
        /// <summary>Reports response metadata before response body data.</summary>
        void OnResponseStarted(UserScriptHttpResponse response);

        /// <summary>Reports request body upload progress.</summary>
        void OnUploadProgress(long loaded, long? total);

        /// <summary>Reports response body download progress.</summary>
        void OnDownloadProgress(long loaded, long? total);

        /// <summary>Reports one response body data segment.</summary>
        void OnResponseData(byte[] buffer, int offset, int count);
    }

    /// <summary>Sends validated HTTP requests on behalf of userscripts.</summary>
    public interface IHttpRequestService
    {
        /// <summary>Starts an HTTP request with its observer attached before any events can occur.</summary>
        IHttpRequestOperation SendAsync(
            UserScriptHttpRequest request,
            IUserScriptHttpObserver observer,
            CancellationToken cancellationToken);
    }

    /// <summary>Represents a running userscript HTTP request.</summary>
    public interface IHttpRequestOperation
    {
        /// <summary>Gets the task completed with final response metadata.</summary>
        Task<UserScriptHttpResponse> Completion { get; }

        /// <summary>Aborts the request.</summary>
        void Abort();
    }

    /// <summary>
    /// Describes a userscript menu command to register with the host.
    /// </summary>
    public sealed class MenuCommandRequest
    {
        /// <summary>Initializes a menu command request.</summary>
        /// <param name="scriptKey">The script installation that owns the command.</param>
        /// <param name="name">The command label.</param>
        /// <param name="accessKey">The optional keyboard access key.</param>
        public MenuCommandRequest(ScriptKey scriptKey, string name, string accessKey)
        {
            ScriptKey = scriptKey;
            Name = !string.IsNullOrWhiteSpace(name)
                ? name
                : throw new ArgumentException("The menu command name is required.", nameof(name));
            AccessKey = accessKey;
        }

        /// <summary>Gets the script installation that owns the command.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the command label.</summary>
        public string Name { get; }

        /// <summary>Gets the optional keyboard access key.</summary>
        public string AccessKey { get; }
    }

    /// <summary>
    /// Represents the lifetime of a registered userscript menu command.
    /// </summary>
    public interface IMenuRegistration : IDisposable
    {
    }

    /// <summary>
    /// Registers userscript commands in a host-provided menu.
    /// </summary>
    public interface IMenuService
    {
        /// <summary>Registers a menu command and its invocation callback.</summary>
        /// <param name="request">The command to register.</param>
        /// <param name="invoked">The callback to invoke when the user selects the command.</param>
        /// <param name="cancellationToken">A token that cancels registration.</param>
        /// <returns>A registration whose disposal removes the command.</returns>
        Task<IMenuRegistration> RegisterAsync(
            MenuCommandRequest request,
            Action invoked,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Describes a notification requested by a userscript.
    /// </summary>
    public sealed class UserScriptNotificationRequest
    {
        /// <summary>Initializes a userscript notification request.</summary>
        /// <param name="scriptKey">The requesting script installation.</param>
        /// <param name="title">The notification title.</param>
        /// <param name="text">The notification body text.</param>
        /// <param name="imageUrl">The optional notification image URL.</param>
        public UserScriptNotificationRequest(ScriptKey scriptKey, string title, string text, string imageUrl)
        {
            ScriptKey = scriptKey;
            Title = title;
            Text = text;
            ImageUrl = imageUrl;
        }

        /// <summary>Gets the requesting script installation.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the notification title.</summary>
        public string Title { get; }

        /// <summary>Gets the notification body text.</summary>
        public string Text { get; }

        /// <summary>Gets the optional notification image URL.</summary>
        public string ImageUrl { get; }
    }

    /// <summary>
    /// Displays host-native notifications requested by userscripts.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>Displays a notification and returns its lifecycle handle.</summary>
        /// <param name="request">The notification to display.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A handle that reports click, close, and completion state.</returns>
        INotificationHandle ShowAsync(UserScriptNotificationRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Represents a host notification and its lifecycle.</summary>
    public interface INotificationHandle : IDisposable
    {
        /// <summary>Completes when the notification is closed or otherwise finished.</summary>
        Task Completion { get; }

        /// <summary>Occurs when the notification is clicked.</summary>
        event EventHandler Clicked;

        /// <summary>Occurs when the notification is closed.</summary>
        event EventHandler Closed;
    }

    /// <summary>
    /// Writes userscript-provided text to the host clipboard.
    /// </summary>
    public interface IClipboardService
    {
        /// <summary>Writes text to the clipboard.</summary>
        /// <param name="text">The text to write.</param>
        /// <param name="mediaType">The media type describing the text.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A task that completes after the clipboard is updated.</returns>
        Task SetTextAsync(string text, string mediaType, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Describes a request to open a browser tab.
    /// </summary>
    public sealed class OpenTabRequest
    {
        /// <summary>Initializes an open-tab request.</summary>
        /// <param name="url">The absolute HTTP or HTTPS URL to open.</param>
        /// <param name="active">Whether the new tab should become active.</param>
        /// <param name="insert">Whether to insert the tab next to the current tab.</param>
        /// <param name="setParent">Whether to associate the current tab as the parent.</param>
        public OpenTabRequest(Uri url, bool active, bool insert, bool setParent)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Active = active;
            Insert = insert;
            SetParent = setParent;
        }

        /// <summary>Gets the absolute URL to open.</summary>
        public Uri Url { get; }

        /// <summary>Gets whether the new tab should become active.</summary>
        public bool Active { get; }

        /// <summary>Gets whether to insert the tab next to the current tab.</summary>
        public bool Insert { get; }

        /// <summary>Gets whether to associate the current tab as the parent.</summary>
        public bool SetParent { get; }
    }

    /// <summary>
    /// Identifies a browser tab opened by the host.
    /// </summary>
    public sealed class OpenTabResult
    {
        /// <summary>Initializes an open-tab result.</summary>
        /// <param name="tabId">The host-defined tab identifier.</param>
        public OpenTabResult(string tabId)
        {
            TabId = tabId;
        }

        /// <summary>Gets the host-defined tab identifier.</summary>
        public string TabId { get; }
    }

    /// <summary>
    /// Opens browser tabs on behalf of userscripts.
    /// </summary>
    public interface ITabService
    {
        /// <summary>Opens a browser tab and returns a lifecycle handle.</summary>
        /// <param name="request">The tab request.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The opened tab handle.</returns>
        Task<ITabHandle> OpenAsync(OpenTabRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Controls the current browser tab for grant-gated window APIs.</summary>
    public interface IUserScriptWindowService
    {
        /// <summary>Closes the current tab unless it is the last available tab.</summary>
        Task<bool> CloseAsync(DocumentFrame frame, CancellationToken cancellationToken);

        /// <summary>Activates the current tab.</summary>
        Task<bool> FocusAsync(DocumentFrame frame, CancellationToken cancellationToken);
    }

    /// <summary>Represents a browser tab opened for a userscript.</summary>
    public interface ITabHandle : IDisposable
    {
        /// <summary>Gets the host-defined tab identifier.</summary>
        string TabId { get; }

        /// <summary>Gets whether the tab has closed.</summary>
        bool Closed { get; }

        /// <summary>Occurs when the tab closes.</summary>
        event EventHandler OnClose;

        /// <summary>Closes the tab.</summary>
        Task CloseAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Describes a file download requested by a userscript.
    /// </summary>
    public sealed class DownloadRequest
    {
        /// <summary>Initializes a download request.</summary>
        /// <param name="scriptKey">The requesting script installation.</param>
        /// <param name="url">The absolute HTTP or HTTPS download URL.</param>
        /// <param name="name">The optional suggested file name.</param>
        /// <param name="saveAs">Whether the host should prompt for a destination.</param>
        /// <param name="headers">Optional HTTP request headers.</param>
        /// <param name="conflictAction">The conflict policy: uniquify, overwrite, or prompt.</param>
        /// <param name="timeout">An optional download timeout.</param>
        public DownloadRequest(ScriptKey scriptKey, Uri url, string name, bool saveAs,
            IDictionary<string, string> headers = null, string conflictAction = "uniquify", TimeSpan? timeout = null)
        {
            ScriptKey = scriptKey;
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Name = name;
            SaveAs = saveAs;
            Headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(
                headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
            ConflictAction = string.IsNullOrEmpty(conflictAction) ? "uniquify" : conflictAction;
            Timeout = timeout;
        }

        /// <summary>Gets the requesting script installation.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the absolute download URL.</summary>
        public Uri Url { get; }

        /// <summary>Gets the optional suggested file name.</summary>
        public string Name { get; }

        /// <summary>Gets whether the host should prompt for a destination.</summary>
        public bool SaveAs { get; }
        /// <summary>Gets request headers supplied to the download.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>Gets the host conflict policy: uniquify, overwrite, or prompt.</summary>
        public string ConflictAction { get; }
        /// <summary>Gets the optional operation timeout.</summary>
        public TimeSpan? Timeout { get; }
    }

    /// <summary>
    /// Identifies a download started by the host.
    /// </summary>
    public sealed class DownloadResult
    {
        /// <summary>Initializes a download result.</summary>
        /// <param name="downloadId">The host-defined download identifier.</param>
        public DownloadResult(string downloadId)
        {
            DownloadId = downloadId;
        }

        /// <summary>Gets the host-defined download identifier.</summary>
        public string DownloadId { get; }
    }

    /// <summary>
    /// Starts file downloads on behalf of userscripts.
    /// </summary>
    public interface IDownloadService
    {
        /// <summary>Starts a file download and returns its lifecycle operation.</summary>
        /// <param name="request">The download request.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The running download operation.</returns>
        Task<IDownloadOperation> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Represents a running userscript download.</summary>
    public interface IDownloadOperation : IDisposable
    {
        /// <summary>Gets the host-defined download identifier.</summary>
        string DownloadId { get; }

        /// <summary>Gets a task completed when the download reaches a terminal state.</summary>
        Task Completion { get; }

        /// <summary>Occurs as bytes are downloaded.</summary>
        event EventHandler<UserScriptDownloadProgress> Progress;

        /// <summary>Occurs when the download completes successfully.</summary>
        event EventHandler Completed;

        /// <summary>Occurs when the download fails.</summary>
        event EventHandler<UserScriptDownloadFailure> Failed;

        /// <summary>Occurs when the download is aborted.</summary>
        event EventHandler Aborted;

        /// <summary>Occurs when the download reaches its configured timeout.</summary>
        event EventHandler TimedOut;

        /// <summary>Aborts the download.</summary>
        void Abort();
    }

    /// <summary>Reports userscript download progress.</summary>
    public sealed class UserScriptDownloadProgress : EventArgs
    {
        /// <summary>Initializes a download progress update.</summary>
        public UserScriptDownloadProgress(long loaded, long? total)
        {
            Loaded = loaded;
            Total = total;
        }

        /// <summary>Gets the number of downloaded bytes.</summary>
        public long Loaded { get; }
        /// <summary>Gets the expected total bytes, if known.</summary>
        public long? Total { get; }
    }

    /// <summary>Describes a userscript download failure.</summary>
    public sealed class UserScriptDownloadFailure : EventArgs
    {
        /// <summary>Initializes a download failure.</summary>
        public UserScriptDownloadFailure(Exception error) { Error = error; }
        /// <summary>Gets the underlying failure.</summary>
        public Exception Error { get; }
    }

    /// <summary>
    /// Persists JSON tab state scoped to a userscript installation.
    /// </summary>
    public interface ITabStateService
    {
        /// <summary>Gets the state associated with the current tab.</summary>
        /// <param name="scriptKey">The userscript installation.</param>
        /// <param name="frame">The requesting document frame.</param>
        /// <param name="cancellationToken">A token that cancels the lookup.</param>
        /// <returns>The tab state encoded as a JSON object.</returns>
        Task<string> GetAsync(ScriptKey scriptKey, DocumentFrame frame, CancellationToken cancellationToken);

        /// <summary>Saves the state associated with the current tab.</summary>
        /// <param name="scriptKey">The userscript installation.</param>
        /// <param name="frame">The requesting document frame.</param>
        /// <param name="jsonValue">The tab state encoded as JSON.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A task that completes after the state is saved.</returns>
        Task SaveAsync(ScriptKey scriptKey, DocumentFrame frame, string jsonValue, CancellationToken cancellationToken);

        /// <summary>Gets states for all tabs accessible to the userscript.</summary>
        /// <param name="scriptKey">The userscript installation.</param>
        /// <param name="cancellationToken">A token that cancels the lookup.</param>
        /// <returns>Tab identifiers mapped to state values encoded as JSON.</returns>
        Task<IReadOnlyDictionary<string, string>> GetAllAsync(ScriptKey scriptKey, CancellationToken cancellationToken);
    }
}
