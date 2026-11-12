using Mzying2001.MonkeySharp.Demo.Localization;
using Mzying2001.MonkeySharp.Core.Permissions;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class WpfPermissionPrompt : IUserScriptPermissionPrompt
    {
        private readonly Dispatcher _dispatcher;
        public event Action<Window> Opened;
        public WpfPermissionPrompt(Dispatcher dispatcher) { _dispatcher = dispatcher; }
        public Task<UserScriptPermissionChoice> PromptAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token)
            => PromptCoreAsync(request, scope, token, false);
        public IUserScriptPermissionPrompt ForPrivateContext() => new PrivatePrompt(this);
        private async Task<UserScriptPermissionChoice> PromptCoreAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token, bool isPrivate)
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
                    if (isPrivate) panel.Children.Add(new TextBlock { Text = Strings.Get("PrivatePermissionHint"), TextWrapping = TextWrapping.Wrap });
                    panel.Children.Add(new TextBlock { Text = Strings.Get("PermissionHint"), MaxWidth = 440, TextWrapping = TextWrapping.Wrap });
                    var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
                    Action<string, string, PermissionDecision, bool> add = (label, automationId, decision, remember) =>
                    {
                        var button = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
                        AutomationProperties.SetAutomationId(button, automationId);
                        button.Click += (_, __) => { completion.TrySetResult(new UserScriptPermissionChoice(decision, remember)); dialog.Close(); };
                        buttons.Children.Add(button);
                    };
                    add(Strings.Get("AllowOnce"), "PermissionAllowOnce", PermissionDecision.Allow, false);
                    add(Strings.Get("RememberAllow"), "PermissionRememberAllow", PermissionDecision.Allow, true);
                    add(Strings.Get("RememberDeny"), "PermissionRememberDeny", PermissionDecision.Deny, true);
                    panel.Children.Add(buttons);
                    dialog = new Window { Title = isPrivate ? Strings.Get("PrivatePermissionTitle") : Strings.Get("PermissionTitle"), Content = panel, Width = 520, SizeToContent = SizeToContent.Height,
                        ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible) dialog.Owner = Application.Current.MainWindow;
                    dialog.Closed += (_, __) => completion.TrySetResult(new UserScriptPermissionChoice(PermissionDecision.Deny));
                    dialog.Show();
                    Opened?.Invoke(dialog);
                });
                return await completion.Task;
            }
        }
        private sealed class PrivatePrompt : IUserScriptPermissionPrompt
        {
            private readonly WpfPermissionPrompt _owner;
            public PrivatePrompt(WpfPermissionPrompt owner) { _owner = owner; }
            public Task<UserScriptPermissionChoice> PromptAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token)
                => _owner.PromptCoreAsync(request, scope, token, true);
        }
    }
}
