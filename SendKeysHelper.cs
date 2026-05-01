using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    // ════════════════════════════════════════════════════════════════════════
    //  NoActivateButton
    //
    //  Every key button MUST return MA_NOACTIVATE from WM_MOUSEACTIVATE.
    //  This is the critical piece that makes the whole keyboard work:
    //
    //    Without it → clicking a key activates our window → target app loses
    //    focus → SendInput sends to the wrong window (our own).
    //
    //    With it    → clicking a key fires Click normally, but focus never
    //    leaves the target app → SendInput goes to the right place every time.
    //
    //  WS_EX_NOACTIVATE on the form alone is NOT enough.
    //  Child controls can still trigger activation via WM_MOUSEACTIVATE.
    //  This override at the button level is required.
    // ════════════════════════════════════════════════════════════════════════
    public class NoActivateButton : Button
    {
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE    = 3;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                // Return MA_NOACTIVATE — the Click event still fires normally.
                // Focus stays in the target application.
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SendKeysHelper
    //
    //  HOW SENDING WORKS
    //  ──────────────────
    //  PRIMARY PATH  — printable Unicode text (letters, digits, symbols):
    //    SendInput with KEYEVENTF_UNICODE.
    //    Injects each character as a Unicode scan code directly into the
    //    hardware input queue, bypassing the keyboard layout table entirely.
    //    Works on AZERTY, QWERTY, Dvorak — any layout.
    //    Does NOT require knowing the target window's HWND.
    //    Does NOT require clipboard access.
    //    Does NOT require any Sleep/settle delay.
    //
    //  SECONDARY PATH — SendKeys syntax ({ENTER}, {F1}, ^c, %{F4} etc.):
    //    SendKeys.SendWait on the UI thread.
    //    Used only for control keys and keyboard shortcuts.
    //
    //  WHY NO SetForegroundWindow IN THE SEND PATH
    //  ─────────────────────────────────────────────
    //  MA_NOACTIVATE on every NoActivateButton ensures the target app KEEPS
    //  focus throughout the entire click → send sequence. Since focus never
    //  left the target app, SendInput automatically goes to the right window.
    //  Calling SetForegroundWindow from a non-foreground process is blocked
    //  by Windows (UIPI / foreground lock) and sleeping on the UI thread is
    //  harmful. RestoreFocusIfLost() exists only as a last-resort fallback
    //  for edge cases (notification popups etc.) — it is a no-op in normal
    //  operation.
    //
    //  STRUCT SIZING
    //  ──────────────
    //  INPUT.Size uses Marshal.SizeOf of the full INPUT struct, which includes
    //  MOUSEINPUT (the largest union member). This matches what user32.dll
    //  expects for the cbSize parameter of SendInput.
    // ════════════════════════════════════════════════════════════════════════
    public static class SendKeysHelper
    {
        // ── Win32 P/Invoke ───────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int    dx, dy;
            public uint   mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint   dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint   uMsg;
            public ushort wParamL, wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT    mi;
            [FieldOffset(0)] public KEYBDINPUT    ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint       type;
            public InputUnion U;
            public static int Size => Marshal.SizeOf<INPUT>();
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        const uint   INPUT_KEYBOARD    = 1;
        const uint   KEYEVENTF_KEYUP   = 0x0002;
        const uint   KEYEVENTF_UNICODE = 0x0004;
        const ushort VK_RETURN         = 0x0D;
        const ushort VK_TAB            = 0x09;
        const ushort VK_LWIN           = 0x5B;

        // ── State ────────────────────────────────────────────────────────────
        private static IntPtr  _targetHwnd = IntPtr.Zero;
        private static Control _uiControl;

        /// <summary>
        /// Called by KeyboardForm's WinEventHook whenever a new external window
        /// becomes foreground. Stored as a fallback — not used in the normal
        /// send path when MA_NOACTIVATE is working correctly.
        /// </summary>
        public static void SetTargetWindow(IntPtr hwnd) { _targetHwnd = hwnd; }

        /// <summary>Called once from KeyboardForm constructor.</summary>
        public static void SetUiControl(Control c) { _uiControl = c; }

        // ── Test mode ─────────────────────────────────────────────────────────
        /// <summary>Set true in unit tests to suppress actual input injection.</summary>
        public static bool TestMode { get; set; } = false;

        // ── Settings ──────────────────────────────────────────────────────────


        // ── Queue + worker thread ─────────────────────────────────────────────
        private static readonly BlockingCollection<string> _queue =
            new BlockingCollection<string>(256);

        private static readonly Thread _worker;
        static SendKeysHelper()
        {
            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name         = "SendInput worker",
            };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        /// <summary>Enqueue a string for sending. Returns immediately, never blocks the UI.</summary>
        public static void Send(string s)
        {
            if (string.IsNullOrEmpty(s) || TestMode) return;
            _queue.TryAdd(s);
        }

        // ── Worker ────────────────────────────────────────────────────────────
        // Batches consecutive plain-text items into one SendInput call.
        // Special-key sequences ({ENTER}, ^c etc.) flush the buffer first,
        // then are sent individually via SendKeys.
        private static readonly StringBuilder _buf = new StringBuilder();
        private const int FlushMs = 80;

        private static void Worker()
        {
            while (true)
            {
                try
                {
                if (TestMode) { Thread.Sleep(100); continue; }

                if (_queue.TryTake(out string s, FlushMs))
                {
                    if (IsPlainText(s))
                    {
                        _buf.Append(s);
                        // Drain any further plain-text items without waiting
                        while (_queue.TryTake(out string next, 0))
                        {
                            if (IsPlainText(next))
                                _buf.Append(next);
                            else
                            {
                                FlushText();
                                RouteNonPlain(next);
                                break;
                            }
                        }
                    }
                    else
                    {
                        FlushText();
                        RouteNonPlain(s);
                    }
                }
                else
                {
                    FlushText();
                    if (_queue.IsCompleted) break;
                }
                } // try
                catch (Exception)
                {
                    // Clear dead key state so a crash in one send doesn't corrupt
                    // subsequent keystrokes
                    _pendingDead = null;
                }
            }
        }

        // ── PRIMARY: SendInput KEYEVENTF_UNICODE ──────────────────────────────
        private static void FlushText()
        {
            if (_buf.Length == 0) return;
            string text = _buf.ToString();
            _buf.Clear();
            text = ApplyPendingDead(text);
            InvokeOnUI(() => SendUnicodeString(text));
        }

        /// <summary>
        /// Sends a Unicode string via SendInput KEYEVENTF_UNICODE.
        ///
        /// Layout-independent: each character is injected as a Unicode scan
        /// code, bypassing the keyboard layout table entirely. Works correctly
        /// on AZERTY, QWERTY, and any other keyboard layout.
        ///
        /// Precondition: the target app holds keyboard focus.
        /// This is guaranteed by MA_NOACTIVATE on every NoActivateButton.
        ///
        /// Control characters:
        ///   \n / \r  →  VK_RETURN virtual key  (KEYEVENTF_UNICODE does not
        ///   \t       →  VK_TAB    virtual key    work for control characters)
        /// </summary>
        private static void SendUnicodeString(string text)
        {
            // Last-resort focus guard. No-op in normal operation.
            RestoreFocusIfLost();

            var inputs = new List<INPUT>(text.Length * 2);
            foreach (char ch in text)
            {
                if (ch == '\n' || ch == '\r')
                {
                    inputs.Add(MakeVk(VK_RETURN, false));
                    inputs.Add(MakeVk(VK_RETURN, true));
                }
                else if (ch == '\t')
                {
                    inputs.Add(MakeVk(VK_TAB, false));
                    inputs.Add(MakeVk(VK_TAB, true));
                }
                else
                {
                    inputs.Add(MakeUni((ushort)ch, false));
                    inputs.Add(MakeUni((ushort)ch, true));
                }
            }

            if (inputs.Count > 0)
            {
                var arr = inputs.ToArray();
                SendInput((uint)arr.Length, arr, INPUT.Size);
            }
        }

        // ── SECONDARY: SendKeys for control sequences ─────────────────────────
        private static void SendSpecial(string s)
        {
            // If a dead key is pending and a control sequence arrives (e.g. {ENTER}),
            // flush the dead key as a literal character first.
            FlushPendingDead();
            InvokeOnUI(() =>
            {
                RestoreFocusIfLost();
                try { SendKeys.SendWait(s); } catch { }
            });
        }

        // ── DEAD KEYS — composed in our keyboard, not by the OS driver ──────
        //
        // We attempted two OS-level approaches:
        //   1. Send VK via SendInput (dwFlags=0)           → driver path unreliable
        //   2. Send scan code via KEYEVENTF_SCANCODE       → dead-key state entered ✓
        //                                                    but the following letter
        //                                                    is sent via KEYEVENTF_UNICODE
        //                                                    which bypasses the driver,
        //                                                    so composition never fires.
        //
        // Root cause: KEYEVENTF_UNICODE injects BELOW the KBD driver stage.
        // The dead-key state machine lives IN the KBD driver. The two paths
        // never meet, so composition is impossible with a mixed approach.
        //
        // SOLUTION: compose in our code.
        //   When Send("dead:^") is called, store '^' as _pendingDead.
        //   When the next Send(s) arrives and s is a single composable letter,
        //   replace s with the precomposed character (e.g. â) and send that.
        //   If s doesn't compose with the pending dead key, flush the dead
        //   character first, then send s normally.
        //   If another dead: arrives, flush the previous pending dead first.

        private static string _pendingDead = null; // the raw char after "dead:"

        // Composition table: dead-char → (base → composed)
        // All non-ASCII characters are written as \uXXXX escapes to avoid
        // any encoding ambiguity in the source file.
        private static readonly Dictionary<char, Dictionary<char, char>> _compose =
            new Dictionary<char, Dictionary<char, char>>
        {
            ['^'] = new Dictionary<char, char> {
                {'a','\u00E2'},{'e','\u00EA'},{'i','\u00EE'},{'o','\u00F4'},{'u','\u00FB'},
                {'A','\u00C2'},{'E','\u00CA'},{'I','\u00CE'},{'O','\u00D4'},{'U','\u00DB'},
            },
            ['\u00A8'] = new Dictionary<char, char> {
                {'a','\u00E4'},{'e','\u00EB'},{'i','\u00EF'},{'o','\u00F6'},{'u','\u00FC'},{'y','\u00FF'},
                {'A','\u00C4'},{'E','\u00CB'},{'I','\u00CF'},{'O','\u00D6'},{'U','\u00DC'},{'Y','\u0178'},
            },
            ['~'] = new Dictionary<char, char> {
                {'a','\u00E3'},{'n','\u00F1'},{'o','\u00F5'},
                {'A','\u00C3'},{'N','\u00D1'},{'O','\u00D5'},
            },
            ['`'] = new Dictionary<char, char> {
                {'a','\u00E0'},{'e','\u00E8'},{'i','\u00EC'},{'o','\u00F2'},{'u','\u00F9'},
                {'A','\u00C0'},{'E','\u00C8'},{'I','\u00CC'},{'O','\u00D2'},{'U','\u00D9'},
            },
            ['\u00B4'] = new Dictionary<char, char> {
                {'a','\u00E1'},{'e','\u00E9'},{'i','\u00ED'},{'o','\u00F3'},{'u','\u00FA'},{'y','\u00FD'},
                {'A','\u00C1'},{'E','\u00C9'},{'I','\u00CD'},{'O','\u00D3'},{'U','\u00DA'},{'Y','\u00DD'},
            },
        };

        private static void RouteNonPlain(string s)
        {
            if (s.StartsWith("wp:", StringComparison.Ordinal))
            {
                // Word prediction — not yet implemented, skip silently
                return;
            }
            else if (s.StartsWith("dead:", StringComparison.Ordinal))
            {
                FlushPendingDead();
                _pendingDead = s.Length > 5 ? s.Substring(5) : null;
            }
            else if (s.StartsWith("win:", StringComparison.Ordinal))
            {
                FlushPendingDead();
                SendWinKey(s.Substring(4)); // strip "win:" prefix
            }
            else
            {
                SendSpecial(s);
            }
        }

        // ── WIN KEY: VK_LWIN + key via SendInput ─────────────────────
        /// <summary>
        /// Sends Win+key by injecting VK_LWIN down, the key, then VK_LWIN up.
        /// SendKeys has no Win modifier prefix, so this must go via SendInput.
        /// <paramref name="key"/> is either a single letter/digit char or a
        /// {KEY} token like {LEFT} or {D} (mapped to the VK below).
        /// </summary>
        private static void SendWinKey(string key)
        {
            InvokeOnUI(() =>
            {
                RestoreFocusIfLost();

                ushort vk = WinKeyPayloadToVk(key);
                if (vk == 0) return; // unknown key — silently skip

                var inputs = new[]
                {
                    MakeVk(VK_LWIN, false),
                    MakeVk(vk,      false),
                    MakeVk(vk,      true),
                    MakeVk(VK_LWIN, true),
                };
                SendInput((uint)inputs.Length, inputs, INPUT.Size);
            });
        }

        private static ushort WinKeyPayloadToVk(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;

            // Single letter a-z
            if (key.Length == 1 && char.IsLetter(key[0]))
                return (ushort)(char.ToUpper(key[0])); // VK_A = 0x41 etc.

            // Single digit 0-9
            if (key.Length == 1 && char.IsDigit(key[0]))
                return (ushort)key[0]; // VK_0 = 0x30 etc.

            // {KEY} token — strip braces
            string k = key.StartsWith("{") && key.EndsWith("}")
                ? key.Substring(1, key.Length - 2).ToUpper()
                : key.ToUpper();

            return k switch
            {
                "LEFT"      => 0x25,
                "UP"        => 0x26,
                "RIGHT"     => 0x27,
                "DOWN"      => 0x28,
                "HOME"      => 0x24,
                "END"       => 0x23,
                "PGUP"      => 0x21,
                "PGDN"      => 0x22,
                "ENTER"     => 0x0D,
                "TAB"       => 0x09,
                "ESC"       => 0x1B,
                "DELETE"    => 0x2E,
                "SPACE"     => 0x20,
                "F1"        => 0x70,
                "F2"        => 0x71,
                "F3"        => 0x72,
                "F4"        => 0x73,
                "F5"        => 0x74,
                "F6"        => 0x75,
                "F7"        => 0x76,
                "F8"        => 0x77,
                "F9"        => 0x78,
                "F10"       => 0x79,
                "F11"       => 0x7A,
                "F12"       => 0x7B,
                "D"         => (ushort)'D',
                "E"         => (ushort)'E',
                "L"         => (ushort)'L',
                "M"         => (ushort)'M',
                "R"         => (ushort)'R',
                "S"         => (ushort)'S',
                _           => 0,
            };
        }

        /// <summary>
        /// Called from FlushText before sending a plain-text batch.
        /// If a dead key is pending and the text starts with a composable letter,
        /// replace that letter with the precomposed character.
        /// If it doesn't compose, flush the dead key first as a literal character.
        /// </summary>
        private static string ApplyPendingDead(string text)
        {
            if (_pendingDead == null || text.Length == 0) return text;

            char dead = _pendingDead[0];
            _pendingDead = null;

            char next = text[0];
            if (_compose.TryGetValue(dead, out var table) &&
                table.TryGetValue(next, out char composed))
            {
                // Compose: replace first char with precomposed character
                return composed + text.Substring(1);
            }
            else
            {
                // No composition: send dead char literally, then text as-is
                return dead + text;
            }
        }

        private static void FlushPendingDead()
        {
            if (_pendingDead == null) return;
            string dead = _pendingDead;
            _pendingDead = null;
            // Send the dead character as a plain Unicode character
            InvokeOnUI(() =>
            {
                RestoreFocusIfLost();
                var arr = new[] { MakeUni((ushort)dead[0], false), MakeUni((ushort)dead[0], true) };
                SendInput(2, arr, INPUT.Size);
            });
        }

        // ── Focus guard ───────────────────────────────────────────────────────
        /// <summary>
        /// Restores focus to the target window if it was unexpectedly stolen
        /// (e.g. by a toast notification or system popup).
        ///
        /// In normal keyboard operation — where MA_NOACTIVATE is working —
        /// GetForegroundWindow() always equals _targetHwnd at this point,
        /// so this method returns immediately without doing anything.
        /// </summary>
        private static void RestoreFocusIfLost()
        {
            var target = _targetHwnd;
            if (target == IntPtr.Zero) return;
            if (GetForegroundWindow() == target) return;
            // Focus was stolen by something outside our control.
            SetForegroundWindow(target);
            Thread.Sleep(30);
        }

        // ── INPUT factories ───────────────────────────────────────────────────
        static INPUT MakeUni(ushort scan, bool up)
        {
            uint flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0u);
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U    = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags }
                }
            };
        }

        static INPUT MakeVk(ushort vk, bool up)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U    = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = up ? KEYEVENTF_KEYUP : 0u }
                }
            };
        }


        // ── UI thread dispatch ────────────────────────────────────────────────
        private static void InvokeOnUI(Action a)
        {
            var ui = _uiControl;
            if (ui == null || ui.IsDisposed) { a(); return; }
            try { ui.Invoke(a); } catch { a(); }
        }

        // ── Public string helpers ─────────────────────────────────────────────

        private static readonly char[] _special =
            { '+', '^', '%', '~', '(', ')', '{', '}', '[', ']' };

        /// <summary>
        /// Escapes SendKeys special characters in a raw string.
        /// Existing {KEY} sequences (e.g. {ENTER}, {F5}, {^}) are preserved unchanged.
        /// Used when storing a key's Send string after Quick Edit.
        /// </summary>
        public static string EscapeForSend(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new StringBuilder(input.Length * 2);
            int i  = 0;
            while (i < input.Length)
            {
                char c = input[i];
                if (c == '{')
                {
                    int close = input.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        sb.Append(input, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }
                if (Array.IndexOf(_special, c) >= 0)
                    sb.Append('{').Append(c).Append('}');
                else
                    sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Wraps a send string with SendKeys modifier prefixes.
        /// e.g. ApplyModifiers("a", shift:true, ctrl:true) → "^+a"
        ///      ApplyModifiers("ab", shift:true)            → "+(ab)"
        /// </summary>
        public static string ApplyModifiers(string send, bool shift, bool ctrl, bool alt)
        {
            if (string.IsNullOrEmpty(send)) return send;
            if (!shift && !ctrl && !alt) return send;
            string prefix = "";
            if (ctrl)  prefix += "^";
            if (alt)   prefix += "%";
            if (shift) prefix += "+";
            bool needsParens = send.Length > 1 || send == " ";
            return needsParens ? $"{prefix}({send})" : $"{prefix}{send}";
        }

        /// <summary>
        /// Returns true if the string should be sent via SendInput Unicode.
        /// Returns false if it contains SendKeys syntax and must use SendKeys.SendWait.
        ///
        /// SendKeys syntax:
        ///   {KEY} sequence present          → false  (e.g. {ENTER}, {F5}, {^})
        ///   modifier prefix + length ≥ 2   → false  (e.g. ^c, %{F4}, +a)
        ///
        /// Plain text (everything else)      → true
        ///
        /// Note: a single ^ % + character (length 1) is plain text and goes
        /// via SendInput Unicode. Only when used as a modifier prefix in a
        /// combination of length ≥ 2 does it trigger the SendKeys path.
        /// </summary>
        public static bool IsPlainText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;

            if (s.Contains('{'))
            {
                int open  = s.IndexOf('{');
                int close = s.IndexOf('}', open + 1);
                if (close > open) return false;
                // Lone { with no closing } → plain text
            }

            if (s.Length >= 2)
            {
                char f = s[0];
                if (f == '^' || f == '%' || f == '+') return false;
            }

            // dead: prefix — handled by SendDeadKey, not Unicode injection
            if (s.StartsWith("dead:", StringComparison.Ordinal)) return false;

            // win: prefix — handled by SendWinKey, not SendKeys
            if (s.StartsWith("win:", StringComparison.Ordinal)) return false;
            // wp: prefix — word prediction slot; not sent directly
            if (s.StartsWith("wp:", StringComparison.Ordinal)) return false;

            return true;
        }

        /// <summary>
        /// Returns true if the string consists entirely of whitespace
        /// (space, tab, CR, LF). Used by unit tests.
        /// </summary>
        public static bool IsWhitespaceOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n') return false;
            return true;
        }

        /// <summary>
        /// Converts a whitespace-only string to its SendKeys equivalent.
        /// Space → ' ', Tab → {TAB}, LF → {ENTER}.
        /// Used by unit tests.
        /// </summary>
        public static string BuildWhitespaceSendKeys(string s)
        {
            var sb = new StringBuilder(s.Length * 2);
            foreach (char c in s)
                switch (c)
                {
                    case ' ':  sb.Append(' ');       break;
                    case '\t': sb.Append("{TAB}");   break;
                    case '\n': sb.Append("{ENTER}"); break;
                }
            return sb.ToString();
        }
    }
}
