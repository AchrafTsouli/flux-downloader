# Video panel visibility fix — extension 0.7.1

The 0.7.0 panel was completely hidden whenever no supported direct video URL was known. This concealed the UI on blob/streaming players, while shadow-root videos were not discovered at all. Static script injection also required navigation after extension updates.

0.7.1 changes:
- Show the panel on visible video elements even while the source is unresolved. The menu displays a waiting, streaming limitation, disabled or connection status instead of disappearing. This does not add HLS/DASH download support.
- Discover videos in open shadow roots and react to inserted players and play/metadata events.
- Enable matching-origin injection into about:blank/srcdoc/blob frames; use the browser-provided origin for policy checks in inherited documents.
- Recover direct media resource URLs already present in the page's resource timing list, without fetching them again. Tab/frame/document scoping is retained.
- Explicit extension panel preference defaults to on regardless of the legacy native default, while global capture and URL exclusions remain enforced.
- Inject the video scripts into existing HTTP tabs after install/update. A same-version guard prevents duplicate panels on repeated injection.
- Add a popup repair button, Refresh video panels on this tab, and display the installed extension version.

Validation: 38 Node tests passed. Actual unpacked extension 0.7.1 was loaded in a clean Brave profile against a local HTTP fixture: direct video, blob video without a downloadable source, delayed open-shadow player and about:srcdoc player all showed visible panels. Repeated real scripting.executeScript calls left one panel. The blob menu's limitation text was checked through Chrome DevTools DOM inspection. No native download was started by these tests.

Reload the extension using the folder dist/BrowserExtension and confirm Extension 0.7.1 in its popup. Existing pages should get panels automatically; Refresh video panels on this tab can reattach them, and a full page refresh clears obsolete script contexts from earlier versions.
