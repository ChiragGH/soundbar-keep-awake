param([switch]$SelfTest, [switch]$AutoStart)

# Local playback only. This script never opens a microphone or makes a network request.
Add-Type -TypeDefinition @'
using System;
using System.IO;

public static class KeepAwakeWave
{
    public static byte[] Create(int seconds, double amplitude)
    {
        const int rate = 44100;
        const int channels = 2;
        const int bits = 16;
        int frames = checked(rate * seconds);
        int dataBytes = checked(frames * channels * (bits / 8));
        using (var stream = new MemoryStream(44 + dataBytes))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * (bits / 8));
            writer.Write((short)(channels * (bits / 8)));
            writer.Write((short)bits);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            for (int i = 0; i < frames; i++)
            {
                // A short fade avoids clicks at the beginning and end.
                double fade = Math.Min(1.0, Math.Min(i, frames - 1 - i) / (rate * 0.05));
                short sample = (short)Math.Round(Math.Sin(2.0 * Math.PI * 440.0 * i / rate)
                    * amplitude * fade * short.MaxValue);
                writer.Write(sample);
                writer.Write(sample);
            }
            return stream.ToArray();
        }
    }
}
'@

if ($SelfTest) {
    $silent = [KeepAwakeWave]::Create(1, 0.0)
    $quiet = [KeepAwakeWave]::Create(1, 0.0056)
    if ([Text.Encoding]::ASCII.GetString($silent, 0, 4) -ne 'RIFF') { throw 'Missing WAV header.' }
    if ($silent.Length -ne 176444 -or $quiet.Length -ne 176444) { throw 'Incorrect WAV length.' }
    if ([BitConverter]::ToInt32($silent, 40) -ne 176400) { throw 'Incorrect audio data length.' }
    if (($silent[44..($silent.Length - 1)] | Where-Object { $_ -ne 0 } | Select-Object -First 1)) {
        throw 'Silent mode contains a nonzero sample.'
    }
    if (-not ($quiet[44..($quiet.Length - 1)] | Where-Object { $_ -ne 0 } | Select-Object -First 1)) {
        throw 'Quiet mode contains no signal.'
    }
    $testStream = New-Object System.IO.MemoryStream(,$silent)
    $testPlayer = New-Object System.Media.SoundPlayer($testStream)
    try { $testPlayer.Load() }
    finally { $testPlayer.Dispose(); $testStream.Dispose() }
    Write-Output 'WAV generation checks passed.'
    return
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$configPath = Join-Path $PSScriptRoot 'settings.json'
$savedInterval = 10
$savedMode = 0
if (Test-Path -LiteralPath $configPath) {
    try {
        $settings = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        if ([int]$settings.intervalMinutes -ge 1 -and [int]$settings.intervalMinutes -le 14) {
            $savedInterval = [int]$settings.intervalMinutes
        }
        if ([int]$settings.signalMode -ge 0 -and [int]$settings.signalMode -le 2) {
            $savedMode = [int]$settings.signalMode
        }
    } catch { }
}

function Save-Settings {
    $settings = [ordered]@{
        intervalMinutes = [int]$interval.Value
        signalMode = [int]$mode.SelectedIndex
    }
    $settings | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Soundbar Keep Awake'
$form.ClientSize = New-Object System.Drawing.Size(440, 295)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

$intro = New-Object System.Windows.Forms.Label
$intro.Location = New-Object System.Drawing.Point(16, 15)
$intro.Size = New-Object System.Drawing.Size(410, 38)
$intro.Text = 'Sends a brief signal to the Windows default audio output every 10 minutes. Select your soundbar as that output first.'
$form.Controls.Add($intro)

$intervalLabel = New-Object System.Windows.Forms.Label
$intervalLabel.Location = New-Object System.Drawing.Point(16, 67)
$intervalLabel.Size = New-Object System.Drawing.Size(135, 24)
$intervalLabel.Text = 'Interval (minutes)'
$form.Controls.Add($intervalLabel)

$interval = New-Object System.Windows.Forms.NumericUpDown
$interval.Location = New-Object System.Drawing.Point(154, 64)
$interval.Size = New-Object System.Drawing.Size(70, 26)
$interval.Minimum = 1
$interval.Maximum = 14
$interval.Value = $savedInterval
$form.Controls.Add($interval)

$modeLabel = New-Object System.Windows.Forms.Label
$modeLabel.Location = New-Object System.Drawing.Point(16, 103)
$modeLabel.Size = New-Object System.Drawing.Size(135, 24)
$modeLabel.Text = 'Signal'
$form.Controls.Add($modeLabel)

$mode = New-Object System.Windows.Forms.ComboBox
$mode.Location = New-Object System.Drawing.Point(154, 100)
$mode.Size = New-Object System.Drawing.Size(270, 26)
$mode.DropDownStyle = 'DropDownList'
[void]$mode.Items.Add('Digital silence')
[void]$mode.Items.Add('Very quiet tone (-45 dB)')
[void]$mode.Items.Add('Quiet tone (-35 dB)')
$mode.SelectedIndex = $savedMode
$form.Controls.Add($mode)

$note = New-Object System.Windows.Forms.Label
$note.Location = New-Object System.Drawing.Point(16, 141)
$note.Size = New-Object System.Drawing.Size(408, 52)
$note.Text = 'Complete silence may not reset an audio-level standby timer. If the soundbar still shuts off, try a quiet tone and adjust its volume on the soundbar.'
$form.Controls.Add($note)

$start = New-Object System.Windows.Forms.Button
$start.Location = New-Object System.Drawing.Point(16, 207)
$start.Size = New-Object System.Drawing.Size(88, 30)
$start.Text = 'Start'
$form.Controls.Add($start)

$stop = New-Object System.Windows.Forms.Button
$stop.Location = New-Object System.Drawing.Point(111, 207)
$stop.Size = New-Object System.Drawing.Size(88, 30)
$stop.Text = 'Stop'
$stop.Enabled = $false
$form.Controls.Add($stop)

$send = New-Object System.Windows.Forms.Button
$send.Location = New-Object System.Drawing.Point(206, 207)
$send.Size = New-Object System.Drawing.Size(88, 30)
$send.Text = 'Send now'
$form.Controls.Add($send)

$status = New-Object System.Windows.Forms.Label
$status.Location = New-Object System.Drawing.Point(16, 252)
$status.Size = New-Object System.Drawing.Size(410, 27)
$status.Text = 'Stopped. Nothing is being played.'
$form.Controls.Add($status)

$notify = New-Object System.Windows.Forms.NotifyIcon
$notify.Icon = [System.Drawing.SystemIcons]::Information
$notify.Text = 'Soundbar Keep Awake'
$notify.Visible = $true
$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
$trayOpen = $trayMenu.Items.Add('Open')
$trayStart = $trayMenu.Items.Add('Start')
$trayStop = $trayMenu.Items.Add('Stop')
$trayExit = $trayMenu.Items.Add('Exit')
$notify.ContextMenuStrip = $trayMenu

function Show-Window {
    $form.Show()
    $form.WindowState = 'Normal'
    $form.ShowInTaskbar = $true
    $form.Activate()
}

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$script:player = $null
$script:stream = $null
$script:nextSend = $null
$script:running = $false

function New-Player {
    $amplitude = switch ($mode.SelectedIndex) {
        1 { 0.0056 }
        2 { 0.0178 }
        default { 0.0 }
    }
    $bytes = [KeepAwakeWave]::Create(8, $amplitude)
    $script:stream = New-Object System.IO.MemoryStream(,$bytes)
    $script:player = New-Object System.Media.SoundPlayer($script:stream)
    $script:player.Load()
}

function Send-Signal {
    if ($null -eq $script:player) { New-Player }
    $script:player.Stop()
    $script:player.Play()
    if ($script:running) {
        $script:nextSend = [DateTime]::Now.AddMinutes([double]$interval.Value)
        $status.Text = "Running. Last sent: $([DateTime]::Now.ToString('HH:mm:ss')). Next: $($script:nextSend.ToString('HH:mm:ss'))."
    } else {
        $status.Text = "Sent once at $([DateTime]::Now.ToString('HH:mm:ss'))."
    }
}

$start.Add_Click({
    try {
        $script:running = $true
        $interval.Enabled = $false
        $mode.Enabled = $false
        $start.Enabled = $false
        $stop.Enabled = $true
        Send-Signal
        $timer.Start()
    } catch {
        $script:running = $false
        $timer.Stop()
        $interval.Enabled = $true
        $mode.Enabled = $true
        $start.Enabled = $true
        $stop.Enabled = $false
        [System.Windows.Forms.MessageBox]::Show("Could not play audio: $($_.Exception.Message)", 'Soundbar Keep Awake') | Out-Null
    }
})

$stop.Add_Click({
    $timer.Stop()
    $script:running = $false
    if ($null -ne $script:player) { $script:player.Stop() }
    $interval.Enabled = $true
    $mode.Enabled = $true
    $start.Enabled = $true
    $stop.Enabled = $false
    $status.Text = 'Stopped. Nothing is being played.'
})

$trayOpen.Add_Click({ Show-Window })
$trayStart.Add_Click({ Show-Window; $start.PerformClick() })
$trayStop.Add_Click({ Show-Window; $stop.PerformClick() })
$trayExit.Add_Click({ $form.Close() })
$notify.Add_DoubleClick({ Show-Window })

$form.Add_Resize({
    if ($form.WindowState -eq 'Minimized') {
        $form.ShowInTaskbar = $false
        $form.Hide()
    }
})

$form.Add_Shown({
    if ($AutoStart) {
        $start.PerformClick()
        $form.WindowState = 'Minimized'
        $form.Hide()
    }
})

$send.Add_Click({
    try { Send-Signal }
    catch { [System.Windows.Forms.MessageBox]::Show("Could not play audio: $($_.Exception.Message)", 'Soundbar Keep Awake') | Out-Null }
})

$mode.Add_SelectedIndexChanged({
    if ($null -ne $script:player) { $script:player.Stop(); $script:player.Dispose(); $script:player = $null }
    if ($null -ne $script:stream) { $script:stream.Dispose(); $script:stream = $null }
    Save-Settings
})

$interval.Add_ValueChanged({ Save-Settings })

$timer.Add_Tick({
    if ($script:running -and [DateTime]::Now -ge $script:nextSend) {
        try { Send-Signal }
        catch {
            $timer.Stop()
            $script:running = $false
            $status.Text = "Playback failed: $($_.Exception.Message)"
            $start.Enabled = $true
            $stop.Enabled = $false
            $interval.Enabled = $true
            $mode.Enabled = $true
        }
    }
})

$form.Add_FormClosed({
    $timer.Stop()
    if ($null -ne $script:player) { $script:player.Stop(); $script:player.Dispose() }
    if ($null -ne $script:stream) { $script:stream.Dispose() }
    $timer.Dispose()
    $notify.Visible = $false
    $notify.Dispose()
    $trayMenu.Dispose()
})

[void][System.Windows.Forms.Application]::Run($form)
