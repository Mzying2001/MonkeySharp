using Mzying2001.MonkeySharp.Core.Runtime;
using System.Threading;

namespace Mzying2001.MonkeySharp.CefSharp
{
    // A menu selection retains the document generation captured when the native menu opened.
    internal sealed class CefSharpMenuTarget
    {
        internal CefSharpMenuTarget(ContextMenuEntry entry, DocumentFrame frame, string nativeFrameId, CancellationToken menuBinding = default)
        { Entry = entry; Frame = frame; NativeFrameId = nativeFrameId; MenuBinding = menuBinding; }
        internal ContextMenuEntry Entry { get; }
        internal DocumentFrame Frame { get; }
        internal string NativeFrameId { get; }
        internal CancellationToken MenuBinding { get; }
    }
}
