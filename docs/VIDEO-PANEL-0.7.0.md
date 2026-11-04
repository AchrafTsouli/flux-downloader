# Video download panel — extension 0.7.0

Chrome and Brave integration now places a compact Apex panel inside the top-left corner of visible HTML video players. It follows scrolling/resizing, uses isolated styles, has keyboard focus and Escape support, and can be dismissed per player. Click the header for available sources, format and known total size, or send all sources to independent Apex confirmation windows. The browser never starts a second download for these actions.

Detection combines video/source elements with observed HTTP response headers, including extensionless media URLs. Content-Range totals take precedence over the size of a partial response. No HEAD requests or media-body downloads are made for detection; unknown sizes remain unknown. Native metadata resolution/download after selection may make its usual requests.

Sources stay scoped to their tab, frame and document. Temporary media URLs are held in extension session storage, removed on tab close, limited to 32 per frame and expired after 30 minutes. The split private worker retains its own session. Cookie collection uses the initiating frame's partition and URL-scoped cookies; referer is the frame page. Nothing is sent to a remote service.

This release supports progressive HTTP video files (MP4, WEBM, OGV, MOV, M4V). It does not assemble HLS/DASH manifests, concatenate stream chunks, merge separate video/audio tracks, or download encrypted media. Known chunk/manifest/separate-track URLs are excluded. Blob URLs themselves are never sent to Apex; an unambiguous single player can use its frame's observed direct sources. Multiple players use matching direct source URLs rather than mixing unrelated video entries. Sites requiring custom request signatures/headers may still reject a transfer.

The extension popup adds **Show “Download this video” panels**. Upgrade initializes this to enabled, without overwriting an existing choice. This explicit per-browser setting takes precedence over the older native media-panel default. Capture disabled and native site/address exclusions are still respected.

## Installation / upgrade

Reload the unpacked extension from `dist/BrowserExtension` on `chrome://extensions` or `brave://extensions`. Accept the new webRequest observation permission if asked, then refresh video tabs. Play a video so its actual source is requested. Incognito requires Allow in Incognito, as before. No native bridge reinstall is needed; the extension ID and download protocol are unchanged.

## Validation

- `node --test BrowserExtension/*.test.js`: 36 passing tests, including parallel observed sources, document/frame isolation, range size, policy enforcement and native dispatch.
- Rendered Chrome fixture: panel/list, size/format display, single source, download all and close interactions passed. Fixture uses a mocked Chrome bridge; live site compatibility is not implied.
- Preview: `.test-tmp/video-panel-preview.png`.
