using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class WpfNotificationService : INotificationService
    {
        private readonly Dispatcher _dispatcher;
        private readonly HttpContentService _content;
        private readonly MainWindowViewModel _main;
        private readonly BrowserTabViewModel _origin;
        public WpfNotificationService(Dispatcher dispatcher, HttpContentService content, MainWindowViewModel main, BrowserTabViewModel origin)
        {
            _dispatcher = dispatcher;
            _content = content;
            _main = main;
            _origin = origin;
        }
        public INotificationHandle Create(UserScriptNotificationRequest request, CancellationToken cancellationToken)
            => new NotificationHandle(this, request, cancellationToken);

        private sealed class NotificationHandle : INotificationHandle
        {
            private readonly WpfNotificationService _owner;
            private readonly UserScriptNotificationRequest _request;
            private readonly CancellationToken _token;
            private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            private CancellationTokenRegistration _registration;
            private readonly CancellationTokenSource _images = new CancellationTokenSource();
            private DispatcherTimer _timeout;
            private Window _window;
            private int _closed;
            private int _started;
            private int _clicked;
            public NotificationHandle(WpfNotificationService owner, UserScriptNotificationRequest request, CancellationToken token)
            {
                _owner = owner;
                _request = request;
                _token = token;
            }
            public void Start()
            {
                if (Interlocked.Exchange(ref _started, 1) != 0) return;
                _registration = _token.Register(Dispose);
                _owner._dispatcher.BeginInvoke(new Action(ShowNotification));
            }

            private void ShowNotification()
            {
                if (_closed != 0) return;
                try
                {
                    if (_request.Highlight && !_owner._origin.IsClosed)
                    {
                        _owner._main.SelectedTab = _owner._origin;
                        Application.Current.MainWindow.Activate();
                    }
                    if (string.IsNullOrEmpty(_request.Text))
                    {
                        Finish();
                        return;
                    }
                    var panel = new StackPanel { Margin = new Thickness(20) };
                    panel.Children.Add(new TextBlock { Text = _request.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
                    var click = new Button { Content = "查看 / 点击通知", Margin = new Thickness(0, 15, 0, 0) };
                    click.Click += (_, __) => { if (Interlocked.Exchange(ref _clicked, 1) == 0) Clicked?.Invoke(this, EventArgs.Empty); };
                    panel.Children.Add(click);
                    var close = new Button { Content = "关闭" };
                    close.Click += (_, __) => Dispose();
                    panel.Children.Add(close);
                    _window = new Window
                    {
                        Title = _request.Title ?? "MonkeySharp",
                        Content = panel,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        Owner = Application.Current.MainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    _window.Closed += (_, __) => Finish();
                    _window.Show();
                    if (!_request.Silent) System.Media.SystemSounds.Asterisk.Play();
                    if (_request.Timeout > 0)
                    {
                        _timeout = new DispatcherTimer(DispatcherPriority.Normal, _owner._dispatcher) { Interval = TimeSpan.FromMilliseconds(_request.Timeout.Value) };
                        _timeout.Tick += TimeoutElapsed;
                        _timeout.Start();
                    }
                    if (!string.IsNullOrWhiteSpace(_request.ImageUrl)) _ = LoadImageAsync(panel);
                }
                catch (Exception error)
                {
                    _completion.TrySetException(error);
                    Dispose();
                }
            }

            public void ResolveClick(bool preventDefault)
            {
                if (Volatile.Read(ref _clicked) == 0 || Volatile.Read(ref _closed) != 0) return;
                _owner._dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_closed != 0) return;
                    if (!preventDefault && _request.Url != null) _owner._main.NewTab(_request.Url.AbsoluteUri, origin: _owner._origin);
                    Dispose();
                }));
            }
            private void TimeoutElapsed(object sender, EventArgs args) => Dispose();
            private async Task LoadImageAsync(StackPanel panel)
            {
                try
                {
                    var content = await _owner._content.FetchAsync(_request.ImageUrl, _images.Token);
                    if (_closed != 0) return;
                    using (var input = new MemoryStream(content.Bytes))
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.DecodePixelWidth = 80;
                        image.StreamSource = input;
                        image.EndInit();
                        image.Freeze();
                        panel.Children.Insert(0, new Image { Source = image, Width = 80, Height = 80, Margin = new Thickness(0, 0, 0, 10) });
                    }
                }
                catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Notification image unavailable: " + error.Message); }
            }
            public Task Completion => _completion.Task;
            public event EventHandler Clicked, Closed;
            private void Finish()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                if (_timeout != null)
                {
                    _timeout.Stop();
                    _timeout.Tick -= TimeoutElapsed;
                    _timeout = null;
                }
                _images.Cancel();
                _registration.Dispose();
                Closed?.Invoke(this, EventArgs.Empty);
                _completion.TrySetResult(null);
            }
            public void Dispose()
            {
                if (_owner._dispatcher.CheckAccess())
                {
                    Finish();
                    _window?.Close();
                }
                else if (!_owner._dispatcher.HasShutdownStarted)
                {
                    _owner._dispatcher.BeginInvoke(new Action(Dispose));
                }
                else
                {
                    Interlocked.Exchange(ref _closed, 1);
                    _completion.TrySetResult(null);
                }
            }
        }
    }
}
