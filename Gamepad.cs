// Gamepad.cs — a game controller as a scanning switch. Two ways to read one, both without the window having the focus:
//   XInput  — Xbox-type controllers (also the Xbox Adaptive Controller and the switches plugged into it);
//   winmm   — the older joystick API, for other game controllers and switch interfaces that show up as one.
// Every button has a code that cannot be confused with a key (a virtual-key code is below 0x100):
//   0x10000 + the XInput button mask (A = 0x1000 …), 0x20001 / 0x20002 the left / right trigger, 0x40000 + n the n-th button of winmm.
// GamepadReader reads the buttons that are down now; GamepadPoller turns that into press and release events (and is what the keyboard
// window and the settings window use). Reading is separate from the diff so the tests can drive the poller without a controller.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OnScreenKeyboard
{
    internal static class Gamepad
    {
        internal const int Base = 0x10000, XInputBase = 0x10000, TriggerBase = 0x20000, JoyBase = 0x40000;

        /// <summary>Whether <paramref name="code"/> is a gamepad button (and not a key).</summary>
        internal static bool IsButton(int code) => code >= Base;

        /// <summary>The name of a button for the screens: "Pad A", "Pad LB", "Pad button 3"; "—" for none (0).</summary>
        internal static string Name(int code)
        {
            if (code == 0) return "—";
            if (code >= JoyBase) return "Pad " + Lang.T("button") + " " + (code - JoyBase);
            if (code >= TriggerBase) return code == TriggerBase + 1 ? "Pad LT" : "Pad RT";
            return (code - XInputBase) switch
            {
                0x0001 => "Pad " + Lang.T("D-pad up"), 0x0002 => "Pad " + Lang.T("D-pad down"),
                0x0004 => "Pad " + Lang.T("D-pad left"), 0x0008 => "Pad " + Lang.T("D-pad right"),
                0x0010 => "Pad Start", 0x0020 => "Pad Back", 0x0040 => "Pad L3", 0x0080 => "Pad R3",
                0x0100 => "Pad LB", 0x0200 => "Pad RB", 0x1000 => "Pad A", 0x2000 => "Pad B", 0x4000 => "Pad X", 0x8000 => "Pad Y",
                var m => "Pad 0x" + m.ToString("X"),
            };
        }
    }

    /// <summary>Reads which gamepad buttons are down. A slot without a controller is looked at again only now and then (asking an empty slot is slow).</summary>
    internal sealed class GamepadReader
    {
        [StructLayout(LayoutKind.Sequential)] private struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
        [StructLayout(LayoutKind.Sequential)] private struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern int XInputGetState(int user, out XINPUT_STATE state);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX { public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2; }
        [DllImport("winmm.dll")] private static extern int joyGetPosEx(int id, ref JOYINFOEX info);

        private const int Slots = 4, RetryMs = 2000, TriggerOn = 150;
        private readonly long[] _xRetry = new long[Slots], _jRetry = new long[Slots];
        private readonly bool[] _xSeen = new bool[Slots];   // the slot had an XInput controller at the last look
        private bool _xinputMissing;

        /// <summary>The codes of the buttons that are down now (empty when there is no controller or the system cannot be asked).</summary>
        internal HashSet<int> Read()
        {
            var down = new HashSet<int>();
            long now = Environment.TickCount64;
            for (int i = 0; i < Slots; i++)
            {
                bool xPresent = false;
                if (!_xinputMissing && now >= _xRetry[i])
                {
                    try
                    {
                        if (XInputGetState(i, out var st) == 0)
                        {
                            xPresent = _xSeen[i] = true;
                            for (int b = 0; b < 16; b++) if ((st.Gamepad.wButtons & (1 << b)) != 0) down.Add(Gamepad.XInputBase + (1 << b));
                            if (st.Gamepad.bLeftTrigger  > TriggerOn) down.Add(Gamepad.TriggerBase + 1);
                            if (st.Gamepad.bRightTrigger > TriggerOn) down.Add(Gamepad.TriggerBase + 2);
                        }
                        else { _xRetry[i] = now + RetryMs; _xSeen[i] = false; }
                    }
                    catch (DllNotFoundException) { _xinputMissing = true; }
                    catch (EntryPointNotFoundException) { _xinputMissing = true; }
                }
                if (now >= _jRetry[i])
                {
                    try
                    {
                        var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = 0xFF };   // JOY_RETURNALL
                        if (joyGetPosEx(i, ref info) == 0)
                        {
                            // An Xbox-type controller shows up in both APIs: its first ten winmm buttons are the same A, B, X, Y, LB, RB, Back,
                            // Start, L3, R3 that XInput names, so they are left out here (one button, one name, "Pad A" and not "Pad button 1").
                            int skip = xPresent || _xSeen[i] ? 10 : 0;
                            for (int b = skip; b < 32; b++) if ((info.dwButtons & (1u << b)) != 0) down.Add(Gamepad.JoyBase + b + 1);
                        }
                        else _jRetry[i] = now + RetryMs;
                    }
                    catch { _jRetry[i] = now + 60_000; }
                }
            }
            return down;
        }
    }

    /// <summary>Polls the controllers on the window's thread (a timer) and reports each button going down or up.</summary>
    internal sealed class GamepadPoller : IDisposable
    {
        private readonly Action<int, bool> _edge;
        private readonly Func<HashSet<int>> _read;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 20 };
        private HashSet<int> _down = new();

        internal GamepadPoller(Action<int, bool> edge, Func<HashSet<int>> read = null)
        {
            _edge = edge;
            _read = read ?? new GamepadReader().Read;
            _timer.Tick += (s, e) => Poll();
        }

        internal void Start() => _timer.Start();
        internal void Stop() => _timer.Stop();

        /// <summary>One look at the controllers: buttons that went down or up since the last look are reported (also the tests call this).</summary>
        internal void Poll() => Update(_read());

        internal void Update(HashSet<int> now)
        {
            foreach (int c in now) if (!_down.Contains(c)) _edge(c, true);
            foreach (int c in _down) if (!now.Contains(c)) _edge(c, false);
            _down = now;
        }

        public void Dispose()
        {
            _timer.Stop(); _timer.Dispose();
            _down = new();                          // nothing is reported any more: the scanner it fed is being taken down too
        }
    }
}
