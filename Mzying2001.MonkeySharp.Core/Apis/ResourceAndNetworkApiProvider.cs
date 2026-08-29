using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>
    /// Adapts host resource and HTTP services to userscript resource and network GM APIs.
    /// </summary>
    public sealed class ResourceAndNetworkApiProvider : IUserScriptApiProvider, IUserScriptNotificationSource
    {
        private static readonly HashSet<string> AllowedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"
        };
        private static readonly HashSet<string> BlockedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Content-Length", "Cookie", "Origin", "Referer", "Connection", "Proxy-Connection",
            "Transfer-Encoding", "Upgrade", "Keep-Alive", "TE", "Trailer"
        };

        private readonly IResourceProvider _resources;
        private readonly IHttpRequestService _http;
        private readonly BridgeOptions _options;
        private readonly IReadOnlyCollection<string> _methods;

        /// <summary>Initializes a resource and network API provider.</summary>
        /// <param name="resources">The optional declared-resource provider.</param>
        /// <param name="http">The optional HTTP request service.</param>
        /// <param name="options">Bridge limits used for resources and responses, or <see langword="null"/> for defaults.</param>
        public ResourceAndNetworkApiProvider(
            IResourceProvider resources = null,
            IHttpRequestService http = null,
            BridgeOptions options = null)
        {
            if (resources == null && http == null)
                throw new ArgumentException("At least one resource or HTTP service is required.");
            _resources = resources;
            _http = http;
            _options = options ?? new BridgeOptions();
            var methods = new List<string>();
            if (resources != null)
            {
                methods.Add("GM.getResourceText");
                methods.Add("GM.getResourceURL");
            }
            if (http != null)
                methods.Add("GM.xmlHttpRequest");
            _methods = new ReadOnlyCollection<string>(methods);
        }

        /// <inheritdoc />
        public event EventHandler<ApiNotificationEventArgs> Notification;

        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods => _methods;

        /// <inheritdoc />
        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            switch (context.Method)
            {
                case "GM.getResourceText":
                case "GM.getResourceURL":
                    return await InvokeResourceAsync(context, cancellationToken).ConfigureAwait(false);
                case "GM.xmlHttpRequest":
                    return await InvokeHttpAsync(context, cancellationToken).ConfigureAwait(false);
                default:
                    throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
        }

        private async Task<ApiResult> InvokeResourceAsync(
            ApiInvocationContext context,
            CancellationToken cancellationToken)
        {
            var name = ProviderParameters.RequiredString(context.Parameters, "name");
            var declaration = context.Installation.Definition.Metadata.Resources
                .SingleOrDefault(item => item.Name == name);
            if (declaration == null)
                throw ProviderParameters.Invalid("The resource '" + name + "' was not declared.");
            var content = await _resources.GetAsync(
                context.Installation,
                declaration,
                cancellationToken).ConfigureAwait(false);
            if (content == null)
                throw new BridgeProtocolException(BridgeErrorCodes.Internal, "The resource provider returned no content.");
            if (content.Bytes.Length > _options.MaxResourceBytes ||
                (content.Text != null && Encoding.UTF8.GetByteCount(content.Text) > _options.MaxResourceBytes))
                throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "The resource exceeds the configured limit.");
            if (context.Method == "GM.getResourceText")
            {
                var text = content.Text ?? Encoding.UTF8.GetString(content.Bytes);
                return ApiResult.FromValue(text);
            }
            return ApiResult.FromValue(
                "data:" + content.MediaType + ";base64," + Convert.ToBase64String(content.Bytes));
        }

        private async Task<ApiResult> InvokeHttpAsync(
            ApiInvocationContext context,
            CancellationToken cancellationToken)
        {
            ProviderParameters.RequireObject(context.Parameters);
            var urlText = ProviderParameters.RequiredString(context.Parameters, "url");
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
                throw ProviderParameters.Invalid("url must be an absolute HTTP or HTTPS URL.");
            if (!ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, url))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "The target is not allowed by @connect.");

            var method = ProviderParameters.OptionalString(context.Parameters, "method") ?? "GET";
            method = method.ToUpperInvariant();
            if (!AllowedMethods.Contains(method))
                throw ProviderParameters.Invalid("The HTTP method is not allowed.");
            var headers = ReadHeaders(context.Parameters);
            var body = ProviderParameters.OptionalString(context.Parameters, "data");
            TimeSpan? timeout = null;
            if (context.Parameters.TryGetProperty("timeout", out var timeoutValue))
            {
                if (timeoutValue.ValueKind != JsonValueKind.Number ||
                    !timeoutValue.TryGetInt32(out var milliseconds) || milliseconds <= 0)
                    throw ProviderParameters.Invalid("timeout must be a positive integer.");
                timeout = TimeSpan.FromMilliseconds(milliseconds);
            }
            var xhrId = ProviderParameters.RequiredInt32(context.Parameters, "xhrId");
            if (xhrId <= 0)
                throw ProviderParameters.Invalid("xhrId must be positive.");

            var progress = new CallbackProgress<UserScriptHttpProgress>(value =>
            {
                var data = JsonSerializer.Serialize(new
                {
                    xhrId,
                    loaded = value.Loaded,
                    total = value.Total
                });
                Notification?.Invoke(this, new ApiNotificationEventArgs(
                    context.Installation.ScriptKey,
                    context.ExecutionId,
                    "xhr-progress",
                    data));
            });
            var response = await _http.SendAsync(
                new UserScriptHttpRequest(
                    method,
                    url,
                    headers,
                    body,
                    timeout,
                    _options.MaxResourceBytes,
                    redirect => ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, redirect)),
                progress,
                cancellationToken).ConfigureAwait(false);
            if (response == null)
                throw new BridgeProtocolException(BridgeErrorCodes.Internal, "The HTTP service returned no response.");
            if (response.RedirectUrls.Any(redirect =>
                !ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, redirect)))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "A redirect escaped the @connect allowlist.");
            if (!ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, response.FinalUrl))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "A redirect escaped the @connect allowlist.");
            if (response.Body.Length > _options.MaxResourceBytes ||
                (response.ResponseText != null && Encoding.UTF8.GetByteCount(response.ResponseText) > _options.MaxResourceBytes))
                throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "The HTTP response exceeds the configured limit.");

            return ApiResult.FromValue(new
            {
                status = response.Status,
                statusText = response.StatusText,
                finalUrl = response.FinalUrl.AbsoluteUri,
                responseHeaders = response.Headers,
                responseText = response.ResponseText,
                responseBase64 = response.ResponseText == null ? Convert.ToBase64String(response.Body) : null
            });
        }

        private static IDictionary<string, string> ReadHeaders(JsonElement parameters)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!parameters.TryGetProperty("headers", out var headers) || headers.ValueKind == JsonValueKind.Null)
                return result;
            if (headers.ValueKind != JsonValueKind.Object)
                throw ProviderParameters.Invalid("headers must be an object.");
            foreach (var header in headers.EnumerateObject())
            {
                if (!IsHeaderName(header.Name) ||
                    BlockedHeaders.Contains(header.Name) ||
                    header.Name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                    header.Name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase) ||
                    header.Value.ValueKind != JsonValueKind.String ||
                    header.Value.GetString().IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                    result.ContainsKey(header.Name))
                    throw ProviderParameters.Invalid("The header '" + header.Name + "' is not allowed.");
                result.Add(header.Name, header.Value.GetString());
            }
            return result;
        }

        private static bool IsHeaderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            const string punctuation = "!#$%&'*+-.^_`|~";
            return name.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                punctuation.IndexOf(character) >= 0);
        }

        internal static bool ConnectAllows(IReadOnlyList<string> declarations, Uri source, Uri target)
        {
            if (declarations == null || declarations.Count == 0 || target == null || !target.IsAbsoluteUri ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
                return false;
            var targetHost = new IdnMapping().GetAscii(target.IdnHost).ToLowerInvariant();
            foreach (var declaration in declarations)
            {
                if (declaration == "*")
                    return true;
                if (declaration == "self" && source != null &&
                    source.Scheme == target.Scheme &&
                    string.Equals(source.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                    source.Port == target.Port)
                    return true;
                var value = declaration;
                if (Uri.TryCreate(declaration, UriKind.Absolute, out var declaredUri))
                {
                    if (declaredUri.Scheme != target.Scheme || declaredUri.Port != target.Port)
                        continue;
                    value = declaredUri.IdnHost;
                }
                var subdomains = value.StartsWith("*.", StringComparison.Ordinal);
                if (subdomains)
                    value = value.Substring(2);
                try
                {
                    value = new IdnMapping().GetAscii(value).ToLowerInvariant();
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (subdomains
                    ? targetHost.Length > value.Length + 1 && targetHost.EndsWith("." + value, StringComparison.Ordinal)
                    : targetHost == value)
                    return true;
            }
            return false;
        }

        private sealed class CallbackProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;

            public CallbackProgress(Action<T> callback)
            {
                _callback = callback;
            }

            public void Report(T value)
            {
                _callback(value);
            }
        }
    }

    internal static class ProviderParameters
    {
        public static void RequireObject(JsonElement parameters)
        {
            if (parameters.ValueKind != JsonValueKind.Object)
                throw Invalid("params must be an object.");
        }

        public static string RequiredString(JsonElement parameters, string name)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                throw Invalid(name + " must be a string.");
            var result = value.GetString();
            if (string.IsNullOrEmpty(result))
                throw Invalid(name + " cannot be empty.");
            return result;
        }

        public static string OptionalString(JsonElement parameters, string name)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind != JsonValueKind.String)
                throw Invalid(name + " must be a string.");
            return value.GetString();
        }

        public static int RequiredInt32(JsonElement parameters, string name)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
                throw Invalid(name + " must be an integer.");
            return result;
        }

        public static bool OptionalBoolean(JsonElement parameters, string name, bool defaultValue = false)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value))
                return defaultValue;
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                throw Invalid(name + " must be a boolean.");
            return value.GetBoolean();
        }

        public static BridgeProtocolException Invalid(string message)
        {
            return new BridgeProtocolException(BridgeErrorCodes.InvalidParams, message);
        }
    }
}
