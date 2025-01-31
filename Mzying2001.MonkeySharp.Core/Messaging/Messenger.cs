using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Messaging
{
    /// <summary>
    /// Proxy class for sending and receiving messages.
    /// </summary>
    public class Messenger
    {
        /// <summary>
        /// Raised when a message is received.
        /// </summary>
        public event MessageEventHandler ReceivedMessage;


        /// <summary>
        /// Send a message to the subscribers.
        /// </summary>
        public object SendMessage(object msg)
        {
            var args = new MessageEventArgs(msg);
            ReceivedMessage?.Invoke(this, args);
            return args.Result;
        }


        /// <summary>
        /// Send a message to the subscribers asynchronously.
        /// </summary>
        public async Task<object> SendMessageAsync(object msg)
        {
            var args = new MessageEventArgs(msg);
            if (ReceivedMessage != null)
                await Task.Run(() => ReceivedMessage.Invoke(this, args));
            return args.Result;
        }
    }
}
