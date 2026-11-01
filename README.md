# Soundbar Keep Awake

A small, offline Windows utility that sends a short audio signal to the **Windows default output** on a timer. It is intended for a soundbar connected to a Windows PC that enters standby after roughly 15 minutes without audio.

## What it does

- Starts with an 8-second digital silence signal, then repeats every 10 minutes.
- Offers two quiet 440 Hz signal levels if the soundbar ignores digital silence.
- Runs in the system tray. Double-click its tray icon to open the controls; right-click for Open, Start, Stop, and Exit.
- Saves the selected interval and signal mode in `settings.json`.
- Uses local audio playback only. It does not open the microphone, record, connect to the internet, or collect data.

The signal goes to the **current Windows default audio output**. The utility cannot affect a soundbar connected only to a TV or another device.

## Install and start with Windows

1. Set the soundbar as the Windows default audio output.
2. In PowerShell, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1` from this folder.
3. The installer places the program in `%LOCALAPPDATA%\Programs\SoundbarKeepAwake`, creates a shortcut in your account's Startup folder, and starts it immediately in the tray. No administrator rights are needed.

At subsequent Windows sign-ins, it starts automatically with the saved settings. The installer does not change the system-wide PowerShell execution policy.

To run it without installing, double-click `Launch Soundbar Keep Awake.cmd` and click **Start**.

## Settings and operation

| Setting | Default | Meaning |
| --- | --- | --- |
| Interval | 10 minutes | Time between the start of each signal; selectable from 1 to 14 minutes. |
| Signal | Digital silence | Zero-value PCM audio samples. |
| Signal duration | 8 seconds | Fixed duration for every send. |

**Send now** plays one signal immediately. **Start** sends immediately and begins the timer. **Stop** halts the timer and current signal. Closing the window exits the utility; minimizing it keeps it running in the tray.

If the soundbar still turns off with digital silence, choose **Very quiet tone (-45 dB)**, then **Quiet tone (-35 dB)** if needed. These tones may be audible at high soundbar volume. Try the lowest level that works. A soundbar that looks for audio above a threshold cannot be reliably kept awake with complete silence.

Windows sleep and sign-out stop the timer. It resumes automatically at the next sign-in if installed. If the soundbar has an auto-standby setting, disabling that setting is usually more reliable.

## Privacy and security

The source uses `System.Media.SoundPlayer` to send a generated WAV signal. It has no microphone capture or network code. The startup shortcut launches the local script under your Windows account. `-ExecutionPolicy Bypass` applies only to that PowerShell process and does not change your account or system policy.

## Uninstall

Exit the app from its tray menu, then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1` from this source folder. This removes the Startup shortcut and installed program, including its saved settings.

## Limitations

Soundbar standby behavior varies by model and input type. Digital silence may not count as activity, and even a quiet tone may be below a model's detection threshold. This tool cannot guarantee that every soundbar stays awake.
