# Compact C# / WPF release (0.8.4)

The size target is the **installed application directory**, including video tools, private .NET runtime, extension, licenses and uninstaller. It does not mean just a compressed installer, an app that needs a machine-wide .NET installation, or moving dependencies to a permanent external cache.

## Measured release

Measured on Windows x64 on 2026-10-16; MB means 1,000,000 bytes.

| Component | Size |
| --- | ---: |
| Complete installed directory, including uninstaller | **97.43 MB** |
| Setup executable | **78.42 MB** |
| Video tools included in the above totals | 24.33 MB |
| Framework-dependent development `dist` | 60.30 MB |

The previous 0.8.3 setup executable was 206.04 MB. This comparison concerns installer downloads; it is not a measurement of the previous installed footprint. The nine installer checks pass, including a real 0.8.3 migration, runtime diagnostics, bridge roundtrip, reinstallation, and preservation of history/user files during uninstall. Exact bytes and artifact hashes are recorded in `docs/measurements/size-0.8.4.json`.

## Changes

- Publish the WPF application as a self-contained single executable with `EnableCompressionInSingleFile=true`, `PublishTrimmed=false`, and `IncludeNativeLibrariesForSelfExtract=false`. WPF and SQLite native DLLs remain normal files beside the application; they are not unpacked into a hidden runtime directory. All managed functionality is retained; WPF assemblies are not trimmed.
- Package the invariant English and French framework resources matching Apex's two supported interface languages. Unused framework translation assemblies are omitted; no application language or feature is removed.
- The same executable handles Chrome native messages when launched with the registered `chrome-extension://.../` argument. It calls the shared bridge code before creating `Application` or loading WPF resources. Normal launches still open the desktop app; `--browser` and `--background` keep the existing tray behavior. This avoids a second self-contained host/runtime. The standalone development host delegates to the same code.
- Use QuickJS-NG 0.17.0 (about 2.2 MB) instead of the approximately 94 MB Node executable. QuickJS is [supported by yt-dlp](https://github.com/yt-dlp/yt-dlp/wiki/EJS); the existing Node path remains a compatibility fallback for old tool folders. No external JS runtime is required by the new package.
- Build FFmpeg 8.1.2 as small LGPL shared libraries with MP4/MOV, Matroska/WebM, MPEG-TS/HLS, AAC and other input/container parsing needed by the existing video workflow. Keep all parsers and bitstream filters, networking with Windows Schannel, AAC/AAC-LATM and VP9 decoders for metadata/probing, stream-copy merging, and FFprobe final validation. Apex does not expose transcoding or editing, so general encoders, hardware codecs and unrelated editing libraries are not shipped.
- Retain yt-dlp, cookies/session handoff, format selection, retained parts for pause/resume, bandwidth limits, connection/fragment settings, safe destination publishing, optional SHA-256 validation and final video-plus-audio checks.

The FFmpeg source is pinned to the official tarball SHA-256. The build script applies a one-line fix to `mov_read_chnl`: return the actual channel parser status instead of an uninitialized local variable. The shipped source notice records the change, build script fingerprint, source URL and license. Shared libraries may be replaced by ABI-compatible builds.

## Builds and upgrades

`scripts/Install-VideoTools.ps1` now selects the compact build by default and uses `Install-CompactVideoTools.ps1`. Build-only LLVM-MinGW/MSYS2 archives are verified by pinned SHA-256, and MSYS2 verifies its build utility packages. These compilers/source trees are **not** installed on destination PCs. `-FullTools` retains the older general-purpose tool bootstrap for development or fixture generation.

The installer registers `ApexDownloader.exe` directly as the native messaging host. The extension ID and native host name are unchanged. When upgrading an installer-managed 0.8.3 full-runtime layout, setup runs that installation's existing uninstaller to remove its tracked obsolete runtime files, then installs the compact layout. Downloaded files, history, preferences and untracked user files are retained. Setup stops if the legacy removal fails; quit Apex normally first. A development `dist` folder is not automatically uninstalled.

`Build-Installer.ps1` fails if staged production files exceed 100,000,000 bytes. `Test-Installer.ps1` additionally checks the **actual installed directory including uninstaller**, verifies file hashes, launches the desktop diagnostic and native host with shared .NET lookup disabled, and sets `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to detect unwanted runtime extraction.

Compressed managed assemblies have a startup decompression cost and can change memory behavior. This change targets disk footprint; it does not claim a measured RAM reduction. Downloads and retained video fragments still need their normal storage, and yt-dlp's transient Python extraction behavior is unchanged.

## Validation artifacts

- `docs/measurements/compact-media-validation.json`: real MP4/WebM/MKV merges, unchanged encoded video packet hashes, and AAC/MPEG-TS compatibility.
- `docs/measurements/installer-validation.json`: measured installed bytes, native messaging, offline WPF/SQLite/DPAPI diagnostic, tool startup, upgrade and uninstall checks.
- `docs/measurements/compact-live-validation.json`: real YouTube MP4 (VP9 and AV1) and WebM downloads with verified audio/video, including a serialized pause/resume roundtrip.
- `scripts/Test-CompactMedia.ps1`: generates short offline fixtures with the old full FFmpeg for testing only, then exercises the compact tools. The full reference executable is not a product dependency.
- `SwiftFetch.MediaTests --live-pause --tools=...`: real public short YouTube selection, paused transfer with retained parts, serialized resume, merge and final audio/video verification.

The existing core and WPF smoke suites cover HTTP resume, queues, session storage, independent windows and tray behavior. Validation is performed on this Windows x64 machine; this is not an exhaustive clean-VM test of all Windows editions.
