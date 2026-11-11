using Mzying2001.MonkeySharp.Core.Runtime;

namespace Mzying2001.MonkeySharp.CefSharp
{
    // A menu selection retains the document generation captured when the native menu opened.
    internal sealed class CefSharpMenuTarget
    {
        internal CefSharpMenuTarget(ContextMenuEntry entry, DocumentFrame frame, string nativeFrameId)
        { Entry = entry; Frame = frame; NativeFrameId = nativeFrameId; }
        internal ContextMenuEntry Entry { get; }
        internal DocumentFrame Frame { get; }
        internal string NativeFrameId { get; }
    }
}
