# Launcher Automation Recorder

Reusable recorder/replayer for the official God2 launcher flow.

## Record

Start after any manual login step is complete:

```powershell
powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Invoke-LauncherAutomationRecord.ps1
```

Manually click the official Launcher only:

1. Accept the agreement.
2. Click Start Game.
3. If `Direct Sound Create failed!` appears, click OK.
4. Press `Ctrl+Shift+F12`.

The recorder writes:

- `Artifacts\ClientInstrumentation\LauncherAutomation\<run-id>\launcher-flow.json`
- `Artifacts\ClientInstrumentation\LauncherAutomation\<run-id>\launcher-flow-summary.md`

Credential policy: plaintext account and password values are never saved. Text keys are redacted and are not replayable.

## Captured Event Fields

Each event stores process/window identity, client-relative coordinates, DPI, event type, before/after conditions, timeout, and a small 32x32 screenshot hash/template around the click point when capture is available. Absolute screen coordinates are intentionally not persisted.

The replayer uses semantic targets first:

- Launcher process and executable path.
- Window class and target process.
- `WM_COMMAND IDOK` or `BM_CLICK` for Direct Sound dialogs.
- Client-relative coordinate replay only as fallback for WebView/owner-drawn regions.

## Replay

```powershell
powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Invoke-LauncherAutomationReplay.ps1 -FlowPath <launcher-flow.json>
```

Replay validates `ctserver.ini`, the `127.0.0.1:2592` listener, the Launcher window, and per-event conditions before sending input.

Replay is deterministic: it does not reorder events and it fails on the exact timed-out step instead of re-entering Launcher UI analysis.
