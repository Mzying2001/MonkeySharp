using Mzying2001.MonkeySharp.Demo.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Core.Updates;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed partial class ScriptManagerViewModel : ObservableObject, IDisposable
    {
        private readonly IUserScriptRepository _repository;
        private readonly HttpContentService _content;
        private readonly UserScriptUpdateService _updates;
        private readonly Func<string, bool> _confirm;
        private readonly DiagnosticsViewModel _diagnostics;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        [ObservableProperty]
        private ScriptItemViewModel _selectedScript;

        private string _origin = "application://editor/new.user.js";

        [ObservableProperty]
        private string _url;

        [ObservableProperty]
        private string _search;

        [ObservableProperty]
        private ScriptStatusFilter _statusFilter = ScriptStatusFilter.All;

        [ObservableProperty]
        private bool _sortAscending = true;

        [ObservableProperty]
        private string _status;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool _busy;

        private bool _refreshing;

        public ScriptManagerViewModel(IUserScriptRepository repository, HttpContentService content, UserScriptUpdateService updates,
            Func<string, bool> confirm, DiagnosticsViewModel diagnostics)
        {
            _repository = repository;
            _content = content;
            _updates = updates;
            _confirm = confirm;
            _diagnostics = diagnostics;
            Editor.PropertyChanged += EditorPropertyChanged;
            FilteredScripts = CollectionViewSource.GetDefaultView(Scripts);
            FilteredScripts.Filter = FilterScript;
            ApplySort();
            Details.Update(Editor.Parse(), _origin, Editor.Source);
        }

        public ScriptEditorViewModel Editor { get; } = new ScriptEditorViewModel();
        public ScriptDetailsViewModel Details { get; } = new ScriptDetailsViewModel();
        public ObservableCollection<ScriptItemViewModel> Scripts { get; } = new ObservableCollection<ScriptItemViewModel>();
        public ICollectionView FilteredScripts { get; }
        public IReadOnlyList<ScriptStatusFilterOption> StatusFilters { get; } = Enum.GetValues(typeof(ScriptStatusFilter))
            .Cast<ScriptStatusFilter>().Select(value => new ScriptStatusFilterOption(value)).ToArray();
        public int FilteredCount => FilteredScripts.Cast<object>().Count();
        public string FilteredCountLabel => Strings.Format("MatchCount", FilteredCount);
        public bool HasScripts => Scripts.Count > 0;
        public bool HasFilteredScripts => FilteredCount > 0;
        public bool ShowNoScripts => !HasScripts;
        public bool ShowNoResults => HasScripts && !HasFilteredScripts;
        public string SortLabel => SortAscending ? Strings.Get("SortAscending") : Strings.Get("SortDescending");
        public bool CanEdit => !Busy;

        [RelayCommand]
        private void New() => NewScript();

        [RelayCommand]
        private async Task Refresh() => await RunAsync(async () => { if (ConfirmDiscard()) await RefreshAsync(); });

        [RelayCommand]
        private Task Save() => RunAsync(SaveAsync);

        [RelayCommand]
        private Task ToggleScript(ScriptItemViewModel item) => RunItemAsync(item, () => ToggleAsync(item));

        [RelayCommand]
        private Task Delete() => RunAsync(DeleteAsync);

        [RelayCommand]
        private Task InstallUrl() => RunAsync(LoadUrlAsync);

        [RelayCommand]
        private Task CheckUpdates() => RunAsync(CheckUpdatesAsync);

        [RelayCommand]
        private void ToggleSort()
        {
            SortAscending = !SortAscending;
        }

        partial void OnSearchChanged(string value) => RefreshFilter();
        partial void OnStatusFilterChanged(ScriptStatusFilter value) => RefreshFilter();
        partial void OnSortAscendingChanged(bool value)
        {
            OnPropertyChanged(nameof(SortLabel));
            ApplySort();
        }

        partial void OnSelectedScriptChanged(ScriptItemViewModel oldValue, ScriptItemViewModel value)
        {
            if (!_refreshing && value != null && !ConfirmDiscard())
            {
                _selectedScript = oldValue;
                OnPropertyChanged(nameof(SelectedScript));
                return;
            }
            if (value == null) return;
            _origin = value.Installation.SourceOrigin;
            Editor.Load(value.Installation.Definition.Source);
            Details.Update(value.Installation.Definition.ParseResult, _origin, value.Installation.Definition.Source);
        }

        public bool ConfirmDiscard() => !Editor.IsDirty || _confirm(Strings.Get("DiscardChanges"));
        public async Task RefreshAsync()
        {
            var key = SelectedScript?.Installation.ScriptKey;
            var scripts = await _repository.GetSnapshotAsync(_cancellation.Token);
            _refreshing = true;
            try
            {
                Scripts.Clear();
                foreach (var installation in scripts) Scripts.Add(new ScriptItemViewModel(installation));
                SelectedScript = Scripts.FirstOrDefault(item => item.Installation.ScriptKey == key);
                RefreshFilter();
            }
            finally { _refreshing = false; }
        }

        private void NewScript()
        {
            if (!ConfirmDiscard()) return;
            LoadDraft(ScriptEditorViewModel.Template, "application://editor/new.user.js", Strings.Get("NewScriptDraft"));
        }

        public Task ImportFileAsync(string path) => RunAsync(async () =>
        {
            if (!ConfirmDiscard()) return;
            var source = await Task.Run(() => UserScriptSourceFile.ReadText(path), _cancellation.Token);
            LoadDraft(source, new Uri(path).AbsoluteUri, Strings.Get("ImportedDraft"));
        });

        private async Task LoadUrlAsync()
        {
            if (!ConfirmDiscard()) return;
            var content = await _content.FetchAsync(Url, _cancellation.Token);
            LoadDraft(content.Text, Url, Strings.Get("DownloadedDraft"));
        }

        private void LoadDraft(string source, string origin, string status)
        {
            SelectedScript = null;
            _origin = origin;
            Editor.Load(source);
            // Source may equal the previous draft, so PropertyChanged alone cannot refresh its origin.
            UpdateDetails();
            Status = status;
        }

        private void UpdateDetails() => Details.Update(Editor.Parse(), _origin, Editor.Source);

        private async Task CheckUpdatesAsync()
        {
            if (SelectedScript == null || !ConfirmDiscard()) return;
            var installation = SelectedScript.Installation;
            var result = await _updates.CheckAsync(installation.ScriptKey, _cancellation.Token);
            switch (result.Status)
            {
                case UserScriptUpdateStatus.NotConfigured:
                    Status = Strings.Get("UpdateNotConfigured");
                    return;
                case UserScriptUpdateStatus.Disabled:
                    Status = Strings.Get("UpdateDisabled");
                    return;
                case UserScriptUpdateStatus.UpToDate:
                    Status = Strings.Format("UpToDate", result.CurrentVersion);
                    return;
                case UserScriptUpdateStatus.Available:
                    if (!_confirm(Strings.Format("ConfirmUpdate", result.CurrentVersion, result.AvailableVersion, result.DownloadUrl)))
                    {
                        Status = Strings.Get("UpdateCanceled");
                        return;
                    }
                    var updated = await _updates.ApplyAsync(result, _cancellation.Token);
                    Editor.Load(updated.Definition.Source);
                    await RefreshAsync();
                    SelectedScript = Scripts.First(item => item.Installation.ScriptKey == updated.ScriptKey);
                    Status = Strings.Format("UpdateInstalled", updated.Definition.Metadata.Version);
                    return;
                default:
                    throw new InvalidOperationException("Unknown update check result.");
            }
        }

        private async Task SaveAsync()
        {
            if (Encoding.UTF8.GetByteCount(Editor.Source ?? string.Empty) > UserScriptSourceFile.MaximumBytes)
                throw new InvalidOperationException(Strings.Get("SourceTooLarge"));
            var parsed = Editor.Parse();
            Details.Update(parsed, _origin, Editor.Source);
            if (!parsed.CanEnable)
            {
                Status = Strings.Get("InvalidMetadata");
                return;
            }
            if (!_confirm(Strings.Format("ConfirmInstall", Details.ToConfirmationText()))) return;
            var installation = SelectedScript == null
                ? await _repository.InstallAsync(Editor.Source, _origin, true, _cancellation.Token)
                : await _repository.UpdateAsync(SelectedScript.Installation.ScriptKey, Editor.Source, _origin, _cancellation.Token);
            Editor.Load(installation.Definition.Source);
            await RefreshAsync();
            SelectedScript = Scripts.First(item => item.Installation.ScriptKey == installation.ScriptKey);
            Status = Strings.Get("ScriptSaved");
        }

        private async Task ToggleAsync(ScriptItemViewModel item)
        {
            if (item == null || !item.Installation.Definition.ParseResult.CanEnable) return;
            var installation = item.Installation;
            item.Update(await _repository.SetEnabledAsync(installation.ScriptKey, !installation.IsEnabled, _cancellation.Token));
            RefreshFilter();
            Status = Strings.Format(item.Installation.IsEnabled ? "ScriptEnabled" : "ScriptDisabled", item.Name);
        }

        private async Task DeleteAsync()
        {
            if (SelectedScript == null || !_confirm(Strings.Get("ConfirmDelete"))) return;
            await _repository.RemoveAsync(SelectedScript.Installation.ScriptKey, _cancellation.Token);
            Editor.Load(ScriptEditorViewModel.Template);
            await RefreshAsync();
            Status = Strings.Get("ScriptDeleted");
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (Busy) return;
            Busy = true;
            try { await action(); }
            catch (OperationCanceledException) { Status = Strings.Get("OperationCanceled"); }
            catch (Exception exception)
            {
                Status = exception.Message;
                _diagnostics.Report(Strings.Format("ManagerError", exception.Message));
            }
            finally { Busy = false; }
        }

        private async Task RunItemAsync(ScriptItemViewModel item, Func<Task> action)
        {
            if (item == null || item.IsBusy) return;
            item.IsBusy = true;
            try { await action(); }
            catch (OperationCanceledException) { Status = Strings.Get("OperationCanceled"); }
            catch (Exception exception)
            {
                Status = exception.Message;
                _diagnostics.Report(Strings.Format("ManagerError", exception.Message));
            }
            finally { item.IsBusy = false; }
        }

        private bool FilterScript(object value)
        {
            var item = (ScriptItemViewModel)value;
            if (StatusFilter == ScriptStatusFilter.Enabled && (!item.Installation.IsEnabled || !item.Installation.Definition.ParseResult.CanEnable)) return false;
            if (StatusFilter == ScriptStatusFilter.Disabled && (item.Installation.IsEnabled || !item.Installation.Definition.ParseResult.CanEnable)) return false;
            if (StatusFilter == ScriptStatusFilter.Unavailable && item.Installation.Definition.ParseResult.CanEnable) return false;
            if (string.IsNullOrWhiteSpace(Search)) return true;
            return item.SearchText.IndexOf(Search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshFilter()
        {
            FilteredScripts.Refresh();
            OnPropertyChanged(nameof(FilteredCount));
            OnPropertyChanged(nameof(FilteredCountLabel));
            OnPropertyChanged(nameof(HasScripts));
            OnPropertyChanged(nameof(HasFilteredScripts));
            OnPropertyChanged(nameof(ShowNoScripts));
            OnPropertyChanged(nameof(ShowNoResults));
        }

        private void ApplySort()
        {
            var view = FilteredScripts as ListCollectionView;
            if (view == null) return;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(ScriptItemViewModel.Name),
                SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(ScriptItemViewModel.ScriptKey), ListSortDirection.Ascending));
            RefreshFilter();
        }

        private void EditorPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Editor.Source)) UpdateDetails();
        }

        public void Dispose()
        {
            Editor.PropertyChanged -= EditorPropertyChanged;
            _cancellation.Cancel();
        }
    }
}
