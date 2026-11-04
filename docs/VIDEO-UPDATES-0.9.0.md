# 0.9.0: video from other sites, playlists, HLS/DASH and updates

## Video beyond YouTube

- **Add URL** has a new checkbox, **Find video on this page**. It is ticked automatically for known video pages; you can tick it yourself for any other page that yt-dlp supports. The **video picker** then lists the qualities, adds one audio-only choice (M4A, or WEBM when the site has no M4A), and offers **Download as a file instead**.
- Video pages recognised automatically by Add URL, the clipboard watcher and the drop target: YouTube, Vimeo, Dailymotion, X/Twitter, Facebook, Instagram, TikTok, Twitch, Reddit, Streamable, Bilibili, Rumble, Odysee and TED. The same list lives in `SwiftFetch.Core/VideoFormats.cs` (`VideoPages.KnownPages`) and in `BrowserExtension/media-policy.js`. Keep the two in step.
- Formats whose codecs aren't reported (common outside YouTube) are treated as present. A video with no sound is labelled *video only* and isn't rejected. DRM, RTMP/RTSP/MMS/F4M/ISM formats and live broadcasts are still refused.
- yt-dlp sometimes picks a different container than the one first requested, for presets or for streams it remuxes. The finished file is then given the real extension (for example `.mkv`), and the name stays unique.
- When a session supplies a referer and user agent, both are passed to yt-dlp. Many streams check them.

## Playlists and channels

Playlist, `watch?v=…&list=…`, `@channel`, `/channel/UC…`, `/c/…` and `/user/…` addresses on YouTube open a list of up to 1,000 entries with checkboxes. Private and deleted entries are skipped. You pick one preset for all of them: Best, up to 2160/1440/1080/720/480/360p, or audio only. Each video becomes its own job, optionally saved in a folder named after the playlist. Presets are stored as `apex-best`, `apex-<height>` or `apex-audio`. yt-dlp resolves them per video with `-f bv*+ba/b -S res:<height>,ext:mp4:m4a` and prints the tracks it chose, so progress and resume work as they do for explicit qualities.

## HLS/DASH streams

- A `.m3u8` or `.mpd` address goes straight to the video picker.
- In the browser, the extension records the manifests that each frame's player loads. When the player uses a `blob:` source, the panel offers that stream's qualities. Downloads are named after the tab title, because manifests rarely carry a title. A page can only ask for a manifest its own frame actually loaded.
- yt-dlp downloads the fragments in parallel (using the connection setting). The bundled FFmpeg then remuxes them into MP4 without re-encoding.
- From the browser, the bridge accepts video jobs only for known sites and manifest addresses. Any other page can still be tried from the app.

## Updates

- **yt-dlp:** checked daily (Options → View, on by default) and from **Help → Check for updates**. The newest release is downloaded from `github.com/yt-dlp/yt-dlp/releases`, compared with the release's `SHA2-256SUMS`, run once to confirm its version, and stored in `%LOCALAPPDATA%\SwiftFetch\tools`. It's used only when it is newer than the bundled copy. The bundled tools are never modified.
- **Apex itself:** the update feed is a `version.json` file holding `version`, `url`, `sha256` and an ECDSA P-256 `signature` over all three.
  1. Run `scripts/New-UpdateKey.ps1` once. It stores a DPAPI-protected private key in `%LOCALAPPDATA%\ApexRelease\` and embeds the public key in `SwiftFetch.Core/UpdateSigningKey.cs`. **Back up that key file.**
  2. After building the installer, run `scripts/Sign-Release.ps1 -Installer <setup.exe> -DownloadUrl <https link> -Version 0.9.0` and upload both files.
  3. Set the feed URL in Options → View, for example `https://github.com/<owner>/<repo>/releases/latest/download/version.json`.

  The app checks once a day. A signed release installs after you confirm: downloads pause, the installer runs silently once Apex exits, and it relaunches Apex. Unsigned feeds, and builds made before a key was generated, only offer to open the download page. A bad signature or checksum is rejected.

## Verification

- `dotnet run --project SwiftFetch.Tests`: 71 checks, including signed and tampered feeds and the rule for choosing the updated yt-dlp.
- `dotnet run --project SwiftFetch.MediaTests`: 14 checks covering generic/HLS parsing, playlists, presets, extension correction and header forwarding. Real-tool modes:
  - `-- --probe=<url> [--download=best|audio|<height>|first]`
  - `-- --update-ytdlp=<scratch folder>`
- `node --test BrowserExtension/*.test.js`: 55 tests, including site and stream detection and stream dispatch.
- Live results on 2026-11-03:
  - Mux HLS test stream: 5 qualities; the 360p preset downloaded 36.7 MB, verified with ffprobe.
  - Archive.org: works.
  - YouTube playlist: 182 entries listed.
  - The updater installed yt-dlp 2026.10.26, which fixes TED pages that the bundled 2026.08.19 copy fails on.
  - Vimeo now needs a signed-in session, which the extension supplies from the browser.
