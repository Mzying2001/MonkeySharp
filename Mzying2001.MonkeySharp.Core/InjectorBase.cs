using Mzying2001.MonkeySharp.Core.Internal;
using Mzying2001.MonkeySharp.Core.Messaging;
using Mzying2001.MonkeySharp.Core.Script;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core
{
    /// <summary>
    /// Base class for injectors.
    /// </summary>
    public abstract class InjectorBase : IList<JScript>, IDisposable
    {
        /// <summary>
        /// True if the object is disposed.
        /// </summary>
        private bool _disposed = false;


        /// <summary>
        /// List of scripts to inject.
        /// </summary>
        private readonly List<JScript> _scripts = new List<JScript>();


        /// <summary>
        /// Proxy object for javascript and C# communication.
        /// </summary>
        private readonly Messenger _messenger = new Messenger();


        /// <summary>
        /// Script to initialize injection.
        /// </summary>
        private readonly JScript _initInjectionScript = JScript.LoadFromString(ResourceLoader.GetInjectJs());


        /// <inheritdoc/>
        public int Count => ((ICollection<JScript>)_scripts).Count;


        /// <inheritdoc/>
        public bool IsReadOnly => ((ICollection<JScript>)_scripts).IsReadOnly;


        /// <inheritdoc/>
        public JScript this[int index]
        {
            get => ((IList<JScript>)_scripts)[index];
            set => ((IList<JScript>)_scripts)[index] = value;
        }


        /// <summary>
        /// Constructor.
        /// </summary>
        public InjectorBase()
        {
            _messenger.ReceivedMessage += ReceivedMessageHandler;
        }


        /// <summary>
        /// Finalizer.
        /// </summary>
        ~InjectorBase()
        {
            Dispose(disposing: false);
        }


        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }


        /// <summary>
        /// Disposes the object.
        /// </summary>
        /// <param name="disposing">True if called from Dispose().</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _messenger.ReceivedMessage -= ReceivedMessageHandler;
                }
                if (!IsBrowserAttached)
                {
                    DetachBrowser();
                }
                _disposed = true;
            }
        }


        /// <summary>
        /// Handles received messages.
        /// </summary>
        private void ReceivedMessageHandler(object sender, MessageEventArgs e)
        {
            List<string> args = new List<string>();

            if (e.Message is IList<object> objs)
            {
                args.AddRange(objs.Select(item => item?.ToString() ?? string.Empty));
            }
            else
            {
                args.Add(e.Message?.ToString() ?? string.Empty);
            }

            e.Result = OnMessage(args.ToArray()) ?? string.Empty;
        }


        /// <inheritdoc/>
        public int IndexOf(JScript item)
        {
            return ((IList<JScript>)_scripts).IndexOf(item);
        }


        /// <inheritdoc/>
        public void Insert(int index, JScript item)
        {
            ((IList<JScript>)_scripts).Insert(index, item);
        }


        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            ((IList<JScript>)_scripts).RemoveAt(index);
        }


        /// <inheritdoc/>
        public void Add(JScript item)
        {
            ((ICollection<JScript>)_scripts).Add(item);
        }


        /// <inheritdoc/>
        public void Clear()
        {
            ((ICollection<JScript>)_scripts).Clear();
        }


        /// <inheritdoc/>
        public bool Contains(JScript item)
        {
            return ((ICollection<JScript>)_scripts).Contains(item);
        }


        /// <inheritdoc/>
        public void CopyTo(JScript[] array, int arrayIndex)
        {
            ((ICollection<JScript>)_scripts).CopyTo(array, arrayIndex);
        }


        /// <inheritdoc/>
        public bool Remove(JScript item)
        {
            return ((ICollection<JScript>)_scripts).Remove(item);
        }


        /// <inheritdoc/>
        public IEnumerator<JScript> GetEnumerator()
        {
            return ((IEnumerable<JScript>)_scripts).GetEnumerator();
        }


        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_scripts).GetEnumerator();
        }


        /// <summary>
        /// Called when a message is received from javascript.
        /// </summary>
        protected virtual string OnMessage(string[] args)
        {
            if (args.Length == 1)
            {
                // TODO: Handle single argument messages.
            }
            else if (args.Length == 2)
            {
                switch (args[0])
                {
                    case "document-start":
                    case "document-body":
                    case "document-end":
                    case "document-idle":
                    case "context-menu":
                        OnPreInjectScript(JScriptInfo.ParseRunAt(args[0]), args[1]);
                        break;
                }
            }
            else
            {
            }
            return null;
        }


        /// <summary>
        /// Called before injecting scripts.
        /// </summary>
        protected virtual void OnPreInjectScript(JScriptRunAt runAt, string url)
        {
            foreach (var script in _scripts)
            {
                if (script.Info.RunAt == runAt && script.MatchUrl(url))
                {
                    InjectScript(script);
                }
            }
        }


        /// <summary>
        /// Attaches the browser.
        /// </summary>
        public void AttachBrowser(object browser)
        {
            OnAttachBrowser(browser, _messenger, () => OnInjectScript(_initInjectionScript, true));
        }


        /// <summary>
        /// Detaches the browser.
        /// </summary>
        public void DetachBrowser()
        {
            OnDetachBrowser(_messenger);
        }


        /// <summary>
        /// Inject script immediately.
        /// </summary>
        public void InjectScript(JScript script)
        {
            OnInjectScript(script, false);
        }


        /// <summary>
        /// Called when injecting a script.
        /// </summary>
        /// <param name="script">The script to inject.</param>
        /// <param name="isInitInjection">True if the script is the initialization script.</param>
        protected abstract void OnInjectScript(JScript script, bool isInitInjection);


        /// <summary>
        /// Called when attaching the browser.
        /// The messenger should be registered as __MonkeySharp_Messenger and __MonkeySharp_AsyncMessenger (for async messages)
        /// to the browser for javascript and C# communication.
        /// When the webpage starts to load, initCallback should be called.
        /// </summary>
        /// <param name="browser">The browser object.</param>
        /// <param name="messenger">Proxy object for javascript and C# communication.</param>
        /// <param name="initCallback">When the webpage starts to load, call this callback.</param>
        protected abstract void OnAttachBrowser(object browser, Messenger messenger, Action initCallback);


        /// <summary>
        /// Called when detaching the browser.
        /// </summary>
        /// <param name="messenger">Proxy object for javascript and C# communication.</param>
        protected abstract void OnDetachBrowser(Messenger messenger);


        /// <summary>
        /// True if the browser is attached.
        /// </summary>
        public abstract bool IsBrowserAttached { get; }
    }
}
