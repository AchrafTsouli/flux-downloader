using System.Net;
using System.Text.RegularExpressions;
namespace SwiftFetch.Core;
public static class SiteGrabber
{
    private static readonly Regex Links = new("href\\s*=\\s*[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    public static async Task<List<string>> CollectAsync(Uri root, string extensions, DownloadSession session, CancellationToken token, System.Net.IWebProxy? proxy = null)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, Proxy = new SettingsProxy(() => proxy) });
        var pages = new Queue<Uri>(); pages.Enqueue(root); var visited = new HashSet<string>(); var files = new HashSet<string>();
        var types = extensions.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(ext => "." + ext.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (pages.Count > 0 && visited.Count < 20 && files.Count < 500)
        {
            var page = pages.Dequeue(); if (!visited.Add(page.AbsoluteUri)) continue;
            using var request = new HttpRequestMessage(HttpMethod.Get, page); session.Apply(request);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308) { if (response.Headers.Location is { } location && Uri.TryCreate(page, location, out var next) && SameSite(root, next)) pages.Enqueue(next); continue; }
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "text/html") continue;
            await using var stream = await response.Content.ReadAsStreamAsync(token); using var memory = new MemoryStream(); byte[] buffer = new byte[8192];
            while (memory.Length <= 2_000_000) { int read = await stream.ReadAsync(buffer, token); if (read == 0) break; memory.Write(buffer,0,read); }
            if (memory.Length > 2_000_000) continue;
            string html = System.Text.Encoding.UTF8.GetString(memory.ToArray());
            foreach (Match match in Links.Matches(html))
            {
                if (!Uri.TryCreate(page, WebUtility.HtmlDecode(match.Groups["url"].Value), out var link) || !SameSite(root, link)) continue;
                link = new UriBuilder(link) { Fragment = "" }.Uri;
                if (types.Contains(Path.GetExtension(link.AbsolutePath))) { files.Add(link.AbsoluteUri); if (files.Count >= 500) break; }
                else if (pages.Count < 100 && !visited.Contains(link.AbsoluteUri) && Path.GetExtension(link.AbsolutePath) is "" or ".html" or ".htm" or ".php") pages.Enqueue(link);
            }
        }
        return files.Order().ToList();
    }
    private static bool SameSite(Uri root, Uri link) => link.UserInfo.Length == 0 && link.GetLeftPart(UriPartial.Authority) == root.GetLeftPart(UriPartial.Authority);
}
