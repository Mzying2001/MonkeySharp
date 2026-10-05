using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.Persistence;
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
        private string _statusFilter = "全部";

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
            _repository = repository; _content = content; _updates = updates; _confirm = confirm; _diagnostics = diagnostics;
            Editor.PropertyChanged += EditorPropertyChanged;
            FilteredScripts = CollectionViewSource.GetDefaultView(Scripts);
            FilteredScripts.Filter = FilterScript;
            ApplySort();
            Details.Update(Editor.Parse(), _origin);
        }

        public ScriptEditorViewModel Editor { get; } = new ScriptEditorViewModel();
        public ScriptDetailsViewModel Details { get; } = new ScriptDetailsViewModel();
        public ObservableCollection<ScriptItemViewModel> Scripts { get; } = new ObservableCollection<ScriptItemViewModel>();
        public ICollectionView FilteredScripts { get; }
        public IReadOnlyList<string> StatusFilters { get; } = new[] { "全部", "已启用", "已停用", "不可用" };
        public int FilteredCount => FilteredScripts.Cast<object>().Count();
        public bool HasScripts => Scripts.Count > 0;
        public bool HasFilteredScripts => FilteredCount > 0;
        public bool ShowNoScripts => !HasScripts;
        public bool ShowNoResults => HasScripts && !HasFilteredScripts;
        public string SortLabel => SortAscending ? "名称升序" : "名称降序";
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
            ApplySort();
        }

        partial void OnSearchChanged(string value) => RefreshFilter();
        partial void OnStatusFilterChanged(string value) => RefreshFilter();
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
            Details.Update(value.Installation.Definition.ParseResult, _origin);
        }

        public bool ConfirmDiscard() => !Editor.IsDirty || _confirm("放弃尚未保存的源码修改？");
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
            _origin = "application://editor/new.user.js";
            SelectedScript = null; Editor.Load(ScriptEditorViewModel.Template);
            Details.Update(Editor.Parse(), _origin);
            Status = "新脚本。保存前会显示权限确认。";
        }

        public Task ImportFileAsync(string path) => RunAsync(async () =>
        {
            if (!ConfirmDiscard()) return;
            var bytes = await Task.Run(() => SqliteUserScriptRepositoryPersistence.ReadBounded(path), _cancellation.Token);
            SelectedScript = null; _origin = new Uri(path).AbsoluteUri;
            Editor.Load(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
            Status = "已导入草稿；审核权限后点击安装 / 保存。";
        });

        private async Task LoadUrlAsync()
        {
            if (!ConfirmDiscard()) return;
            var content = await _content.FetchAsync(Url, _cancellation.Token);
            SelectedScript = null; _origin = Url; Editor.Load(content.Text);
            Status = "已下载草稿；尚未安装。请审核源码并点击安装 / 保存。";
        }

        private async Task CheckUpdatesAsync()
        {
            if (SelectedScript == null || !ConfirmDiscard()) return;
            var installation = SelectedScript.Installation;
            var result = await _updates.CheckAsync(installation.ScriptKey, _cancellation.Token);
            switch (result.Status)
            {
                case UserScriptUpdateStatus.NotConfigured:
                    Status = "该脚本未配置版本或更新地址。";
                    return;
                case UserScriptUpdateStatus.Disabled:
                    Status = "该脚本通过 @downloadURL none 禁用了更新检查。";
                    return;
                case UserScriptUpdateStatus.UpToDate:
                    Status = "脚本已是最新版本 v" + result.CurrentVersion + "。";
                    return;
                case UserScriptUpdateStatus.Available:
                    if (!_confirm("检测到脚本更新：v" + result.CurrentVersion + " → v" + result.AvailableVersion +
                        "。下载并替换当前脚本吗？\n\n下载地址：" + result.DownloadUrl))
                    {
                        Status = "已取消安装更新。";
                        return;
                    }
                    var updated = await _updates.ApplyAsync(result, _cancellation.Token);
                    Editor.Load(updated.Definition.Source);
                    await RefreshAsync();
                    SelectedScript = Scripts.First(item => item.Installation.ScriptKey == updated.ScriptKey);
                    Status = "已安装更新 v" + updated.Definition.Metadata.Version + " 并刷新浏览器标签。";
                    return;
                default:
                    throw new InvalidOperationException("Unknown update check result.");
            }
        }

        private async Task SaveAsync()
        {
            if (Encoding.UTF8.GetByteCount(Editor.Source ?? string.Empty) > SqliteUserScriptRepositoryPersistence.MaximumSourceBytes)
                throw new InvalidOperationException("源码超过 10 MiB 限制。");
            var parsed = Editor.Parse(); Details.Update(parsed, _origin);
            if (!parsed.CanEnable) { Status = "元数据无效；旧脚本未改变。"; return; }
            if (!_confirm("允许以下脚本在匹配的网站运行并使用所声明的权限？\n\n" + Details.ToConfirmationText())) return;
            var installation = SelectedScript == null
                ? await _repository.InstallAsync(Editor.Source, _origin, true, _cancellation.Token)
                : await _repository.UpdateAsync(SelectedScript.Installation.ScriptKey, Editor.Source, _origin, _cancellation.Token);
            Editor.Load(installation.Definition.Source);
            await RefreshAsync();
            SelectedScript = Scripts.First(item => item.Installation.ScriptKey == installation.ScriptKey);
            Status = "已保存并刷新浏览器标签。";
        }

        private async Task ToggleAsync(ScriptItemViewModel item)
        {
            if (item == null || !item.Installation.Definition.ParseResult.CanEnable) return;
            var installation = item.Installation;
            if (!installation.IsEnabled && !_confirm("启用并允许脚本权限？\n\n" + ScriptEditorViewModel.Describe(installation.Definition.ParseResult)))
            {
                // WPF toggles the control before invoking the command. Re-publish the
                // unchanged installation so the OneWay binding reads the actual state.
                item.Update(installation);
                return;
            }
            item.Update(await _repository.SetEnabledAsync(installation.ScriptKey, !installation.IsEnabled, _cancellation.Token));
            RefreshFilter();
            Status = (item.Installation.IsEnabled ? "已启用：" : "已停用：") + item.Name;
        }

        private async Task DeleteAsync()
        {
            if (SelectedScript == null || !_confirm("删除该脚本、源码文件及其 GM 存储数据？")) return;
            await _repository.RemoveAsync(SelectedScript.Installation.ScriptKey, _cancellation.Token);
            Editor.Load(ScriptEditorViewModel.Template); await RefreshAsync(); Status = "已删除脚本。";
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (Busy) return;
            Busy = true;
            try { await action(); }
            catch (OperationCanceledException) { Status = "操作已取消。"; }
            catch (Exception exception) { Status = exception.Message; _diagnostics.Report("脚本管理器：" + exception.Message); }
            finally { Busy = false; }
        }

        private async Task RunItemAsync(ScriptItemViewModel item, Func<Task> action)
        {
            if (item == null || item.IsBusy) return;
            item.IsBusy = true;
            try { await action(); }
            catch (OperationCanceledException) { Status = "操作已取消。"; }
            catch (Exception exception) { Status = exception.Message; _diagnostics.Report("脚本管理器：" + exception.Message); }
            finally { item.IsBusy = false; }
        }

        private bool FilterScript(object value)
        {
            var item = (ScriptItemViewModel)value;
            if (StatusFilter == "已启用" && (!item.Installation.IsEnabled || !item.Installation.Definition.ParseResult.CanEnable)) return false;
            if (StatusFilter == "已停用" && (item.Installation.IsEnabled || !item.Installation.Definition.ParseResult.CanEnable)) return false;
            if (StatusFilter == "不可用" && item.Installation.Definition.ParseResult.CanEnable) return false;
            if (string.IsNullOrWhiteSpace(Search)) return true;
            return item.SearchText.IndexOf(Search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshFilter()
        {
            FilteredScripts.Refresh();
            OnPropertyChanged(nameof(FilteredCount));
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
            if (args.PropertyName == nameof(Editor.Source)) Details.Update(Editor.Parse(), _origin);
        }

        public void Dispose()
        {
            Editor.PropertyChanged -= EditorPropertyChanged;
            _cancellation.Cancel();
        }
    }

    public sealed class ScriptItemViewModel : ObservableObject
    {
        private UserScriptInstallation _installation;
        private bool _isBusy;

        public ScriptItemViewModel(UserScriptInstallation installation) { _installation = installation; }
        public UserScriptInstallation Installation => _installation;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                OnPropertyChanged(nameof(CanToggle));
            }
        }
        public bool CanToggle => !_isBusy && Installation.Definition.ParseResult.CanEnable;
        public string Name => Installation.Definition.Metadata?.Name ?? Installation.ScriptKey.ToString();
        public string Version => string.IsNullOrWhiteSpace(Installation.Definition.Metadata?.Version) ? "无版本" : "v" + Installation.Definition.Metadata.Version;
        public string Description => string.IsNullOrWhiteSpace(Installation.Definition.Metadata?.Description) ? "未提供描述" : Installation.Definition.Metadata.Description;
        public string ScriptKey => Installation.ScriptKey.ToString();
        public string SearchText => string.Join("\n", Name, Description, Installation.Definition.Metadata?.Namespace, Installation.SourceOrigin);
        public string IconUrl
        {
            get
            {
                var metadata = Installation.Definition.Metadata;
                return !string.IsNullOrWhiteSpace(metadata?.Icon64Url) ? metadata.Icon64Url : metadata?.IconUrl;
            }
        }
        public string IconFallbackText => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
        public string StatusLabel => !Installation.Definition.ParseResult.CanEnable ? "不可用" : Installation.IsEnabled ? "已启用" : "已停用";
        public string ToggleLabel => Installation.IsEnabled ? "停用" : "启用";
        public string StatusBrush => !Installation.Definition.ParseResult.CanEnable ? "#B42318" : Installation.IsEnabled ? "#2F855A" : "#94A3B8";
        public string DisplayName => StatusLabel + " " + Name + "  " + Version;

        public void Update(UserScriptInstallation installation)
        {
            _installation = installation ?? throw new ArgumentNullException(nameof(installation));
            OnPropertyChanged(string.Empty);
        }
    }
}
