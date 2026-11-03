using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Soundbar Keep Awake")]
[assembly: AssemblyDescription("Offline Windows audio keep-awake utility")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace SoundbarKeepAwake {
    public sealed class Settings {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public int LevelDb { get; set; }
        public bool Continuous { get; set; }
        public bool Enabled { get; set; }
        public Settings() { LevelDb = -40; Enabled = true; }
        public Settings Copy() { return (Settings)MemberwiseClone(); }
    }
    static class Storage {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundbarKeepAwake");
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static Settings Load() {
            try {
                var settings = Json.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json")));
                if (settings == null) return new Settings();
                settings.LevelDb = Math.Max(-60, Math.Min(-20, settings.LevelDb));
                return settings;
            } catch { return new Settings(); }
        }
        public static void Write(string name, object data) {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, name), temp = file + ".tmp";
            File.WriteAllText(temp, Json.Serialize(data));
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        }
        public static void Save(Settings settings) { Write("settings.json", settings); }
    }
    static class Installation {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "SoundbarKeepAwake";
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);
        public static readonly string Exe = Path.Combine(Folder, Name + ".exe");
        public static bool StartupEnabled() {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) {
                return key != null && String.Equals(key.GetValue(Name) as string, "\"" + Application.ExecutablePath + "\" --background", StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void SetStartup(bool enabled) {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (enabled) key.SetValue(Name, "\"" + Application.ExecutablePath + "\" --background");
                else key.DeleteValue(Name, false);
            }
        }
        public static void Install(bool quiet) {
            Directory.CreateDirectory(Folder);
            string oldScript = Path.Combine(Folder, "Soundbar Keep Awake.ps1");
            // Stop only the legacy PowerShell process whose command line names this installed script.
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='powershell.exe'"))
            using (var results = searcher.Get()) {
                foreach (ManagementObject process in results) {
                    string command = process["CommandLine"] as string;
                    if (command != null && command.IndexOf(oldScript, StringComparison.OrdinalIgnoreCase) >= 0) {
                        try { using(var p = Process.GetProcessById(Convert.ToInt32(process["ProcessId"]))) { p.Kill(); p.WaitForExit(5000); } } catch (ArgumentException) {}
                    }
                }
            }
            string legacyShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Soundbar Keep Awake.lnk");
            if (File.Exists(legacyShortcut)) {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic shortcut = shell.CreateShortcut(legacyShortcut);
                string arguments = shortcut.Arguments;
                if (arguments.IndexOf(oldScript, StringComparison.OrdinalIgnoreCase) >= 0) File.Delete(legacyShortcut);
                AudioDevices.Release(shortcut); AudioDevices.Release(shell);
            }
            Program.SignalExit();
            foreach (var process in Process.GetProcessesByName(Name)) {
                using(process) {
                    if (process.Id == Process.GetCurrentProcess().Id) continue;
                    try { if (String.Equals(process.MainModule.FileName, Exe, StringComparison.OrdinalIgnoreCase) && !process.WaitForExit(6000)) throw new IOException("Close the previous app before updating."); }
                    catch (System.ComponentModel.Win32Exception) {}
                }
            }
            if (!String.Equals(Application.ExecutablePath, Exe, StringComparison.OrdinalIgnoreCase)) File.Copy(Application.ExecutablePath, Exe, true);
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.SetValue(Name, "\"" + Exe + "\" --background");
            dynamic menuShell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic menuShortcut = menuShell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Soundbar Keep Awake.lnk"));
            menuShortcut.TargetPath = Exe;
            menuShortcut.WorkingDirectory = Folder;
            menuShortcut.Description = "Keep your Bluetooth soundbar awake";
            menuShortcut.Save();
            AudioDevices.Release(menuShortcut); AudioDevices.Release(menuShell);
            // Preserve the old files as a local backup; none are registered to run.
            foreach (string name in new[] { "Soundbar Keep Awake.ps1", "Launch Soundbar Keep Awake.cmd", "settings.json" }) {
                string path = Path.Combine(Folder, name);
                if (File.Exists(path)) {
                    string backup = Path.Combine(Folder, "legacy-backup"); Directory.CreateDirectory(backup);
                    File.Copy(path, Path.Combine(backup, name), true); File.Delete(path);
                }
            }
            var info = new ProcessStartInfo(Exe, "--background") { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(info);
            if (!quiet) MessageBox.Show("Installed and started in the system tray. Windows sign-in now launches the app directly.", "Soundbar Keep Awake");
        }
    }
    static class Program {
        internal const string ShowEvent = @"Local\SoundbarKeepAwake.v2.Show";
        internal const string ExitEvent = @"Local\SoundbarKeepAwake.v2.Exit";
        public static void SignalExit() { try { using (var signal = EventWaitHandle.OpenExisting(ExitEvent)) signal.Set(); } catch (WaitHandleCannotBeOpenedException) {} }
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try {
                if (args.Contains("--probe")) { File.WriteAllText(args[Array.IndexOf(args, "--probe") + 1], Storage.Json.Serialize(AudioDevices.List())); return; }
                if (args.Contains("--exit")) { SignalExit(); return; }
                if (args.Contains("--install") || String.Equals(Path.GetFileNameWithoutExtension(Application.ExecutablePath), "SoundbarKeepAwake-Setup", StringComparison.OrdinalIgnoreCase)) { Installation.Install(args.Contains("--quiet")); return; }
                if (args.Contains("--disable-startup")) { Installation.SetStartup(false); SignalExit(); return; }
                bool created;
                using (var mutex = new Mutex(true, @"Local\SoundbarKeepAwake.v2", out created)) {
                    if (!created) {
                        try { using (var signal = EventWaitHandle.OpenExisting(ShowEvent)) signal.Set(); } catch (WaitHandleCannotBeOpenedException) {}
                        return;
                    }
                    try { Application.Run(new MainWindow(args.Contains("--background"))); } finally { mutex.ReleaseMutex(); }
                }
            } catch (Exception ex) {
                try { Storage.Write("error.json", new { TimeUtc = DateTime.UtcNow.ToString("o"), Error = ex.ToString() }); } catch {}
                if (!args.Contains("--quiet")) MessageBox.Show(ex.Message, "Soundbar Keep Awake", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }
    }
    sealed class MainWindow : Form {
        readonly ComboBox output = new ComboBox(), mode = new ComboBox();
        readonly NumericUpDown level = new NumericUpDown();
        readonly Label status = new Label();
        readonly CheckBox startup = new CheckBox();
        readonly AudioEngine engine = new AudioEngine();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEvent);
        readonly EventWaitHandle exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEvent);
        readonly Settings settings;
        bool quitting, loading = true; int ticks;
        public MainWindow(bool background) {
            settings = Storage.Load();
            Text = "Soundbar Keep Awake"; Font = new Font("Segoe UI", 10); ClientSize = new Size(600, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AddLabel("Soundbar Keep Awake", 20, 15, 560, 35, 18);
            AddLabel("Local playback only. No microphone, recording, or internet access.", 20, 53, 560, 32, 10);
            AddLabel("Soundbar output", 20, 94, 145, 28, 10);
            output.SetBounds(165, 90, 320, 28); output.DropDownStyle = ComboBoxStyle.DropDownList; Controls.Add(output);
            AddButton("Refresh", 495, 89, 85, delegate { RefreshDevices(); });
            AddLabel("Signal", 20, 136, 140, 28, 10);
            mode.SetBounds(165, 132, 415, 28); mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Items.Add("Quiet pulses every 5 minutes"); mode.Items.Add("Continuous quiet tone"); mode.SelectedIndex = settings.Continuous ? 1 : 0; Controls.Add(mode);
            AddLabel("Tone level (dB)", 20, 178, 145, 28, 10);
            level.SetBounds(165, 174, 85, 28); level.Minimum = -60; level.Maximum = -20; level.Increment = 5; level.Value = settings.LevelDb; Controls.Add(level);
            AddLabel("More negative = quieter", 265, 178, 315, 28, 10);
            AddLabel("A faint signal keeps Bluetooth streaming. An 8-second tone every five minutes helps reset audio inactivity. The tone may be audible. If standby continues, raise its level by 5 dB.", 20, 216, 560, 66, 10);
            AddButton("Apply / Start", 20, 290, 120, delegate { Apply(); });
            AddButton("Pause", 150, 290, 90, delegate { settings.Enabled = false; Storage.Save(settings); engine.Stop(); });
            AddButton("Exit", 250, 290, 80, delegate { Quit(); });
            startup.SetBounds(20, 334, 560, 27); startup.Text = "Start this app when I sign in to Windows"; startup.Checked = Installation.StartupEnabled(); Controls.Add(startup);
            startup.CheckedChanged += delegate { if (!loading) { try { Installation.SetStartup(startup.Checked); } catch (Exception ex) { MessageBox.Show(ex.Message); } } };
            status.SetBounds(20, 371, 560, 53); Controls.Add(status);
            var menu = new ContextMenuStrip(); menu.Items.Add("Open", null, delegate { ShowWindow(); });
            menu.Items.Add("Resume", null, delegate { settings.Enabled = true; Storage.Save(settings); if (!String.IsNullOrEmpty(settings.DeviceId)) engine.Start(settings); });
            menu.Items.Add("Pause", null, delegate { settings.Enabled = false; Storage.Save(settings); engine.Stop(); });
            menu.Items.Add("Exit", null, delegate { Quit(); });
            tray.Icon = SystemIcons.Information; tray.Text = "Soundbar Keep Awake"; tray.ContextMenuStrip = menu; tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };
            RefreshDevices(); loading = false;
            Shown += delegate {
                if (settings.Enabled && !String.IsNullOrEmpty(settings.DeviceId)) engine.Start(settings);
                if (background) Hide();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            timer.Interval = 1000; timer.Tick += delegate { TickStatus(); }; timer.Start();
            FormClosed += delegate {
                timer.Dispose(); engine.Dispose();
                try { Storage.Write("status.json", engine.Snapshot()); } catch {}
                tray.Visible = false; tray.Dispose(); menu.Dispose(); showSignal.Dispose(); exitSignal.Dispose();
            };
        }
        void AddLabel(string text, int x, int y, int width, int height, float size) {
            var label = new Label { Text = text, Font = new Font("Segoe UI", size) }; label.SetBounds(x, y, width, height); Controls.Add(label);
        }
        void AddButton(string text, int x, int y, int width, Action action) {
            var button = new Button { Text = text }; button.SetBounds(x, y, width, 32); button.Click += delegate { try { action(); } catch(Exception ex) { MessageBox.Show(ex.Message, Text); } }; Controls.Add(button);
        }
        void RefreshDevices() {
            var previous = output.SelectedItem as OutputDevice;
            string selected = previous == null ? settings.DeviceId : previous.Id;
            List<OutputDevice> devices;
            try { devices = AudioDevices.List(); }
            catch { devices = new List<OutputDevice>(); } // Audio services may still be starting at sign-in.
            output.Items.Clear(); foreach(var device in devices) output.Items.Add(device);
            if (String.IsNullOrEmpty(selected)) { try { selected = AudioDevices.DefaultId(); } catch {} }
            var choice = devices.FirstOrDefault(d => d.Id == selected);
            if (choice == null && !String.IsNullOrEmpty(settings.DeviceId)) {
                choice = new OutputDevice { Id = settings.DeviceId, Name = settings.DeviceName ?? "Saved soundbar", Connected = false }; output.Items.Add(choice);
            }
            if (choice != null) output.SelectedItem = choice;
            if (String.IsNullOrEmpty(settings.DeviceId) && choice != null) {
                settings.DeviceId = choice.Id; settings.DeviceName = choice.Name; Storage.Save(settings);
            }
        }
        void Apply() {
            var device = output.SelectedItem as OutputDevice;
            if (device == null) throw new InvalidOperationException("Select the soundbar output first.");
            settings.DeviceId = device.Id; settings.DeviceName = device.Name; settings.Continuous = mode.SelectedIndex == 1;
            settings.LevelDb = (int)level.Value; settings.Enabled = true; Storage.Save(settings); engine.Start(settings);
        }
        void TickStatus() {
            if (exitSignal.WaitOne(0)) { Quit(); return; }
            if (showSignal.WaitOne(0)) ShowWindow();
            var current = engine.Snapshot();
            status.Text = current.State == "Playing" ? "Playing to " + settings.DeviceName + "\nStream advancing: " + TimeSpan.FromSeconds(current.StreamSeconds).ToString(@"hh\:mm\:ss") : current.State + (String.IsNullOrEmpty(current.Error) ? "" : ": " + current.Error);
            tray.Text = "Soundbar Keep Awake: " + current.State;
            if (++ticks % 5 == 0) { try { Storage.Write("status.json", current); } catch {} }
        }
        void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void Quit() { quitting = true; Close(); }
    }
}
