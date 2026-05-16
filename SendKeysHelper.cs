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

    /// <summary>
    /// A button that deliberately does NOT steal focus from the application the
    /// user is typing into.
    ///
    /// When the user clicks any key on the on-screen keyboard, Windows normally
    /// activates (brings to front and gives focus to) the window that was clicked.
    /// That would shift focus away from the target app — meaning any keystrokes
    /// we inject via SendInput would go to our own keyboard window instead.
    ///
    /// The fix is to intercept the <c>WM_MOUSEACTIVATE</c> Windows message and
    /// respond with <c>MA_NOACTIVATE</c>, which tells Windows: "handle the click
    /// event as usual, but do NOT change which window has focus."
    ///
    /// Note: setting <c>WS_EX_NOACTIVATE</c> on the parent form alone is not
    /// sufficient — child controls such as buttons can still trigger activation.
    /// This per-button override is what makes the whole system work reliably.
    /// </summary>
    public class NoActivateButton : Button
    {
        // Windows message ID for "should this mouse click activate this window?"
        private const int WM_MOUSEACTIVATE = 0x0021;

        // Return value meaning "process the click, but do NOT activate this window".
        // Value 3 in the Win32 API; MA_NOACTIVATE is the symbolic constant.
        private const int MA_NOACTIVATE    = 3;

        /// <summary>
        /// Intercepts the low-level Windows message pump for this button.
        /// All messages except <c>WM_MOUSEACTIVATE</c> are passed through to
        /// the default handler unchanged.
        /// </summary>
        /// <param name="m">
        /// The Windows message to process. The <c>Msg</c> field identifies the
        /// message type; <c>Result</c> is where we write our response.
        /// </param>
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

    /// <summary>
    /// Central hub for all keyboard input injection.
    ///
    /// <para>
    /// <b>Primary path — printable text:</b> Characters are sent via the Win32
    /// <c>SendInput</c> API with the <c>KEYEVENTF_UNICODE</c> flag. This injects
    /// each character as a raw Unicode scan code directly into the hardware input
    /// queue, completely bypassing the keyboard layout driver. That means the same
    /// code works correctly regardless of whether the user's hardware keyboard is
    /// configured as QWERTY, AZERTY, Dvorak, or anything else.
    /// </para>
    ///
    /// <para>
    /// <b>Secondary path — control keys and shortcuts:</b> Strings such as
    /// <c>{ENTER}</c>, <c>^c</c> (Ctrl+C), or <c>%{F4}</c> (Alt+F4) use
    /// <c>SendKeys.SendWait</c>, which understands this special syntax.
    /// </para>
    ///
    /// <para>
    /// <b>Dead keys:</b> Accented characters composed from a dead key (e.g. the
    /// circumflex <c>^</c> followed by <c>e</c> producing <c>ê</c>) are handled
    /// entirely in software using a composition table, because mixing
    /// <c>KEYEVENTF_UNICODE</c> with OS-level dead-key state is not possible.
    /// </para>
    ///
    /// <para>
    /// <b>Threading:</b> All sends are queued from the UI thread and consumed by a
    /// dedicated background worker thread. The worker dispatches the actual
    /// <c>SendInput</c> / <c>SendKeys</c> calls back to the UI thread via
    /// <c>Control.Invoke</c>. This keeps the UI responsive even when large
    /// amounts of text are being sent.
    /// </para>
    /// </summary>
    public static class SendKeysHelper
    {
        // ── Win32 P/Invoke ───────────────────────────────────────────────────
        // The following structs mirror the Win32 INPUT, KEYBDINPUT, MOUSEINPUT,
        // and HARDWAREINPUT structures used by the SendInput API.
        // They must match the exact binary layout that user32.dll expects.

        /// <summary>
        /// Win32 MOUSEINPUT structure — describes a mouse event.
        /// Included here because it is the largest member of the InputUnion,
        /// which determines the overall size of the INPUT struct.
        /// We never actually send mouse events; this is only for size alignment.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int    dx, dy;           // cursor movement deltas (pixels)
            public uint   mouseData;        // wheel delta or X-button identifier
            public uint   dwFlags;          // event type flags (MOUSEEVENTF_*)
            public uint   time;             // timestamp (0 = system fills it in)
            public IntPtr dwExtraInfo;      // extra application-defined data
        }

        /// <summary>
        /// Win32 KEYBDINPUT structure — describes a keyboard event.
        /// This is the struct we fill in for every key press and release we send.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;          // virtual-key code (0 when using Unicode)
            public ushort wScan;        // hardware scan code or Unicode code point
            public uint   dwFlags;      // KEYEVENTF_* flags (e.g. KEYEVENTF_UNICODE)
            public uint   time;         // timestamp (0 = system fills it in)
            public IntPtr dwExtraInfo;  // extra application-defined data
        }

        /// <summary>
        /// Win32 HARDWAREINPUT structure — describes a message from hardware other
        /// than keyboard or mouse. Included only to complete the union definition;
        /// we never use this path.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint   uMsg;      // message generated by the input hardware
            public ushort wParamL;   // low word of lParam for that message
            public ushort wParamH;   // high word of lParam for that message
        }

        /// <summary>
        /// Win32 union — only one of the three input-type structs is active at a time.
        /// All three fields start at offset 0 (they overlay each other in memory).
        /// We only use the <c>ki</c> (keyboard) field.
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT    mi;   // mouse event data
            [FieldOffset(0)] public KEYBDINPUT    ki;   // keyboard event data
            [FieldOffset(0)] public HARDWAREINPUT hi;   // other hardware event data
        }

        /// <summary>
        /// Win32 INPUT structure — the top-level structure passed to SendInput.
        /// The <c>type</c> field indicates which union member to use.
        /// <c>Size</c> is computed from the full struct including the largest
        /// union member (MOUSEINPUT), which is what user32.dll expects.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint       type;  // INPUT_MOUSE=0, INPUT_KEYBOARD=1, INPUT_HARDWARE=2
            public InputUnion U;     // the actual event payload
            // Marshal.SizeOf includes padding; this must match what SendInput expects.
            public static int Size => Marshal.SizeOf<INPUT>();
        }

        /// <summary>
        /// Win32 SendInput — injects keyboard and mouse events into the input queue.
        /// Returns the number of events successfully injected (may be less than
        /// <paramref name="nInputs"/> if another process has blocked input injection).
        /// </summary>
        /// <param name="nInputs">Number of events in the <paramref name="pInputs"/> array.</param>
        /// <param name="pInputs">Array of INPUT structures describing the events.</param>
        /// <param name="cbSize">Size of a single INPUT structure in bytes.</param>
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        /// <summary>
        /// Win32 SetForegroundWindow — attempts to bring a window to the front and
        /// give it keyboard focus. Only used as a last-resort fallback; Windows
        /// blocks this call from non-foreground processes in most circumstances.
        /// </summary>
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Win32 GetForegroundWindow — returns the HWND of the window that
        /// currently has keyboard focus. Used to detect when focus has been
        /// unexpectedly stolen from the target application.
        /// </summary>
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        // Constant: tells SendInput that the event is a keyboard event.
        const uint   INPUT_KEYBOARD    = 1;

        // Flag: the key is being released (without this flag it means key-down).
        const uint   KEYEVENTF_KEYUP   = 0x0002;

        // Flag: wScan contains a Unicode character code point, not a hardware scan code.
        // This is the flag that makes our sending layout-independent.
        const uint   KEYEVENTF_UNICODE = 0x0004;

        // Virtual-key codes for special keys that cannot use the Unicode path.
        const ushort VK_RETURN         = 0x0D;  // Enter key
        const ushort VK_TAB            = 0x09;  // Tab key
        const ushort VK_LWIN           = 0x5B;  // Left Windows key

        // ── State ────────────────────────────────────────────────────────────

        // HWND of the application we are typing into. Updated every time the
        // foreground window changes (via a WinEvent hook in KeyboardForm).
        // Used only as a fallback in RestoreFocusIfLost().
        private static IntPtr  _targetHwnd = IntPtr.Zero;

        // Reference to a UI control (the keyboard form). Required to marshal
        // SendInput and SendKeys calls onto the UI thread via Control.Invoke.
        private static Control _uiControl;

        /// <summary>
        /// Stores the HWND of the application window the user is typing into.
        ///
        /// Called by <c>KeyboardForm</c>'s WinEvent hook whenever a new external
        /// window becomes the foreground window. The stored HWND is only used as a
        /// fallback in <see cref="RestoreFocusIfLost"/>; in normal operation (where
        /// <see cref="NoActivateButton"/> keeps focus in the target app) it is never
        /// needed.
        /// </summary>
        /// <param name="hwnd">
        /// Window handle of the newly focused external application window.
        /// Pass <c>IntPtr.Zero</c> to clear the stored target.
        /// </param>
        public static void SetTargetWindow(IntPtr hwnd) { _targetHwnd = hwnd; }

        /// <summary>
        /// Registers the UI control that is used to marshal calls to the UI thread.
        /// Must be called once from the <c>KeyboardForm</c> constructor before any
        /// keys are pressed.
        /// </summary>
        /// <param name="c">
        /// Any live Windows Forms control on the UI thread — typically the
        /// keyboard form itself.
        /// </param>
        public static void SetUiControl(Control c) { _uiControl = c; }

        // ── Test mode ─────────────────────────────────────────────────────────

        /// <summary>
        /// When set to <c>true</c>, <see cref="Send"/> silently discards all input
        /// and the worker thread spins idle without calling SendInput or SendKeys.
        /// Set this in unit tests to prevent actual keystrokes from being injected
        /// into whatever window happens to have focus during testing.
        /// </summary>
        public static bool TestMode { get; set; } = false;

        // ── Settings ──────────────────────────────────────────────────────────


        // ── Queue + worker thread ─────────────────────────────────────────────

        // Thread-safe bounded queue that decouples the UI thread (which enqueues
        // strings) from the worker thread (which drains and sends them).
        // Capacity 256 is generous — typical key presses arrive one at a time.
        private static readonly BlockingCollection<string> _queue =
            new BlockingCollection<string>(256);

        private static readonly Thread _worker;

        /// <summary>
        /// Static constructor — runs once when the class is first used.
        /// Starts the background worker thread that drains the send queue.
        /// The thread is marked as a background thread so it does not prevent
        /// the application from exiting.
        /// STA (Single-Threaded Apartment) is required by <c>SendKeys.SendWait</c>.
        /// </summary>
        static SendKeysHelper()
        {
            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name         = "SendInput worker",
            };
            // SendKeys.SendWait requires an STA thread to function correctly.
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        /// <summary>
        /// Queues a string for sending to the focused application.
        /// Returns immediately and never blocks the UI thread.
        ///
        /// <para>The string can be:</para>
        /// <list type="bullet">
        ///   <item>Plain text: sent via <c>SendInput</c> with Unicode injection.</item>
        ///   <item>SendKeys syntax (e.g. <c>{ENTER}</c>, <c>^c</c>): sent via <c>SendKeys.SendWait</c>.</item>
        ///   <item><c>dead:X</c>: registers X as a pending dead key for composition.</item>
        ///   <item><c>win:X</c>: sends Win+X via SendInput virtual-key injection.</item>
        ///   <item><c>wp:X</c>: word-prediction slot (currently a no-op).</item>
        /// </list>
        /// </summary>
        /// <param name="s">The string to send. Null or empty strings are ignored.</param>
        public static void Send(string s)
        {
            if (string.IsNullOrEmpty(s) || TestMode) return;
            _queue.TryAdd(s);
        }

        // ── Worker ────────────────────────────────────────────────────────────
        // Batches consecutive plain-text items into one SendInput call.
        // Special-key sequences ({ENTER}, ^c etc.) flush the buffer first,
        // then are sent individually via SendKeys.

        // Accumulates consecutive plain-text items so they can be sent as a
        // single SendInput call (more efficient and avoids timing gaps).
        private static readonly StringBuilder _buf = new StringBuilder();

        // How long the worker waits for a new item before flushing any
        // accumulated text and looping. 80 ms is short enough to feel
        // instantaneous, long enough to batch rapid key presses together.
        private const int FlushMs = 80;

        /// <summary>
        /// Background worker loop. Runs for the lifetime of the application.
        ///
        /// <para>
        /// The loop tries to pull an item from the queue, waiting up to
        /// <c>FlushMs</c> milliseconds. If an item arrives and it is plain text,
        /// it is appended to a buffer and any further immediately-available plain
        /// text items are drained into the same buffer (batching). When a
        /// non-plain item is encountered (or the queue drains), the buffer is
        /// flushed as a single <c>SendInput</c> call, and then the non-plain item
        /// is routed appropriately.
        /// </para>
        ///
        /// <para>
        /// If the wait times out with nothing in the queue, any leftover buffered
        /// text is flushed, and the loop checks whether the queue has been
        /// permanently completed (application shutdown).
        /// </para>
        /// </summary>
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
                                // Non-plain item arrived — flush accumulated text
                                // first so ordering is preserved, then handle it.
                                FlushText();
                                RouteNonPlain(next);
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Non-plain item — flush any pending text before processing
                        // it so characters appear in the correct order.
                        FlushText();
                        RouteNonPlain(s);
                    }
                }
                else
                {
                    // Timeout — no items arrived within FlushMs.
                    // Flush any text that has been buffered so far.
                    FlushText();
                    // If the queue has been marked complete, exit the loop.
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

        /// <summary>
        /// Sends the text accumulated in <c>_buf</c> via Unicode injection and
        /// clears the buffer. Also applies any pending dead-key composition
        /// (e.g. transforms "^" + "e" into "ê") before sending.
        /// Does nothing if the buffer is empty.
        /// </summary>
        private static void FlushText()
        {
            if (_buf.Length == 0) return;
            string text = _buf.ToString();
            _buf.Clear();
            // Apply dead-key composition before sending (may prepend a literal
            // dead character or replace the first character with a composed one).
            text = ApplyPendingDead(text);
            // SendInput must be called on the UI thread because SendKeys (used
            // elsewhere) is not thread-safe, and keeping all input on one thread
            // avoids interleaving issues.
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

            // Each character requires two INPUT structs: one key-down and one key-up.
            // Pre-allocating with the expected capacity avoids list resizing.
            var inputs = new List<INPUT>(text.Length * 2);
            foreach (char ch in text)
            {
                if (ch == '\n' || ch == '\r')
                {
                    // Newline/carriage return — must use the virtual key path because
                    // KEYEVENTF_UNICODE does not work for control characters.
                    inputs.Add(MakeVk(VK_RETURN, false)); // key-down
                    inputs.Add(MakeVk(VK_RETURN, true));  // key-up
                }
                else if (ch == '\t')
                {
                    // Tab — same reason as above; use virtual key.
                    inputs.Add(MakeVk(VK_TAB, false));
                    inputs.Add(MakeVk(VK_TAB, true));
                }
                else
                {
                    // All other printable characters use the Unicode injection path.
                    // Casting char to ushort gives us the UTF-16 code unit, which is
                    // what wScan expects when KEYEVENTF_UNICODE is set.
                    inputs.Add(MakeUni((ushort)ch, false)); // key-down
                    inputs.Add(MakeUni((ushort)ch, true));  // key-up
                }
            }

            if (inputs.Count > 0)
            {
                var arr = inputs.ToArray();
                SendInput((uint)arr.Length, arr, INPUT.Size);
            }
        }

        // ── SECONDARY: SendKeys for control sequences ─────────────────────────

        /// <summary>
        /// Sends a SendKeys-syntax string (e.g. <c>{ENTER}</c>, <c>^c</c>,
        /// <c>%{F4}</c>) via <c>SendKeys.SendWait</c>.
        ///
        /// If a dead key is currently pending (e.g. the user pressed "^" as a dead
        /// key and then pressed a special key before any composable letter), the dead
        /// key is flushed as a literal character first so nothing is lost.
        ///
        /// <c>SendWait</c> blocks until all injected events have been processed,
        /// which is why it is marshalled onto the UI thread rather than called from
        /// the worker thread directly.
        /// </summary>
        /// <param name="s">A SendKeys-syntax string (not plain Unicode text).</param>
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

        // The character of the currently pending dead key (e.g. '^' for circumflex).
        // Null means no dead key is waiting. Set by RouteNonPlain when a "dead:X"
        // message arrives; cleared by ApplyPendingDead or FlushPendingDead.
        private static string _pendingDead = null;

        // Composition table: dead-char → (base letter → precomposed character)
        // All non-ASCII characters are written as \uXXXX escapes to avoid
        // any encoding ambiguity in the source file.
        //
        // Supported dead keys:
        //   '^'      circumflex accent   (â ê î ô û etc.)
        //   '¨' diaeresis / umlaut  (ä ë ï ö ü etc.)  — the ¨ character
        //   '~'      tilde               (ã ñ õ etc.)
        //   '`'      grave accent        (à è ì ò ù etc.)
        //   '´' acute accent        (á é í ó ú etc.)  — the ´ character
        private static readonly Dictionary<char, Dictionary<char, char>> _compose =
            new Dictionary<char, Dictionary<char, char>>
        {
            ['^'] = new Dictionary<char, char> {
                {'a','â'},{'e','ê'},{'i','î'},{'o','ô'},{'u','û'},
                {'A','Â'},{'E','Ê'},{'I','Î'},{'O','Ô'},{'U','Û'},
            },
            ['¨'] = new Dictionary<char, char> {
                {'a','ä'},{'e','ë'},{'i','ï'},{'o','ö'},{'u','ü'},{'y','ÿ'},
                {'A','Ä'},{'E','Ë'},{'I','Ï'},{'O','Ö'},{'U','Ü'},{'Y','Ÿ'},
            },
            ['~'] = new Dictionary<char, char> {
                {'a','ã'},{'n','ñ'},{'o','õ'},
                {'A','Ã'},{'N','Ñ'},{'O','Õ'},
            },
            ['`'] = new Dictionary<char, char> {
                {'a','à'},{'e','è'},{'i','ì'},{'o','ò'},{'u','ù'},
                {'A','À'},{'E','È'},{'I','Ì'},{'O','Ò'},{'U','Ù'},
            },
            ['´'] = new Dictionary<char, char> {
                {'a','á'},{'e','é'},{'i','í'},{'o','ó'},{'u','ú'},{'y','ý'},
                {'A','Á'},{'E','É'},{'I','Í'},{'O','Ó'},{'U','Ú'},{'Y','Ý'},
            },
        };

        /// <summary>
        /// Dispatches a non-plain-text string to the correct send path.
        ///
        /// <para>Recognised prefixes:</para>
        /// <list type="bullet">
        ///   <item><c>wp:</c> — word-prediction slot; silently ignored (not yet implemented).</item>
        ///   <item><c>dead:</c> — registers the character after the colon as the current pending dead key.</item>
        ///   <item><c>win:</c> — sends Win + the specified key via <see cref="SendWinKey"/>.</item>
        ///   <item>(anything else) — treated as SendKeys syntax and forwarded to <see cref="SendSpecial"/>.</item>
        /// </list>
        /// </summary>
        /// <param name="s">A non-plain-text string pulled from the send queue.</param>
        private static void RouteNonPlain(string s)
        {
            if (s.StartsWith("wp:", StringComparison.Ordinal))
            {
                // Word prediction — not yet implemented, skip silently
                return;
            }
            else if (s.StartsWith("dead:", StringComparison.Ordinal))
            {
                // A new dead key is arriving. If one was already pending (i.e. the
                // user pressed two dead keys in a row), flush the first one literally.
                FlushPendingDead();
                // Store the character after "dead:" (e.g. "^" from "dead:^").
                // s.Length > 5 guards against a malformed "dead:" with no payload.
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
        /// <param name="key">
        /// The key to combine with the Windows key. Can be a single character
        /// (e.g. <c>"d"</c> for Win+D) or a brace-enclosed token (e.g.
        /// <c>"{LEFT}"</c> for Win+Left Arrow). Case-insensitive.
        /// </param>
        private static void SendWinKey(string key)
        {
            InvokeOnUI(() =>
            {
                RestoreFocusIfLost();

                ushort vk = WinKeyPayloadToVk(key);
                if (vk == 0) return; // unknown key — silently skip

                // Press and release sequence: LWIN down, key down, key up, LWIN up.
                // This matches what Windows expects for a Win+key shortcut.
                var inputs = new[]
                {
                    MakeVk(VK_LWIN, false),  // Win key down
                    MakeVk(vk,      false),  // shortcut key down
                    MakeVk(vk,      true),   // shortcut key up
                    MakeVk(VK_LWIN, true),   // Win key up
                };
                SendInput((uint)inputs.Length, inputs, INPUT.Size);
            });
        }

        /// <summary>
        /// Converts a Win-key payload string (e.g. <c>"d"</c>, <c>"{LEFT}"</c>)
        /// into a Win32 virtual-key code.
        /// Returns 0 if the key is not recognised, which the caller treats as
        /// "silently skip".
        /// </summary>
        /// <param name="key">
        /// Single letter, single digit, or brace-enclosed token (case-insensitive).
        /// </param>
        /// <returns>The virtual-key code, or 0 if unrecognised.</returns>
        private static ushort WinKeyPayloadToVk(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;

            // Single letter a-z: virtual-key codes for letters equal their
            // ASCII upper-case value (VK_A = 0x41, VK_B = 0x42, … VK_Z = 0x5A).
            if (key.Length == 1 && char.IsLetter(key[0]))
                return (ushort)(char.ToUpper(key[0])); // VK_A = 0x41 etc.

            // Single digit 0-9: virtual-key codes equal their ASCII value
            // (VK_0 = 0x30, VK_1 = 0x31, … VK_9 = 0x39).
            if (key.Length == 1 && char.IsDigit(key[0]))
                return (ushort)key[0]; // VK_0 = 0x30 etc.

            // {KEY} token — strip the surrounding braces before looking up the name.
            string k = key.StartsWith("{") && key.EndsWith("}")
                ? key.Substring(1, key.Length - 2).ToUpper()
                : key.ToUpper();

            // Map token names to Win32 virtual-key codes (hex values from winuser.h).
            return k switch
            {
                "LEFT"      => 0x25,  // VK_LEFT
                "UP"        => 0x26,  // VK_UP
                "RIGHT"     => 0x27,  // VK_RIGHT
                "DOWN"      => 0x28,  // VK_DOWN
                "HOME"      => 0x24,  // VK_HOME
                "END"       => 0x23,  // VK_END
                "PGUP"      => 0x21,  // VK_PRIOR (Page Up)
                "PGDN"      => 0x22,  // VK_NEXT  (Page Down)
                "ENTER"     => 0x0D,  // VK_RETURN
                "TAB"       => 0x09,  // VK_TAB
                "ESC"       => 0x1B,  // VK_ESCAPE
                "DELETE"    => 0x2E,  // VK_DELETE
                "SPACE"     => 0x20,  // VK_SPACE
                "F1"        => 0x70,  // VK_F1
                "F2"        => 0x71,  // VK_F2
                "F3"        => 0x72,  // VK_F3
                "F4"        => 0x73,  // VK_F4
                "F5"        => 0x74,  // VK_F5
                "F6"        => 0x75,  // VK_F6
                "F7"        => 0x76,  // VK_F7
                "F8"        => 0x77,  // VK_F8
                "F9"        => 0x78,  // VK_F9
                "F10"       => 0x79,  // VK_F10
                "F11"       => 0x7A,  // VK_F11
                "F12"       => 0x7B,  // VK_F12
                // Common Windows shortcuts by letter (Win+D = show desktop, etc.)
                "D"         => (ushort)'D',
                "E"         => (ushort)'E',
                "L"         => (ushort)'L',
                "M"         => (ushort)'M',
                "R"         => (ushort)'R',
                "S"         => (ushort)'S',
                _           => 0,     // unrecognised — caller will skip silently
            };
        }

        /// <summary>
        /// Called from <see cref="FlushText"/> before sending a plain-text batch.
        /// Checks whether a dead key is waiting and, if so, tries to compose it
        /// with the first character of <paramref name="text"/>.
        ///
        /// <para>Example: pending dead = <c>'^'</c>, text = <c>"elephant"</c>
        /// → returns <c>"êléphant"</c> (only the first character is composed).</para>
        ///
        /// <para>If the first character does not have a composition for the pending
        /// dead key (e.g. pending = <c>'^'</c>, text starts with <c>'b'</c>),
        /// the dead character is prepended literally and text is returned unchanged.</para>
        /// </summary>
        /// <param name="text">The plain-text string about to be sent.</param>
        /// <returns>
        /// The (possibly modified) string to send. The pending dead key is always
        /// cleared as a side effect of calling this method.
        /// </returns>
        private static string ApplyPendingDead(string text)
        {
            if (_pendingDead == null || text.Length == 0) return text;

            char dead = _pendingDead[0];
            _pendingDead = null; // clear regardless of whether composition succeeds

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

        /// <summary>
        /// Immediately sends any pending dead key as a literal Unicode character
        /// without composing it, then clears the pending state.
        ///
        /// Called before any non-composable event (e.g. a control key or a second
        /// dead key) to ensure the previously pressed dead key is not silently lost.
        /// Does nothing if no dead key is pending.
        /// </summary>
        private static void FlushPendingDead()
        {
            if (_pendingDead == null) return;
            string dead = _pendingDead;
            _pendingDead = null;
            // Send the dead character as a plain Unicode character
            InvokeOnUI(() =>
            {
                RestoreFocusIfLost();
                // One key-down + one key-up for the dead character itself.
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
            // If we don't know the target window, there is nothing to restore.
            if (target == IntPtr.Zero) return;
            // Fast path: focus is still where it should be — nothing to do.
            if (GetForegroundWindow() == target) return;
            // Focus was stolen by something outside our control.
            // SetForegroundWindow posts an async activation message — no sleep is needed.
            // Sleeping here blocks the UI thread for 30 ms on every keystroke where focus
            // was unexpectedly stolen, which is worse than the original problem.
            SetForegroundWindow(target);
        }

        // ── INPUT factories ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a keyboard INPUT structure for a single Unicode character event.
        /// Used for all printable characters (sent via the KEYEVENTF_UNICODE path).
        /// </summary>
        /// <param name="scan">
        /// The Unicode code point of the character (cast from <c>char</c> to
        /// <c>ushort</c>). Stored in the <c>wScan</c> field; <c>wVk</c> is left 0.
        /// </param>
        /// <param name="up">
        /// <c>true</c> for a key-release event; <c>false</c> for a key-press event.
        /// </param>
        /// <returns>A fully initialised INPUT structure ready for SendInput.</returns>
        static INPUT MakeUni(ushort scan, bool up)
        {
            // Combine KEYEVENTF_UNICODE with KEYEVENTF_KEYUP when releasing the key.
            uint flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0u);
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U    = new InputUnion
                {
                    // wVk = 0 because we are using the Unicode path, not a virtual key.
                    ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags }
                }
            };
        }

        /// <summary>
        /// Creates a keyboard INPUT structure for a virtual-key event.
        /// Used for control characters (Enter, Tab) and modifier keys (Win).
        /// </summary>
        /// <param name="vk">
        /// The Win32 virtual-key code (e.g. <c>VK_RETURN</c> = 0x0D).
        /// Stored in the <c>wVk</c> field; <c>wScan</c> is left 0.
        /// </param>
        /// <param name="up">
        /// <c>true</c> for a key-release event; <c>false</c> for a key-press event.
        /// </param>
        /// <returns>A fully initialised INPUT structure ready for SendInput.</returns>
        static INPUT MakeVk(ushort vk, bool up)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U    = new InputUnion
                {
                    // wScan = 0 because we are using the virtual-key path.
                    ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = up ? KEYEVENTF_KEYUP : 0u }
                }
            };
        }


        // ── UI thread dispatch ────────────────────────────────────────────────

        /// <summary>
        /// Marshals an action onto the UI thread using <c>Control.Invoke</c>.
        ///
        /// <c>SendKeys.SendWait</c> and (for safety) <c>SendInput</c> are called
        /// from the UI thread to avoid threading issues. If the UI control is not
        /// available (null or already disposed), the action is run on the calling
        /// thread as a fallback — this should not happen in production but prevents
        /// a hard crash during teardown.
        /// </summary>
        /// <param name="a">The action to run on the UI thread.</param>
        private static void InvokeOnUI(Action a)
        {
            var ui = _uiControl;
            // If no UI control is registered, or if it has been disposed (e.g.
            // during application shutdown), run the action on the current thread.
            if (ui == null || ui.IsDisposed) { a(); return; }
            try { ui.Invoke(a); } catch { a(); }
        }

        // ── Public string helpers ─────────────────────────────────────────────

        // Characters that have special meaning in SendKeys syntax and must be
        // wrapped in braces (e.g. '+' becomes '{+}') to be treated as literals.
        private static readonly char[] _special =
            { '+', '^', '%', '~', '(', ')', '{', '}', '[', ']' };

        /// <summary>
        /// Escapes SendKeys special characters in a raw string so the string can
        /// be passed to <c>SendKeys.SendWait</c> without unintended side effects.
        ///
        /// <para>
        /// SendKeys gives special meaning to: <c>+ ^ % ~ ( ) { } [ ]</c>.
        /// This method wraps each such character in braces — e.g. <c>^</c>
        /// becomes <c>{^}</c> — so it is treated as a literal character.
        /// </para>
        ///
        /// <para>
        /// Exception: existing <c>{KEY}</c> sequences (e.g. <c>{ENTER}</c>,
        /// <c>{F5}</c>, <c>{^}</c>) are preserved unchanged, because they are
        /// intentional SendKeys tokens, not raw characters to be escaped.
        /// </para>
        ///
        /// Used when storing a key's send string after Quick Edit in the editor UI.
        /// </summary>
        /// <param name="input">The raw string entered by the user.</param>
        /// <returns>
        /// The same string with all bare special characters escaped for SendKeys,
        /// or the original string if it is null or empty.
        /// </returns>
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
                    // Look ahead for a matching closing brace.
                    int close = input.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        // This is an existing {KEY} token — copy it as-is.
                        sb.Append(input, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                    // Lone '{' with no closing '}' — fall through and escape it below.
                }
                if (Array.IndexOf(_special, c) >= 0)
                    // Wrap in braces so SendKeys treats it as a literal character.
                    sb.Append('{').Append(c).Append('}');
                else
                    sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Wraps a send string with SendKeys modifier prefixes based on the
        /// active modifier keys.
        ///
        /// <para>SendKeys modifier prefixes: <c>^</c> = Ctrl, <c>%</c> = Alt,
        /// <c>+</c> = Shift. When the target string is longer than one character
        /// (or is a space), it is wrapped in parentheses so all characters
        /// receive the modifier — e.g. <c>+(ab)</c> types "AB".</para>
        ///
        /// <para>Examples:</para>
        /// <list type="bullet">
        ///   <item><c>ApplyModifiers("a", shift:true, ctrl:true)</c> → <c>"^+a"</c></item>
        ///   <item><c>ApplyModifiers("ab", shift:true)</c> → <c>"+(ab)"</c></item>
        ///   <item><c>ApplyModifiers("c", ctrl:true)</c> → <c>"^c"</c> (Ctrl+C)</item>
        /// </list>
        /// </summary>
        /// <param name="send">The base send string (may include SendKeys syntax).</param>
        /// <param name="shift"><c>true</c> if the Shift modifier should be applied.</param>
        /// <param name="ctrl"><c>true</c> if the Ctrl modifier should be applied.</param>
        /// <param name="alt"><c>true</c> if the Alt modifier should be applied.</param>
        /// <returns>
        /// The send string with the appropriate prefix(es) prepended, or the
        /// original string if no modifiers are active.
        /// </returns>
        public static string ApplyModifiers(string send, bool shift, bool ctrl, bool alt)
        {
            if (string.IsNullOrEmpty(send)) return send;
            if (!shift && !ctrl && !alt) return send;
            string prefix = "";
            if (ctrl)  prefix += "^";  // Ctrl prefix
            if (alt)   prefix += "%";  // Alt prefix
            if (shift) prefix += "+";  // Shift prefix
            // A string longer than one character needs parentheses so all characters
            // are modified. A lone space also needs them because " " parses oddly.
            bool needsParens = send.Length > 1 || send == " ";
            return needsParens ? $"{prefix}({send})" : $"{prefix}{send}";
        }

        /// <summary>
        /// Returns <c>true</c> if the string should be sent via the
        /// <c>SendInput</c> Unicode injection path, or <c>false</c> if it contains
        /// SendKeys syntax and must use <c>SendKeys.SendWait</c>.
        ///
        /// <para>Classified as <b>NOT plain text</b> (returns <c>false</c>) when:</para>
        /// <list type="bullet">
        ///   <item>Contains a complete <c>{KEY}</c> sequence (e.g. <c>{ENTER}</c>, <c>{F5}</c>).</item>
        ///   <item>Starts with a modifier prefix character (<c>^</c> <c>%</c> <c>+</c>)
        ///         AND has length ≥ 2 — meaning it is a shortcut, not a bare symbol.</item>
        ///   <item>Starts with a recognised protocol prefix: <c>dead:</c>, <c>win:</c>, <c>wp:</c>.</item>
        /// </list>
        ///
        /// <para><b>Important edge case:</b> a single <c>^</c>, <c>%</c>, or <c>+</c>
        /// character (length 1) is treated as plain text and goes via the Unicode path.
        /// Only when followed by more characters does it become a modifier prefix.</para>
        /// </summary>
        /// <param name="s">The send string to classify.</param>
        /// <returns><c>true</c> if plain Unicode text; <c>false</c> if SendKeys syntax or protocol string.</returns>
        public static bool IsPlainText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;

            if (s.Contains('{'))
            {
                int open  = s.IndexOf('{');
                int close = s.IndexOf('}', open + 1);
                if (close > open) return false; // valid {KEY} token found
                // Lone { with no closing } → plain text
            }

            if (s.Length >= 2)
            {
                char f = s[0];
                // A modifier-prefix character at the start of a multi-character string
                // means it is a SendKeys shortcut, not plain text.
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
        /// Returns <c>true</c> if the string consists entirely of whitespace
        /// characters (space, tab, carriage return, or line feed).
        ///
        /// Returns <c>false</c> for null or empty strings. Used by unit tests to
        /// distinguish pure-whitespace send strings from mixed-content ones.
        /// </summary>
        /// <param name="s">The string to check.</param>
        /// <returns>
        /// <c>true</c> if every character is a whitespace character;
        /// <c>false</c> otherwise.
        /// </returns>
        public static bool IsWhitespaceOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n') return false;
            return true;
        }

        /// <summary>
        /// Converts a whitespace-only string to its SendKeys equivalent so that
        /// whitespace characters can be sent via <c>SendKeys.SendWait</c> if needed.
        ///
        /// <para>Conversion rules:</para>
        /// <list type="table">
        ///   <item><term>Space (<c>' '</c>)</term><description>Kept as a literal space.</description></item>
        ///   <item><term>Tab (<c>'\t'</c>)</term><description>Becomes <c>{TAB}</c>.</description></item>
        ///   <item><term>Line feed (<c>'\n'</c>)</term><description>Becomes <c>{ENTER}</c>.</description></item>
        /// </list>
        ///
        /// Carriage return (<c>'\r'</c>) is silently dropped because <c>'\n'</c> already
        /// represents a newline in SendKeys syntax. Used by unit tests.
        /// </summary>
        /// <param name="s">
        /// A string containing only whitespace characters
        /// (validated separately by <see cref="IsWhitespaceOnly"/>).
        /// </param>
        /// <returns>A SendKeys-syntax string equivalent of the input.</returns>
        public static string BuildWhitespaceSendKeys(string s)
        {
            var sb = new StringBuilder(s.Length * 2);
            foreach (char c in s)
                switch (c)
                {
                    case ' ':  sb.Append(' ');       break;
                    case '\t': sb.Append("{TAB}");   break;
                    case '\n': sb.Append("{ENTER}"); break;
                    // '\r' is intentionally not mapped — it is ignored.
                }
            return sb.ToString();
        }
    }
}
