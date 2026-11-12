using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class WpfMenuService : IMenuService
    {
        private readonly BrowserTabViewModel _tab;
        private readonly Dispatcher _dispatcher;
        private readonly IUserScriptRepository _repository;
        private readonly Dictionary<Tuple<string, string, string, string>, ScriptMenuCommand> _groups = new Dictionary<Tuple<string, string, string, string>, ScriptMenuCommand>();

        public WpfMenuService(BrowserTabViewModel tab, Dispatcher dispatcher, IUserScriptRepository repository)
        {
            _tab = tab;
            _dispatcher = dispatcher;
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<IMenuRegistration> RegisterAsync(MenuCommandRequest request, Action<MenuCommandInvocation> invoked, CancellationToken cancellationToken)
        {
            var installation = await _repository.GetAsync(request.ScriptKey, cancellationToken).ConfigureAwait(false);
            return await _dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_tab.IsClosed) throw new OperationCanceledException();
                var key = Tuple.Create(request.ScriptKey.ToString(), request.Name, request.Title ?? "", request.AccessKey ?? "");
                if (!_groups.TryGetValue(key, out var command))
                {
                    command = new ScriptMenuCommand(request, installation.Definition.Metadata.Name);
                    _groups.Add(key, command);
                    _tab.MenuCommands.Add(command);
                }
                command.Subscribers.Add(invoked, request.AutoClose);
                command.Changed();
                return (IMenuRegistration)new MenuRegistration(() =>
                    _dispatcher.BeginInvoke(new Action(() => RemoveSubscriber(key, command, invoked))));
            });
        }

        private void RemoveSubscriber(Tuple<string, string, string, string> key, ScriptMenuCommand command,
            Action<MenuCommandInvocation> invoked)
        {
            command.Subscribers.Remove(invoked);
            command.Changed();
            if (command.Subscribers.Count != 0) return;
            _groups.Remove(key);
            _tab.MenuCommands.Remove(command);
        }

        private sealed class MenuRegistration : IMenuRegistration
        {
            private Action _remove;
            public MenuRegistration(Action remove) { _remove = remove; }
            public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
        }
    }
}
