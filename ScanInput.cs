// ScanInput.cs — the switch keys of scanning. The keyboard never takes the focus, so the key of a switch is caught system-wide with a
// low-level keyboard hook and swallowed (the program that has the focus must not type it). Esc is caught the same way while a scan runs:
// it pauses the scan (the select key starts it again).
// Keys sent by this program itself (SendInput, flag "injected") always pass, also when they are the same key as the switch.
// Handle() holds the decisions and works without a hook, so the tests can drive it.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OnScreenKeyboard
{
    internal sealed class ScanInput : IDisposable
    {
        private readonly Func<ScanSettings> _settings;
        private readonly Action _press, _step, _escape;
        private readonly Func<bool> _running;               // Esc is taken only while a scan runs (null: always)
        private readonly HashSet<int> _swallowed = new();   // keys whose key-down was taken: their repeats and key-up are taken too

        /// <summary>Test seam: Install() does not set the system-wide hook (a real hook would catch the keys of the whole machine).</summary>
        internal static bool HookDisabledForTest;

        internal ScanInput(Func<ScanSettings> settings, Action press, Action step, Action escape, Func<bool> running = null)
        { _settings = settings; _press = press; _step = step; _escape = escape; _running = running; }

        /// <summary>Whether the key event is taken (true) or goes on to the program that has the focus (false).</summary>
        internal bool Handle(int vk, bool down, bool injected)
        {
            if (injected) return false;
            if (!down) return _swallowed.Remove(vk);
            if (_swallowed.Contains(vk)) return true;                  // auto-repeat of a key that is held

            var s = _settings();
            if (vk == ScanSettings.VkEscape)
            {
                if (_running != null && !_running()) return false;      // no scan to pause: Esc is a normal key
                _swallowed.Add(vk); _escape(); return true;
            }
            if (vk == s.SelectKey)                     { _swallowed.Add(vk); _press();  return true; }
            if (s.TwoSwitch && vk == s.StepKey)        { _swallowed.Add(vk); _step();   return true; }
            return false;
        }

        // ── The hook ──────────────────────────────────────────────────
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        private const uint LLKHF_INJECTED = 0x10;

        private IntPtr _hook = IntPtr.Zero;
        private HookProc _proc;                                         // kept in a field so the GC cannot free it while the hook is active

        internal bool Installed => _hook != IntPtr.Zero;

        /// <summary>Starts catching the switch keys. False when the system refused the hook.</summary>
        internal bool Install()
        {
            if (Installed || HookDisabledForTest) return Installed;
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            return Installed;
        }

        internal void Uninstall()
        {
            _swallowed.Clear();
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        public void Dispose() => Uninstall();

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN, up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (down || up)
                {
                    var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    // The actions only queue work for the window (the hook must answer within a fraction of a second or Windows drops it).
                    if (Handle((int)k.vkCode, down, (k.flags & LLKHF_INJECTED) != 0)) return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }
}
