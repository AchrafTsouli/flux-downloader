# Performance and project cleanup — 0.8.1

## Faster video menus

The panel prepares video qualities as soon as a supported, visible player is detected in the active tab. Clicking a ready panel uses its already-rendered list. YouTube metadata is first read from the current page's player response in the MAIN world. Only bounded title/format metadata is copied back; signed URLs, cookies, signatures and player scripts are not exposed by that reader. It validates the current video ID to avoid stale SPA responses and ignores known DRM/live data.

If player metadata is missing, a native extraction is queued in the background. At most one such extraction runs per extension context, and concurrent requests for the same page share a result. Cached metadata lasts 15 minutes, with 8 entries in worker memory and 16 in browser session storage; tab closure removes its entries. A restarted worker can use session cache without rerunning the tools. Explicit Reload available qualities forces a fresh native lookup, which also provides a fallback if a page-advertised format is unavailable to the downloader.

First opening a newly loaded video can still wait if neither page metadata nor background extraction is ready. This is not a guarantee that an uncached network extraction is instantaneous. Native extraction fetches metadata/JavaScript; it does not preload the video body.

Page discovery is event driven with cached video elements and a slower fallback scan. Hidden tabs avoid periodic scans, offscreen players avoid automatic extraction, and reinjection disposes observers/timers/listeners from the current implementation. Refresh old video tabs once when upgrading from versions without this cleanup hook.

## App work and memory

- Idle history is no longer serialized, hashed and saved every two seconds.
- Active checkpoints atomically update only running downloads, preserving all other history, settings and pending browser requests. User actions and lifecycle/state changes still save complete snapshots.
- Idle rows are refreshed when the main window is activated instead of being re-notified continuously.
- Progress windows refresh at 500 ms instead of 120 ms. Hidden/minimized windows still check completion but skip rebuilding their visual trees and connection grids.
- No forced collection loop, working-set trimming or arbitrary memory cap was added. An experiment with .NET memory conservation did not materially improve this workload and was not adopted.

An isolated WPF test used 1,000 completed history entries and measured 8 seconds visible plus 8 seconds hidden. These are single-process samples, not guaranteed results on every machine:

| Visible idle sample | Before | After |
|---|---:|---:|
| Managed allocations / 8 seconds | 22.268 MiB | 0.00782 MiB |
| Process CPU / 8 seconds | 1062.5 ms | 140.6 ms |
| Private bytes | 89.54 MiB | 98.56 MiB |
| Working set | 163.97 MiB | 171.91 MiB |

The measured benefit is reduced allocation/CPU churn, not a demonstrated reduction of the WPF idle footprint. The main RAM safeguards are avoiding heavy extractor processes when player data exists, serializing fallback extractions, bounding caches and stopping hidden visual rebuilds. Video tools still use transient memory during real downloads/assembly. Raw samples are in docs/measurements/performance-before-0.8.1.json and performance-after-0.8.1.json.

## Cleanup

scripts/Clean-Project.ps1 removes only generated project artifacts: .test-tmp, project bin/obj directories, cached video-tool archives/extractions, Python bytecode, CLI telemetry and the obsolete root UI preview. It verifies absolute targets remain in this workspace and refuses reparse-point traversal. It preserves sources, docs/tests, NuGet dependency caches, dist, the installed video-tool executables and Downloads. It never accesses the user's real AppData download history or Downloads folder.

Browser regression scripts previously held in .test-tmp now live in BrowserExtension/tests. Use an installed Playwright module or set APEX_PLAYWRIGHT_MODULE to its location. Cleanup results are recorded in docs/measurements/cleanup-0.8.1.json. Building after cleanup regenerates the project outputs; normal build/publish commands restore from the retained dependency cache.

## Verification

- 48 extension tests pass: policy, player metadata, cache persistence, concurrency limits and bridge dispatch.
- 67 core integration checks pass, including incremental checkpoint preservation and rollback.
- WPF progress/start/finish lifecycle smoke tests pass after reducing visual updates.
- Browser fixture: qualities prepared before clicking, menu opened in 72 ms without another extraction.
- Real separate-track HTTPS formats 160+140 on the short public test video downloaded and assembled successfully (506,018-byte result).
- Real extension tests cover direct/blob players, late shadow roots and inherited frames.

Final fast-path check: the current public YouTube page for the short test video returned 14 adaptive format records. A sanitized metadata snapshot was supplied to the real extension in Brave; it prepared 5 video/audio quality choices before clicking, opened the menu in 7 ms, and its cache reported source=player. No yt-dlp/Node extraction was needed for that list. This test used real current page metadata in a controlled player document, rather than measuring a complete live YouTube page load. The native fallback and the independent download/mux pipeline remain separate paths.

Both WPF window lifecycle and queue/tray regression smoke tests passed after reducing progress refresh frequency.
