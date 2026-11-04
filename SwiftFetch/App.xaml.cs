using System.IO;
using System.IO.Pipes;
using System.Windows;
using SwiftFetch.Bridge;

namespace SwiftFetch;

public partial class App : Application
{
    private Mutex? instance;
    // Setup checks this session-local mutex before upgrading/uninstalling.
    // Keep it alive even while the main window is hidden in the tray.
    private Mutex? installerLock;
    private readonly CancellationTokenSource stopping = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--verify-installation", StringComparer.Ordinal))
        {
            Shutdown(InstallationCheck.Run());
            return;
        }
        installerLock = new Mutex(false, @"Local\ApexDownloader.Running");
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftFetch");
        Directory.CreateDirectory(logDir);
        string logPath = Path.Combine(logDir, "app-startup.log");
        void Log(string msg) { try { File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n"); } catch { } }
        Log("=== OnStartup started ===");
        bool browserLaunch = e.Args.Contains("--browser", StringComparer.Ordinal);
        bool backgroundLaunch = e.Args.Contains("--background", StringComparer.Ordinal);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            instance = new Mutex(true, @"Local\" + Protocol.InstanceName, out bool first);
            Log($"Mutex created. First={first}, Name=Local\\{Protocol.InstanceName}");
            if (!first)
            {
                if (browserLaunch || backgroundLaunch) { Shutdown(); return; }
                Log("Secondary instance detected. Signaling existing instance...");
                bool signaled = false;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var response = await AppConnection.SendAsync(new("show"), false, timeout.Token);
                    signaled = response.Ok;
                    Log($"Existing instance responded: {signaled}");
                }
                catch (Exception ex) { Log($"Could not contact existing instance: {ex.Message}"); }

                if (signaled)
                {
                    Log("Active instance successfully brought to foreground. Exiting secondary launcher.");
                    Shutdown();
                    return;
                }

                Log("The existing instance did not respond; its downloads were preserved.");
                MessageBox.Show("Flux is already running but did not respond. Close its existing window normally and try again. Its downloads have not been stopped.", "Flux", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            Log("Instantiating MainWindow...");
            var window = new MainWindow();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += (_, args) => Log($"DispatcherUnhandledException: {args.Exception}");
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Log($"AppDomain UnhandledException: {args.ExceptionObject}");
            window.Closing += (_, args) => Log($"window.Closing called! Cancel={args.Cancel}");
            window.Closed += (_, _) => Log("window.Closed called!");
            window.StateChanged += (_, _) => Log($"window.StateChanged: {window.WindowState}");
            window.IsVisibleChanged += (_, _) => Log($"window.IsVisibleChanged: {window.IsVisible}");

            if (browserLaunch)
            {
                window.EnableBackgroundTray();
                Log("Browser launch: main window remains hidden.");
            }
            else if (backgroundLaunch) window.EnableBackgroundTray();
            else
            {
                window.Show();
                window.Activate();
                window.Focus();
                Log("Main window displayed.");
            }
            _ = ListenAsync(window);
            if (browserLaunch) window.RestorePendingConfirmations();
        }
        catch (Exception ex)
        {
            Log($"FATAL Startup error: {ex}");
            MessageBox.Show($"Flux could not start:\n\n{ex.Message}\n\nSee log at: {logPath}", "Flux Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task ListenAsync(MainWindow window)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Protocol.InstanceName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await Protocol.ReadAsync<BridgeRequest>(pipe, timeout.Token);
                if (request == null) continue;
                string? error = Protocol.Validate(request);
                var response = error == null ? window.AcceptBrowserRequest(request) : new BridgeResponse(false, error);
                await Protocol.WriteAsync(pipe, response, timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (System.Text.Json.JsonException) { }
            catch (Exception ex)
            {
                window.SetIntegrationMessage("Browser integration: " + ex.Message);
                try { await Task.Delay(500, stopping.Token); } catch (OperationCanceledException) { }
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        stopping.Cancel();
        instance?.Dispose();
        installerLock?.Dispose();
        base.OnExit(e);
    }
}
