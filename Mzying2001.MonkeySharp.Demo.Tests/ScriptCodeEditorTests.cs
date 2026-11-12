using ICSharpCode.AvalonEdit.Highlighting;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Demo.Views;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class ScriptCodeEditorTests
    {
        [Fact]
        public Task EditingAndUndoKeepTheBindingCaretAndDirtyState() => OnDispatcher(async () =>
        {
            var model = new ScriptEditorViewModel();
            var editor = Bind(model);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal(model.Source, editor.Text);
            Assert.False(model.IsDirty);

            editor.CaretOffset = 5;
            editor.Document.Insert(editor.Document.TextLength, "// 用户修改\n");
            Assert.Equal(editor.Text, model.Source);
            Assert.Equal(5, editor.CaretOffset);
            Assert.True(model.IsDirty);
            Assert.True(editor.CanUndo);
            Assert.NotNull(editor.GetBindingExpression(ScriptCodeEditor.SourceTextProperty));

            editor.Undo();
            Assert.Equal(ScriptEditorViewModel.Template, model.Source);
            Assert.False(model.IsDirty);
            editor.Redo();
            Assert.Equal(editor.Text, model.Source);
            Assert.True(model.IsDirty);

            model.Load(model.Source);
            Assert.False(model.IsDirty);
            Assert.True(editor.CanUndo);
            editor.Undo();
            Assert.True(model.IsDirty);
        });

        [Fact]
        public Task LoadingDifferentSourcesResetsUndoAndKeepsSubsequentEditsBound() => OnDispatcher(async () =>
        {
            var model = new ScriptEditorViewModel();
            var editor = Bind(model);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            editor.Document.Insert(0, "// old edit\n");

            foreach (var source in new[] { "const second = 2;\r\n", "let third = 3;\n" })
            {
                model.Load(source);
                Assert.Equal(source, editor.Text);
                Assert.False(model.IsDirty);
                Assert.Equal(0, editor.CaretOffset);
                Assert.False(editor.CanUndo);
                Assert.False(editor.CanRedo);

                editor.Document.Insert(editor.Document.TextLength, "// new edit\n");
                Assert.Equal(source + "// new edit\n", model.Source);
                Assert.True(model.IsDirty);
                Assert.NotNull(editor.GetBindingExpression(ScriptCodeEditor.SourceTextProperty));
            }
        });

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("// 中文\r\nconst a = 1;\r\n")]
        [InlineData("// 中文\nconst a = 1;\n")]
        public Task LoadingSourcePreservesTextAndHandlesEmptyDocuments(string source) => OnDispatcher(() =>
        {
            var editor = new ScriptCodeEditor { SourceText = source };
            Assert.Equal(source ?? string.Empty, editor.Text);
            Assert.False(editor.CanUndo);
            Assert.True(editor.ShowLineNumbers);
            Assert.False(editor.WordWrap);
            return Task.CompletedTask;
        });

        [Fact]
        public Task EditingCommandsIndentMultipleLinesAndInheritIndentation() => OnDispatcher(() =>
        {
            var model = new ScriptEditorViewModel();
            model.Load("const a = 1;\nconst b = 2;");
            var editor = Bind(model);
            editor.SelectAll();
            EditingCommands.TabForward.Execute(null, editor.TextArea);
            Assert.Equal("    const a = 1;\n    const b = 2;", model.Source);
            EditingCommands.TabBackward.Execute(null, editor.TextArea);
            Assert.Equal("const a = 1;\nconst b = 2;", model.Source);

            model.Load("    const a = 1;");
            editor.Select(editor.Document.TextLength, 0);
            editor.TextArea.PerformTextInput("\n");
            Assert.Equal("    ", editor.Document.GetText(editor.Document.GetLineByNumber(2)));
            Assert.Equal(editor.Text, model.Source);
            return Task.CompletedTask;
        });

        [Theory]
        [InlineData("const count = 42;", "const", "JavaScriptKeyWords")]
        [InlineData("let count = 42;", "let", "JavaScriptKeyWords")]
        [InlineData("const count = 42;", "42", "Digits")]
        [InlineData("// @name 中文脚本", "@name", "Comment")]
        [InlineData("/* block */ const count = 42;", "block", "Comment")]
        [InlineData("const text = \"say \\\"hi\\\"\";", "hi", "String")]
        [InlineData("const text = 'value';", "value", "Character")]
        [InlineData("async function run() { await Promise.resolve(); }", "async", "Keyword")]
        [InlineData("async function run() { await Promise.resolve(); }", "await", "Keyword")]
        [InlineData("function* run() { yield 1; }", "yield", "Keyword")]
        [InlineData("const text = `first\\`\n// string ${value}`;\nconst after = 1;", "// string", "TemplateString")]
        [InlineData("const text = `first\\`\n// string ${value}`;\nconst after = 1;", "const after", "JavaScriptKeyWords")]
        public Task HighlighterColorsJavaScriptTokens(string source, string token, string colorName) => OnDispatcher(() =>
        {
            var editor = new ScriptCodeEditor { SourceText = source };
            var offset = source.IndexOf(token, StringComparison.Ordinal);
            using (var highlighter = new DocumentHighlighter(editor.Document, editor.SyntaxHighlighting))
            {
                var line = highlighter.HighlightLine(editor.Document.GetLocation(offset).Line);
                Assert.Contains(line.Sections, section => section.Offset <= offset &&
                    offset < section.Offset + section.Length && section.Color.Name == colorName);
            }
            return Task.CompletedTask;
        });

        private static ScriptCodeEditor Bind(ScriptEditorViewModel model)
        {
            var editor = new ScriptCodeEditor { DataContext = model };
            editor.SetBinding(ScriptCodeEditor.SourceTextProperty, new Binding(nameof(ScriptEditorViewModel.Source))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return editor;
        }

        private static Task OnDispatcher(Func<Task> action)
        {
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await action(); completion.TrySetResult(null); }
                    catch (Exception exception) { completion.TrySetException(exception); }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                }));
                Dispatcher.Run();
            })
            { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }
    }
}
