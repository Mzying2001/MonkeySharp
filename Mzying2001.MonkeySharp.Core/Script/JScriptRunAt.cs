namespace Mzying2001.MonkeySharp.Core.Script
{
    /// <summary>
    /// Represents the time when the script will be injected.
    /// </summary>
    public enum JScriptRunAt
    {
        /// <summary>
        /// The script will be injected as fast as possible.
        /// </summary>
        DocumentStart,

        /// <summary>
        /// The script will be injected if the body element exists.
        /// </summary>
        DocumentBody,

        /// <summary>
        /// The script will be injected when or after the DOMContentLoaded event was dispatched.
        /// </summary>
        DocumentEnd,

        /// <summary>
        /// The script will be injected after the DOMContentLoaded event was dispatched.
        /// </summary>
        DocumentIdle,

        /// <summary>
        /// The script will be injected if it is clicked at the browser context menu.
        /// </summary>
        ContextMenu,
    }
}
