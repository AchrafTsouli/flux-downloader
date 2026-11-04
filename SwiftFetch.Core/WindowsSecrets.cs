using System.Security.Cryptography;
using System.Text;

namespace SwiftFetch.Core;

public static class WindowsSecrets
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SwiftFetch/state/v1");
    public static byte[] Protect(byte[] value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("SwiftFetch session storage requires Windows DPAPI.");
        return ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
    }
    public static byte[] Unprotect(byte[] value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("SwiftFetch session storage requires Windows DPAPI.");
        return ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
    }
}
