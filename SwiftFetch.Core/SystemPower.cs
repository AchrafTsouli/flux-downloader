using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwiftFetch.Core;

public enum QueueCompletionAction
{
    None = 0,
    CloseApplication = 1,
    Sleep = 2,
    Shutdown = 3,
    ExecuteCommand = 4,
    Hibernate = 5
}

public static class SystemPower
{
    [DllImport("PowrProf.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    public static void Trigger(QueueCompletionAction action, string? command = null, bool force = false)
    {
        switch (action)
        {
            case QueueCompletionAction.CloseApplication:
                Environment.Exit(0);
                break;

            case QueueCompletionAction.Sleep:
                EnableShutdownPrivilege();
                if (!SetSuspendState(false, false, false)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                break;
            case QueueCompletionAction.Hibernate:
                StartShutdown(["/h"]);
                break;

            case QueueCompletionAction.Shutdown:
                StartShutdown(force ? ["/s","/t","0","/f"] : ["/s","/t","0"]);
                break;

            case QueueCompletionAction.ExecuteCommand:
                if (!string.IsNullOrWhiteSpace(command))
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}") { CreateNoWindow = true });
                }
                break;
        }
    }
    private static void StartShutdown(string[] arguments)
    {
        var start = new ProcessStartInfo("shutdown.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        Process.Start(start);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Id; public uint Attributes; }
    [DllImport("advapi32.dll",SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool LookupPrivilegeValue(string? system,string name,out Luid id);
    [DllImport("advapi32.dll",SetLastError=true)] private static extern bool AdjustTokenPrivileges(IntPtr token,bool disable,ref TokenPrivileges privileges,uint length,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(),0x20|0x8,out var token)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!LookupPrivilegeValue(null,"SeShutdownPrivilege",out var id)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var privileges = new TokenPrivileges { Count = 1, Id = id, Attributes = 2 };
            if (!AdjustTokenPrivileges(token,false,ref privileges,0,IntPtr.Zero,IntPtr.Zero) || Marshal.GetLastWin32Error() != 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { CloseHandle(token); }
    }
}
