using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SwiftFetch.Bridge;

public static class AppConnection
{
    public static string ExecutablePath => File.Exists(Path.Combine(AppContext.BaseDirectory, "Flux.exe"))
        ? Path.Combine(AppContext.BaseDirectory, "Flux.exe")
        : Path.Combine(AppContext.BaseDirectory, "SwiftFetch.exe");
    public static async Task<BridgeResponse> SendAsync(BridgeRequest request, bool launch, CancellationToken token)
    {
        using var pipe = new NamedPipeClientStream(".", Protocol.InstanceName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(500, token); }
        catch (TimeoutException) when (launch)
        {
            string executable = ExecutablePath;
            if (!File.Exists(executable)) return new(false, "Flux was not found beside the browser bridge.");
            // Launch via shell so it has its own desktop session and doesn't inherit browser stdio pipes.
            using var launched = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
                Arguments = request.Action == "download" ? "--browser" : ""
            });
            await pipe.ConnectAsync(12000, token);
        }
        if (request.Action is "download" or "show") GrantForegroundPermission(pipe);
        await Protocol.WriteAsync(pipe, request, token);
        return await Protocol.ReadAsync<BridgeResponse>(pipe, token) ?? new(false, "SwiftFetch closed before acknowledging the download.");
    }

    private static void GrantForegroundPermission(NamedPipeClientStream pipe)
    {
        // Grant only to the connected application, never to arbitrary processes.
        // Windows may deny the grant when the caller itself has no foreground rights;
        // delivery of the download must still proceed in that case.
        if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId))
            AllowSetForegroundWindow(processId);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
