using Mzying2001.MonkeySharp.Demo.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Mzying2001.MonkeySharp.Demo.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed partial class BrowserTabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = Strings.Get("NewTab");

        [ObservableProperty]
        private string _address;

        [ObservableProperty]
        private string _iconUrl;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _canGoBack;

        [ObservableProperty]
        private bool _canGoForward;

        [ObservableProperty]
        private string _error;

        private readonly ObservableCollection<ScriptMenuRow> _menuRows = new ObservableCollection<ScriptMenuRow>();
        public ReadOnlyObservableCollection<ScriptMenuRow> MenuRows { get; }

        public string TabId { get; } = Guid.NewGuid().ToString("N");
        public bool IsIncognito { get; }
        public string PrivacyLabel => IsIncognito ? Strings.Get("PrivateLabel") : string.Empty;
        public string ParentTabId { get; set; }
        public IBrowserCommands Browser { get; set; }
        public ObservableCollection<Services.ScriptMenuCommand> MenuCommands { get; } = new ObservableCollection<Services.ScriptMenuCommand>();
        public bool IsClosed { get; private set; }

        public event EventHandler Closed;

        public BrowserTabViewModel(string address, bool isIncognito = false)
        {
            MenuRows = new ReadOnlyObservableCollection<ScriptMenuRow>(_menuRows);
            MenuCommands.CollectionChanged += (_, __) => UpdateMenuRows();
            Address = address;
            IsIncognito = isIncognito;
        }

        private void UpdateMenuRows()
        {
            var headers = _menuRows.Where(row => row.IsHeader).ToDictionary(row => row.ScriptKey);
            var commands = _menuRows.Where(row => !row.IsHeader).ToDictionary(row => row.Command);
            var rows = new List<ScriptMenuRow>();
            foreach (var group in MenuCommands.GroupBy(command => command.ScriptKey))
            {
                if (!headers.TryGetValue(group.Key, out var header)) header = new ScriptMenuRow(group.First(), true);
                header.HasSeparator = rows.Count != 0;
                rows.Add(header);
                foreach (var command in group)
                    rows.Add(commands.TryGetValue(command, out var row) ? row : new ScriptMenuRow(command, false));
            }

            // Keep existing containers and avoid transiently emptying an open menu
            // when scripts add or remove commands with autoClose disabled.
            for (var index = 0; index < rows.Count; index++)
            {
                if (index < _menuRows.Count && ReferenceEquals(_menuRows[index], rows[index])) continue;
                var oldIndex = _menuRows.IndexOf(rows[index]);
                if (oldIndex >= 0) _menuRows.Move(oldIndex, index);
                else _menuRows.Insert(index, rows[index]);
            }
            while (_menuRows.Count > rows.Count) _menuRows.RemoveAt(_menuRows.Count - 1);
        }

        internal void Close()
        {
            if (IsClosed) return;
            IsClosed = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
