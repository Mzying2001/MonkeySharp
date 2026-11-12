using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed partial class ScriptMenuCommand : ObservableObject
    {
        internal readonly Dictionary<Action<MenuCommandInvocation>, bool> Subscribers = new Dictionary<Action<MenuCommandInvocation>, bool>();
        public string Label { get; }
        public string AccessKey { get; }
        public string Title { get; }
        public bool StaysOpenOnClick => Subscribers.Values.Any(autoClose => !autoClose);
        internal ScriptMenuCommand(MenuCommandRequest request)
        {
            Label = request.Name + " · " + request.ScriptKey.ToString().Substring(0, 8);
            AccessKey = request.AccessKey;
            Title = request.Title;
        }
        internal void Changed() => OnPropertyChanged(nameof(StaysOpenOnClick));
        [RelayCommand]
        private void Invoke(MenuCommandInvocation invocation)
        {
            foreach (var callback in Subscribers.Keys.ToArray())
            {
                try { callback(invocation ?? new MenuCommandInvocation()); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            }
        }
    }
}
