// ScanSounds.cs — the sounds of scanning: short, dry ticks like a key being pressed (the system sounds are long bells). The sounds are
// made here as small WAV files in memory, so no sound file has to be shipped.

using System;
using System.IO;
using System.Media;

namespace OnScreenKeyboard
{
    internal static class ScanSounds
    {
        private static SoundPlayer _select, _step;

        /// <summary>A key was selected: a short, firm tick.</summary>
        internal static void PlaySelect() => Play(ref _select, ms: 28, hz: 1500, noise: 0.55, volume: 0.55, decayMs: 7);

        /// <summary>The highlight moved: a lighter, shorter tick.</summary>
        internal static void PlayStep() => Play(ref _step, ms: 14, hz: 2600, noise: 0.35, volume: 0.30, decayMs: 4);

        private static void Play(ref SoundPlayer cache, int ms, double hz, double noise, double volume, double decayMs)
        {
            try
            {
                cache ??= new SoundPlayer(new MemoryStream(Wave(ms, hz, noise, volume, decayMs)));
                cache.Play();                      // asynchronous: does not hold up the keyboard
            }
            catch { /* no sound device: scanning goes on without sound */ }
        }

        /// <summary>A mono 16-bit 22 kHz WAV of a click: a sine and some noise that die away quickly.</summary>
        internal static byte[] Wave(int ms, double hz, double noise, double volume, double decayMs)
        {
            const int rate = 22050;
            int n = rate * ms / 1000;
            var rnd = new Random(7);
            using var m = new MemoryStream();
            using var w = new BinaryWriter(m);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate * 1000;                         // ms
                double env = Math.Exp(-t / decayMs) * Math.Min(1, t / 0.5);   // 0.5 ms attack: no click of its own at the start
                double s = Math.Sin(2 * Math.PI * hz * t / 1000) * (1 - noise) + (rnd.NextDouble() * 2 - 1) * noise;
                w.Write((short)(s * env * volume * short.MaxValue));
            }
            w.Flush();
            return m.ToArray();
        }
    }
}
