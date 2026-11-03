# Soundbar Keep Awake

An offline Windows tray app for a PC-connected soundbar that goes into standby when audio stops. Version 2 is a compiled C# Windows executable. Startup launches the EXE directly; no PowerShell, command shell, scheduled script, browser, or administrator rights are needed to run it.

## Download and install

Download **SoundbarKeepAwake-v2.0.0.zip** from [Releases](https://github.com/ChiragGH/soundbar-keep-awake/releases/latest), extract it, and double-click **SoundbarKeepAwake-Setup.exe**. Setup is a compiled executable too; it does not run a shell or script. You may instead run `SoundbarKeepAwake.exe --install` directly.

Installation copies the app to `%LOCALAPPDATA%\Programs\SoundbarKeepAwake`, adds a Start menu shortcut, enables startup for your Windows account, and starts the app in the tray. It replaces the version 1 Startup shortcut, stops only the old installed soundbar PowerShell process, and backs up the old files in `legacy-backup`.

Open **Soundbar Keep Awake** from Start or double-click its tray icon. Select your soundbar output, then click **Apply / Start**. The selected device is saved by its Windows endpoint ID. Changing the Windows default output will not redirect the signal to another speaker or headphones. When the saved output is unavailable, the app waits and retries every five seconds. Power on and reconnect the soundbar yourself if it has already switched off.

You can also run `SoundbarKeepAwake.exe` without installing. It creates local settings but does not enable startup unless you check the startup option.

## Why version 1 could fail

Version 1 played eight seconds of all-zero audio every ten minutes through the Windows default output. A soundbar can ignore digital silence when checking for inactivity. A running PowerShell process also did not establish that audio was still reaching the soundbar.

Version 2 holds a shared WASAPI playback stream open on the selected soundbar. Its default settings are:

| Setting | Default |
| --- | --- |
| Background signal | Continuous 80 Hz signal at -70 dBFS |
| Standby prevention pulse | 440 Hz tone at -40 dBFS for 8 seconds |
| Pulse interval | 5 minutes, including a pulse when playback starts |
| Pulse fade | 50 ms fade in/out to reduce clicks |
| Recovery | Retry every 5 seconds if the output disappears or playback stalls |

The background signal keeps the audio transport streaming, and the periodic tone provides a real audio signal for standby detection. **The tone may be audible. Complete silence cannot be guaranteed to prevent standby on hardware that requires a detectable signal.**

If it still turns off, raise **Tone level** by 5 dB (for example, -40 to -35) and click **Apply / Start**. If it is too audible, lower the level. **Continuous quiet tone** is an alternative for devices that require uninterrupted detectable audio. Volume and mute settings in Windows and on the soundbar still affect the physical output. No software can guarantee compatibility with every hardware standby rule.

## Controls

- **Apply / Start:** save the output and signal settings and start or restart playback.
- **Pause:** stop playback and save the paused state, including across sign-in.
- **Close window:** hide to the system tray; playback continues.
- **Exit:** stop playback and close the app. Startup remains enabled if checked.
- **Start this app when I sign in:** add or remove this app's per-user startup registration.

The status line reports whether audio is playing, waiting for a device, or stopped. **Stream advancing** counts frames consumed by the Windows audio engine. This confirms software playback progress; it is not a measurement of soundbar power or microphone input. Windows sleep pauses delivery; the app retries if the audio device is invalidated after resume.

## Privacy and security

The app uses playback interfaces only. There is no microphone capture, audio loopback capture, network access, telemetry, auto-updater, or third-party runtime download. Local `settings.json` and a rolling `status.json` are stored in `%LOCALAPPDATA%\SoundbarKeepAwake`. Startup failures may also write `error.json` there. Status includes the chosen output name, stream progress and error text, never recorded audio.

Startup is the `SoundbarKeepAwake` value in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, pointing directly to the installed EXE with `--background`. The program runs with your normal user permissions. The binary is not code-signed. Source and SHA-256 checksums are provided for inspection; the executable format itself does not make software inherently safer.

## Remove the app

Uncheck **Start this app when I sign in**, then click **Exit**. Delete `%LOCALAPPDATA%\Programs\SoundbarKeepAwake`, `%LOCALAPPDATA%\SoundbarKeepAwake`, and the **Soundbar Keep Awake** shortcut in your Start menu. Alternatively, `SoundbarKeepAwake.exe --disable-startup` removes startup and tells the running instance to exit before you delete its files.

## Build and check

Requires Windows with .NET Framework 4.x and its built-in C# compiler. Run `build.cmd` from a source checkout. It produces the app and Setup executables, compiles the test runner, and checks the signal's timing, nonzero background, amplitude bounds, fades, PCM/float encoding, and cancellation when an output is missing. No package restore or network is needed. The build command is for source development only and is never used by the installed app.

`SoundbarKeepAwake.exe --probe "%LOCALAPPDATA%\SoundbarKeepAwake\probe.json" --quiet` writes playback endpoint names and connection states. The parent folder must exist. It does not open an audio capture stream.

The audio renderer follows Microsoft's [shared WASAPI rendering model](https://learn.microsoft.com/en-us/windows/win32/coreaudio/rendering-a-stream).
