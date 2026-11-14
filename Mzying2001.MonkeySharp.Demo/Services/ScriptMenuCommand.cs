using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed partial class ScriptMenuCommand : ObservableObject
    {
        internal readonly Dictionary<Action<MenuCommandInvocation>, bool> Subscribers = new Dictionary<Action<MenuCommandInvocation>, bool>();
        public string Label { get; }
        public ScriptKey ScriptKey { get; }
        public string ScriptName { get; }
        public string AccessKey { get; }
        public string Title { get; }
        public bool StaysOpenOnClick => Subscribers.Values.Any(autoClose => !autoClose);
        internal ScriptMenuCommand(MenuCommandRequest request, string scriptName)
        {
            Label = request.Name;
            ScriptKey = request.ScriptKey;
            ScriptName = scriptName;
            AccessKey = request.AccessKey;
            Title = request.Title;
        }
        internal void Changed() => OnPropertyChanged(nameof(StaysOpenOnClick));
        [RelayCommand]
        private void Invoke(MenuCommandInvocation invocation)
        {
            Runtime.SmokeTrace.Write("menu-invoked", new { command = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this), Label, subscribers = Subscribers.Count });
            foreach (var callback in Subscribers.Keys.ToArray())
            {
                try { callback(invocation ?? new MenuCommandInvocation()); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            }
        }
    }
}
