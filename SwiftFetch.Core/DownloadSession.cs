using System.Net.Http.Headers;

namespace SwiftFetch.Core;

public sealed class DownloadSession
{
    public string Origin { get; set; } = "";
    public string? Referer { get; set; }
    public string? UserAgent { get; set; }
    public string? Authorization { get; set; }
    public List<SessionCookie> Cookies { get; set; } = [];

    public void Validate(string downloadUrl)
    {
        if (!Uri.TryCreate(Origin, UriKind.Absolute, out var origin) || origin.Scheme is not ("http" or "https") || origin.UserInfo.Length > 0 || origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0 || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var download) || origin.GetLeftPart(UriPartial.Authority) != download.GetLeftPart(UriPartial.Authority))
            throw new ArgumentException("The session origin must match the download URL.");
        if (Cookies == null || Cookies.Count > 200) throw new ArgumentException("The session contains too many cookies.");
        foreach (var cookie in Cookies)
        {
            if (cookie == null || string.IsNullOrEmpty(cookie.Name) || cookie.Name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '=' or ';' or ',') || cookie.Value == null || cookie.Value.Any(c => char.IsControl(c) || c == ';') || !DomainMatches(origin.IdnHost, cookie.Domain, cookie.HostOnly) || cookie.Path == null || !cookie.Path.StartsWith('/'))
                throw new ArgumentException("Invalid session cookie.");
        }
        if (Cookies.Sum(c => c.Name.Length + c.Value.Length) > 24000) throw new ArgumentException("Session cookies are too large.");
        foreach (var header in new[] { Referer, UserAgent, Authorization })
            if (header?.Length > 8192 || header?.Any(char.IsControl) == true) throw new ArgumentException("Invalid session header.");
        if (Authorization != null && !AuthenticationHeaderValue.TryParse(Authorization, out _)) throw new ArgumentException("Invalid authorization header.");
        if (Referer != null && (!Uri.TryCreate(Referer, UriKind.Absolute, out var page) || page.Scheme is not ("http" or "https") || page.UserInfo.Length > 0)) throw new ArgumentException("Invalid referring page.");
    }

    public void Apply(HttpRequestMessage request)
    {
        var target = request.RequestUri!;
        var origin = new Uri(Origin);
        bool sameOrigin = target.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority);
        if (UserAgent != null)
        {
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        }
        if (sameOrigin && Authorization != null) request.Headers.Authorization = AuthenticationHeaderValue.Parse(Authorization);
        if (Referer != null)
        {
            var page = new Uri(Referer);
            if (!(page.Scheme == "https" && target.Scheme == "http"))
                request.Headers.Referrer = new Uri(new UriBuilder(page) { Fragment = "" }.Uri.AbsoluteUri);
        }
        string cookieHeader = string.Join("; ", Cookies.Where(cookie =>
            DomainMatches(target.IdnHost, cookie.Domain, cookie.HostOnly) && (!cookie.Secure || target.Scheme == "https") &&
            (cookie.ExpiresUnixSeconds == null || cookie.ExpiresUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) &&
            PathMatches(target.AbsolutePath, cookie.Path)).OrderByDescending(c => c.Path.Length).Select(c => c.Name + "=" + c.Value));
        if (cookieHeader.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
    }

    public static bool DomainMatches(string host, string domain, bool hostOnly)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        domain = domain.TrimStart('.').ToLowerInvariant();
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) || (!hostOnly && host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
    public static bool PathMatches(string path, string cookiePath) => path == cookiePath || (path.StartsWith(cookiePath, StringComparison.Ordinal) && (cookiePath.EndsWith('/') || path.Length > cookiePath.Length && path[cookiePath.Length] == '/'));
}

public sealed class SessionCookie
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Path { get; set; } = "/";
    public bool HostOnly { get; set; } = true;
    public bool Secure { get; set; }
    public double? ExpiresUnixSeconds { get; set; }
}
