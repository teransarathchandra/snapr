using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Snapr.Core;

namespace Snapr.App;

public partial class App : Application
{
    Mutex? _singleInstance;
    AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "Snapr.SingleInstance.v1", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Write("Unhandled exception (AppDomain)", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Write("Unobserved task exception", a.Exception); a.SetObserved(); };

        base.OnStartup(e);
        _controller = new AppController();

        var args = e.Args;
        var open = Array.IndexOf(args, "--open");
        if (open >= 0 && open + 1 < args.Length) _controller.OpenImageFile(args[open + 1]);
        else if (args.Contains("--editor")) _controller.ShowEditor();
    }

    void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Log.Write("Unhandled exception", e.Exception);
        var answer = MessageBox.Show($"Snapr hit an error and wrote it to the log.\n\n{e.Exception.Message}\n\nOpen the log?",
            "Snapr", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (answer == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(Log.TodayPath) { UseShellExecute = true });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
