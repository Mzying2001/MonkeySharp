using Mzying2001.MonkeySharp.Demo.Localization;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public enum ScriptStatusFilter { All, Enabled, Disabled, Unavailable }

    public sealed class ScriptStatusFilterOption
    {
        public ScriptStatusFilterOption(ScriptStatusFilter value) { Value = value; }
        public ScriptStatusFilter Value { get; }
        public string Label => Strings.Get(Value.ToString());
    }
}
