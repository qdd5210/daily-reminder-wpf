using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace DailyReminder;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var instanceScope = AppPaths.ResolveDataDirectory(e.Args).ToUpperInvariant();
        var instanceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceScope)));
        var instanceName = $"Local\\DailyReminder-{instanceKey}";
        _instanceMutex = new Mutex(true, instanceName, out _ownsMutex);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + "-activate");
        if (!_ownsMutex)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

        var minimized = e.Args.Any(arg =>
            string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        MainWindow window;
        try
        {
            window = new MainWindow(minimized);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"启动失败：{exception.Message}", "每日待办", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        MainWindow = window;
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(window.ShowPanel)),
            null,
            Timeout.Infinite,
            false);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
