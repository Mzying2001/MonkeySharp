using Mzying2001.MonkeySharp.Core.Internal;
using Mzying2001.MonkeySharp.Core.Messaging;
using Mzying2001.MonkeySharp.Core.Model;
using Mzying2001.MonkeySharp.Core.Script;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

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
        /// True if scripts should be injected in strict mode.
        /// </summary>
        public bool ForceUseStrict { get; set; }


        /// <summary>
        /// Interface for data storage, used by GM_getValue and GM_setValue.
        /// </summary>
        public IDataStore DataStore { get; set; }


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
            var args = new List<string>();

            if (e.Message is IList<object> list)
                args.AddRange(list.Select(item => item?.ToString()));
            else args.Add(e.Message?.ToString());

            e.Result = OnMessage(args.ToArray());
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
        /// <param name="args">The arguments from the message.</param>
        /// <returns>The JavaScript object in JSON format, or null if result is undefined.</returns>
        protected virtual string OnMessage(string[] args)
        {
            string result = null;

            if (args.Length == 2)
            {
                string msg = args[0];
                string param = args[1];

                switch (msg)
                {
                    case "document-start":
                    case "document-body":
                    case "document-end":
                    case "document-idle":
                    case "context-menu":
                        if (param != null)
                        {
                            var url = JsonSerializer.Deserialize<string>(param);
                            OnInjectScripts(JScriptMeta.ParseRunAt(msg), url);
                        }
                        break;

                    case "script-start":
                        if (param != null)
                        {
                            var scriptId = JsonSerializer.Deserialize<string>(param);
                            OnScriptStart(scriptId);
                        }
                        break;

                    case "script-end":
                        if (param != null)
                        {
                            var scriptId = JsonSerializer.Deserialize<string>(param);
                            OnScriptEnd(scriptId);
                        }
                        break;

                    case "unsafeWindow":
                    case "window.focus":
                    case "window.close":
                        if (param != null)
                        {
                            var apiParam = JsonSerializer.Deserialize<ApiParam>(param);
                            TryExecuteApi(msg, apiParam.ScriptId, () => result = "true");
                        }
                        break;

                    case "GM_log":
                        if (param != null)
                        {
                            var apiParam = JsonSerializer.Deserialize<ApiParam>(param);
                            TryExecuteApi(msg, apiParam.ScriptId, () => GM_log(apiParam));
                        }
                        break;

                    case "GM_getValue":
                        if (param != null)
                        {
                            var apiParam = JsonSerializer.Deserialize<ApiParam>(param);
                            TryExecuteApi(msg, apiParam.ScriptId, () => result = GM_getValue(apiParam));
                        }
                        break;

                    case "GM_setValue":
                        if (param != null)
                        {
                            var apiParam = JsonSerializer.Deserialize<ApiParam>(param);
                            TryExecuteApi(msg, apiParam.ScriptId, () => GM_setValue(apiParam));
                        }
                        break;

                    case "GM_deleteValue":
                        if (param != null)
                        {
                            var apiParam = JsonSerializer.Deserialize<ApiParam>(param);
                            TryExecuteApi(msg, apiParam.ScriptId, () => GM_deleteValue(apiParam));
                        }
                        break;

                    case "GM_listValues":
                        if (param != null)
                        {
                            var apiParam = JsonSerializer.Deserialize<ApiParam>(param);
                            TryExecuteApi(msg, apiParam.ScriptId, () => result = JsonSerializer.Serialize(GM_listValues(apiParam)));
                        }
                        break;

                    default:
                        ConsoleLog($"Unhandled message: {msg}");
                        break;
                }
            }

            return result;
        }


        /// <summary>
        /// Logs a message to the console.
        /// </summary>
        /// <param name="apiParam">The parameters from the API call.</param>
        protected virtual void GM_log(ApiParam apiParam)
        {
            JsObject message = apiParam.GetData<JsObject>();
            ConsoleLog(message.Json, true);
        }


        /// <summary>
        /// Retrieves the value of a specific key from the userscript's storage.
        /// </summary>
        /// <param name="apiParam">The parameters from the API call.</param>
        /// <returns>The JavaScript object in JSON format, or null if result is undefined.</returns>
        protected virtual string GM_getValue(ApiParam apiParam)
        {
            IDataStore service = DataStore ?? MemDataStore.Instance;
            service.Retrieve(GetDataStoreContext(apiParam.ScriptId), apiParam.GetData<string>(), out string value);
            return value;
        }


        /// <summary>
        /// Sets the value of a specific key in the userscript's storage.
        /// </summary>
        /// <param name="apiParam">The parameters from the API call.</param>
        protected virtual void GM_setValue(ApiParam apiParam)
        {
            StorePair pair = apiParam.GetData<StorePair>();
            IDataStore service = DataStore ?? MemDataStore.Instance;
            service.Store(GetDataStoreContext(apiParam.ScriptId), pair.Key, pair.Value.Json);
        }


        /// <summary>
        /// Deletes a key from the userscript's storage.
        /// </summary>
        /// <param name="apiParam">The parameters from the API call.</param>
        protected virtual void GM_deleteValue(ApiParam apiParam)
        {
            IDataStore service = DataStore ?? MemDataStore.Instance;
            service.Remove(GetDataStoreContext(apiParam.ScriptId), apiParam.GetData<string>());
        }


        /// <summary>
        /// Retrieves an array of all keys in the userscript's storage.
        /// </summary>
        /// <param name="apiParam">The parameters from the API call.</param>
        /// <returns>An array of keys, if no key found, returns an empty array.</returns>
        protected virtual string[] GM_listValues(ApiParam apiParam)
        {
            IDataStore service = DataStore ?? MemDataStore.Instance;
            return service.ListKeys(GetDataStoreContext(apiParam.ScriptId));
        }


        /// <summary>
        /// Gets the data store context for the specified script ID.
        /// </summary>
        protected string GetDataStoreContext(string scriptId)
        {
            if (TryGetScriptById(scriptId, out JScript script))
            {
                return $"{script.Metadata.Namespace}_{script.Metadata.Name}";
            }
            else
            {
                return scriptId;
            }
        }


        /// <summary>
        /// Logs a message to the console.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="isJson">True if the message is JSON string.</param>
        protected void ConsoleLog(string message, bool isJson = false)
        {
            ExecuteScriptAsync("__MonkeySharp.consoleLog", message, isJson);
        }


        /// <summary>
        /// Verifies the access permission of the specified API.
        /// </summary>
        protected virtual bool VerifyApiAccessPermission(string api, string scriptId)
        {
            if (TryGetScriptById(scriptId, out JScript script))
            {
                return script.Metadata.Grant.Contains(api);
            }
            return false;
        }


        /// <summary>
        /// Called when the access to an API is denied.
        /// </summary>
        protected virtual void OnApiAccessDenied(string api, string scriptId)
        {
            string scriptName = GetScriptNameOrId(scriptId);
            ConsoleLog($"Script '{scriptName}' access to API '{api}' is denied.");
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
            return TryGetScriptById(scriptId, out JScript script) ? script.Metadata.Name : scriptId;
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
                if (script.Metadata.RunAt == runAt && script.MatchUrl(url))
                {
                    // Script start
                    scriptInjectionBuilder.AppendLine($"__MonkeySharp_CurrentScriptId = '{script.ScriptId}';");
                    scriptInjectionBuilder.AppendLine("__MonkeySharp.sendMsg('script-start', __MonkeySharp_CurrentScriptId);");
                    scriptInjectionBuilder.AppendLine("try { with (this) { (() => {");

                    // Add 'use strict' if needed
                    if (ForceUseStrict)
                        scriptInjectionBuilder.AppendLine("'use strict';");

                    // Add unsafeWindow if needed
                    if (script.Metadata.Grant.Contains("unsafeWindow"))
                        scriptInjectionBuilder.AppendLine("const unsafeWindow = this.unsafeWindow;");

                    // Prevent script from accessing internal objects
                    scriptInjectionBuilder.AppendLine("var __MonkeySharp_CurrentScriptId = undefined;");
                    scriptInjectionBuilder.AppendLine("var __MonkeySharp_AsyncMessenger = undefined;");
                    scriptInjectionBuilder.AppendLine("var __MonkeySharp_Messenger = undefined;");
                    scriptInjectionBuilder.AppendLine("var __MonkeySharp_Injected = undefined");
                    scriptInjectionBuilder.AppendLine("var __MonkeySharp = undefined;");

                    // Add the script
                    scriptInjectionBuilder.AppendLine(script.ScriptText);

                    // Script end
                    scriptInjectionBuilder.AppendLine("})(); } } catch (e) { __MonkeySharp.consoleLog(e); }");
                    scriptInjectionBuilder.AppendLine($"__MonkeySharp.sendMsg('script-end', __MonkeySharp_CurrentScriptId);");
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
