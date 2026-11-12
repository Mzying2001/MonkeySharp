using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class WpfWindowService : IUserScriptWindowService
    {
        private readonly MainWindowViewModel _main;
        private readonly BrowserTabViewModel _origin;
        private readonly Dispatcher _dispatcher;

        public WpfWindowService(MainWindowViewModel main, BrowserTabViewModel origin, Dispatcher dispatcher)
        {
            _main = main;
            _origin = origin;
            _dispatcher = dispatcher;
        }

        public async Task<bool> CloseAsync(DocumentFrame frame, CancellationToken cancellationToken)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_origin.IsClosed || _main.Tabs.Count <= 1) return false;
                _main.CloseTab(_origin);
                return true;
            });
        }

        public async Task<bool> FocusAsync(DocumentFrame frame, CancellationToken cancellationToken)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_origin.IsClosed || !_main.Tabs.Contains(_origin)) return false;
                _main.SelectedTab = _origin;
                return true;
            });
        }
    }
}
