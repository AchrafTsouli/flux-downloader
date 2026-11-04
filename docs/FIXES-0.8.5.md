# Tray icon and transfer-rate display (0.8.5)

The installed executable is `ApexDownloader.exe`. Tray icon loading previously
looked for `SwiftFetch.exe`, causing the installed build to fall back to a generic
Windows icon. It now loads the shipped `app.ico`, with the running executable as
fallback. The offline installation diagnostic verifies that Windows successfully
loads the application icon rather than the generic fallback.

HTTP transfers still report progress at most every 200 ms. Video progress still
comes from yt-dlp. Both feed a shared display-only exponential moving average
with a two-second time constant, independent of event frequency. The progress
window continues refreshing every 500 ms. The main list, progress window and
remaining-time estimate all use the same smoothed speed. Transfer limits,
downloaded bytes, connection count and saved checkpoints are unaffected.

The first valid sample displays immediately. Sustained speed changes settle
over several seconds, while brief bursts are attenuated. Missing progress for
more than one second causes the estimate to decay; after ten seconds without
reports it shows no rate. Pausing, finishing and restarting clear the display
estimate, so a resumed transfer does not reuse its old speed.

Validation covers deterministic burst/stall/restart/invalid-sample cases,
download-row speed/ETA integration, independent window/tray behavior, and the
installed package's runtime, icon, native bridge, video-tool startup, upgrade and
uninstall checks. No video engine or format selection logic changed.

Validated release: 69 core checks, display speed/ETA integration, grouped tray and queue smoke tests, and nine installer checks passed. Complete installed footprint: 97.46 MB; setup: 78.42 MB (decimal). The installation diagnostic loaded the branded tray icon successfully. See docs/measurements/validation-0.8.5.json and installer-validation.json.
