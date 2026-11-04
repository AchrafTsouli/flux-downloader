# Flux development history

Flux was developed as Apex Downloader (internal name SwiftFetch) up to version 0.9.1. This page keeps the original milestone notes.


A native Windows download manager built with C# / WPF and .NET 10. No trial timer, ads, or paid dependencies.

**Windows installer:** run `installers/ApexDownloader-Setup-0.8.7-win-x64.exe` on a Windows 10 22H2 / Windows 11 64-bit PC. The private .NET runtime and video tools are included in the compact package. See [installation, extension setup, rebuilding and validation](INSTALLER.md). Double-click `Build Installer.cmd` to rebuild locally without Codex or AI tokens.

The October 14 reliability update shares file metadata probing between the start dialog and the engine, fixes ordinary/partitioned cookie handoff (extension 0.5.2), saves browser requests before acknowledging them, and avoids misleading authentication labels for HTML pages or HTTP 403 responses. See [HTTP fixes and validation](FIXES-HTTP-2026-10-14.md). The [status audit](STATUS-AUDIT-2026-10-14.md) records the broader implementation state and remaining work.

Browser confirmation now appears independently of the manager, including a cold launch. Captures awaiting confirmation are stored separately and do not appear in download history. Cancel creates no placeholder; Start Download or the explicit Download Later choice adds the task. See [browser confirmation workflow](BROWSER-CONFIRMATION.md).

Apex and the Chrome/Brave extension **0.8.0** add YouTube quality selection, separate audio/video downloads, retained pause/resume and automatic FFmpeg assembly. See [video workflow, setup and verification](VIDEO-0.8.0.md).

## Current milestone (0.9.0)

Version 0.9.0 adds video from sites beyond YouTube, YouTube playlists and channels with quality presets, audio-only downloads, HLS/DASH streams (including blob: players in the extension, version 0.9.0), a verified daily yt-dlp updater, and signed in-app updates. See [video, streams and updates](VIDEO-UPDATES-0.9.0.md).


Version 0.8.6 raises start and completion dialogues independently above other applications, including when the manager is hidden. See [window attention behavior and validation](FIXES-0.8.6.md).

Version 0.8.5 fixes the installed tray icon and smooths the displayed transfer rate and remaining time for HTTP/video downloads. See [behavior and validation](FIXES-0.8.5.md).

Version 0.8.4 keeps C# / WPF and targets a complete installed footprint below 100 MB. One executable serves the desktop app and native messaging, compressed managed assemblies keep the private runtime compact, QuickJS replaces Node.js, and a purpose-built shared FFmpeg/FFprobe retains packet-copy video/audio assembly and final validation. See [size optimization and validation](SIZE-0.8.4.md).

Version 0.8.3 fixes **Open with...** in the completion dialogue using the supported Windows `SHOpenWithDialog` API with the completion window as its owner. Cancelling the picker is silent; a missing file is reported instead of doing nothing. See [Microsoft's API documentation](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenwithdialog).

Version 0.8.2 brings the independent start download dialogue to the foreground for browser captures, including when the manager is hidden in the tray. The native bridge grants foreground permission only to its connected app process; the dialogue is briefly raised without remaining always on top. Extension 0.8.1 is unchanged.

Version 0.8.1 prepares video qualities before clicking, reads already-loaded player metadata when available, bounds fallback extraction/cache memory, and removes repeated idle history work. See [performance measurements and cleanup](PERFORMANCE-0.8.1.md).

Version 0.7.5 applies the app's dark/light preference to the native Windows title bar, retaining standard window controls. On Windows 11 the dark caption colors also apply when Windows uses light mode.

Version 0.7.4 restores separate unfinished queue views: selecting Queues shows the main queue, selecting a queue shows only its unfinished files, and All Downloads keeps the complete history. This supersedes 0.7.3 navigation. See [current queue behavior](QUEUE-VIEWS-0.7.4.md).

Version 0.7.3 keeps download history visible when a queue is selected; queue commands target that selection, while the independent scheduler lists its unfinished files. This supersedes the queue filtering described for 0.7.1/0.7.2. See [queue selection behavior](QUEUE-SELECTION-0.7.3.md).

Version 0.7.2 makes the manager more compact, redraws toolbar icons, improves category selection contrast and folder icons, and reduces menu spacing. See [visual update](VISUAL-0.7.2.md).

Version 0.7.1 separates unfinished queue views from all-download history, opens queue settings on double-click, adds drag selection with auto-scroll, and remembers the delete-confirmation preference. See [queue and selection corrections](FIXES-QUEUES-0.7.1.md).

Version 0.7 adds a persistent shared tray icon, close-to-tray, grouped progress windows, multiple row selection, main/synchronization queues and an independent scheduler with file ordering, dates/weekdays, retries and completion actions. See [0.7 behavior and validation](MILESTONE-0.7.md).

Version 0.6.1 makes the download windows independent, fixes clipping and saved per-file settings, and adds a progress notification-area button. See [dialog fixes and validation](FIXES-DIALOGS-0.6.1.md).

Version 0.6 adds the separate-window workflow, redesigned menus, eleven settings pages, named scheduled queues, browser capture policy, proxy/site-login settings, temporary folders, per-file limits and appearance controls. See [0.6 implementation and limits](MILESTONE-0.6.md).

Version 0.5 adds dynamic segment redistribution, transactional SQLite storage, DPAPI-protected per-download sessions, browser cookie/referrer/user-agent handoff, and manual HTTP Basic/Bearer login. See [docs/MILESTONE-0.5.md](MILESTONE-0.5.md) for migration, extension permissions, backup behavior, and authentication limits.

The complete supplied feature inventory is preserved in [docs/master-feature-inventory.md](master-feature-inventory.md). The 0.4 foundation architecture is documented in [docs/FOUNDATIONS.md](FOUNDATIONS.md); 0.5 supersedes its JSON-only storage and static-segmentation limits.

New in 0.4:

- Typed task states, error categories, HTTP/final URL/MIME metadata, timestamps, and file categories.
- Automatic retries for temporary network/server errors with bounded exponential delays.
- A FIFO queue with configurable simultaneous-download limits; **Pause all**, **Resume all**, and **Save for later**.
- **Settings** remembers the download directory, default connections, active-download limit, and retry count.
- Versioned history, migration from the previous format, atomic snapshots, and a last-good backup.
- Segment metadata saved before transfer, durable byte checkpoints, and safe disposal of unrecorded fragments.
- Optional expected SHA-256 verification before publishing a completed file.

Existing features:

- Paste direct HTTP/HTTPS file links and choose a destination folder.
- Select 1, 2, 4, 8 or 16 connections per file. Segments are used for files of at least 1 MB when the server supports byte ranges.
- See download progress, speed, estimated time, and status.
- Pause and resume. Partial files and history survive an app restart; downloads reopen paused unless their queue has Start on startup enabled.
- Resume directly from saved byte positions using HTTP Range, including servers without ETag/Last-Modified such as the tested MediaFire server. The default path requests only the remaining bytes, plus a one-byte capability probe and headers. Older single-part jobs are supported too. Changed validators, changed size or servers without byte-range support require a restart; the app explains why. When the server supplies no validator, same-size changes can go undetected. Settings offers an optional full check which rereads saved bytes over the Internet; it is off by default. A failed or cancelled full check preserves saved parts. See [resume behavior and official IDM references](RESUME.md).
- Fall back to one connection for servers without byte ranges.
- Download multiple files, and show their destination in Explorer.
- Existing files are preserved; new downloads get a numbered filename when needed.
- Chrome/Brave extension: automatic direct-download handoff, right-click file links, capture toggle, and connection test.
- Dark interface, file-type badges, source domains, search, library filters, and state-aware controls.
- Real server filenames, including UTF-8 names. Existing completed downloads with UUID names are repaired when their links still work. Rename a completed or paused file from the app.

## Run

Install the .NET 10 SDK for development, or the .NET 10 Desktop Runtime to run the framework-dependent release on another PC.

```powershell
dotnet run --project SwiftFetch/SwiftFetch.csproj
```

Build the Windows release:

```powershell
./scripts/Build-Release.ps1
```

Launch `dist/SwiftFetch.exe`.

## Chrome / Brave integration

Close the old SwiftFetch window before updating its release files.

1. Double-click `dist/Install Browser Integration.cmd`. This registers the native messaging host for the current Windows account; administrator rights are not needed. If the bridge is already registered, repeat this step only after moving the release directory.
2. Open `chrome://extensions` in Chrome or `brave://extensions` in Brave. Enable **Developer mode**, choose **Load unpacked**, and select the absolute `dist/BrowserExtension` folder.
3. Pin SwiftFetch's extension and click **Test connection**. **Capture browser downloads** starts enabled. Click an ordinary file download link, or right-click a direct file link and choose **Download with SwiftFetch**.

SwiftFetch opens automatically and receives downloads in its existing window. A page content script catches normal clicks on recognizable file links (such as `.exe`, `.zip`, release assets, and links with a `download` attribute) before browser navigation begins, including private windows when the extension is allowed there. Modified clicks, ordinary webpages, media-viewing links, and unsupported schemes keep their usual behavior. If direct handoff fails, the original link is clicked again and marked for browser-only fallback.

For downloads that cannot be identified from a click (such as JavaScript buttons or URLs without file extensions), the extension waits for the browser's filename-determination event. It pauses the download until the app saves and acknowledges the job, then cancels the browser copy and removes that entry from browser history. The browser can briefly start or display a download during this fallback path; Chromium's download events do not provide universal pre-download interception. If the host is unavailable or rejects the job, the browser download resumes. A badge and popup show the most recent integration error. Acknowledgement confirms that SwiftFetch accepted the job; later network failures appear in the app.

The extension needs `downloads`, `nativeMessaging`, `contextMenus`, `storage`, `cookies`, and HTTP/HTTPS host access. Cookie collection is scoped to the requested URL. Reload the extension after updating to 0.5.1, allow new permissions if prompted, and refresh open webpages. For private windows, enable **Allow in Incognito** (Chrome) or **Allow in Private** (Brave) in the extension's Details, then reopen the private window. Generated `blob:`/`data:` files, browser-flagged downloads on the fallback path, and downloads already completed or paused stay in the browser. Cookie sessions and manual Basic/Bearer credentials are supported; POST, some cross-domain authentication, browser-bound protections and single-use links can still require the browser. The right-click command sends the actual link; it does not extract files from a webpage.

Private integration uses Chromium's [split incognito context](https://developer.chrome.com/docs/extensions/reference/manifest/incognito). Cookies come from the private tab's store, or the private worker's default store when download events have no tab ID. Private extension status is held in session memory; only the capture setting is shared with normal windows. Files explicitly sent to the Windows app still use SwiftFetch's usual saved history and encrypted session persistence; the desktop app does not yet have an ephemeral private-download mode.

The bridge accepts only the fixed extension ID included in the manifest. App communication uses a Windows named pipe restricted to the current user. It does not start a local HTTP server. Request IDs are saved with download history to prevent repeated requests from making duplicate jobs.

To remove integration, run `dist/Uninstall-BrowserBridge.ps1` and remove the extension from each browser. Keep the `dist` directory together and rebuild/re-register if you move it.

Reference: [Chrome native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging), [Chrome downloads API](https://developer.chrome.com/docs/extensions/reference/api/downloads), and [Brave extension support](https://support.brave.com/hc/en-us/articles/360017909112-How-can-I-add-extensions-to-Brave).

## Verify

```powershell
dotnet run --project SwiftFetch.Tests/SwiftFetch.Tests.csproj
node --test BrowserExtension/transfer.test.js
dotnet run --project SwiftFetch.BridgeTests/SwiftFetch.BridgeTests.csproj
dotnet run --project SwiftFetch.UiSmoke/SwiftFetch.UiSmoke.csproj -- --integration
dotnet run --project SwiftFetch.UiSmoke/SwiftFetch.UiSmoke.csproj -- --metadata-tests
dotnet run --project SwiftFetch.UiSmoke/SwiftFetch.UiSmoke.csproj -- --capture-tests
```

The package-free integration harness exercises the engine, bounded queue, and state store: file/hash equality, safe resume, filename handling, transient retries, permanent errors, checksum verification, disk-space preflight, checkpoint ordering, tail recovery, legacy migration, and backup/corruption recovery. Extension tests simulate Chromium APIs for handoff, fallback, recovery, and native-port errors. Bridge checks run the actual host executable with framed messages and a per-user Windows pipe. Build the release and close SwiftFetch before bridge checks, and run Windows pipe checks under your normal account. UI checks use isolated state to verify browser requests, queue limits, deferred tasks, settings, and restart recovery.

## Current limits

The app supports direct HTTP/HTTPS downloads, named queues/scheduling, bandwidth limits and supported YouTube extraction. FTP and protected/DRM streaming are not implemented; video extraction covers the sites the bundled (auto-updated) yt-dlp supports. The browser extension is installed locally in developer mode; it is not published in the Chrome Web Store. Connection count does not guarantee faster downloads; speed depends on the server and available bandwidth. The Windows installer is self-contained; broad clean-machine compatibility testing and publisher code signing remain distribution work.

History and settings are stored in `%LOCALAPPDATA%/SwiftFetch/history.db`, with `history.backup.db` backups. Legacy JSON is imported once and preserved. URLs and session details are protected for the Windows account with DPAPI. Segment files stay beside the destination until completion. Assembly temporarily requires an additional file's worth of disk space. Rename paused files through the app. Resume checks validators and committed bytes; an expected SHA-256 adds publisher-checksum verification. Hardware power-loss behavior and publication-time recovery still need further qualification.


