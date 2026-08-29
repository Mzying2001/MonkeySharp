using System;

namespace Mzying2001.MonkeySharp.Core.Domain
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class MetadataDiagnostic
    {
        public MetadataDiagnostic(string code, DiagnosticSeverity severity, string message, int lineNumber = 0)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            LineNumber = lineNumber;
        }

        public string Code { get; }
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public int LineNumber { get; }
    }

    public sealed class UserScriptDiagnostic
    {
        public UserScriptDiagnostic(
            string code,
            DiagnosticSeverity severity,
            string message,
            Exception exception = null,
            ScriptKey? scriptKey = null,
            string documentId = null,
            string frameId = null,
            string requestId = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Exception = exception;
            ScriptKey = scriptKey;
            DocumentId = documentId;
            FrameId = frameId;
            RequestId = requestId;
        }

        public string Code { get; }
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public ScriptKey? ScriptKey { get; }
        public string DocumentId { get; }
        public string FrameId { get; }
        public string RequestId { get; }
    }

    public class UserScriptException : Exception
    {
        public UserScriptException(string message) : base(message) { }
        public UserScriptException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class MetadataValidationException : UserScriptException
    {
        public MetadataValidationException(MetadataParseResult parseResult)
            : base("The userscript metadata is invalid.")
        {
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
        }

        public MetadataParseResult ParseResult { get; }
    }

    public class BridgeProtocolException : UserScriptException
    {
        public BridgeProtocolException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    public sealed class PermissionDeniedException : UserScriptException
    {
        public PermissionDeniedException(string message) : base(message) { }
    }

    public sealed class UnsupportedApiException : UserScriptException
    {
        public UnsupportedApiException(string message) : base(message) { }
    }
}
