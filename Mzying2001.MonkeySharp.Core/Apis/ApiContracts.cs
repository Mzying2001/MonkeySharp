using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>
    /// Contains the serialized JavaScript value returned by a userscript API provider.
    /// </summary>
    public sealed class ApiResult
    {
        private ApiResult(string json)
        {
            Json = json;
        }

        /// <summary>Gets the serialized JSON value returned to JavaScript.</summary>
        public string Json { get; }

        /// <summary>Creates a result from an already serialized JSON value.</summary>
        /// <param name="json">A valid JSON value.</param>
        /// <returns>A result that preserves the supplied JSON.</returns>
        public static ApiResult FromJson(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            using (JsonDocument.Parse(json)) { }
            return new ApiResult(json);
        }

        /// <summary>Serializes a .NET value into an API result.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>A result containing the serialized value.</returns>
        public static ApiResult FromValue<T>(T value)
        {
            return new ApiResult(JsonSerializer.Serialize(value));
        }

        /// <summary>Gets the protocol sentinel that represents JavaScript <c>undefined</c>.</summary>
        public static ApiResult Undefined { get; } =
            new ApiResult("{\"$monkeySharpType\":\"undefined\"}");
    }

    /// <summary>
    /// Provides the authenticated execution and request data for an API invocation.
    /// </summary>
    public sealed class ApiInvocationContext
    {
        /// <summary>Initializes an API invocation context.</summary>
        /// <param name="installation">The userscript installation making the request.</param>
        /// <param name="frame">The document frame that issued the request.</param>
        /// <param name="executionId">The active script execution identifier.</param>
        /// <param name="requestId">The bridge request identifier.</param>
        /// <param name="method">The requested API method.</param>
        /// <param name="parameters">The request parameters.</param>
        /// <param name="authorizeTargetAsync">An optional callback that reauthorizes changed targets.</param>
        public ApiInvocationContext(
            UserScriptInstallation installation,
            DocumentFrame frame,
            string executionId,
            string requestId,
            string method,
            JsonElement parameters,
            Func<string, CancellationToken, Task<bool>> authorizeTargetAsync = null)
        {
            Installation = installation;
            Frame = frame;
            ExecutionId = executionId;
            RequestId = requestId;
            Method = method;
            Parameters = parameters;
            AuthorizeTargetAsync = authorizeTargetAsync;
        }

        /// <summary>Gets the userscript installation making the request.</summary>
        public UserScriptInstallation Installation { get; }

        /// <summary>Gets the document frame that issued the request.</summary>
        public DocumentFrame Frame { get; }

        /// <summary>Gets the active script execution identifier.</summary>
        public string ExecutionId { get; }

        /// <summary>Gets the bridge request identifier.</summary>
        public string RequestId { get; }

        /// <summary>Gets the requested API method.</summary>
        public string Method { get; }

        /// <summary>Gets the request parameters.</summary>
        public JsonElement Parameters { get; }

        /// <summary>Gets an optional callback that reauthorizes a changed security-sensitive target.</summary>
        public Func<string, CancellationToken, Task<bool>> AuthorizeTargetAsync { get; }
    }

    /// <summary>
    /// Handles one or more named userscript API methods.
    /// </summary>
    public interface IUserScriptApiProvider
    {
        /// <summary>Gets the API method names handled by this provider.</summary>
        IReadOnlyCollection<string> Methods { get; }

        /// <summary>Invokes a userscript API method.</summary>
        /// <param name="context">The authenticated invocation context.</param>
        /// <param name="cancellationToken">A token that cancels the invocation.</param>
        /// <returns>The value to return to JavaScript.</returns>
        Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Provides a provider-generated event to route to an active userscript execution.
    /// </summary>
    public sealed class ApiNotificationEventArgs : EventArgs
    {
        /// <summary>Initializes API notification event data.</summary>
        /// <param name="scriptKey">The target script installation.</param>
        /// <param name="executionId">The target execution identifier, if restricted to one execution.</param>
        /// <param name="eventName">The JavaScript event name.</param>
        /// <param name="dataJson">The event data encoded as JSON.</param>
        public ApiNotificationEventArgs(
            ScriptKey scriptKey,
            string executionId,
            string eventName,
            string dataJson)
        {
            ScriptKey = scriptKey;
            ExecutionId = executionId;
            EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
            DataJson = dataJson ?? throw new ArgumentNullException(nameof(dataJson));
            using (JsonDocument.Parse(dataJson)) { }
        }

        /// <summary>Gets the target script installation.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the target execution identifier, if restricted to one execution.</summary>
        public string ExecutionId { get; }

        /// <summary>Gets the JavaScript event name.</summary>
        public string EventName { get; }

        /// <summary>Gets the event data encoded as JSON.</summary>
        public string DataJson { get; }
    }

    /// <summary>
    /// Exposes asynchronous notifications emitted by an API provider.
    /// </summary>
    public interface IUserScriptNotificationSource
    {
        /// <summary>Occurs when an event should be delivered to an active userscript execution.</summary>
        event EventHandler<ApiNotificationEventArgs> Notification;
    }

    /// <summary>
    /// Receives notification when a userscript execution ends.
    /// </summary>
    public interface IUserScriptExecutionObserver
    {
        /// <summary>Releases provider state associated with a completed execution.</summary>
        /// <param name="executionId">The completed execution identifier.</param>
        void OnExecutionEnded(string executionId);
    }

    /// <summary>Provides execution bootstrap data for compatibility facades.</summary>
    public interface IUserScriptCompatibilityBootstrapProvider
    {
        /// <summary>Prepares provider-owned data for one authenticated execution.</summary>
        /// <param name="installation">The script installation being executed.</param>
        /// <param name="frame">The document frame being executed.</param>
        /// <param name="capabilities">The normalized capabilities granted to the execution.</param>
        /// <param name="cancellationToken">A token that cancels bootstrap preparation.</param>
        /// <returns>A resource-name keyed compatibility contribution.</returns>
        Task<IReadOnlyDictionary<string, CompatibilityResourceSnapshot>> PrepareAsync(
            UserScriptInstallation installation,
            DocumentFrame frame,
            IReadOnlyCollection<string> capabilities,
            CancellationToken cancellationToken);
    }

    /// <summary>Contains text and data URL representations of a declared resource.</summary>
    public sealed class CompatibilityResourceSnapshot
    {
        /// <summary>Initializes a resource snapshot.</summary>
        /// <param name="text">The decoded text representation.</param>
        /// <param name="url">The data URL representation.</param>
        public CompatibilityResourceSnapshot(string text, string url)
        {
            Text = text;
            Url = url;
        }

        /// <summary>Gets the decoded text representation.</summary>
        public string Text { get; }

        /// <summary>Gets the data URL representation.</summary>
        public string Url { get; }
    }

    /// <summary>
    /// Represents a value logged by a userscript execution.
    /// </summary>
    public sealed class UserScriptLogEntry
    {
        /// <summary>Initializes a userscript log entry.</summary>
        /// <param name="scriptKey">The script installation that produced the entry.</param>
        /// <param name="documentId">The document in which the script was running.</param>
        /// <param name="jsonValue">The logged value encoded as JSON.</param>
        public UserScriptLogEntry(ScriptKey scriptKey, string documentId, string jsonValue)
        {
            ScriptKey = scriptKey;
            DocumentId = documentId;
            JsonValue = jsonValue;
        }

        /// <summary>Gets the script installation that produced the entry.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the document in which the script was running.</summary>
        public string DocumentId { get; }

        /// <summary>Gets the logged value encoded as JSON.</summary>
        public string JsonValue { get; }
    }
}
