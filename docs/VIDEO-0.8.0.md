# Video qualities and audio/video assembly — 0.8.0

Apex 0.8.0 and extension 0.8.0 add YouTube video extraction, selected-quality downloads and automatic audio/video assembly. This replaces the earlier YouTube streaming limitation; other sites still use direct media detection.

## Flow

1. Open the video panel on a YouTube watch page, Shorts page or embedded player. Opening the menu requests available qualities; detection does not run an extraction every polling interval.
2. The native bridge runs the bundled yt-dlp extractor and returns title, selected format IDs, container, height/frame rate and size when supplied by the site. Extraction fetches player metadata/JavaScript, not full video bodies. Approximate sizes carry ≈ and unavailable sizes remain unknown.
3. The browser sends the selected format IDs and the page URL to Apex. Browser cookies remain scoped to that URL, frame partition and normal/private profile. Discovery does not open the Apex manager window.
4. The existing independent download confirmation appears. No library row is inserted until Start or Download Later is chosen. The confirmation uses media metadata instead of probing the HTML watch page as a file.
5. The queue runs yt-dlp, which resolves fresh source links, retains partial files/fragments, downloads the selected audio/video pair and invokes bundled FFmpeg to assemble them without transcoding.
6. Apex verifies that the result contains both video and audio using FFprobe. It publishes the destination only after successful assembly/verification. Existing destinations are never overwritten.

## Persistence and pause

The typed video selection, per-track progress and temporary directory are saved with the existing encrypted SQLite job payload. Pause terminates only the job's child process tree and preserves its part files and fragment checkpoints. Resume launches the same format selection with --continue in the same workspace; expiring source URLs are re-extracted. The last incomplete streaming fragment may need to be fetched again. Pausing while assembling retains the source parts for another assembly attempt.

Progress windows show per-track bytes. Estimated totals are labelled and final size is replaced by the actual assembled file size. Download Later, the main queue, tray operation and independent confirmation/progress/completion windows use the existing lifecycle.

## Tools

Run scripts/Install-VideoTools.ps1 to prepare the workspace video-tools directory, then scripts/Build-Release.ps1. The installer retrieves yt-dlp from its official GitHub release, FFmpeg/FFprobe from BtbN's LGPL Windows build, and Node 24 LTS from nodejs.org. Each artifact is checked against its published SHA-256 digest before use. Source URLs/digests and licenses are included in dist/video-tools. The application never automatically downloads or executes a new tool version at runtime.

Keep dist/video-tools beside Apex and the native host. The tools add roughly 370 MB to the unpacked release. Tool updates require explicitly running the installer and release script again. No global tool installation is required.

## Scope and limits

- The new extractor flow is limited to supported HTTPS YouTube video pages, including watch, Shorts and HTTPS embedded-player URLs. Progressive direct files on other sites retain the existing flow. This is not yet a general extractor UI for all websites.
- No DRM extraction, live recording or playlist batch expansion. Site/account restrictions are reported as errors; browser cookies do not guarantee access to every restricted video.
- HLS/DASH and separate tracks chosen by the YouTube extractor are handled by yt-dlp. Arbitrary non-YouTube manifest URLs are not exposed as downloadable files.
- File speed limits and a share of the configured global cap are supplied when a process starts. Pause/resume applies changed limits. External video jobs do not participate in the core engine's shared real-time bandwidth limiter, so mixed concurrent direct/video transfers do not have an exact aggregate cap yet.
- No metadata extraction result is advertised as an exact final container size. Final mux overhead can differ from summed source sizes.

## Verification

- 41 extension tests: policy/frame isolation, lazy extraction, cached format choices, typed dispatch, current-page origin checks and existing browser download handoff.
- 8 media pipeline checks: format/audio pairing, size estimates, malformed options, DRM/live exclusions, output verification, retained pause state and no overwrite.
- Isolated WPF video smoke test: confirmation metadata, no row before confirmation, Download Later, estimated size and persisted plan.
- A real public 19-second YouTube video was extracted and downloaded at 144p with a separate audio track. Actual pause retained 22,216 bytes; the job was serialized/reloaded and resumed, and FFprobe confirmed the final audio/video output (363,915 bytes). This was a short test, not a full-size video download.
- Browser fixture checked menu qualities, frame rate, estimated/unknown sizes and selected-format dispatch. Native bridge and existing core regression checks are run before release.

Final release checks: 10 native bridge checks and all 66 existing core integration checks passed. The real Brave extension/native-host/extractor path returned 7 distinct audio/video quality choices after equivalent variants were grouped; codec names distinguish H.264, VP9 and AV1 choices. The browser test served a controlled player document at the public test video URL while the native host performed real YouTube metadata extraction. No download/library entry was created by that menu-only check.
