using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed partial class ScriptMenuCommand : ObservableObject, IMenuRegistration
    {
        private readonly Action<ScriptMenuCommand> _remove;
        private readonly Action _invoked;
        private int _disposed;

        public string Label { get; }
        public string AccessKey { get; }

        public ScriptMenuCommand(string label, string accessKey, Action invoked, Action<ScriptMenuCommand> remove)
        {
            Label = label;
            AccessKey = accessKey;
            _remove = remove;
            _invoked = invoked;
        }

        [RelayCommand]
        private void Invoke() { if (_disposed == 0) _invoked(); }

        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _remove(this); }
    }

    public sealed class WpfMenuService : IMenuService
    {
        private readonly BrowserTabViewModel _tab;
        private readonly Dispatcher _dispatcher;

        public WpfMenuService(BrowserTabViewModel tab, Dispatcher dispatcher)
        {
            _tab = tab;
            _dispatcher = dispatcher;
        }

        public async Task<IMenuRegistration> RegisterAsync(MenuCommandRequest request, Action invoked, CancellationToken cancellationToken)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_tab.IsClosed) throw new OperationCanceledException();
                var command = new ScriptMenuCommand(request.Name + " · " + request.ScriptKey.ToString().Substring(0, 8),
                    request.AccessKey, invoked, item => _dispatcher.BeginInvoke(new Action(() => _tab.MenuCommands.Remove(item))));
                _tab.MenuCommands.Add(command);
                return (IMenuRegistration)command;
            });
        }
    }

    public sealed class WpfClipboardService : IClipboardService
    {
        private readonly Dispatcher _dispatcher;

        public WpfClipboardService(Dispatcher dispatcher) { _dispatcher = dispatcher; }

        public async Task SetTextAsync(string text, string mediaType, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var data = new DataObject();
                        data.SetText(text ?? string.Empty, TextDataFormat.UnicodeText);
                        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                            data.SetData(DataFormats.Html, HtmlClipboard(text ?? string.Empty));
                        Clipboard.SetDataObject(data, true);
                    });
                    return;
                }
                catch (ExternalException) when (attempt < 4) { await Task.Delay(60, cancellationToken).ConfigureAwait(false); }
            }
        }

        private static string HtmlClipboard(string text)
        {
            const string header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string prefix = "<html><body><!--StartFragment-->";
            const string suffix = "<!--EndFragment--></body></html>";
            var startHtml = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
            var startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
            var endFragment = startFragment + Encoding.UTF8.GetByteCount(text);
            return string.Format(header, startHtml, endFragment + Encoding.UTF8.GetByteCount(suffix), startFragment, endFragment) + prefix + text + suffix;
        }
    }

    public sealed class WpfNotificationService : INotificationService
    {
        private readonly Dispatcher _dispatcher;
        private readonly HttpContentService _content;

        public WpfNotificationService(Dispatcher dispatcher, HttpContentService content)
        {
            _dispatcher = dispatcher;
            _content = content;
        }

        public INotificationHandle ShowAsync(UserScriptNotificationRequest request, CancellationToken cancellationToken)
            => new NotificationHandle(_dispatcher, _content, request, cancellationToken);

        private sealed class NotificationHandle : INotificationHandle
        {
            private readonly Dispatcher _dispatcher;
            private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            private CancellationTokenRegistration _registration;
            private readonly CancellationTokenSource _images = new CancellationTokenSource();
            private Window _window;
            private int _closed;

            public NotificationHandle(Dispatcher dispatcher, HttpContentService content, UserScriptNotificationRequest request, CancellationToken token)
            {
                _dispatcher = dispatcher;
                _registration = token.Register(Dispose);
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_closed != 0) return;
                    var panel = new StackPanel { Margin = new Thickness(20) };
                    panel.Children.Add(new TextBlock { Text = request.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
                    var click = new Button { Content = "查看 / 点击通知", Margin = new Thickness(0, 15, 0, 0) };
                    click.Click += (sender, args) => { Clicked?.Invoke(this, EventArgs.Empty); Dispose(); };
                    panel.Children.Add(click);
                    var close = new Button { Content = "关闭" };
                    close.Click += (sender, args) => Dispose();
                    panel.Children.Add(close);
                    _window = new Window
                    {
                        Title = request.Title ?? "MonkeySharp",
                        Content = panel,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        Owner = Application.Current.MainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    _window.Closed += (sender, args) => Finish();
                    _window.Show();
                    if (!string.IsNullOrWhiteSpace(request.ImageUrl)) _ = LoadImageAsync(content, request.ImageUrl, panel);
                }));
            }

            private async Task LoadImageAsync(HttpContentService content, string url, StackPanel panel)
            {
                try
                {
                    var imageContent = await content.FetchAsync(url, _images.Token);
                    if (_closed != 0) return;
                    using (var input = new MemoryStream(imageContent.Bytes))
                    {
                        var image = new BitmapImage();
                        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 80;
                        image.StreamSource = input; image.EndInit(); image.Freeze();
                        panel.Children.Insert(0, new Image { Source = image, Width = 80, Height = 80, Margin = new Thickness(0, 0, 0, 10) });
                    }
                }
                catch (Exception exception) { System.Diagnostics.Trace.TraceWarning("Notification image unavailable: " + exception.Message); }
            }

            public Task Completion => _completion.Task;
            public event EventHandler Clicked;
            public event EventHandler Closed;

            private void Finish()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                _images.Cancel();
                _registration.Dispose();
                Closed?.Invoke(this, EventArgs.Empty);
                _completion.TrySetResult(null);
            }

            public void Dispose()
            {
                if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(new Action(() =>
                {
                    Finish();
                    _window?.Close();
                }));
                else _completion.TrySetResult(null);
            }
        }
    }

    public sealed class WpfTabService : ITabService
    {
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
                var tab = _main.NewTab(request.Url.AbsoluteUri, request.Active, _origin, request.Insert, request.SetParent);
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
