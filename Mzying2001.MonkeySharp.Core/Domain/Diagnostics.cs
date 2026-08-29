using System;

namespace Mzying2001.MonkeySharp.Core.Domain
{
    /// <summary>
    /// Specifies the severity of a userscript diagnostic.
    /// </summary>
    public enum DiagnosticSeverity
    {
        /// <summary>Provides informational context that does not indicate a problem.</summary>
        Info,

        /// <summary>Identifies a potential problem that does not prevent execution.</summary>
        Warning,

        /// <summary>Identifies a problem that prevents the affected operation.</summary>
        Error
    }

    /// <summary>
    /// Describes a problem found while parsing or validating userscript metadata.
    /// </summary>
    public sealed class MetadataDiagnostic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MetadataDiagnostic"/> class.
        /// </summary>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="severity">The diagnostic severity.</param>
        /// <param name="message">The human-readable diagnostic message.</param>
        /// <param name="lineNumber">The one-based source line number, or zero when no line is available.</param>
        public MetadataDiagnostic(string code, DiagnosticSeverity severity, string message, int lineNumber = 0)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            LineNumber = lineNumber;
        }

        /// <summary>Gets the stable diagnostic code.</summary>
        public string Code { get; }

        /// <summary>Gets the diagnostic severity.</summary>
        public DiagnosticSeverity Severity { get; }

        /// <summary>Gets the human-readable diagnostic message.</summary>
        public string Message { get; }

        /// <summary>Gets the one-based source line number, or zero when unavailable.</summary>
        public int LineNumber { get; }
    }

    /// <summary>
    /// Describes a runtime, bridge, or host diagnostic associated with userscript execution.
    /// </summary>
    public sealed class UserScriptDiagnostic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserScriptDiagnostic"/> class.
        /// </summary>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="severity">The diagnostic severity.</param>
        /// <param name="message">The human-readable diagnostic message.</param>
        /// <param name="exception">The exception that caused the diagnostic, if any.</param>
        /// <param name="scriptKey">The affected script installation, if known.</param>
        /// <param name="documentId">The affected document identifier, if known.</param>
        /// <param name="frameId">The affected frame identifier, if known.</param>
        /// <param name="requestId">The affected bridge request identifier, if known.</param>
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

        /// <summary>Gets the stable diagnostic code.</summary>
        public string Code { get; }

        /// <summary>Gets the diagnostic severity.</summary>
        public DiagnosticSeverity Severity { get; }

        /// <summary>Gets the human-readable diagnostic message.</summary>
        public string Message { get; }

        /// <summary>Gets the exception that caused the diagnostic, if any.</summary>
        public Exception Exception { get; }

        /// <summary>Gets the affected script installation, if known.</summary>
        public ScriptKey? ScriptKey { get; }

        /// <summary>Gets the affected document identifier, if known.</summary>
        public string DocumentId { get; }

        /// <summary>Gets the affected frame identifier, if known.</summary>
        public string FrameId { get; }

        /// <summary>Gets the affected bridge request identifier, if known.</summary>
        public string RequestId { get; }
    }

    /// <summary>
    /// Represents an error raised by the MonkeySharp userscript runtime.
    /// </summary>
    public class UserScriptException : Exception
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public UserScriptException(string message) : base(message) { }

        /// <summary>Initializes the exception with an error message and its underlying cause.</summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that caused the current exception.</param>
        public UserScriptException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an attempt to use a userscript whose metadata is invalid.
    /// </summary>
    public sealed class MetadataValidationException : UserScriptException
    {
        /// <summary>Initializes the exception from the invalid parse result.</summary>
        /// <param name="parseResult">The parse result containing validation diagnostics.</param>
        public MetadataValidationException(MetadataParseResult parseResult)
            : base("The userscript metadata is invalid.")
        {
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
        }

        /// <summary>Gets the parse result containing the validation failures.</summary>
        public MetadataParseResult ParseResult { get; }
    }

    /// <summary>
    /// Represents a stable bridge protocol error that can be returned to a userscript.
    /// </summary>
    public class BridgeProtocolException : UserScriptException
    {
        /// <summary>Initializes the exception with a protocol error code and message.</summary>
        /// <param name="code">The stable bridge error code.</param>
        /// <param name="message">The message that describes the protocol error.</param>
        public BridgeProtocolException(string code, string message) : base(message)
        {
            Code = code;
        }

        /// <summary>Gets the stable bridge error code.</summary>
        public string Code { get; }
    }

    /// <summary>
    /// Represents an operation denied by the host permission policy.
    /// </summary>
    public sealed class PermissionDeniedException : UserScriptException
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The message that describes the denial.</param>
        public PermissionDeniedException(string message) : base(message) { }
    }

    /// <summary>
    /// Represents an API that is unavailable in the current host.
    /// </summary>
    public sealed class UnsupportedApiException : UserScriptException
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The message that describes the unsupported API.</param>
        public UnsupportedApiException(string message) : base(message) { }
    }
}
