<p align="center"><img src="SwiftFetch/app-icon.png" width="96" alt="Flux icon"></p>

<h1 align="center">Flux</h1>

<p align="center">A fast, free download manager for Windows, with browser capture and video downloads.<br>No ads, no trial timer, no account.</p>

<p align="center"><a href="https://github.com/AchrafTsouli/flux-downloader/releases/latest"><b>Download the latest installer</b></a></p>

## Features

- **Faster downloads:** up to 16 connections per file, with segments rebalanced while it downloads.
- **Reliable resume:** pause, close the app or lose the connection; Flux continues from the saved byte position. Optional SHA-256 verification, and a disk-space check before starting.
- **Browser capture:** the Chrome / Edge / Brave extension hands downloads and magnet links to Flux, together with the cookies and referring page they need.
- **Video and audio:** pick a quality on YouTube, Vimeo, X, TikTok, Instagram and many other sites, or use **Find available qualities** on any page with a video. Also downloads HLS/DASH streams, YouTube playlists and channels, and audio-only M4A.
- **Download part of a video:** choose a start and end time; fast cut in seconds, or exact to the second using Windows' own H.264 encoder.
- **Torrents:** magnet links and .torrent files, choose which files to download, public trackers and DHT to find peers, no sharing afterwards unless you want it.
- **Strict sites:** replays the browser's own request headers, retries blocked video sites while imitating Chrome, and lowers connections per site when a server insists (and remembers it).
- **Drag and drop:** drag a finished file from the completion window to the desktop, a folder or a chat.
- **Queues and scheduling:** named queues, start and stop times, weekdays, a limit on simultaneous downloads, and shutdown or sleep when everything is done.
- **Speed limits,** globally or per file.
- **Stays up to date:** the video downloader (yt-dlp) updates itself daily, and Flux offers new releases signed by the project.
- **Private by design:** saved links and logins are encrypted for your Windows account. The browser talks to Flux only through a Windows named pipe; there is no local web server.

## Install

1. Download `Flux-Setup-<version>-win-x64.exe` from [Releases](https://github.com/AchrafTsouli/flux-downloader/releases/latest) and run it. It needs Windows 10 22H2 or Windows 11 (64-bit) and no administrator rights. Everything it needs, including the .NET runtime and the video tools, is inside the installer.
2. Keep **Connect Flux to Chrome and Brave** ticked.
3. In `chrome://extensions` (or `edge://extensions`, `brave://extensions`), turn on **Developer mode**, choose **Load unpacked** and select the `BrowserExtension` folder in the Flux installation folder. The setup page the installer opens shows the exact path.
4. Click the Flux toolbar icon, then **Test connection**.

Windows SmartScreen may warn about the installer because it is not code-signed yet. Choose **More info → Run anyway**.

## Build from source

Requirements: Windows, the [.NET 10 SDK](https://dotnet.microsoft.com/download), [PowerShell 7.4+](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) and Node.js (for the extension tests).

```powershell
git clone https://github.com/AchrafTsouli/flux-downloader.git
cd flux-downloader
pwsh scripts/Get-VideoTools.ps1        # restores yt-dlp, FFmpeg and QuickJS, verified against video-tools/sources.json
dotnet run --project SwiftFetch/SwiftFetch.csproj
```

To build the installer, double-click `Build Installer.cmd`. The result is written to `installers/`.

Tests:

```powershell
dotnet run --project SwiftFetch.Tests            # engine, storage, updates
dotnet run --project SwiftFetch.MediaTests       # video formats, playlists, streams
dotnet run --project SwiftFetch.UiSmoke -- --integration
node --test BrowserExtension/*.test.js           # browser extension
```

## Releasing a new version

1. Once only: `pwsh scripts/New-UpdateKey.ps1` creates the update signing key and embeds its public half in the app. Back up the key file it prints; it never goes into the repository.
2. Raise `<Version>` in `SwiftFetch/SwiftFetch.csproj`, commit and push.
3. `pwsh scripts/Publish-Release.ps1` builds the installer, signs `version.json` and publishes both as a GitHub release.

Installed copies check `releases/latest/download/version.json` once a day and offer to install a correctly signed release.

## Project layout

| Folder | Contents |
|---|---|
| `SwiftFetch/` | WPF desktop app |
| `SwiftFetch.Core/` | Download engine, video tools, storage, updates |
| `SwiftFetch.Bridge/`, `SwiftFetch.NativeHost/` | Browser bridge (native messaging over a named pipe) |
| `BrowserExtension/` | Chrome / Edge / Brave extension |
| `installer/`, `scripts/` | Inno Setup installer and build, release and tool scripts |
| `docs/` | Design notes and [development history](docs/HISTORY.md) |

The internal project names (`SwiftFetch`) date from before the rename and are kept so existing installations upgrade cleanly.

## License

Flux is released under the [MIT License](LICENSE): anyone may use, modify and redistribute it.

## Third-party components

Flux bundles [yt-dlp](https://github.com/yt-dlp/yt-dlp) (Unlicense), a minimal LGPL build of [FFmpeg](https://ffmpeg.org) (build script and source link in `video-tools/`), and [QuickJS-ng](https://github.com/quickjs-ng/quickjs) (MIT). Their licences are installed in the `licenses` folder.

You are responsible for respecting copyright and the terms of the sites you download from.
