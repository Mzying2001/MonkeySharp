using Mzying2001.MonkeySharp.Demo.Localization;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Demo.Persistence;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mzying2001.MonkeySharp.Demo.Views
{
    public partial class PermissionsWindow : Window
    {
        private readonly SqlitePermissionDecisionStore _decisions;
        private readonly Func<string, Task> _revoke;

        internal PermissionsWindow(SqlitePermissionDecisionStore decisions, Func<string, Task> revoke)
        {
            InitializeComponent();
            _decisions = decisions;
            _revoke = revoke;
        }

        internal async Task LoadAsync()
        {
            var entries = await _decisions.ListAsync(CancellationToken.None);
            foreach (var group in entries.GroupBy(entry => entry.ScriptKey))
            {
                EntriesPanel.Children.Add(new TextBlock { Text = group.Key, Margin = new Thickness(0, 16, 0, 4) });
                foreach (var entry in group)
                {
                    var decision = await _decisions.GetAsync(entry, CancellationToken.None);
                    EntriesPanel.Children.Add(new TextBlock
                    {
                        Text = entry.Kind + " · " + entry.Target + " · " + decision,
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                var revoke = new Button
                {
                    Content = Strings.Get("RevokeScriptPermissions"),
                    Tag = group.Key,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                revoke.Click += RevokeClicked;
                EntriesPanel.Children.Add(revoke);
            }

            if (entries.Count == 0)
                EntriesPanel.Children.Add(new TextBlock { Text = Strings.Get("NoRememberedPermissions"), Margin = new Thickness(0, 15, 0, 0) });
        }

        private async void RevokeClicked(object sender, RoutedEventArgs args)
        {
            await _revoke((string)((Button)sender).Tag);
            Close();
        }
    }
}
