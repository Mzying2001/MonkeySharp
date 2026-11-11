using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    internal static class DownloadFilePicker
    {
        internal const string Title = "Save userscript download";
        internal static string Select(string name, string directory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = Application.Current.MainWindow;
            var ownerHandle = new WindowInteropHelper(owner).Handle;
            using (cancellationToken.Register(() => owner.Dispatcher.BeginInvoke(new Action(() => Close(ownerHandle)))))
            {
                var dialog = new SaveFileDialog { Title = Title, FileName = name, InitialDirectory = directory, OverwritePrompt = true };
                return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
            }
        }

        // Close only this process's owned download picker, including timeout/abort during its modal loop.
        internal static bool Close(IntPtr owner)
        {
            var found = false;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var process);
                if (process != (uint)Process.GetCurrentProcess().Id || GetWindow(window, 4) != owner) return true;
                var title = new System.Text.StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                if (title.ToString() != Title) return true;
                found = PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                return false;
            }, IntPtr.Zero);
            return found;
        }
        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
