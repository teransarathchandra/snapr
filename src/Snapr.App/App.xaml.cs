using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
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
            ForwardToRunningInstance(e.Args);
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

        _pipeStop = new CancellationTokenSource();
        _ = Task.Run(() => ServeOpenRequests(_pipeStop.Token));
    }

    const string PipeName = "Snapr.Open.v1";
    CancellationTokenSource? _pipeStop;

    /// <summary>Second launch: hand the file (or "bring the editor forward") to the running Snapr.</summary>
    static void ForwardToRunningInstance(string[] args)
    {
        try
        {
            var open = Array.IndexOf(args, "--open");
            var message = open >= 0 && open + 1 < args.Length ? Path.GetFullPath(args[open + 1]) : "--editor";
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            client.Write(Encoding.UTF8.GetBytes(message + "\n"));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            Log.Write("Could not pass the request to the running Snapr", ex);
        }
    }

    /// <summary>First instance: one UTF-8 line per connection (max 4 KB) - a file path, or "--editor".</summary>
    async Task ServeOpenRequests(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(stop);
                var buffer = new byte[4096];
                var length = 0;
                int read;
                while (length < buffer.Length && (read = await server.ReadAsync(buffer.AsMemory(length), stop)) > 0)
                {
                    length += read;
                    if (buffer.AsSpan(0, length).IndexOf((byte)'\n') >= 0) break;
                }
                var line = Encoding.UTF8.GetString(buffer, 0, length).Split('\n')[0].Trim();
                if (line.Length == 0) continue;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (line == "--editor") _controller?.ShowEditor();
                    else _controller?.OpenImageFile(line);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException)
            {
                if (stop.IsCancellationRequested) return;
                Log.Write("Open pipe error", ex);
                await Task.Delay(500, stop).ContinueWith(_ => { });
            }
        }
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
        _pipeStop?.Cancel();
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
