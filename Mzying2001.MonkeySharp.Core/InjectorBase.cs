using Mzying2001.MonkeySharp.Core.Internal;
using Mzying2001.MonkeySharp.Core.Messaging;
using Mzying2001.MonkeySharp.Core.Script;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        /// Lock object.
        /// </summary>
        private readonly object _syncLock = new object();


        /// <summary>
        /// True if <see cref="_scripts"/> has changed.
        /// </summary>
        private volatile bool _scriptListChanged = false;


        /// <summary>
        /// Stores the scripts by ID.
        /// </summary>
        private readonly Dictionary<string, JScript> _scriptMap = new Dictionary<string, JScript>();


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
        private readonly string _initInjectionScript = ResourceLoader.GetInitJs();


        /// <summary>
        /// Template for script injection.
        /// </summary>
        private readonly string _injectScriptTemplate = ResourceLoader.GetInjectJs();


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
            lock (_syncLock)
            {
                _scriptListChanged = true;
                ((IList<JScript>)_scripts).Insert(index, item);
            }
        }


        /// <inheritdoc/>
        public void RemoveAt(int index)
        {
            lock (_syncLock)
            {
                _scriptListChanged = true;
                ((IList<JScript>)_scripts).RemoveAt(index);
            }
        }


        /// <inheritdoc/>
        public void Add(JScript item)
        {
            lock (_syncLock)
            {
                _scriptListChanged = true;
                ((ICollection<JScript>)_scripts).Add(item);
            }
        }


        /// <inheritdoc/>
        public void Clear()
        {
            lock (_syncLock)
            {
                _scriptListChanged = true;
                ((ICollection<JScript>)_scripts).Clear();
            }
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
            lock (_syncLock)
            {
                _scriptListChanged = true;
                return ((ICollection<JScript>)_scripts).Remove(item);
            }
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
        /// Tries to get the script by ID.
        /// </summary>
        public bool TryGetScriptById(string scriptId, out JScript result)
        {
            if (_scriptListChanged)
            {
                lock (_syncLock)
                {
                    if (_scriptListChanged)
                    {
                        _scriptMap.Clear();
                        for (int i = 0; i < _scripts.Count; i++)
                            _scriptMap.Add(_scripts[i].ScriptId, _scripts[i]);
                        _scriptListChanged = false;
                    }
                }
            }
            return _scriptMap.TryGetValue(scriptId, out result);
        }


        /// <summary>
        /// Gets the script by ID.
        /// </summary>
        public JScript GetScriptById(string scriptId)
        {
            if (TryGetScriptById(scriptId, out JScript result))
            {
                return result;
            }
            else
            {
                throw new KeyNotFoundException($"Script with ID {scriptId} not found.");
            }
        }


        /// <summary>
        /// Called when a message is received from javascript.
        /// </summary>
        protected virtual string OnMessage(string[] args)
        {
            if (args.Length != 0)
            {
                string msg = args[0];
                switch (msg)
                {
                    case "document-start":
                    case "document-body":
                    case "document-end":
                    case "document-idle":
                    case "context-menu":
                        if (args.Length == 2)
                        {
                            string url = args[1];
                            OnInjectScripts(JScriptInfo.ParseRunAt(msg), url);
                        }
                        break;

                    case "script-start":
                        if (args.Length == 2)
                        {
                            string scriptId = args[1];
                            OnScriptStart(scriptId);
                        }
                        break;

                    case "script-end":
                        if (args.Length == 2)
                        {
                            string scriptId = args[1];
                            OnScriptEnd(scriptId);
                        }
                        break;

                    case "GM_log":
                        if (args.Length == 3)
                        {
                            string scriptId = args[1];
                            string message = args[2];
                            TryExecuteApi(msg, scriptId, () => GM_log(message));
                        }
                        break;
                }
            }
            return null;
        }


        /// <summary>
        /// Logs a message to the console.
        /// </summary>
        protected virtual void GM_log(string message)
        {
            ExecuteScriptAsync("__MonkeySharp.consoleLog", message);
        }


        /// <summary>
        /// Verifies the access permission of the specified API.
        /// </summary>
        protected virtual bool VerifyApiAccessPermission(string api, string scriptId)
        {
            if (TryGetScriptById(scriptId, out JScript script))
            {
                return script.Info.Grant.Contains(api);
            }
            return false;
        }


        /// <summary>
        /// Called when the access to an API is denied.
        /// </summary>
        protected virtual void OnApiAccessDenied(string api, string scriptId)
        {
            string scriptName = GetScriptNameOrId(scriptId);
            GM_log($"Script '{scriptName}' access to API '{api}' is denied.");
        }


        /// <summary>
        /// Executes the specified API, or calls <see cref="OnApiAccessDenied"/> if the permission is denied.
        /// </summary>
        private bool TryExecuteApi(string api, string scriptId, Action execute)
        {
            if (VerifyApiAccessPermission(api, scriptId))
            {
                execute?.Invoke();
                return true;
            }
            else
            {
                OnApiAccessDenied(api, scriptId);
                return false;
            }
        }


        /// <summary>
        /// Gets the name of the script by ID, or the ID if the script is not found.
        /// </summary>
        private string GetScriptNameOrId(string scriptId)
        {
            return TryGetScriptById(scriptId, out JScript script) ? script.Info.Name : scriptId;
        }


        /// <summary>
        /// Injects scripts for the specified run-at and url.
        /// </summary>
        protected virtual void OnInjectScripts(JScriptRunAt runAt, string url)
        {
            List<JScript> scripts;
            lock (_syncLock)
            {
                scripts = new List<JScript>(_scripts);
            }

            var scriptInjectionBuilder = new StringBuilder();

            foreach (JScript script in scripts)
            {
                if (script.Info.RunAt == runAt && script.MatchUrl(url))
                {
                    scriptInjectionBuilder.AppendLine("try {");
                    scriptInjectionBuilder.AppendLine($"__MonkeySharp_CurrentScriptId = '{script.ScriptId}';");
                    scriptInjectionBuilder.AppendLine($"__MonkeySharp.sendMsg(['script-start', __MonkeySharp_CurrentScriptId]);");
                    scriptInjectionBuilder.AppendLine(script.ScriptText);
                    scriptInjectionBuilder.AppendLine($"__MonkeySharp.sendMsg(['script-end', __MonkeySharp_CurrentScriptId]);");
                    scriptInjectionBuilder.AppendLine("} catch (e) { __MonkeySharp.consoleLog(e); }");
                }
            }

            if (scriptInjectionBuilder.Length != 0)
            {
                ExecuteScriptAsync(_injectScriptTemplate.Replace(
                    "/*==========REPLACE_CODE_HERE==========*/", scriptInjectionBuilder.ToString()));
            }
        }


        /// <summary>
        /// Called when a script starts.
        /// </summary>
        protected virtual void OnScriptStart(string scriptId)
        {
        }


        /// <summary>
        /// Called when a script ends.
        /// </summary>
        protected virtual void OnScriptEnd(string scriptId)
        {
        }


        /// <summary>
        /// Attaches the browser.
        /// </summary>
        public void AttachBrowser(object browser)
        {
            OnAttachBrowser(browser, _messenger, () => ExecuteScriptAsync(_initInjectionScript));
        }


        /// <summary>
        /// Detaches the browser.
        /// </summary>
        public void DetachBrowser()
        {
            OnDetachBrowser(_messenger);
        }


        /// <summary>
        /// True if the browser is attached.
        /// </summary>
        public abstract bool IsBrowserAttached { get; }


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
        /// Execute javascript in the context of the attached browser's main frame asynchronously.
        /// </summary>
        /// <param name="script">The script to execute.</param>
        public abstract void ExecuteScriptAsync(string script);


        /// <summary>
        /// Execute javascript in the context of the attached browser's main frame asynchronously.
        /// </summary>
        /// <param name="function">The function to execute.</param>
        /// <param name="args">The arguments to pass to the function.</param>
        public abstract void ExecuteScriptAsync(string function, params object[] args);
    }
}
