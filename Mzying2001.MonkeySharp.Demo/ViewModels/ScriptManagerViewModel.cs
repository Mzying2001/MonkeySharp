using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.Persistence;
using Mzying2001.MonkeySharp.Demo.Services;
using System;
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
        private string _status;

        [ObservableProperty]
        private string _preview;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool _busy;

        private bool _refreshing;

        public ScriptManagerViewModel(IUserScriptRepository repository, HttpContentService content,
            Func<string, bool> confirm, DiagnosticsViewModel diagnostics)
        {
            _repository = repository; _content = content; _confirm = confirm; _diagnostics = diagnostics;
            FilteredScripts = CollectionViewSource.GetDefaultView(Scripts);
            FilteredScripts.Filter = item => string.IsNullOrWhiteSpace(Search) || ((ScriptItemViewModel)item).DisplayName.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public ScriptEditorViewModel Editor { get; } = new ScriptEditorViewModel();
        public ObservableCollection<ScriptItemViewModel> Scripts { get; } = new ObservableCollection<ScriptItemViewModel>();
        public ICollectionView FilteredScripts { get; }
        public bool CanEdit => !Busy;

        [RelayCommand]
        private void New() => NewScript();

        [RelayCommand]
        private void Validate() => Preview = ScriptEditorViewModel.Describe(Editor.Parse());

        [RelayCommand]
        private async Task Refresh() => await RunAsync(async () => { if (ConfirmDiscard()) await RefreshAsync(); });

        [RelayCommand]
        private Task Save() => RunAsync(SaveAsync);

        [RelayCommand]
        private Task Toggle() => RunAsync(ToggleAsync);

        [RelayCommand]
        private Task Delete() => RunAsync(DeleteAsync);

        [RelayCommand]
        private Task InstallUrl() => RunAsync(LoadUrlAsync);

        partial void OnSearchChanged(string value) => FilteredScripts.Refresh();

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
            Preview = ScriptEditorViewModel.Describe(value.Installation.Definition.ParseResult) + "\n来源：" + _origin;
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
            }
            finally { _refreshing = false; }
        }

        private void NewScript()
        {
            if (!ConfirmDiscard()) return;
            Editor.Load(ScriptEditorViewModel.Template); SelectedScript = null;
            _origin = "application://editor/new.user.js";
            Preview = "新脚本。保存前会显示权限确认。";
        }

        public Task ImportFileAsync(string path) => RunAsync(async () =>
        {
            if (!ConfirmDiscard()) return;
            var bytes = await Task.Run(() => SqliteUserScriptRepositoryPersistence.ReadBounded(path), _cancellation.Token);
            Editor.Load(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
            SelectedScript = null; _origin = new Uri(path).AbsoluteUri;
            Preview = ScriptEditorViewModel.Describe(Editor.Parse());
            Status = "已导入草稿；审核权限后点击安装 / 保存。";
        });

        private async Task LoadUrlAsync()
        {
            if (!ConfirmDiscard()) return;
            var content = await _content.FetchAsync(Url, _cancellation.Token);
            Editor.Load(content.Text); SelectedScript = null; _origin = Url;
            Preview = ScriptEditorViewModel.Describe(Editor.Parse());
            Status = "已下载草稿；尚未安装。请审核源码并点击安装 / 保存。";
        }

        private async Task SaveAsync()
        {
            if (Encoding.UTF8.GetByteCount(Editor.Source ?? string.Empty) > SqliteUserScriptRepositoryPersistence.MaximumSourceBytes)
                throw new InvalidOperationException("源码超过 10 MiB 限制。");
            var parsed = Editor.Parse(); Preview = ScriptEditorViewModel.Describe(parsed);
            if (!parsed.CanEnable) { Status = "元数据无效；旧脚本未改变。"; return; }
            if (!_confirm("允许以下脚本在匹配的网站运行并使用所声明的权限？\n\n" + Preview)) return;
            var installation = SelectedScript == null
                ? await _repository.InstallAsync(Editor.Source, _origin, true, _cancellation.Token)
                : await _repository.UpdateAsync(SelectedScript.Installation.ScriptKey, Editor.Source, _origin, _cancellation.Token);
            Editor.Load(installation.Definition.Source);
            await RefreshAsync();
            SelectedScript = Scripts.First(item => item.Installation.ScriptKey == installation.ScriptKey);
            Status = "已保存并刷新浏览器标签。";
        }

        private async Task ToggleAsync()
        {
            if (SelectedScript == null || !ConfirmDiscard()) return;
            var installation = SelectedScript.Installation;
            if (!installation.IsEnabled && !_confirm("启用并允许脚本权限？\n\n" + ScriptEditorViewModel.Describe(installation.Definition.ParseResult))) return;
            await _repository.SetEnabledAsync(installation.ScriptKey, !installation.IsEnabled, _cancellation.Token);
            await RefreshAsync(); Status = "已更改脚本启用状态。";
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

        public void Dispose() { _cancellation.Cancel(); }
    }

    public sealed class ScriptItemViewModel
    {
        public ScriptItemViewModel(UserScriptInstallation installation) { Installation = installation; }
        public UserScriptInstallation Installation { get; }
        public string DisplayName => (!Installation.Definition.ParseResult.CanEnable ? "[不可用] " : Installation.IsEnabled ? "[启用] " : "[停用] ") +
            (Installation.Definition.Metadata?.Name ?? Installation.ScriptKey.ToString()) + "  " + Installation.Definition.Metadata?.Version;
    }
}
