using System.Net.Http;
using System.Text.RegularExpressions;

namespace SwiftFetch.Core;

public static class MediaResolver
{
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

    public static async Task<(string ResolvedUrl, DownloadSession ResolvedSession)?> TryResolveAsync(string url, DownloadSession? session, HttpClient client, CancellationToken token = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        // 1. mp4upload.com
        if (DownloadSession.DomainMatches(uri.IdnHost, "mp4upload.com", false))
        {
            var match = Regex.Match(uri.AbsolutePath, @"^/(?:embed-)?([a-zA-Z0-9]+)(?:\.html)?$");
            if (match.Success && !uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                string id = match.Groups[1].Value;
                string embedUrl = $"https://www.mp4upload.com/embed-{id}.html";
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, embedUrl);
                    req.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Referer", "https://www.mp4upload.com/");
                    session?.Apply(req);
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, token);
                    if (resp.IsSuccessStatusCode)
                    {
                        string html = await resp.Content.ReadAsStringAsync(token);
                        var vMatch = Regex.Match(html, @"src:\s*[""'](https?://[^""']+\.mp4[^""']*)[""']");
                        if (!vMatch.Success)
                            vMatch = Regex.Match(html, @"(https?://[a-zA-Z0-9\.\-:]+/[^""']*?\.mp4[^""']*)");

                        if (vMatch.Success)
                        {
                            string directUrl = vMatch.Groups[1].Value;
                            var videoUri = new Uri(directUrl);
                            if (videoUri.UserInfo.Length > 0 || uri.Scheme == "https" && videoUri.Scheme != "https") return null;
                            var resolvedSession = new DownloadSession
                            {
                                Origin = videoUri.GetLeftPart(UriPartial.Authority) + "/",
                                Referer = "https://www.mp4upload.com/",
                                UserAgent = session?.UserAgent ?? BrowserUserAgent,
                                Authorization = session != null && new Uri(session.Origin).GetLeftPart(UriPartial.Authority) == videoUri.GetLeftPart(UriPartial.Authority) ? session.Authorization : null
                            };
                            if (session?.Cookies != null)
                            {
                                foreach (var c in session.Cookies)
                                    if (DownloadSession.DomainMatches(videoUri.IdnHost, c.Domain, c.HostOnly)) resolvedSession.Cookies.Add(c);
                            }
                            return (directUrl, resolvedSession);
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (HttpRequestException) { }
            }
        }

        return null;
    }
}
