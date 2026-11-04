using System.Net;
namespace SwiftFetch.Core;
internal sealed class SettingsProxy(Func<IWebProxy?> current) : IWebProxy
{
    public ICredentials? Credentials { get => current()?.Credentials; set { if (current() is { } proxy) proxy.Credentials = value; } }
    public Uri? GetProxy(Uri destination) => current()?.GetProxy(destination) ?? destination;
    public bool IsBypassed(Uri host) => current()?.IsBypassed(host) ?? true;
}
public static class CommandArguments
{
    public static List<string> Split(string value)
    {
        var result = new List<string>(); var token = new System.Text.StringBuilder(); bool quoted = false;
        foreach (char c in value) { if (c == '"') quoted = !quoted; else if (char.IsWhiteSpace(c) && !quoted) { if (token.Length > 0) { result.Add(token.ToString()); token.Clear(); } } else token.Append(c); }
        if (quoted) throw new ArgumentException("An argument has an unmatched quote.");
        if (token.Length > 0) result.Add(token.ToString());
        return result;
    }
}
