using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Mzying2001.MonkeySharp.Demo.Runtime.SmokeBrowser;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class ScriptMenuSmokeTest
    {
        private static MenuItem Menu => (MenuItem)Application.Current.MainWindow.FindName("ScriptMenu");

        internal static Task WaitVisibilityAsync(bool visible) => WaitAsync(() => Task.FromResult(
            Menu.Visibility == (visible ? Visibility.Visible : Visibility.Collapsed)));

        private static async Task<MenuItem[]> OpenAsync()
        {
            await WaitVisibilityAsync(true);
            Application.Current.MainWindow.Activate();
            Menu.Focus();
            Menu.IsSubmenuOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Input);
            await WaitAsync(() => Task.FromResult(Menu.Items.Count > 0 && Enumerable.Range(0, Menu.Items.Count)
                .All(index => Menu.ItemContainerGenerator.ContainerFromIndex(index) is MenuItem)));
            return Enumerable.Range(0, Menu.Items.Count).Select(index => (MenuItem)Menu.ItemContainerGenerator.ContainerFromIndex(index)).ToArray();
        }

        internal static async Task VerifyGroupsAsync(BrowserTabViewModel tab, Dictionary<string, bool> checks, string screenshot)
        {
            var items = await OpenAsync();
            try
            {
                var headers = items.Where(item => ((ScriptMenuRow)item.DataContext).IsHeader).ToArray();
                var commands = items.Except(headers).ToArray();
                checks["menuScriptNames"] = headers.Select(item => (string)item.Header).OrderBy(name => name)
                    .SequenceEqual(new[] { "Demo all-services smoke", "merged-menu" });
                checks["menuGroupHeaders"] = headers.All(item => !item.IsEnabled && !item.Focusable && item.Command == null);
                checks["menuGroupTextAlignment"] = headers.All(header =>
                {
                    var key = ((ScriptMenuRow)header.DataContext).ScriptKey;
                    var command = commands.First(item => ((ScriptMenuRow)item.DataContext).ScriptKey == key);
                    return Math.Abs(LabelLeft(header) - LabelLeft(command)) < 0.5;
                });
                checks["menuCommandLabels"] = commands.Select(item => (string)item.Header).OrderBy(name => name)
                    .SequenceEqual(new[] { "Demo smoke menu", "Merged command" });
                checks["menuCommandBindings"] = commands.All(item =>
                {
                    var row = (ScriptMenuRow)item.DataContext;
                    return ReferenceEquals(item.Command, row.Command.InvokeCommand) && Equals(item.ToolTip, row.Command.Title) &&
                        (item.InputGestureText ?? "") == (row.Command.AccessKey ?? "") && item.StaysOpenOnClick == row.Command.StaysOpenOnClick;
                });
                var focused = commands[0].Focus();
                var down = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(commands[0]), 0, Key.Down)
                    { RoutedEvent = Keyboard.KeyDownEvent };
                InputManager.Current.ProcessInput(down);
                checks["menuKeyboardNavigation"] = focused && ReferenceEquals(Keyboard.FocusedElement, commands[1]) && headers.All(item => !item.Focus());
                checks["menuRowsMatchCommands"] = items.Length == tab.MenuCommands.Count + headers.Length;
                var root = (UIElement)PresentationSource.FromVisual(items[0]).RootVisual;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.RenderSize.Width), (int)Math.Ceiling(root.RenderSize.Height), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(screenshot)) encoder.Save(output);
            }
            finally { Menu.IsSubmenuOpen = false; }
        }

        private static double LabelLeft(MenuItem item)
        {
            var label = (string)item.Header;
            var text = Descendants(item).OfType<FrameworkElement>().First(element =>
                element is AccessText access && access.Text == label || element is TextBlock block && block.Text == label);
            var root = (Visual)PresentationSource.FromVisual(item).RootVisual;
            return text.TransformToAncestor(root).Transform(new Point()).X;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }

        internal static async Task<bool> InvokeAsync(ScriptMenuCommand command)
        {
            var item = (await OpenAsync()).Single(candidate => ReferenceEquals(((ScriptMenuRow)candidate.DataContext).Command, command));
            var clicked = false;
            RoutedEventHandler onClick = (_, __) => clicked = true;
            item.Click += onClick;
            try
            {
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
                ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitAsync(() => Task.FromResult(clicked));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                return Menu.IsSubmenuOpen == command.StaysOpenOnClick;
            }
            finally
            {
                item.Click -= onClick;
                Menu.IsSubmenuOpen = false;
            }
        }
    }
}
