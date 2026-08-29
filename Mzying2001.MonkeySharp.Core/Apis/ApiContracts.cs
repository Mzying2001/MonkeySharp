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
    public sealed class ApiResult
    {
        private ApiResult(string json)
        {
            Json = json;
        }

        public string Json { get; }

        public static ApiResult FromJson(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            using (JsonDocument.Parse(json)) { }
            return new ApiResult(json);
        }

        public static ApiResult FromValue<T>(T value)
        {
            return new ApiResult(JsonSerializer.Serialize(value));
        }

        public static ApiResult Undefined { get; } =
            new ApiResult("{\"$monkeySharpType\":\"undefined\"}");
    }

    public sealed class ApiInvocationContext
    {
        public ApiInvocationContext(
            UserScriptInstallation installation,
            DocumentFrame frame,
            string executionId,
            string requestId,
            string method,
            JsonElement parameters)
        {
            Installation = installation;
            Frame = frame;
            ExecutionId = executionId;
            RequestId = requestId;
            Method = method;
            Parameters = parameters;
        }

        public UserScriptInstallation Installation { get; }
        public DocumentFrame Frame { get; }
        public string ExecutionId { get; }
        public string RequestId { get; }
        public string Method { get; }
        public JsonElement Parameters { get; }
    }

    public interface IUserScriptApiProvider
    {
        IReadOnlyCollection<string> Methods { get; }
        Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken);
    }

    public sealed class ApiNotificationEventArgs : EventArgs
    {
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

        public ScriptKey ScriptKey { get; }
        public string ExecutionId { get; }
        public string EventName { get; }
        public string DataJson { get; }
    }

    public interface IUserScriptNotificationSource
    {
        event EventHandler<ApiNotificationEventArgs> Notification;
    }

    public interface IUserScriptExecutionObserver
    {
        void OnExecutionEnded(string executionId);
    }

    public sealed class UserScriptLogEntry
    {
        public UserScriptLogEntry(ScriptKey scriptKey, string documentId, string jsonValue)
        {
            ScriptKey = scriptKey;
            DocumentId = documentId;
            JsonValue = jsonValue;
        }

        public ScriptKey ScriptKey { get; }
        public string DocumentId { get; }
        public string JsonValue { get; }
    }
}
