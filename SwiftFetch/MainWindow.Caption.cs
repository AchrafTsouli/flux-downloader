using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SwiftFetch;

public partial class MainWindow
{
    private void ApplyCaptionTheme()
    {
        // Theme settings also run before SourceInitialized; do not create an HWND here.
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        int dark = settings.DarkMode ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Explicit colors keep the app's caption dark even with Windows in light mode.
            int caption = settings.DarkMode ? 0x202020 : unchecked((int)0xFFFFFFFF);
            int text = settings.DarkMode ? 0xF0F0F0 : unchecked((int)0xFFFFFFFF);
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
