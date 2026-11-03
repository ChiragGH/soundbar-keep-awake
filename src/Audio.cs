using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace SoundbarKeepAwake {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator {}
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceEnumerator {
        void EnumAudioEndpoints(int flow, uint mask, out IDeviceCollection devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceCollection { void GetCount(out uint count); void Item(uint index, out IDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevice {
        void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        void OpenPropertyStore(uint access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit)] struct PropVariant {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Value;
        [FieldOffset(16)] public IntPtr Padding;
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient {
        void Initialize(int shareMode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start(); void Stop(); void Reset(); void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IRenderClient { void GetBuffer(uint frames, out IntPtr data); void ReleaseBuffer(uint frames, uint flags); }

    public sealed class OutputDevice {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Connected { get; set; }
        public override string ToString() { return Name + (Connected ? "" : " (disconnected)"); }
    }
    static class AudioDevices {
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
        public static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        public static List<OutputDevice> List() {
            var result = new List<OutputDevice>();
            var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
            IDeviceCollection devices = null;
            try {
                enumerator.EnumAudioEndpoints(0, 15, out devices); // Render endpoints only. No capture/loopback.
                uint count; devices.GetCount(out count);
                for (uint i = 0; i < count; i++) {
                    IDevice device = null; IPropertyStore store = null;
                    try {
                        devices.Item(i, out device);
                        string id; uint state; device.GetId(out id); device.GetState(out state);
                        device.OpenPropertyStore(0, out store);
                        var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                        PropVariant value; store.GetValue(ref key, out value);
                        string name;
                        try { name = Marshal.PtrToStringUni(value.Value); } finally { PropVariantClear(ref value); }
                        result.Add(new OutputDevice { Id = id, Name = name, Connected = state == 1 });
                    } finally { Release(store); Release(device); }
                }
            } finally { Release(devices); Release(enumerator); }
            return result;
        }
        public static string DefaultId() {
            var enumerator = (IDeviceEnumerator)new DeviceEnumerator(); IDevice device = null;
            try { enumerator.GetDefaultAudioEndpoint(0, 1, out device); string id; device.GetId(out id); return id; }
            finally { Release(device); Release(enumerator); }
        }
    }
    static class Signal {
        // A nonzero stream keeps the Bluetooth audio transport active between pulses.
        public static double Sample(long frame, int rate, bool continuous, int levelDb) {
            return SampleAtAmplitude(frame, rate, continuous, Math.Pow(10.0, levelDb / 20.0));
        }
        static double SampleAtAmplitude(long frame, int rate, bool continuous, double amplitude) {
            double time = (double)frame / rate;
            double fadeIn = Math.Min(1.0, time / 0.05);
            double baseTone = 0.000316227766 * Math.Sin(2 * Math.PI * 80 * time); // -70 dBFS
            double position = time % 300.0;
            double envelope = continuous ? 1.0 : (position < 8.0 ? Math.Min(1.0, Math.Min(position, 8.0 - position) / 0.05) : 0.0);
            return fadeIn * (baseTone + envelope * amplitude * Math.Sin(2 * Math.PI * 440 * time));
        }
        public static byte[] Encode(long start, uint frames, int rate, int channels, int bits, bool floating, bool continuous, int level) {
            int bytesPerSample = bits / 8;
            byte[] bytes = new byte[checked((int)frames * channels * bytesPerSample)];
            double amplitude = Math.Pow(10.0, level / 20.0);
            float[] floats = floating ? new float[checked((int)frames * channels)] : null;
            long maximum = (1L << (bits - 1)) - 1;
            int offset = 0;
            for (int f = 0; f < frames; f++) {
                double sample = SampleAtAmplitude(start + f, rate, continuous, amplitude);
                int pcm = (int)Math.Round(sample * maximum);
                for (int c = 0; c < channels; c++) {
                    if (floating) floats[f * channels + c] = (float)sample;
                    else for(int b = 0; b < bytesPerSample; b++) bytes[offset + b] = (byte)(pcm >> (8 * b));
                    offset += bytesPerSample;
                }
            }
            if (floating) Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
    public sealed class EngineStatus {
        public string State { get; set; }
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string UpdatedUtc { get; set; }
        public long FramesConsumed { get; set; }
        public double StreamSeconds { get; set; }
        public int Reconnects { get; set; }
        public string Error { get; set; }
    }
    sealed class AudioEngine : IDisposable {
        readonly ManualResetEvent cancel = new ManualResetEvent(false);
        Thread worker;
        readonly object sync = new object();
        EngineStatus status = new EngineStatus { State = "Stopped" };
        public EngineStatus Snapshot() { lock(sync) return status; }
        void SetStatus(string state, Settings settings, long consumed, int rate, int retries, string error) {
            lock(sync) status = new EngineStatus { State = state, DeviceId = settings.DeviceId, DeviceName = settings.DeviceName,
                FramesConsumed = consumed, StreamSeconds = rate > 0 ? (double)consumed / rate : 0, Reconnects = retries,
                Error = error, UpdatedUtc = DateTime.UtcNow.ToString("o") };
        }
        public void Start(Settings settings) {
            Stop(); cancel.Reset();
            Settings snapshot = settings.Copy();
            worker = new Thread(delegate() { Run(snapshot); });
            worker.IsBackground = true; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
        }
        public void Stop() {
            cancel.Set();
            if (worker != null) { worker.Join(); worker = null; }
            lock(sync) status = new EngineStatus { State = "Stopped", UpdatedUtc = DateTime.UtcNow.ToString("o") };
        }
        void Run(Settings settings) {
            int retries = 0;
            while (!cancel.WaitOne(0)) {
                IDeviceEnumerator enumerator = null; IDevice device = null; IAudioClient client = null; IRenderClient render = null;
                IntPtr format = IntPtr.Zero;
                try {
                    SetStatus("Connecting", settings, 0, 0, retries, null);
                    enumerator = (IDeviceEnumerator)new DeviceEnumerator();
                    enumerator.GetDevice(settings.DeviceId, out device);
                    uint state; device.GetState(out state);
                    if (state != 1) throw new InvalidOperationException("Soundbar is disconnected. Turn it on and connect Bluetooth.");
                    object audioObject; Guid audioId = typeof(IAudioClient).GUID;
                    device.Activate(ref audioId, 23, IntPtr.Zero, out audioObject); client = (IAudioClient)audioObject;
                    client.GetMixFormat(out format);
                    int tag = (ushort)Marshal.ReadInt16(format, 0), channels = (ushort)Marshal.ReadInt16(format, 2);
                    int rate = Marshal.ReadInt32(format, 4), block = (ushort)Marshal.ReadInt16(format, 12), bits = (ushort)Marshal.ReadInt16(format, 14);
                    if (tag == 65534) tag = Marshal.ReadInt32(format, 24); // KSDATAFORMAT_SUBTYPE_PCM or IEEE_FLOAT
                    bool floating = tag == 3;
                    if (!(floating && bits == 32) && !(tag == 1 && (bits == 16 || bits == 24 || bits == 32)))
                        throw new NotSupportedException("Unsupported output format. Use a standard PCM output format in Windows Sound settings.");
                    if (block != channels * bits / 8) throw new NotSupportedException("Unexpected audio block size.");
                    client.Initialize(0, 0, 2000000, 0, format, IntPtr.Zero); // shared-mode 200ms render buffer
                    uint size; client.GetBufferSize(out size);
                    object renderObject; Guid renderId = typeof(IRenderClient).GUID;
                    client.GetService(ref renderId, out renderObject); render = (IRenderClient)renderObject;
                    long written = 0, previous = -1; var progress = System.Diagnostics.Stopwatch.StartNew();
                    Fill(render, size, ref written, rate, channels, bits, floating, settings);
                    client.Start();
                    while (!cancel.WaitOne(20)) {
                        uint padding; client.GetCurrentPadding(out padding);
                        long consumed = written - padding;
                        if (consumed != previous) { previous = consumed; progress.Restart(); }
                        if (progress.Elapsed.TotalSeconds > 5) throw new InvalidOperationException("Audio stream stopped advancing; reconnecting.");
                        if (size > padding) Fill(render, size - padding, ref written, rate, channels, bits, floating, settings);
                        SetStatus("Playing", settings, consumed, rate, retries, null);
                    }
                } catch (Exception ex) {
                    retries++; SetStatus("Waiting / retrying", settings, 0, 0, retries, ex.Message);
                } finally {
                    if (client != null) { try { client.Stop(); } catch {} }
                    AudioDevices.Release(render); AudioDevices.Release(client); AudioDevices.Release(device); AudioDevices.Release(enumerator);
                    if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                }
                if (!cancel.WaitOne(0)) cancel.WaitOne(5000);
            }
        }
        static void Fill(IRenderClient render, uint count, ref long frame, int rate, int channels, int bits, bool floating, Settings settings) {
            byte[] bytes = Signal.Encode(frame, count, rate, channels, bits, floating, settings.Continuous, settings.LevelDb);
            IntPtr target; render.GetBuffer(count, out target);
            try { Marshal.Copy(bytes, 0, target, bytes.Length); }
            finally { render.ReleaseBuffer(count, 0); }
            frame += count;
        }
        public void Dispose() { Stop(); cancel.Dispose(); }
    }
}
