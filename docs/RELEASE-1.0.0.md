# Flux 1.0.0

## Download part of a video
In the video picker, tick **Download only part of the video** and enter a start and end time (`90`, `1:30` or `1:02:03`). **Open at start time** opens the video in your browser at that point, so you can check the part first.

- **Fast cut:** only the part is fetched from the site, in seconds and at the original quality. It starts at the nearest keyframe, so a player may show a second or two before your start time.
- **Exact to the second:** the part is re-encoded with Windows' own H.264 encoder (Media Foundation), so it starts on the exact frame. The encoder is part of Windows, so no large codec is bundled; FFmpeg grows by about 1.8 MB and stays LGPL. Sources in AV1 can't be cut exactly: choose an H.264 or VP9 quality, or use Fast cut. The second progress bar shows the re-encoding.

## Sites that refuse downloads (HTTP 403)
- **Browser headers:** the extension records the headers the browser sent for each request (language, `Sec-Fetch-*`, client hints) and Flux replays them, so sites that accept only "the browser's own request" let Flux in.
- **Direct file links:** if a link still answers 403, Flux tries once more the way a browser opens a typed address, and keeps that form for the rest of the download.
- **Video sites:** if a site refuses yt-dlp (403, or a Cloudflare check), Flux retries once while imitating Chrome's network signature (`--impersonate chrome`) and remembers that for the download.

## Fewer connections where a site insists
When a site refuses an extra connection (403, 429 or 503 on a request), only that connection stops. Its part is taken over by the others, the download continues without restarting, and the site is remembered with the number of connections that worked. Subdomains share their site's limit. (Ezoo halves the count; Flux keeps every connection that still works.)

## Large files
- **No second copy:** a finished video is moved into place instead of being copied again, so large files finish in about half the time.
- **Second progress bar:** the progress window shows a bar for the finishing stage: joining video and audio, saving, copying to another drive (with a percentage) or exact cutting.

## Drag the finished file
In the **Download complete** window, the icon is now the file's real Windows icon. Drag it to the desktop, a folder or a chat. Dropping always copies, so the download list keeps its file.

## Torrents
- **Adding:** magnet links (Add URL, clipboard, or a click on a web page) and `.torrent` files (**File → Open torrent file…**) open the torrent window.
- **File list:** for a magnet link, Flux first asks peers for the file list, then shows every file with its size so you can untick what you don't need. Only the chosen files are downloaded.
- **Peers:** public trackers are added to every torrent, and DHT, local peer discovery and peer exchange are on, so links with dead trackers still find peers.
- **Sharing:** nothing is uploaded after the download finishes unless you choose to share for a number of minutes.
- **Pause and resume:** both continue where the download stopped.
- **Engine:** aria2 1.37.0 (GPL-2.0, run as a separate program; licence in `video-tools`). It listens only on 127.0.0.1 with a random token for each start, and stops together with Flux.

## Also
- Microsoft Edge is connected by the installer, along with Chrome and Brave.
- `video-tools/sources.json` records every bundled binary, including aria2, and `scripts/Get-VideoTools.ps1` verifies them all.
