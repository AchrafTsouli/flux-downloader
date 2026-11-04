using SwiftFetch.Bridge;

namespace SwiftFetch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault()?.StartsWith("chrome-extension://", StringComparison.Ordinal) == true)
        {
            NativeBridgeHost.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        RunDesktop();
    }

    // Avoid initializing WPF on the native messaging path.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunDesktop()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
