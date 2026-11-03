using System;
namespace SoundbarKeepAwake {
    static class Tests {
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static double Peak(double begin, double duration, bool continuous, int level) {
            double peak = 0; for(long i = (long)(begin * 48000); i < (long)((begin + duration) * 48000); i++) peak = Math.Max(peak, Math.Abs(Signal.Sample(i, 48000, continuous, level)));
            return peak;
        }
        static void Main() {
            Assert(Signal.Sample(0, 48000, false, -40) == 0, "Start must fade from zero.");
            Assert(Peak(1, .1, false, -40) > .009, "Pulse must contain a real tone.");
            Assert(Peak(20, .1, false, -40) > .0003, "Stream must remain nonzero between pulses.");
            Assert(Peak(20, .1, false, -40) < .00032, "Inter-pulse stream must stay faint.");
            Assert(Peak(301, .1, false, -40) > .009, "Pulse must recur at five minutes.");
            Assert(Peak(20, .1, true, -40) > .009, "Continuous mode must keep tone active.");
            Assert(Peak(1, .1, true, -20) < .101, "Maximum signal must not clip.");
            foreach(int bits in new[]{16,24,32}) {
                byte[] data = Signal.Encode(48000, 100, 48000, 2, bits, false, false, -40);
                Assert(data.Length == 100 * 2 * bits / 8, "Incorrect PCM frame layout.");
                for(int i=0;i<data.Length;i+=bits/8*2) for(int j=0;j<bits/8;j++) Assert(data[i+j]==data[i+bits/8+j], "Stereo channels must match.");
                for(int f=0;f<100;f++) {
                    int decoded=0; int offset=f*2*bits/8;
                    for(int b=0;b<bits/8;b++) decoded |= data[offset+b] << (8*b);
                    if(bits<32 && (decoded & (1 << (bits-1))) != 0) decoded |= -1 << bits;
                    long expected=(long)Math.Round(Signal.Sample(48000+f,48000,false,-40)*((1L<<(bits-1))-1));
                    Assert(decoded==expected, "PCM samples must preserve sign and amplitude.");
                }
            }
            byte[] floating = Signal.Encode(48001, 100, 48000, 2, 32, true, false, -40);
            Assert(Math.Abs(BitConverter.ToSingle(floating,0)) < .011, "Invalid float conversion.");
            using(var engine=new AudioEngine()) {
                engine.Start(new Settings { DeviceId="nonexistent-device-for-test", DeviceName="Missing output" });
                System.Threading.Thread.Sleep(1000);
                Assert(engine.Snapshot().State=="Waiting / retrying", "Missing endpoint must wait without falling back to another output.");
                engine.Stop(); Assert(engine.Snapshot().State=="Stopped", "Retry wait must be cancellable.");
            }
            Console.WriteLine("PASS: fades, nonzero stream, five-minute recurrence, levels, PCM and float encoding.");
        }
    }
}
