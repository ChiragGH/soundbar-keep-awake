# Changes

## 2.0.0

- Replaced the PowerShell app with a compiled Windows Forms executable.
- Replaced isolated digital-silence playback with a continuous nonzero stream and adjustable quiet pulses every five minutes.
- Added explicit playback-device selection, persistent endpoint IDs, automatic retries, and a playback-progress watchdog.
- Added direct EXE startup, Start menu access, a single-instance guard, and local status diagnostics.
- Added migration from the legacy startup script without changing Windows security settings.

## 1.0

Initial PowerShell prototype with periodic digital silence and optional quiet tones.
