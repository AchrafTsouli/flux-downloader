using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
namespace SwiftFetch;
public static class IndependentDialogs
{
    private sealed class State { public bool Accepted; }
    private static readonly ConditionalWeakTable<Window, State> states = new();
    public static Task<bool> ShowAsync(Window window, bool bringToFront = false)
    {
        var state = new State(); states.Add(window,state);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => { states.Remove(window); completion.TrySetResult(state.Accepted); };
        window.Owner = null; window.ShowInTaskbar = true; window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        bool wasTopmost = window.Topmost;
        DispatcherTimer? raiseTimer = null;
        EventHandler? releaseRaise = null;
        void RestoreOrder()
        {
            raiseTimer?.Stop();
            window.Deactivated -= releaseRaise;
            window.Closed -= releaseRaise;
            if (window.IsVisible) window.Topmost = wasTopmost;
        }
        try
        {
            if (bringToFront)
            {
                window.ShowActivated = true;
                // Establish the initial Z order before Show, even if Windows
                // declines a background process's request for keyboard focus.
                window.Topmost = true;
            }
            window.Show();
            if (bringToFront)
            {
                // Raise only this independent dialogue, including when the manager is hidden.
                // The bridge grants foreground permission before dispatching a browser request.
                window.Activate();
                SetForegroundWindow(new WindowInteropHelper(window).Handle);
                // Let the first frame paint above the foreground app, then
                // restore normal stacking. Switching away releases it sooner.
                releaseRaise = (_, _) => RestoreOrder();
                window.Deactivated += releaseRaise;
                window.Closed += releaseRaise;
                raiseTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(750) };
                raiseTimer.Tick += releaseRaise;
                raiseTimer.Start();
            }
        }
        catch { RestoreOrder(); states.Remove(window); throw; }
        return completion.Task;
    }
    public static void Finish(Window window, bool accepted)
    {
        if (states.TryGetValue(window,out var state)) state.Accepted = accepted;
        else window.DialogResult = accepted; // Compatibility for callers explicitly using ShowDialog.
        window.Close();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
