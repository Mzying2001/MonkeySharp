namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public interface IBrowserCommands
    {
        void Load(string address);
        void Back();
        void Forward();
        void Reload();
        void Stop();
        void ShowDevTools();
    }
}
