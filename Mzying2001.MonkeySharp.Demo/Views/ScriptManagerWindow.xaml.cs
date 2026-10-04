using Microsoft.Win32;
using Mzying2001.MonkeySharp.Demo.Runtime;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System.Windows;

namespace Mzying2001.MonkeySharp.Demo.Views
{
    public partial class ScriptManagerWindow : Window
    {
        private readonly ScriptManagerViewModel _model;
        private bool _closeApproved;
        public ScriptManagerWindow(DemoRuntime runtime)
        {
            InitializeComponent();
            _model = new ScriptManagerViewModel(runtime.Repository, runtime.Content, runtime.Updates,
                message => MessageBox.Show(this, message, "脚本权限与修改确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes,
                runtime.MainWindow.Diagnostics);
            DataContext = _model;
            TrustWarning.Text = DemoRuntime.TrustWarning;
            Loaded += (sender, args) => _model.RefreshCommand.Execute(null);
            Closing += (sender, args) => { if (!CanClose()) args.Cancel = true; };
            Closed += (sender, args) => _model.Dispose();
        }
        public bool CanClose() => _closeApproved || (_closeApproved = _model.ConfirmDiscard());
        private async void ImportFile(object sender, RoutedEventArgs args)
        {
            var dialog = new OpenFileDialog { Filter = "Userscript (*.user.js;*.js)|*.user.js;*.js|所有文件|*.*" };
            if (dialog.ShowDialog(this) == true) await _model.ImportFileAsync(dialog.FileName);
        }
    }
}
