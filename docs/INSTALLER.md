# Windows installer

Build output: `installers/ApexDownloader-Setup-0.8.7-win-x64.exe`, accompanied by a SHA-256 checksum file.

Version 0.8.6 measures **78.42 MB for setup** and **97.46 MB installed**, including the private runtime, video tools and uninstaller (decimal MB). See [measurements and validation](SIZE-0.8.4.md).

## Installation

Copy the single setup `.exe` to the destination PC and run it. The wizard supports English and French and installs for the current Windows user, normally under `%LOCALAPPDATA%\Programs\Apex Downloader`. It targets Windows 10 22H2 / Windows 11, x64-compatible systems. Windows on ARM can use x64 emulation; a native ARM64 package has not been built or validated. macOS, Linux and 32-bit Windows are not supported by this WPF package.

The package contains the .NET 10 desktop runtime, native SQLite, yt-dlp, compact FFmpeg/FFprobe shared libraries and QuickJS-NG. No SDK, external .NET installation, tool download or internet connection is needed **to install** it. Downloading files naturally requires connectivity. A single C# executable serves the desktop app and native messaging with a private runtime and compressed managed assemblies. WPF and SQLite native DLLs stay beside it, without a hidden runtime extraction copy. See [compact package design and validation](SIZE-0.8.4.md).

The wizard offers a desktop shortcut, Chrome/Brave bridge registration, and optional startup in the system tray. Startup is disabled by default. The Start menu includes an extension setup guide that displays the installed extension's exact folder path.

Chrome/Brave still require **Developer mode → Load unpacked → installed BrowserExtension folder**. The native host registration is automatic when selected, but the extension itself is not on the Chrome Web Store and is not silently installed or enabled. Reload an existing extension and refresh video tabs after updates. If migrating from the development folder, remove the old unpacked entry and load the installed copy: both intentionally have the same extension ID.

## Updates and removal

Run a newer setup over the existing installation. The stable AppId retains the chosen folder. When upgrading the installer-managed full-runtime 0.8.3 layout, setup runs that copy's uninstaller first to remove its tracked obsolete files, retaining history, downloads and untracked files. Installation and uninstallation check the application's `Local\ApexDownloader.Running` mutex even when it is hidden in the tray. Quit through **Tasks → Exit** or the tray menu first; setup does not force-stop downloads. The development build from before this mutex was added must also be exited before migrating.

Windows Settings → Apps can uninstall Apex. Only packaged files, generated bridge manifest, owned browser registrations, startup command and shortcuts are removed. Downloaded files, partially downloaded files, `%LOCALAPPDATA%\SwiftFetch` history/preferences and unknown user-created files are retained. Remove the unpacked browser extension separately. If another copy has replaced a native host registration or startup command, uninstallation leaves that other copy's value intact.

This first setup is **unsigned**. Windows may identify its publisher as unknown or show a SmartScreen reputation prompt. Authenticode signing with the publisher's certificate remains a release-distribution step; no self-signed trust certificate is installed. The adjacent SHA-256 file identifies the exact produced setup but is not a publisher signature.

## Rebuilding

Double-click **`Build Installer.cmd`** at the project root. It runs the extension
checks, publishes the application, bundles the extension and video tools, and
creates the setup in `installers`. The console stays open so you can read the
result; a dated `build-*.log` is saved beside the installer. This runs entirely
on your PC and does not use Codex or AI tokens. Internet access can be needed
for .NET package restore or the first Inno Setup bootstrap.

This launcher requires PowerShell 7.4+, the .NET 10 SDK and Node.js for the
extension checks, plus the compact video tools already present in `video-tools`.
It stops with a clear error if a prerequisite is missing. It does not install
the produced setup or alter the running application.

Equivalent command from PowerShell:

```powershell
./scripts/Build-LocalInstaller.ps1
```

Prerequisites for the **build machine only**: .NET 10 SDK, bundled tools in `video-tools`, and Inno Setup 6.7.3. [Inno Setup](https://jrsoftware.org/isdl.php) is the packaging compiler. Check its license if using the compiler commercially; it is not required on destination PCs.

```powershell
./scripts/Build-Installer.ps1 -BootstrapCompiler
```

The bootstrap downloads the pinned official Inno Setup executable, verifies GitHub's published SHA-256 and its Pyrsys B.V. Authenticode signature, and installs compiler tooling under `.tools/InnoSetup` without file associations or shortcuts. An existing compiler can instead be passed with `-CompilerPath 'C:\...\ISCC.exe'`.

The build stages only compiled binaries, self-contained runtimes, production extension files, video tools/licenses and guides in `artifacts/installer/win-x64`; source files, tests, caches, downloads, credentials and history are excluded. `payload-sha256.json` inventories the staged files. `-SkipPublish` recompiles the setup from an already prepared stage for installer-script changes.

Runtime updates require rebuilding and redistributing the self-contained package; target PCs do not independently patch the private runtime. Keep video tools and their license/source metadata together. Building is separate from `Build-Release.ps1`, so the existing development `dist` and its shortcuts are not replaced while producing an installer.

## Validation

```powershell
./scripts/Test-Installer.ps1 -InstallerPath ./installers/ApexDownloader-Setup-0.8.6-win-x64.exe
```

The validation script requires PowerShell 7.4 or newer on the test machine. It refuses to replace an already installed Apex copy. It installs to an isolated path containing spaces and Unicode, checks the SHA-256 payload inventory, native host manifest, Chrome/Brave registration, Start menu shortcut and optional startup command, then launches the installed native host and all four video tools. It checks that setup/uninstall refuse an active app mutex, reinstalls the package, and uninstalls it while checking preservation of user-created files, other installations' registrations and actual history. Existing bridge/startup values are restored afterward. Logs live under `.test-tmp`; the compact final report is `docs/measurements/installer-validation.json`.

The installed application's offline diagnostic can also be run directly:

```powershell
& "$env:LOCALAPPDATA\Programs\Apex Downloader\ApexDownloader.exe" --verify-installation
```

It checks WPF resources/native window creation, native SQLite and DPAPI in a disposable database, and bundled tool presence, then exits without opening the manager, starting transfers or reading real user history. Exit code 0 means success; diagnostics are written to `%TEMP%\Apex-installation-check.log`.

Validation on this development PC checks the bundled runtime with machine-wide .NET lookup disabled. It is not a clean-VM compatibility test of every Windows release, CPU architecture or SmartScreen policy.


