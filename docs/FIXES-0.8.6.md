# Start and completion window attention (0.8.6)

Browser start confirmations already used the independent-dialog foreground path.
Completion windows had two plain `Show()` call sites: one when no progress window
was open and one from a progress window's completion callback. Both now use the
same independent-dialog foreground path as the start confirmation.

The helper establishes topmost ordering before the window is shown, requests
activation, and temporarily raises the window for 750 ms so its first frame
appears above other applications even when Windows declines a background focus
request. The previous topmost state is restored after that interval or sooner
when the user switches away. The timer is stopped on close. No input is simulated
and no thread input queues are attached.

Start and completion windows remain ownerless, modeless and visible in the
taskbar. The manager stays hidden if it was hidden, and remains usable when open.
Completion dialog preferences and duplicate-suppression logic are unchanged.

The independent-window smoke test covers browser confirmation while the manager
is hidden, initial raised ordering, removal of temporary topmost state, completion
notification while hidden, independent concurrent windows and tray behavior.
Windows denied foreground rights to the isolated test session, so the strict
keyboard-focus assertion was skipped; visibility, initial raising and release
assertions passed.

The installer upgrade from 0.8.5 and all nine installation/runtime/bridge/uninstall checks passed. Installed footprint: 97.46 MB; setup: 78.42 MB (decimal). See docs/measurements/validation-0.8.6.json and installer-validation.json.
