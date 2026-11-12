using CommunityToolkit.Mvvm.ComponentModel;
using Mzying2001.MonkeySharp.Core.Domain;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class ScriptMenuRow : ObservableObject
    {
        private bool _hasSeparator;
        public ScriptKey ScriptKey { get; }
        public string Label { get; }
        public ScriptMenuCommand Command { get; }
        public bool IsHeader => Command == null;
        public bool HasSeparator
        {
            get => _hasSeparator;
            internal set => SetProperty(ref _hasSeparator, value);
        }

        internal ScriptMenuRow(ScriptMenuCommand command, bool isHeader)
        {
            ScriptKey = command.ScriptKey;
            Label = isHeader ? command.ScriptName : command.Label;
            Command = isHeader ? null : command;
        }
    }
}
