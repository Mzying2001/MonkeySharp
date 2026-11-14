using Mzying2001.MonkeySharp.Core.Runtime;

namespace Mzying2001.MonkeySharp.CefSharp
{
    internal static class CefSharpDocumentIdentity
    {
        // URL snapshots can change while the native document and its owner remain live.
        internal static bool Matches(DocumentFrame current, DocumentFrame captured)
        {
            return current != null && captured != null &&
                current.BrowserSessionId == captured.BrowserSessionId &&
                current.DocumentId == captured.DocumentId && current.FrameId == captured.FrameId &&
                current.ExecutionWorld == captured.ExecutionWorld && current.ScriptScope == captured.ScriptScope &&
                current.RevisionScope == captured.RevisionScope && current.IsMainFrame == captured.IsMainFrame &&
                current.BridgeIntegrity == captured.BridgeIntegrity;
        }
    }
}
