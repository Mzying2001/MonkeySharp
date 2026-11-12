using System;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed class CompatibilityStatusViewModel
    {
        public CompatibilityStatusViewModel(string category, string message, string brush)
        {
            Category = category ?? throw new ArgumentNullException(nameof(category));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Brush = brush ?? throw new ArgumentNullException(nameof(brush));
        }

        public string Category { get; }
        public string Message { get; }
        public string Brush { get; }
        public string Display => Category + ": " + Message;
    }
}
