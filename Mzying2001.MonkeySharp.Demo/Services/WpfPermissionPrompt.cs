using Mzying2001.MonkeySharp.Core.Permissions;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class WpfPermissionPrompt : IUserScriptPermissionPrompt
    {
        private readonly Dispatcher _dispatcher;
        public event Action<Window> Opened;
        public WpfPermissionPrompt(Dispatcher dispatcher) { _dispatcher = dispatcher; }
        public async Task<UserScriptPermissionChoice> PromptAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token)
        {
            Window dialog = null;
            var completion = new TaskCompletionSource<UserScriptPermissionChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => { completion.TrySetCanceled(); if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(new Action(() => dialog?.Close())); }))
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    var panel = new StackPanel { Margin = new Thickness(20) };
                    panel.Children.Add(new TextBlock { Text = request.Installation.Definition.Metadata.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    panel.Children.Add(new TextBlock { Text = scope.Kind + "\n" + scope.Target, Margin = new Thickness(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap });
                    panel.Children.Add(new TextBlock { Text = "允许一次仅适用于当前合并等待的请求；记忆决定仅适用于此安装、权限和目标。导航会取消失效的等待。", MaxWidth = 440, TextWrapping = TextWrapping.Wrap });
                    var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
                    Action<string, PermissionDecision, bool> add = (label, decision, remember) =>
                    {
                        var button = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
                        button.Click += (_, __) => { completion.TrySetResult(new UserScriptPermissionChoice(decision, remember)); dialog.Close(); };
                        buttons.Children.Add(button);
                    };
                    add("允许一次", PermissionDecision.Allow, false);
                    add("记忆允许", PermissionDecision.Allow, true);
                    add("拒绝并记住", PermissionDecision.Deny, true);
                    panel.Children.Add(buttons);
                    dialog = new Window { Title = "MonkeySharp 权限请求", Content = panel, Width = 520, SizeToContent = SizeToContent.Height,
                        ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible) dialog.Owner = Application.Current.MainWindow;
                    dialog.Closed += (_, __) => completion.TrySetResult(new UserScriptPermissionChoice(PermissionDecision.Deny));
                    dialog.Show();
                    Opened?.Invoke(dialog);
                });
                return await completion.Task;
            }
        }
    }
}
