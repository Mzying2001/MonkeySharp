using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class WpfTabService : ITabService
    {
        public bool SupportsIncognito => true;
        private readonly MainWindowViewModel _main;
        private readonly BrowserTabViewModel _origin;
        private readonly Dispatcher _dispatcher;

        public WpfTabService(MainWindowViewModel main, BrowserTabViewModel origin, Dispatcher dispatcher)
        {
            _main = main;
            _origin = origin;
            _dispatcher = dispatcher;
        }

        public async Task<ITabHandle> OpenAsync(OpenTabRequest request, CancellationToken cancellationToken)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_origin.IsClosed) throw new OperationCanceledException();
                var tab = _main.NewTab(request.Url.AbsoluteUri, request.Active, _origin, request.Insert, request.SetParent, request.InsertIndex, request.Incognito);
                return (ITabHandle)new TabHandle(_main, tab, _dispatcher);
            });
        }

        private sealed class TabHandle : ITabHandle
        {
            private readonly MainWindowViewModel _main;
            private readonly BrowserTabViewModel _tab;
            private readonly Dispatcher _dispatcher;
            public string TabId => _tab.TabId;
            public bool Closed => _tab.IsClosed;
            public event EventHandler OnClose;

            public TabHandle(MainWindowViewModel main, BrowserTabViewModel tab, Dispatcher dispatcher)
            {
                _main = main;
                _tab = tab;
                _dispatcher = dispatcher;
                _tab.Closed += TabClosed;
            }

            private void TabClosed(object sender, EventArgs args) => OnClose?.Invoke(this, EventArgs.Empty);

            public async Task CloseAsync(CancellationToken cancellationToken)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _main.CloseTab(_tab);
                });
            }

            public void Dispose()
            {
                _tab.Closed -= TabClosed;
            }
        }
    }
}
