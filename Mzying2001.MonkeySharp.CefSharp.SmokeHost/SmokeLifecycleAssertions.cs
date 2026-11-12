using Mzying2001.MonkeySharp.Core.Runtime;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class SmokeLifecycleAssertions
    {
        internal static bool HasDocumentEnded(IEnumerable<DocumentLifecycleEventArgs> events, string documentId)
        {
            return events.Any(item => item.Frame.DocumentId == documentId &&
                (item.Kind == DocumentLifecycleKind.ContextReleased || item.Kind == DocumentLifecycleKind.NavigationSuperseded));
        }
    }
}
