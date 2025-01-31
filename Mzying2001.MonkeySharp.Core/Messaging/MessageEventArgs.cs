using System;

namespace Mzying2001.MonkeySharp.Core.Messaging
{
    /// <summary>
    /// Event handler for message event.
    /// </summary>
    public delegate void MessageEventHandler(object sender, MessageEventArgs e);


    /// <summary>
    /// Event arguments for message event.
    /// </summary>
    public class MessageEventArgs : EventArgs
    {
        /// <summary>
        /// Set to true if the message is handled.
        /// </summary>
        public bool Handled { get; set; }


        /// <summary>
        /// Message object.
        /// </summary>
        public object Message { get; }


        /// <summary>
        /// Result of the message.
        /// </summary>
        public object Result { get; set; }


        /// <summary>
        /// Create a new instance of MessageEventArgs.
        /// </summary>
        public MessageEventArgs(object msg) => Message = msg;
    }
}
