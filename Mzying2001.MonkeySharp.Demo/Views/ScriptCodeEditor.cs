using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Mzying2001.MonkeySharp.Demo.Localization;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;

namespace Mzying2001.MonkeySharp.Demo.Views
{
    public sealed class ScriptCodeEditor : TextEditor
    {
        public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
            nameof(SourceText), typeof(string), typeof(ScriptCodeEditor),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSourceTextChanged));

        private static readonly Lazy<IHighlightingDefinition> JavaScriptHighlighting = new Lazy<IHighlightingDefinition>(LoadHighlighting);
        private bool _synchronizing;

        public ScriptCodeEditor()
        {
            ShowLineNumbers = true;
            WordWrap = false;
            SyntaxHighlighting = JavaScriptHighlighting.Value;
            Options.IndentationSize = 4;
            Options.ConvertTabsToSpaces = true;
            ContextMenu = CreateContextMenu();
            TextChanged += OnEditorTextChanged;
        }

        public string SourceText
        {
            get => (string)GetValue(SourceTextProperty);
            set => SetValue(SourceTextProperty, value);
        }

        private ContextMenu CreateContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem("EditorUndo", ApplicationCommands.Undo, "Ctrl+Z"));
            menu.Items.Add(CreateMenuItem("EditorRedo", ApplicationCommands.Redo, "Ctrl+Y"));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("EditorCut", ApplicationCommands.Cut, "Ctrl+X"));
            menu.Items.Add(CreateMenuItem("EditorCopy", ApplicationCommands.Copy, "Ctrl+C"));
            menu.Items.Add(CreateMenuItem("EditorPaste", ApplicationCommands.Paste, "Ctrl+V"));
            menu.Items.Add(CreateMenuItem("Delete", ApplicationCommands.Delete, "Del"));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("EditorSelectAll", ApplicationCommands.SelectAll, "Ctrl+A"));
            return menu;
        }

        private MenuItem CreateMenuItem(string resourceKey, RoutedCommand command, string gesture)
            => new MenuItem
            {
                Header = Strings.Get(resourceKey),
                Command = command,
                // A context menu lives in a separate popup; route commands to AvalonEdit explicitly.
                CommandTarget = TextArea,
                InputGestureText = gesture
            };

        private static void OnSourceTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var editor = (ScriptCodeEditor)sender;
            var source = (string)args.NewValue ?? string.Empty;
            if (editor._synchronizing || editor.Text == source) return;
            editor._synchronizing = true;
            try
            {
                // Replacing a document resets its caret and undo history; binding echoes must not do this.
                editor.Text = source;
            }
            finally { editor._synchronizing = false; }
        }

        private void OnEditorTextChanged(object sender, EventArgs args)
        {
            if (_synchronizing) return;
            _synchronizing = true;
            try { SetCurrentValue(SourceTextProperty, Text); }
            finally { _synchronizing = false; }
        }

        private static IHighlightingDefinition LoadHighlighting()
        {
            using (var stream = typeof(ScriptCodeEditor).Assembly.GetManifestResourceStream(
                "Mzying2001.MonkeySharp.Demo.Views.UserscriptJavaScript.xshd"))
            using (var reader = XmlReader.Create(stream))
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
    }
}
