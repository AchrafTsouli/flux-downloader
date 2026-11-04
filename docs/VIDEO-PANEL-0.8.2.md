# Video panel 0.8.2 / installer 0.8.7

The overlay now requires an actively playing, decoded video without a media
error or attached DRM keys, plus a recognized complete file or prepared
YouTube quality catalog. Paused previews, failed players and unsupported
streams do not show an empty download overlay. In a feed, one playing player
per frame is chosen, preferring audible playback and then recent playback.
YouTube auxiliary/ad players are excluded when the main player is identified.
This does not add extraction support for new sites or DRM content.

Drag the bar or its dotted handle with the mouse. A drag does not open the
source menu. Placement stays fixed while scrolling and is clamped when the
window is resized or the menu is opened. The handle also accepts arrow keys
(Shift for larger steps) and Home to return to automatic placement. Placement
belongs to that player on the current page, not a saved browser preference.

Both static and update-time script injection load the new shared panel policy.
Reload extension 0.8.2 and refresh existing video tabs after copying the update.
`dist/BrowserExtension` and the 0.8.7 installer contain the updated files.

Validation: 51 extension Node tests passed. A separate headless Chrome test
uses simulated video states with real DOM layout and pointer input: paused/
failed/unsupported players, one active panel, ordinary menu clicks, dragging,
scrolling, resize and pause. This is not a live Facebook site test. Evidence
is in `.test-tmp/video-panel`; run `scripts/Test-VideoPanel.cjs` with Playwright
available (or set `APEX_PLAYWRIGHT_MODULE` to its module path).

`Build Installer.cmd` runs the local build without an AI service. The tested
PowerShell launcher writes an installer, checksum and transcript under
`installers`. Build 0.8.7 staged a 93.02 MB self-contained payload; setup is
unsigned as before. See `INSTALLER.md` for prerequisites and distribution scope.
