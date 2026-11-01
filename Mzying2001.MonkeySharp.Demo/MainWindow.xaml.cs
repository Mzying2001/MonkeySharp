using Mzying2001.MonkeySharp.Demo.Runtime;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Demo.Views;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mzying2001.MonkeySharp.Demo
{
    public partial class MainWindow : Window
    {
        private readonly DemoRuntime _runtime;
        private readonly Dictionary<BrowserTabViewModel, BrowserTabView> _views = new Dictionary<BrowserTabViewModel, BrowserTabView>();
        private ScriptManagerWindow _manager;
        private bool _closing;
        public MainWindow(DemoRuntime runtime)
        {
            InitializeComponent();
            _runtime = runtime; DataContext = runtime.MainWindow;
            TrustWarning.Text = DemoRuntime.TrustWarning;
            runtime.MainWindow.Tabs.CollectionChanged += TabsChanged;
            runtime.MainWindow.PropertyChanged += ModelChanged;
            runtime.MainWindow.ScriptManagerRequested += OpenManager;
            Closing += WindowClosing;
            PreviewKeyDown += (sender, args) => { if (args.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { AddressBar.Focus(); AddressBar.SelectAll(); args.Handled = true; } };
            PreviewKeyDown += (sender, args) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Alt) return;
                var key = (args.Key == Key.System ? args.SystemKey : args.Key).ToString();
                var command = runtime.MainWindow.SelectedTab?.MenuCommands.FirstOrDefault(item => string.Equals(item.AccessKey, key, StringComparison.OrdinalIgnoreCase));
                if (command != null) { command.InvokeCommand.Execute(null); args.Handled = true; }
            };
        }

        private async void TabsChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            foreach (var tab in _views.Keys.Where(tab => !_runtime.MainWindow.Tabs.Contains(tab)).ToArray())
            {
                var view = _views[tab]; _views.Remove(tab); BrowserPanels.Children.Remove(view);
                try { await view.DisposeAsync(); } catch (Exception exception) { _runtime.MainWindow.Diagnostics.Report(exception.ToString()); }
            }
            foreach (var tab in _runtime.MainWindow.Tabs.Where(tab => !_views.ContainsKey(tab)).ToArray())
            {
                try
                {
                    var view = new BrowserTabView(_runtime, tab);
                    _views.Add(tab, view); BrowserPanels.Children.Add(view);
                }
                catch (Exception exception) { tab.Error = exception.Message; _runtime.MainWindow.Diagnostics.Report(exception.ToString()); }
            }
            UpdateSelection();
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(MainWindowViewModel.SelectedTab)) UpdateSelection(); }
        private void UpdateSelection()
        {
            foreach (var pair in _views) pair.Value.Visibility = pair.Key == _runtime.MainWindow.SelectedTab ? Visibility.Visible : Visibility.Hidden;
        }
        private void OpenManager(object sender, EventArgs args)
        {
            if (_manager == null)
            {
                _manager = new ScriptManagerWindow(_runtime) { Owner = this };
                _manager.Closed += (closedSender, closedArgs) => _manager = null;
                _manager.Show();
            }
            else _manager.Activate();
        }
        private void AddressKeyDown(object sender, KeyEventArgs args)
        { if (args.Key == Key.Enter) { _runtime.MainWindow.NavigateCommand.Execute(null); args.Handled = true; } }
        private void BrowserTabsPreviewMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton != MouseButton.Middle) return;
            var item = ItemsControl.ContainerFromElement(BrowserTabs, args.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.DataContext is BrowserTabViewModel tab)
            {
                _runtime.MainWindow.CloseTabCommand.Execute(tab);
                args.Handled = true;
            }
        }
        private void BrowserTabsPreviewMouseWheel(object sender, MouseWheelEventArgs args)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(BrowserTabs);
            if (scrollViewer == null || scrollViewer.ScrollableWidth <= 0) return;

            var offset = Math.Max(0, Math.Min(scrollViewer.ScrollableWidth,
                scrollViewer.HorizontalOffset - args.Delta));
            if (Math.Abs(offset - scrollViewer.HorizontalOffset) < double.Epsilon) return;

            scrollViewer.ScrollToHorizontalOffset(offset);
            args.Handled = true;
        }
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match) return match;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }
        private void OpenDataDirectory(object sender, RoutedEventArgs args) => OpenDirectory(_runtime.Paths.DataDirectory);
        private void OpenDownloadsDirectory(object sender, RoutedEventArgs args) => OpenDirectory(_runtime.Paths.DownloadsDirectory);
        private void OpenDirectory(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception exception) { _runtime.MainWindow.Diagnostics.Report(exception.Message); }
        }

        private async void WindowClosing(object sender, CancelEventArgs args)
        {
            args.Cancel = true;
            if (_closing) return;
            if (_manager != null && !_manager.CanClose()) return;
            _closing = true; IsEnabled = false;
            _manager?.Close();
            try { await _runtime.ShutdownAsync(); }
            catch (Exception exception) { MessageBox.Show(exception.Message, "关闭失败"); }
            _runtime.MainWindow.Tabs.CollectionChanged -= TabsChanged;
            _runtime.MainWindow.PropertyChanged -= ModelChanged;
            _runtime.MainWindow.ScriptManagerRequested -= OpenManager;
            Closing -= WindowClosing;
            Close(); Application.Current.Shutdown();
        }

        internal async Task CloseForSmokeAsync(int exitCode)
        {
            _closing = true;
            await _runtime.ShutdownAsync();
            Closing -= WindowClosing;
            Close(); Application.Current.Shutdown(exitCode);
        }
    }
}
