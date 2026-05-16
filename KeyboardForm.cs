using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>
    /// The main on-screen keyboard window.
    ///
    /// <para>
    /// This form is the heart of the application. It displays a grid of keyboard
    /// buttons, handles all user interaction (clicking keys, editing the layout,
    /// saving/loading files), and coordinates with <see cref="WordPredictor"/> for
    /// word prediction and <see cref="SendKeysHelper"/> for injecting keystrokes into
    /// the focused application.
    /// </para>
    ///
    /// <para>Key design decisions:</para>
    /// <list type="bullet">
    ///   <item><b>No focus stealing</b> — <c>WS_EX_NOACTIVATE</c> and
    ///   <c>NoActivateButton</c> ensure clicking the keyboard never takes focus away
    ///   from the application the user is typing into.</item>
    ///   <item><b>Three modes</b> — Normal (typing), Edit (layout editing), and
    ///   GearPlacement (repositioning the gear button).</item>
    ///   <item><b>All state in memory</b> — the layout is auto-saved to XML after every
    ///   edit so no data is lost if the app closes unexpectedly.</item>
    /// </list>
    /// </summary>
    public class KeyboardForm : Form
    {
        // ── Win32 ────────────────────────────────────────────────────
        // These constants let us call Windows API functions that WinForms
        // does not expose directly.
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        /// <summary>
        /// Tells Windows to keep this window above all other windows (or removes
        /// that guarantee) depending on the <see cref="WindowState.AlwaysOnTop"/> setting.
        /// Uses <c>SetWindowPos</c> with <c>SWP_NOACTIVATE</c> so the call does not
        /// steal focus.
        /// </summary>
        private void ForceTopMost()
        {
            if (!IsHandleCreated) return;
            var target = _window.AlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST;
            SetWindowPos(Handle, target, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override CreateParams CreateParams
        {
            // WS_EX_NOACTIVATE: clicking keyboard never steals focus from target app.
            // WS_EX_TOOLWINDOW removed — it caused a small non-standard title bar and
            // suppressed minimize/maximize buttons. ShowInTaskbar=false hides from taskbar instead.
            get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_NOACTIVATE | 0x02000000; return cp; }  // 0x02000000 = WS_EX_COMPOSITED
        }

        // ── State ────────────────────────────────────────────────────
        private GridLayout          _layout;
        private readonly Dictionary<GridCell, Button> _buttons = new();
        internal VisualTheme  _theme  = new VisualTheme();
        internal WindowState  _window = new WindowState();
        internal LayoutMeta   _meta   = new LayoutMeta();

        private enum Mode { Normal, Edit, GearPlacement }

        // Gear button appearance per mode
        private static readonly Color _gearNormalBg  = ColorTranslator.FromHtml("#2A2A4A");
        private static readonly Color _gearNormalFg  = ColorTranslator.FromHtml("#CCCCFF");
        private static readonly Color _gearEditBg    = Color.FromArgb(200, 100, 0);   // amber
        private static readonly Color _stripEditColor = Color.FromArgb(220, 120, 0);  // orange
        private Mode _mode = Mode.Normal;

        // ── Drag-to-swap (Edit mode) ──────────────────────────────────
        private GridCell _dragCandidate = null;   // cell under mousedown; set before threshold
        private Point    _dragStartPt   = Point.Empty;

        // Sentinel object used as drag data when the gear button itself is being repositioned.
        private sealed class GearDragToken { }
        private static readonly GearDragToken _gearDragSentinel = new GearDragToken();

        // ── Format clipboard (Edit mode) ─────────────────────────────
        private KeyProps _copiedFormatting = null;  // null = nothing copied yet
        private bool     _fmtPaintMode    = false;  // true = format-painter active; next click applies fmt
        private KeyProps _copiedKey        = null;  // full key copy (content + formatting)
        private bool     _keyPaintMode    = false;  // true = key-painter active; next click pastes key

        // ── Undo / Redo ───────────────────────────────────────────────
        private readonly Stack<(GridLayout Layout, VisualTheme Theme, WindowState Window, LayoutMeta Meta)> _undoStack = new();
        private readonly Stack<(GridLayout Layout, VisualTheme Theme, WindowState Window, LayoutMeta Meta)> _redoStack = new();

        private Button _gearBtn;
        private System.Windows.Forms.Timer _holdTimer;  // fires after 1 s when HoldToEdit is on
        private Panel  _editStrip;   // thin colored bar along bottom — signals edit/quickedit mode
        private Panel  _toolbar;     // toolbar row 1: file ops + mode buttons (Edit+QuickEdit)
        private Panel  _toolbarEdit; // toolbar row 2: key/grid actions (Edit only)
        private ToolbarButton _btnEdit;       // toolbar: switch to Edit mode
        private ToolbarButton _btnExitEdit;   // toolbar: return to Normal mode
        private ToolbarButton _btnEditKeyboard; // toolbar: open keyboard editor
        private ToolbarButton _btnLoad;       // toolbar: load layout file
        private ToolbarButton _btnSave;       // toolbar: save layout file
        private ToolbarButton _btnUndo;       // toolbar: undo last edit
        private ToolbarButton _btnRedo;       // toolbar: redo last undone edit
        private Label         _lblFilename;   // toolbar: current file name

        // ── Selection (Edit mode) ─────────────────────────────────────
        private GridCell      _selectedCell = null;
        private ToolbarButton _btnKeyEdit;    // toolbar2: open key editor
        private ToolbarButton _btnKeyRemove;  // toolbar2: clear key
        private ToolbarButton _btnCopyFmt;    // toolbar2: copy formatting + enter paint mode
        private ToolbarButton _btnCopyKey;    // toolbar2: copy full key + enter key-paint mode
        private Label         _lblSelectedKey;// toolbar2: selected key info

        // ── Grid action buttons (toolbar2, Edit only) ─────────────────
        private ToolbarButton _btnAddRowAbove, _btnAddRowBelow;
        private ToolbarButton _btnAddColLeft,  _btnAddColRight;
        private ToolbarButton _btnRemoveRow,   _btnRemoveCol;
        private ToolbarButton _btnMergeRight,  _btnMergeDown;
        private ToolbarButton _btnSplitCell;

        private ToolTip          _toolTip;          // shared tooltip for all toolbar buttons
        private string _currentFilePath = null;

        // ── Word prediction ──────────────────────────────────────────
        private readonly WordPredictor _predictor = new WordPredictor(7);

        private const int Pad = 8, Gap = 4;

        // Latched modifier cells
        private readonly HashSet<GridCell> _latchedMods = new HashSet<GridCell>();
        // Sticky modifiers: first click = latched (clears after next key),
        // second click = locked (stays until clicked again).
        private readonly HashSet<GridCell> _lockedMods  = new HashSet<GridCell>();

        // ── WinEvent hook — tracks last focused external window ───────
        // Used by SendKeysHelper to restore focus before sending.
        // WINEVENT_SKIPOWNPROCESS ensures our own window is ignored.
        private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd,
            int obj, int child, uint thread, uint time);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eMin, uint eMax,
            IntPtr mod, WinEventDelegate proc, uint pid, uint tid, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr h);
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private WinEventDelegate _hookDelegate;  // keep alive — GC must not collect
        private IntPtr           _hookHandle = IntPtr.Zero;

        /// <summary>
        /// Installs a system-wide hook that fires whenever any window in any
        /// application gains focus.  We use this to track the last external window
        /// so <see cref="SendKeysHelper"/> knows where to direct keystrokes.
        /// <para>
        /// <c>WINEVENT_SKIPOWNPROCESS</c> ensures our own form never appears as the
        /// target, which would send keystrokes to ourselves.
        /// </para>
        /// </summary>
        private void RegisterFocusHook()
        {
            _hookDelegate = (hook, evt, hwnd, obj, child, thread, time) =>
            {
                if (hwnd != IntPtr.Zero)
                    SendKeysHelper.SetTargetWindow(hwnd);
            };
            _hookHandle = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _hookDelegate, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        /// <summary>Returns true if the given cell represents a modifier key (Shift, Ctrl, etc.).</summary>
        private bool IsModifier(GridCell cell) =>
            cell != null && KeyLayout.ModifierLabels.Contains(cell.Props.Label);

        /// <summary>
        /// Returns true if any currently latched modifier has the given label.
        /// Used to check whether Shift, Ctrl, Alt, or AltGr is currently active.
        /// </summary>
        private bool AnyModifier(string label)
        {
            foreach (var cell in _latchedMods)
                if (cell.Props.Label == label) return true;
            return false;
        }

        /// <summary>True when Shift or Caps Lock is currently latched.</summary>
        private bool ShiftActive => AnyModifier("Shift") || AnyModifier("Caps");
        /// <summary>True when AltGr is currently latched.</summary>
        private bool AltGrActive => AnyModifier("AltGr");

        /// <summary>
        /// Latches all Shift keys so the next letter is typed as a capital.
        /// Called by the word predictor at the start of a new sentence.
        /// </summary>
        private void LatchShiftForSentence()
        {
            foreach (var cell in _layout.Cells)
                if (cell.Props.Label == "Shift" && !_latchedMods.Contains(cell))
                    _latchedMods.Add(cell);
            RefreshAllButtons();
            ApplyWPTags();
        }

        /// <summary>
        /// Removes any non-locked Shift latches. Called by the predictor after a
        /// capital letter has been committed so the keyboard returns to lower case.
        /// </summary>
        private void UnlatchShift()
        {
            bool removed = _latchedMods.RemoveWhere(c =>
                c.Props.Label == "Shift" && !_lockedMods.Contains(c)) > 0;
            if (removed) RefreshAllButtons();
            ApplyWPTags();
        }

        // ── Constructor ──────────────────────────────────────────────

        /// <summary>
        /// Creates and initialises the keyboard window.
        /// <para>
        /// In order:
        /// <list type="number">
        ///   <item>Sets window chrome properties (no taskbar button, always on top, no focus stealing).</item>
        ///   <item>Loads the English translation and the word-prediction database if present.</item>
        ///   <item>Wires the predictor events (predictions changed → refresh buttons, shift latch → capitalise, inject send → type).</item>
        ///   <item>Builds the default QWERTY layout, gear button, edit strip, and toolbars.</item>
        ///   <item>Calls <see cref="TryAutoLoad"/> to restore the last-used layout from disk.</item>
        ///   <item>Registers window-resize and close handlers for auto-save and cleanup.</item>
        ///   <item>Installs the system-wide focus hook via <see cref="RegisterFocusHook"/>.</item>
        /// </list>
        /// </para>
        /// </summary>
        public KeyboardForm()
        {
            Text            = "On-Screen Keyboard";
            BackColor       = _theme.BackgroundColor;
            Opacity         = _theme.Opacity;
            TopMost         = true;
            ShowInTaskbar   = false;   // hide from taskbar without WS_EX_TOOLWINDOW
            MinimumSize     = new Size(400, 150);
            Size            = new Size(1050, 290);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;
            Font            = new Font("Arial", 9f, FontStyle.Bold);
            KeyPreview      = true;

            Lang.Load("en");
            // Load word prediction database if present next to the exe
            string wpDbPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "worddb.xml");
            if (File.Exists(wpDbPath)) WordDatabase.Load(wpDbPath);
            _predictor.PredictionsChanged += () => { if (IsHandleCreated) BeginInvoke((Action)ApplyWPTags); };
            _predictor.ShiftLatchChanged  += latch => { if (latch) LatchShiftForSentence(); else UnlatchShift(); };
            _predictor.InjectSend         += s => SendKeysHelper.Send(s);
            SendKeysHelper.SetUiControl(this);

            _layout = KeyLayout.BuildDefaultQwerty();
            BuildGearButton();
            BuildEditStrip();
            BuildToolbar();
            // Timer for deferred single-click in Edit mode
            RebuildAllButtons();

            void onLangChanged() { _meta.Language = Lang.CurrentCode; RefreshToolbarButtonLabels(); }
            Lang.LanguageChanged += onLangChanged;

            TryAutoLoad();

            ResizeEnd   += (s, e) =>
            {
                _window.WindowWidth  = Width;
                _window.WindowHeight = Height;
                AutoSave();
            };
            SizeChanged += (s, e) => LayoutButtons();
            Shown       += (s, e) => { LayoutButtons(); ForceTopMost(); _gearBtn.BringToFront(); _predictor.OnSentenceStart(); };
            Activated   += (s, e) => ForceTopMost();
            KeyDown     += OnFormKeyDown;
            RegisterFocusHook();
            FormClosing += (s, e) =>
            {
                Lang.LanguageChanged -= onLangChanged;
                if (_hookHandle != IntPtr.Zero) { UnhookWinEvent(_hookHandle); _hookHandle = IntPtr.Zero; }
                _toolTip?.Dispose();
                AutoSave();
                _lastGearFont?.Dispose();
                foreach (var f in _fontCache.Values) f.Dispose();
                _fontCache.Clear();
                // Dispose all remaining buttons
                foreach (var btn in _buttons.Values) btn.Dispose();
                _buttons.Clear();
            };
        }

        // ── Drag-to-move (used when title bar is hidden) ───────────────
        // Standard Windows technique: send WM_NCLBUTTONDOWN/HTCAPTION so the
        // OS moves the window exactly as if the user clicked the title bar.
        // This does not conflict with button Click events and works on all
        // controls regardless of mouse capture.

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION        = 2;

        /// <summary>
        /// Shows or hides the Windows title bar based on <see cref="WindowState.HideTitlebar"/>.
        /// When hidden, <see cref="ForwardMouseEvents"/> lets the user drag the window by
        /// clicking on the toolbar or empty key areas.
        /// </summary>
        private void ApplyTitlebarState()
        {
            bool hide = _window.HideTitlebar;
            FormBorderStyle = hide ? FormBorderStyle.None : FormBorderStyle.Sizable;
            // Both MaximizeBox and MinimizeBox must be true for the minimize
            // button to appear in the title bar. We allow maximize here —
            // the user can drag the keyboard back to their preferred size.
            MaximizeBox = !hide;
            MinimizeBox = !hide;
            ForceTopMost();
        }

        /// <summary>
        /// Creates the small gear (⚙) button that floats over the keyboard grid.
        /// <para>
        /// The gear button has three distinct behaviours depending on context:
        /// <list type="bullet">
        ///   <item><b>Left-click</b> — toggles Edit mode (or, when HoldToEdit is on, is
        ///   handled by a 1-second hold timer instead).</item>
        ///   <item><b>Right-click</b> — shows the gear context menu (currently: move gear).</item>
        ///   <item><b>Drag in Edit mode</b> — repositions the gear overlay to a different cell.</item>
        ///   <item><b>Drag in Normal mode</b> — drags the entire window.</item>
        /// </list>
        /// </para>
        /// <para>
        /// Note: the button is NOT added to <c>Controls</c> here. It is added at the end of
        /// <see cref="RebuildAllButtons"/> so it is always the last (topmost) control.
        /// </para>
        /// </summary>
        private void BuildGearButton()
        {
            _gearBtn = new NoActivateButton
            {
                Text      = "⚙",
                FlatStyle = FlatStyle.Flat,
                TabStop   = false,
                Margin    = new Padding(0),
                BackColor = _gearNormalBg,
                ForeColor = _gearNormalFg,
                Font      = new Font("Segoe UI", 10f),
            };
            _gearBtn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 120);
            _gearBtn.FlatAppearance.BorderSize  = 1;
            _gearBtn.AllowDrop = true;

            // Left-click toggles edit mode (immediate), unless HoldToEdit is on —
            // in that case the Click event is suppressed and the timer handles it.
            _gearBtn.Click      += (s, e) =>
            {
                if (_meta.HoldToEdit) return;   // handled by _holdTimer instead
                SetMode(_mode == Mode.Edit ? Mode.Normal : Mode.Edit);
            };
            _gearBtn.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) ShowGearMenu(); };

            // Hold-to-edit timer: fires after 1 second when HoldToEdit setting is on.
            _holdTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _holdTimer.Tick += (s, e) =>
            {
                _holdTimer.Stop();
                _gearBtn.BackColor = _mode == Mode.Edit ? _gearEditBg : _gearNormalBg; // restore colour
                SetMode(_mode == Mode.Edit ? Mode.Normal : Mode.Edit);
            };

            // Dual-mode drag handler:
            //   Edit mode  → drag the gear button to a new cell (DoDragDrop with GearDragToken)
            //   Other modes → drag the whole window (WM_NCLBUTTONDOWN on HTCAPTION)
            bool   gearArming  = false;
            Point  gearDownScr = Point.Empty;

            _gearBtn.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                gearArming  = true;
                gearDownScr = _gearBtn.PointToScreen(e.Location);
                if (_meta.HoldToEdit)
                {
                    // Visual feedback: darken button while the user holds it down.
                    _gearBtn.BackColor = Color.FromArgb(80, 80, 110);
                    _holdTimer.Start();
                }
            };
            _gearBtn.MouseMove += (s, e) =>
            {
                if (!gearArming || e.Button != MouseButtons.Left) { gearArming = false; return; }
                var cur = _gearBtn.PointToScreen(e.Location);
                if (Math.Abs(cur.X - gearDownScr.X) < 4 &&
                    Math.Abs(cur.Y - gearDownScr.Y) < 4) return;
                gearArming = false;
                _holdTimer.Stop();                              // cancel hold if user dragged
                _gearBtn.BackColor = _mode == Mode.Edit ? _gearEditBg : _gearNormalBg;
                if (_mode == Mode.Edit)
                {
                    // Reposition gear by dragging — dropped cell becomes new gear home.
                    _gearBtn.DoDragDrop(_gearDragSentinel, DragDropEffects.Move);
                }
                else
                {
                    // Move the whole window (same as ForwardMouseEvents behaviour).
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
                }
            };
            _gearBtn.MouseUp += (s, e) =>
            {
                gearArming = false;
                if (_holdTimer.Enabled)                        // released before 1 s — cancel
                {
                    _holdTimer.Stop();
                    _gearBtn.BackColor = _mode == Mode.Edit ? _gearEditBg : _gearNormalBg;
                }
            };

            // NOTE: _gearBtn is NOT added to Controls here.
            // It is re-added at the end of RebuildAllButtons so it is always
            // the last control added — guaranteeing it sits on top in z-order.
        }

        /// <summary>
        /// Creates the thin coloured bar docked to the bottom of the window.
        /// It is hidden in Normal mode and shown in Edit / GearPlacement mode as a
        /// visual indicator that the keyboard is not in its typing state.
        /// </summary>
        private void BuildEditStrip()
        {
            _editStrip = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 5,
                Visible   = false,
                BackColor = Color.Orange,
            };
            Controls.Add(_editStrip);
        }

        /// <summary>
        /// Creates the two toolbar panels that appear when Edit mode is active.
        /// <list type="bullet">
        ///   <item><c>_toolbar</c> (row 1) — file operations and mode-switch buttons.</item>
        ///   <item><c>_toolbarEdit</c> (row 2) — key/grid editing actions, visible only in Edit mode.</item>
        /// </list>
        /// Both panels use <c>DockStyle.None</c> and are positioned manually by
        /// <see cref="LayoutButtons"/> to guarantee a fixed top-to-bottom order.
        /// </summary>
        private void BuildToolbar()
        {
            _toolTip = new ToolTip { ShowAlways = true, AutoPopDelay = 6000, InitialDelay = 400, ReshowDelay = 200 };

            _toolbar = new Panel
            {
                Height    = 54,
                Dock      = DockStyle.None,   // positioned manually in LayoutButtons
                BackColor = Fluent.DarkBg,
                Visible   = false,
            };
            ForwardMouseEvents(_toolbar);
            BuildToolbarButtons();

            _toolbarEdit = new Panel
            {
                Height    = 54,
                Dock      = DockStyle.None,   // positioned manually in LayoutButtons
                BackColor = Fluent.DarkBg2,
                Visible   = false,
            };
            ForwardMouseEvents(_toolbarEdit);
            BuildToolbarEditButtons();

            // Both panels use DockStyle.None — LayoutButtons() positions them explicitly:
            //   _toolbar    at y=0                      (row 1: file/mode buttons)
            //   _toolbarEdit at y=_toolbar.Height       (row 2: key/grid actions)
            // This avoids the WinForms DockStyle.Top stacking-order ambiguity entirely.
            Controls.Add(_toolbar);
            Controls.Add(_toolbarEdit);
        }

        /// <summary>
        /// Splits a translated label at the first space so the emoji sits on its
        /// own line above the text: "✏ Edit" → "✏\nEdit".
        /// Labels without a space are returned unchanged.
        /// </summary>
        private static string TwoLine(string s)
        {
            int sp = s.IndexOf(' ');
            return sp < 0 ? s : s.Substring(0, sp) + "\n" + s.Substring(sp + 1);
        }

        /// <summary>
        /// Creates all buttons for toolbar row 1 (Load, Save, Undo, Redo, Edit, Keyboard, Exit)
        /// and the filename label, wires their click handlers, and subscribes to the panel's
        /// Resize event so buttons stay correctly positioned when the window is resized.
        /// </summary>
        private void BuildToolbarButtons()
        {
            ToolbarButton MakeBtn(string icon, string label)
            {
                var b = new ToolbarButton { IconGlyph = icon, Text = label };
                _toolbar.Controls.Add(b);
                return b;
            }

            _btnLoad         = MakeBtn(FIcon.Load,     Lang.T("tb: Load"));
            _btnSave         = MakeBtn(FIcon.Save,     Lang.T("tb: Save"));
            _btnUndo         = MakeBtn(FIcon.Undo,     Lang.T("tb: Undo"));
            _btnRedo         = MakeBtn(FIcon.Redo,     Lang.T("tb: Redo"));
            _btnEdit         = MakeBtn(FIcon.Edit,     Lang.T("tb: Edit"));
            _btnEditKeyboard = MakeBtn(FIcon.Settings, Lang.T("tb: Keyboard"));
            _btnExitEdit     = MakeBtn(FIcon.Exit,     Lang.T("tb: Exit"));

            _lblFilename = new Label
            {
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(160, 170, 200),
                BackColor = Color.Transparent,
                Font      = new Font("Segoe UI", 11f),
                AutoSize  = false,
            };
            _toolbar.Controls.Add(_lblFilename);

            _btnLoad.Click         += (s, e) => LoadSettings();
            _btnSave.Click         += (s, e) => SaveSettings(false);
            _btnUndo.Click         += (s, e) => Undo();
            _btnRedo.Click         += (s, e) => Redo();
            _btnEdit.Click         += (s, e) => SetMode(Mode.Edit);
            _btnEditKeyboard.Click += (s, e) => OpenKeyboardEditor();
            _btnExitEdit.Click     += (s, e) => SetMode(Mode.Normal);

            _toolbar.Resize += (s, e) => PositionToolbarControls();
        }

        /// <summary>
        /// Lays out the row-1 toolbar buttons and filename label within the toolbar panel.
        /// Load/Save/Undo/Redo are anchored to the left; Exit/Keyboard/Edit are anchored to
        /// the right; the filename label fills the space between them.
        /// </summary>
        private void PositionToolbarControls()
        {
            const int h = 48, y = 3, gap = 2;
            int W = _toolbar.ClientSize.Width;
            if (W < 50) return;

            // Left side: Load, Save, Undo, Redo
            int lx = 2;
            _btnLoad.SetBounds(lx, y, 60, h); lx += 60 + gap;
            _btnSave.SetBounds(lx, y, 60, h); lx += 60 + gap;
            _btnUndo.SetBounds(lx, y, 64, h); lx += 64 + gap;
            _btnRedo.SetBounds(lx, y, 64, h); lx += 64 + gap;

            // Right side (from right edge inward): Exit, Keyboard, Edit
            int rx = W - 2;
            rx -= 65;       _btnExitEdit    .SetBounds(rx, y, 65,  h);
            rx -= gap + 88; _btnEditKeyboard.SetBounds(rx, y, 88,  h);
            rx -= gap + 72; _btnEdit        .SetBounds(rx, y, 72,  h);

            // Filename label fills the middle gap
            int lblX = lx + 4;
            int lblW = Math.Max(0, rx - gap - lblX);
            _lblFilename.SetBounds(lblX, y, lblW, h);
        }

        /// <summary>
        /// Creates all buttons for toolbar row 2 (key actions on the left, grid actions on the
        /// right, selected-key info label in the middle), wires their click handlers, and
        /// sets initial tooltip text.
        /// </summary>
        private void BuildToolbarEditButtons()
        {
            ToolbarButton MakeBtn(string icon, string label)
            {
                var b = new ToolbarButton { IconGlyph = icon, Text = label };
                _toolbarEdit.Controls.Add(b);
                return b;
            }

            // ── Key action buttons (left) ──────────────────────────────
            _btnKeyEdit   = MakeBtn(FIcon.Edit,   Lang.T("tb: Edit key"));
            _btnKeyRemove = MakeBtn(FIcon.Delete,  Lang.T("tb: Remove"));
            _btnCopyFmt   = MakeBtn(FIcon.Brush,  Lang.T("tb: Copy fmt"));
            _btnCopyKey   = MakeBtn(FIcon.Copy,   Lang.T("tb: Copy key"));

            // ── Selected key label (middle, flexible) ──────────────────
            _lblSelectedKey = new Label
            {
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(160, 170, 200),
                BackColor = Color.Transparent,
                Font      = new Font("Segoe UI", 10f),
                AutoSize  = false,
                Text      = "—",
            };
            _toolbarEdit.Controls.Add(_lblSelectedKey);

            // ── Grid action buttons (right) ────────────────────────────
            _btnAddRowAbove = MakeBtn(FIcon.ArrowUp,    Lang.T("tb: Row"));
            _btnAddRowBelow = MakeBtn(FIcon.ArrowDown,  Lang.T("tb: Row"));
            _btnAddColLeft  = MakeBtn(FIcon.ArrowLeft,  Lang.T("tb: Col"));
            _btnAddColRight = MakeBtn(FIcon.ArrowRight, Lang.T("tb: Col"));
            _btnRemoveRow   = MakeBtn(FIcon.Remove,     Lang.T("tb: Del row"));
            _btnRemoveCol   = MakeBtn(FIcon.Remove,     Lang.T("tb: Del col"));
            _btnMergeRight  = MakeBtn(FIcon.Merge,      Lang.T("tb: Merge R"));
            _btnMergeDown   = MakeBtn(FIcon.Merge,      Lang.T("tb: Merge D"));
            _btnSplitCell   = MakeBtn(FIcon.Split,      Lang.T("tb: Split"));

            // ── Wire key action handlers ───────────────────────────────
            _btnKeyEdit.Click += (s, e) =>
            {
                if (_selectedCell != null) OpenEditor(_selectedCell);
            };
            _btnKeyRemove.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                var cell = _selectedCell;
                if (cell.ColSpan > 1 || cell.RowSpan > 1)
                {
                    FillFreedSpanCells(cell, 1, 1);
                    cell.ColSpan = 1; cell.RowSpan = 1;
                }
                cell.Props = new KeyProps("", "");
                _latchedMods.Remove(cell); _lockedMods.Remove(cell);
                _selectedCell = null;
                NormaliseWPSlots();
                LayoutButtons(); RefreshAllButtons(skipFontCalc: true); SyncPredictorSlotCount(); AutoSave();
            };
            _btnCopyFmt.Click += (s, e) =>
            {
                if (_fmtPaintMode)
                {
                    // Second click cancels paint mode
                    _fmtPaintMode = false;
                    UpdatePaintModeCursors();
                    RefreshToolbarEditState();
                    return;
                }
                if (_selectedCell == null) return;
                var p = _selectedCell.Props;
                _copiedFormatting = new KeyProps("", "")
                {
                    FontName = p.FontName, FontSize = p.FontSize,
                    FontColor = p.FontColor, KeyColor = p.KeyColor,
                    BorderColor = p.BorderColor, BorderThickness = p.BorderThickness,
                    GroupName = p.GroupName,
                };
                _fmtPaintMode = true;
                UpdatePaintModeCursors();
                RefreshToolbarEditState();
            };
            _btnCopyKey.Click += (s, e) =>
            {
                if (_keyPaintMode)
                {
                    _keyPaintMode = false;
                    UpdateKeyPaintModeCursors();
                    RefreshToolbarEditState();
                    return;
                }
                if (_selectedCell == null) return;
                _copiedKey = _selectedCell.Props.Clone();
                _keyPaintMode = true;
                UpdateKeyPaintModeCursors();
                RefreshToolbarEditState();
            };

            // ── Wire grid action handlers ──────────────────────────────
            _btnAddRowAbove.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                _layout.InsertRow(_selectedCell.Row, true,  _theme); RebuildAllButtons(); AutoSave();
            };
            _btnAddRowBelow.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                _layout.InsertRow(_selectedCell.Row, false, _theme); RebuildAllButtons(); AutoSave();
            };
            _btnAddColLeft.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                _layout.InsertCol(_selectedCell.Col, true,  _theme); RebuildAllButtons(); AutoSave();
            };
            _btnAddColRight.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                _layout.InsertCol(_selectedCell.Col, false, _theme); RebuildAllButtons(); AutoSave();
            };
            _btnRemoveRow.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                if (_layout.RemoveRow(_selectedCell.Row)) { _selectedCell = null; RebuildAllButtons(); AutoSave(); }
            };
            _btnRemoveCol.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                if (_layout.RemoveCol(_selectedCell.Col)) { _selectedCell = null; RebuildAllButtons(); AutoSave(); }
            };
            _btnMergeRight.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                if (_layout.MergeRight(_selectedCell.Row, _selectedCell.Col)) { RebuildAllButtons(); AutoSave(); }
            };
            _btnMergeDown.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                if (_layout.MergeDown(_selectedCell.Row, _selectedCell.Col)) { RebuildAllButtons(); AutoSave(); }
            };
            _btnSplitCell.Click += (s, e) =>
            {
                if (_selectedCell == null) return;
                PushUndo();
                _layout.SplitCell(_selectedCell.Row, _selectedCell.Col, _theme);
                _selectedCell = null; RebuildAllButtons(); AutoSave();
            };

            _toolbarEdit.Resize += (s, e) => PositionToolbarEditControls();

            // Set initial tooltip text (also called on every language change)
            RefreshToolbarTooltips();
        }

        /// <summary>
        /// Lays out the row-2 toolbar buttons and selected-key label within the edit toolbar panel.
        /// Key-action buttons are anchored to the left; grid-action buttons to the right;
        /// the selected-key label fills the space between them.
        /// </summary>
        private void PositionToolbarEditControls()
        {
            const int h = 48, y = 3, gap = 2;
            int W = _toolbarEdit.ClientSize.Width;
            if (W < 100) return;

            // Left: key action buttons
            int lx = 2;
            _btnKeyEdit  .SetBounds(lx, y, 52, h); lx += 52 + gap;
            _btnKeyRemove.SetBounds(lx, y, 52, h); lx += 52 + gap;
            _btnCopyFmt  .SetBounds(lx, y, 58, h); lx += 58 + gap;
            _btnCopyKey  .SetBounds(lx, y, 58, h); lx += 58 + gap;

            // Right: grid action buttons (from right edge inward)
            int rx = W - 2;
            rx -= 54; _btnSplitCell  .SetBounds(rx, y, 54, h);
            rx -= gap + 52; _btnMergeDown  .SetBounds(rx, y, 52, h);
            rx -= gap + 52; _btnMergeRight .SetBounds(rx, y, 52, h);
            rx -= gap + 52; _btnRemoveCol  .SetBounds(rx, y, 52, h);
            rx -= gap + 52; _btnRemoveRow  .SetBounds(rx, y, 52, h);
            rx -= gap + 48; _btnAddColRight.SetBounds(rx, y, 48, h);
            rx -= gap + 48; _btnAddColLeft .SetBounds(rx, y, 48, h);
            rx -= gap + 48; _btnAddRowBelow.SetBounds(rx, y, 48, h);
            rx -= gap + 48; _btnAddRowAbove.SetBounds(rx, y, 48, h);

            // Middle: selected key label fills the remaining gap
            int lblX = lx + 4;
            int lblW = Math.Max(0, rx - 4 - lblX);
            _lblSelectedKey.SetBounds(lblX, y, lblW, h);
        }

        /// <summary>
        /// Re-reads all toolbar button labels and tooltips from the translation system
        /// and forces a repaint. Called whenever the UI language is changed at runtime.
        /// </summary>
        private void RefreshToolbarButtonLabels()
        {
            // Row 1 button labels
            if (_btnLoad         != null) _btnLoad.Text         = Lang.T("tb: Load");
            if (_btnSave         != null) _btnSave.Text         = Lang.T("tb: Save");
            if (_btnUndo         != null) _btnUndo.Text         = Lang.T("tb: Undo");
            if (_btnRedo         != null) _btnRedo.Text         = Lang.T("tb: Redo");
            if (_btnEdit         != null) _btnEdit.Text         = Lang.T("tb: Edit");
            if (_btnEditKeyboard != null) _btnEditKeyboard.Text = Lang.T("tb: Keyboard");
            if (_btnExitEdit     != null) _btnExitEdit.Text     = Lang.T("tb: Exit");

            // Row 2 — key actions
            if (_btnKeyEdit   != null) _btnKeyEdit.Text   = Lang.T("tb: Edit key");
            if (_btnKeyRemove != null) _btnKeyRemove.Text = Lang.T("tb: Remove");
            if (_btnCopyFmt   != null) _btnCopyFmt.Text   = Lang.T("tb: Copy fmt");
            if (_btnCopyKey   != null) _btnCopyKey.Text   = Lang.T("tb: Copy key");

            // Row 2 — grid actions
            if (_btnAddRowAbove != null) _btnAddRowAbove.Text = Lang.T("tb: Row");
            if (_btnAddRowBelow != null) _btnAddRowBelow.Text = Lang.T("tb: Row");
            if (_btnAddColLeft  != null) _btnAddColLeft.Text  = Lang.T("tb: Col");
            if (_btnAddColRight != null) _btnAddColRight.Text = Lang.T("tb: Col");
            if (_btnRemoveRow   != null) _btnRemoveRow.Text   = Lang.T("tb: Del row");
            if (_btnRemoveCol   != null) _btnRemoveCol.Text   = Lang.T("tb: Del col");
            if (_btnMergeRight  != null) _btnMergeRight.Text  = Lang.T("tb: Merge R");
            if (_btnMergeDown   != null) _btnMergeDown.Text   = Lang.T("tb: Merge D");
            if (_btnSplitCell   != null) _btnSplitCell.Text   = Lang.T("tb: Split");

            // Force repaint so new labels are visible immediately
            _toolbar?.Invalidate(true);
            _toolbarEdit?.Invalidate(true);

            // Tooltips
            RefreshToolbarTooltips();
        }

        /// <summary>
        /// Assigns translated tooltip strings to every toolbar button.
        /// Also called from <see cref="RefreshToolbarButtonLabels"/> so tooltips stay in
        /// sync when the language changes.
        /// </summary>
        private void RefreshToolbarTooltips()
        {
            if (_toolTip == null || _btnEdit == null) return;

            // Row 1
            _toolTip.SetToolTip(_btnLoad,      Lang.T("tip: Load"));
            _toolTip.SetToolTip(_btnSave,      Lang.T("tip: Save"));
            _toolTip.SetToolTip(_btnUndo,      Lang.T("tip: Undo"));
            _toolTip.SetToolTip(_btnRedo,      Lang.T("tip: Redo"));
            _toolTip.SetToolTip(_btnEdit,         Lang.T("tip: Edit mode"));
            _toolTip.SetToolTip(_btnEditKeyboard, Lang.T("tip: Edit Keyboard"));
            _toolTip.SetToolTip(_btnExitEdit,     Lang.T("tip: Exit edit mode"));

            if (_btnKeyEdit == null) return;

            // Row 2 — key actions
            _toolTip.SetToolTip(_btnKeyEdit,   Lang.T("tip: Edit key"));
            _toolTip.SetToolTip(_btnKeyRemove, Lang.T("tip: Remove key"));
            _toolTip.SetToolTip(_btnCopyFmt,   Lang.T("tip: Copy formatting"));
            _toolTip.SetToolTip(_btnCopyKey,   Lang.T("tip: Copy key"));

            // Row 2 — grid actions
            _toolTip.SetToolTip(_btnAddRowAbove, Lang.T("tip: Insert row above"));
            _toolTip.SetToolTip(_btnAddRowBelow, Lang.T("tip: Insert row below"));
            _toolTip.SetToolTip(_btnAddColLeft,  Lang.T("tip: Insert column left"));
            _toolTip.SetToolTip(_btnAddColRight, Lang.T("tip: Insert column right"));
            _toolTip.SetToolTip(_btnRemoveRow,   Lang.T("tip: Remove row"));
            _toolTip.SetToolTip(_btnRemoveCol,   Lang.T("tip: Remove column"));
            _toolTip.SetToolTip(_btnMergeRight,  Lang.T("tip: Merge right"));
            _toolTip.SetToolTip(_btnMergeDown,   Lang.T("tip: Merge down"));
            _toolTip.SetToolTip(_btnSplitCell,   Lang.T("tip: Split cell"));
        }

        // ── Rebuild all buttons ──────────────────────────────────────

        /// <summary>
        /// Discards all existing key buttons and creates fresh ones from the current layout.
        /// <para>
        /// This is the "nuclear option" — call it after loading a new layout or making
        /// structural changes (row/column insert/delete, span changes). For lighter updates
        /// (colour or label changes only) use <see cref="RefreshAllButtons"/> instead.
        /// </para>
        /// <para>
        /// The gear button is always added last so it stays on top in z-order regardless
        /// of how many key buttons exist.
        /// </para>
        /// </summary>
        private void RebuildAllButtons()
        {
            SuspendLayout();
            foreach (var btn in _buttons.Values) { Controls.Remove(btn); btn.Dispose(); }
            _buttons.Clear();
            // Clear font cache when rebuilding — old layout may have used different fonts
            foreach (var f in _fontCache.Values) f.Dispose();
            _fontCache.Clear();
            _latchedMods.RemoveWhere(c => !_layout.Cells.Contains(c));
            _lockedMods.RemoveWhere(c  => !_layout.Cells.Contains(c));

            bool shifted = ShiftActive, altGr = AltGrActive;
            foreach (var cell in _layout.Cells)
            {
                var btn = CreateButton(cell, shifted, altGr);
                _buttons[cell] = btn;
                Controls.Add(btn);
            }
            // Re-add gear button LAST so it is always on top in z-order.
            // Removing and re-adding is more reliable than BringToFront when
            // controls are added/removed dynamically.
            Controls.Remove(_gearBtn);
            Controls.Add(_gearBtn);
            ResumeLayout();
            if (IsHandleCreated) LayoutButtons();

            // Tell the predictor how many slots this layout actually uses so it
            // generates enough predictions (max slot index + 1, capped at 10).
            SyncPredictorSlotCount();
        }

        /// <summary>
        /// Scans the current layout for wp: keys and updates the predictor's
        /// slot count to match.  Call after any change that may add or remove
        /// word-prediction keys (rebuild, load, or key-editor save).
        /// </summary>
        private void SyncPredictorSlotCount()
        {
            int maxWpSlot = -1;
            foreach (var cell in _layout.Cells)
                if (cell.Props.Send != null &&
                    cell.Props.Send.StartsWith("wp:") &&
                    int.TryParse(cell.Props.Send.Substring(3), out int s) &&
                    s > maxWpSlot)
                    maxWpSlot = s;
            if (maxWpSlot >= 0)
                _predictor.SetSlotCount(maxWpSlot + 1);
        }

        /// <summary>
        /// Re-numbers wp: cells so slots are contiguous (0, 1, 2, …).
        /// Call after any edit that may remove or reorder word-prediction keys.
        /// </summary>
        private void NormaliseWPSlots()
        {
            var wpCells = _layout.Cells
                .Where(c => c.Props.Send != null &&
                            c.Props.Send.StartsWith("wp:", StringComparison.Ordinal) &&
                            int.TryParse(c.Props.Send.Substring(3), out _))
                .OrderBy(c => c.Row).ThenBy(c => c.Col)
                .ToList();
            for (int i = 0; i < wpCells.Count; i++)
                wpCells[i].Props.Send = "wp:" + i;
        }

        /// <summary>
        /// Creates a single keyboard button for the given <paramref name="cell"/> and
        /// wires all mouse event handlers needed for Normal, Edit, and GearPlacement modes.
        /// <para>
        /// Uses <see cref="NoActivateButton"/> so clicking the key never steals focus from
        /// the application the user is typing into.
        /// </para>
        /// <para>
        /// Drag-to-swap: in Edit mode, dragging beyond the system drag threshold starts a
        /// <c>DoDragDrop</c> operation that passes the source <see cref="GridCell"/> as data.
        /// The corresponding <c>DragDrop</c> handler on the target button calls
        /// <see cref="SwapCells"/>.
        /// </para>
        /// </summary>
        /// <param name="cell">The grid cell this button represents.</param>
        /// <param name="shifted">Whether Shift is currently active (affects the displayed label).</param>
        /// <param name="altGr">Whether AltGr is currently active.</param>
        private Button CreateButton(GridCell cell, bool shifted, bool altGr)
        {
            var p   = cell.Props;
            // NoActivateButton overrides WM_MOUSEACTIVATE → MA_NOACTIVATE so
            // clicking a key never steals focus from the target application.
            var btn = new NoActivateButton
            {
                Text      = "",  // cleared by UpdateCornerTag; we owner-draw via OnButtonPaint
                FlatStyle = FlatStyle.Flat, TabStop = false, Margin = new Padding(0),
                AutoSize  = false,
                AllowDrop = true,  // needed for drag-to-swap in Edit mode
            };
            btn.FlatAppearance.BorderSize = 1;
            ApplyPropsToButton(btn, p, false);
            ApplyEmptyKeyStyle(btn, p);

            btn.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (_mode == Mode.Edit)
                {
                    _selectedCell = cell;
                    UpdateSelectedKeyLabel();

                    if (_fmtPaintMode)
                    {
                        ApplyFormatPainter(cell);
                        _fmtPaintMode = false;
                        UpdatePaintModeCursors();
                        RefreshToolbarEditState();
                        LayoutButtons(); RefreshAllButtons(skipFontCalc: true);
                        return;
                    }
                    if (_keyPaintMode)
                    {
                        if (_copiedKey != null)
                        {
                            PushUndo();
                            cell.Props = _copiedKey.Clone();
                            _latchedMods.Remove(cell); _lockedMods.Remove(cell);
                            NormaliseWPSlots();
                            SyncPredictorSlotCount(); AutoSave();
                        }
                        _keyPaintMode = false;
                        UpdateKeyPaintModeCursors();
                        RefreshToolbarEditState();
                        LayoutButtons(); RefreshAllButtons(skipFontCalc: true);
                        return;
                    }

                    RefreshAllButtons();        // redraws selection border
                    RefreshToolbarEditState();  // updates enabled states

                    if (e.Clicks == 2)
                    {
                        _dragCandidate = null;
                        OpenEditor(cell);
                    }
                    else
                    {
                        // Record as drag candidate; actual drag starts on MouseMove
                        // once the system drag threshold is crossed.
                        _dragCandidate = cell;
                        _dragStartPt   = btn.PointToScreen(e.Location);
                    }
                }
                else
                {
                    OnKeyClick(cell);
                }
            };

            btn.MouseMove += (s, e) =>
            {
                if (_mode != Mode.Edit) return;
                if (e.Button != MouseButtons.Left) return;
                if (_dragCandidate == null) return;

                // Only start drag once mouse has moved beyond the system threshold
                var cur = btn.PointToScreen(e.Location);
                var sz  = SystemInformation.DragSize;
                if (Math.Abs(cur.X - _dragStartPt.X) < sz.Width &&
                    Math.Abs(cur.Y - _dragStartPt.Y) < sz.Height) return;

                var src = _dragCandidate;
                _dragCandidate = null;

                // DoDragDrop blocks until drop or cancel; pass the source GridCell as data
                btn.DoDragDrop(src, DragDropEffects.Move);
            };

            btn.MouseUp += (s, e) => { _dragCandidate = null; };

            // ── Drop target events ────────────────────────────────────
            btn.DragEnter += (s, e) =>
            {
                if (_mode != Mode.Edit) { e.Effect = DragDropEffects.None; return; }

                bool isGear = e.Data.GetDataPresent(typeof(GearDragToken));
                bool isKey  = e.Data.GetDataPresent(typeof(GridCell));

                if (!isGear && !isKey) { e.Effect = DragDropEffects.None; return; }
                if (isKey)
                {
                    var src = (GridCell)e.Data.GetData(typeof(GridCell));
                    if (src == cell) { e.Effect = DragDropEffects.None; return; }
                }

                e.Effect = DragDropEffects.Move;
                // Highlight the drop target with a bright border
                btn.FlatAppearance.BorderColor = Color.Gold;
                btn.FlatAppearance.BorderSize  = 3;
                btn.Invalidate();
            };

            btn.DragLeave += (s, e) =>
            {
                // Restore the button's normal appearance
                ApplyPropsToButton(btn, cell.Props, _latchedMods.Contains(cell), _lockedMods.Contains(cell));
                ApplyEmptyKeyStyle(btn, cell.Props);
                btn.Invalidate();
            };

            btn.DragDrop += (s, e) =>
            {
                // Restore border first (DragLeave does not fire when drop succeeds)
                ApplyPropsToButton(btn, cell.Props, false);
                ApplyEmptyKeyStyle(btn, cell.Props);

                if (_mode != Mode.Edit) return;

                // Gear-reposition drop: move gear to this cell, stay in Edit mode.
                if (e.Data.GetDataPresent(typeof(GearDragToken)))
                {
                    _meta.GearRow = cell.Row;
                    _meta.GearCol = cell.Col;
                    LayoutButtons();       // reposition gear overlay immediately
                    RefreshAllButtons(skipFontCalc: true);
                    AutoSave();
                    return;
                }

                if (!e.Data.GetDataPresent(typeof(GridCell))) return;
                var src = (GridCell)e.Data.GetData(typeof(GridCell));
                if (src == null || src == cell) return;

                SwapCells(src, cell);
            };

            btn.Paint += OnButtonPaint;
            return btn;
        }

        /// <summary>
        /// <summary>
        /// Removes any cells (and their buttons) that fall inside <paramref name="cell"/>'s
        /// current span, excluding the cell itself.
        /// Call this AFTER setting the new (larger) span values so the correct area is swept.
        /// This is the mirror of <see cref="FillFreedSpanCells"/>: where that method creates
        /// cells when a span shrinks, this one removes them when a span grows.
        /// </summary>
        private void AbsorbCoveredCells(GridCell cell)
        {
            var toRemove = new List<GridCell>();
            foreach (var other in _layout.Cells)
            {
                if (other == cell) continue;
                if (other.Row >= cell.Row && other.Row < cell.Row + cell.RowSpan &&
                    other.Col >= cell.Col && other.Col < cell.Col + cell.ColSpan)
                    toRemove.Add(other);
            }
            foreach (var r in toRemove)
            {
                _layout.Cells.Remove(r);
                if (_buttons.TryGetValue(r, out var oldBtn))
                {
                    Controls.Remove(oldBtn);
                    oldBtn.Dispose();
                    _buttons.Remove(r);
                }
                _latchedMods.Remove(r);
                _lockedMods.Remove(r);
            }
        }

        /// <summary>
        /// Fills any grid positions that fall inside <paramref name="cell"/>'s current span
        /// but would be outside a new span of <paramref name="newColSpan"/> × <paramref name="newRowSpan"/>
        /// with fresh empty GridCells + buttons.  Call this BEFORE shrinking the span.
        /// </summary>
        private void FillFreedSpanCells(GridCell cell, int newColSpan, int newRowSpan)
        {
            bool shifted = ShiftActive, altGr = AltGrActive;

            for (int dr = 0; dr < cell.RowSpan; dr++)
            {
                for (int dc = 0; dc < cell.ColSpan; dc++)
                {
                    if (dr == 0 && dc == 0) continue;           // top-left stays with the cell
                    if (dr < newRowSpan && dc < newColSpan) continue; // still inside new span

                    int nr = cell.Row + dr;
                    int nc = cell.Col + dc;
                    if (nr >= _layout.Rows || nc >= _layout.Cols) continue;

                    // Don't double-create if a cell already exists at this position
                    bool exists = false;
                    foreach (var c in _layout.Cells)
                        if (c.Row == nr && c.Col == nc) { exists = true; break; }
                    if (exists) continue;

                    var emptyCell = new GridCell(nr, nc, new KeyProps("", ""), 1, 1);
                    _layout.Cells.Add(emptyCell);

                    var newBtn = CreateButton(emptyCell, shifted, altGr);
                    _buttons[emptyCell] = newBtn;
                    Controls.Add(newBtn);
                    Controls.SetChildIndex(_gearBtn, 0); // keep gear on top
                }
            }
        }

        /// <summary>
        /// Swaps the content of two grid cells.
        /// If the source has a span larger than 1×1 it is first shrunk to 1×1.
        /// Clears all modifier latch/lock state since a modifier key may have moved.
        /// </summary>
        private void SwapCells(GridCell src, GridCell tgt)
        {
            PushUndo();
            // Resize source to 1×1 before swapping if it currently spans multiple cells.
            // The extra positions that were covered by the span become independent empty
            // cells; create a GridCell + Button for each so they remain reachable.
            if (src.ColSpan > 1 || src.RowSpan > 1)
            {
                FillFreedSpanCells(src, 1, 1);
                src.ColSpan = 1;
                src.RowSpan = 1;
            }

            // Swap Props
            var tmpProps  = src.Props;
            src.Props     = tgt.Props;
            tgt.Props     = tmpProps;

            // Swap spans (source is already 1×1; target keeps its original span)
            (src.ColSpan, tgt.ColSpan) = (tgt.ColSpan, src.ColSpan);
            (src.RowSpan, tgt.RowSpan) = (tgt.RowSpan, src.RowSpan);

            // src has inherited tgt's original span and may now overlap empty cells
            // that FillFreedSpanCells previously created. Remove them.
            AbsorbCoveredCells(src);

            // A modifier key may have moved to a different cell — clear all state
            _latchedMods.Clear();
            _lockedMods.Clear();

            LayoutButtons();
            RefreshAllButtons(skipFontCalc: true);
            AutoSave();
        }

        /// <summary>
        /// Attaches drag-to-move to any child control using the WM_NCLBUTTONDOWN
        /// technique. A 4px movement threshold separates drags from clicks, so
        /// button Click events still fire normally on short presses.
        /// Works whether the title bar is shown or hidden.
        /// </summary>
        private void ForwardMouseEvents(Control ctrl)
        {
            bool   arming  = false;
            Point  downScr = Point.Empty;

            ctrl.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                arming  = true;
                downScr = ctrl.PointToScreen(e.Location);
            };
            ctrl.MouseMove += (s, e) =>
            {
                if (!arming) return;
                var cur = ctrl.PointToScreen(e.Location);
                if (Math.Abs(cur.X - downScr.X) < 4 &&
                    Math.Abs(cur.Y - downScr.Y) < 4) return;
                // Movement threshold exceeded — initiate system window move.
                arming = false;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            };
            ctrl.MouseUp += (s, e) => { arming = false; };
        }

        /// <summary>
        /// Empty keys (no Label and no Send): invisible/disabled in Normal mode,
        /// visible gray placeholder in Edit mode.
        /// </summary>
        private bool IsEmptyKey(KeyProps p) =>
            string.IsNullOrEmpty(p.Label) && string.IsNullOrEmpty(p.Send) &&
            !KeyLayout.ModifierLabels.Contains(p.Label);

        private void ApplyEmptyKeyStyle(Button btn, KeyProps p)
        {
            if (!IsEmptyKey(p)) return;
            if (_mode == Mode.Edit)
            {
                btn.Visible   = true;   // restore visibility — btn may have been hidden in Normal mode
                btn.Enabled   = true;
                btn.BackColor = Color.FromArgb(55, 55, 75);
                btn.ForeColor = Color.FromArgb(110, 110, 130);
                btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 100);
                btn.FlatAppearance.BorderSize  = 1;
                btn.Text = "";
            }
            else if (_mode == Mode.GearPlacement)
            {
                // Empty cells must be visible and clickable so the user can
                // place the gear button on them.  The blue highlight is applied
                // by RefreshAllButtons; just ensure the button is shown.
                btn.Visible = true;
                btn.Enabled = true;
                btn.Text    = "";
            }
            else
            {
                // Make the blank cell button fully invisible — the gear button
                // is added on top of it as the last control in Controls, but
                // a disabled FlatStyle button still paints itself and can
                // obscure whatever is beneath it on some Windows themes.
                btn.Visible = false;
            }
        }

        // ── Layout ───────────────────────────────────────────────────
        // The gear button is a floating overlay, not part of the grid.
        // It is positioned over the designated "gear cell" (default: row 0, last column).
        // That cell is kept blank in the XML (Label="" Send="") so it acts
        // as a reserved slot — invisible in normal mode, the gear sits on top.
        /// <summary>
        /// Computes and assigns pixel bounds to every key button and the gear overlay.
        /// <para>
        /// The available space (window minus toolbar height and padding) is divided equally
        /// among all grid columns and rows. Multi-cell spans receive proportionally larger bounds.
        /// This method is called on every resize, mode change, and after structural edits.
        /// </para>
        /// </summary>
        private void LayoutButtons()
        {
            int rows = _layout.Rows, cols = _layout.Cols;
            if (rows == 0 || cols == 0) return;
            if (ClientSize.Width < 50 || ClientSize.Height < 50) return;

            // Position toolbar panels explicitly (DockStyle.None) so the row order is
            // always deterministic: _toolbar (row 1) at y=0, _toolbarEdit (row 2) below it.
            int th = 0;
            int W  = ClientSize.Width;
            if (_toolbar != null && _toolbar.Visible)
            {
                _toolbar.SetBounds(0, 0, W, _toolbar.Height);
                th += _toolbar.Height;
                PositionToolbarControls();
            }
            if (_toolbarEdit != null && _toolbarEdit.Visible)
            {
                _toolbarEdit.SetBounds(0, th, W, _toolbarEdit.Height);
                th += _toolbarEdit.Height;
                PositionToolbarEditControls();
            }

            // No extra column reserved — gear overlays the top-right grid cell
            int usableW = ClientSize.Width  - Pad * 2 - Gap * (cols - 1);
            int usableH = ClientSize.Height - th - Pad * 2 - Gap * (rows - 1);
            float cellW = Math.Max(8f, (float)usableW / cols);
            float cellH = Math.Max(8f, (float)usableH / rows);

            bool shifted = ShiftActive, altGr = AltGrActive;
            var placed = new HashSet<GridCell>();

            SuspendLayout();
            try
            {
                foreach (var cell in _layout.Cells)
                {
                    if (!_buttons.TryGetValue(cell, out var btn)) continue;
                    if (placed.Contains(cell)) continue;
                    placed.Add(cell);

                    int x = Pad + (int)(cell.Col * (cellW + Gap));
                    int y = th + Pad + (int)(cell.Row * (cellH + Gap));
                    int w = Math.Max(8, (int)(cell.ColSpan * cellW + (cell.ColSpan - 1) * Gap));
                    int h = Math.Max(8, (int)(cell.RowSpan * cellH + (cell.RowSpan - 1) * Gap));

                    btn.SetBounds(x, y, w, h);
                    if (w > 8 && h > 8)
                        btn.Region = Fluent.RoundedRegion(w, h, 4);
                    SetButtonFont(btn, cell.Props, h, w, shifted, altGr);
                    ApplyEmptyKeyStyle(btn, cell.Props);
                }
            }
            finally { ResumeLayout(false); }

            // Gear button: overlays the designated gear cell (default: row 0, last column)
            var (gRow, gCol) = GearCell(cols);
            int gearX = Pad + (int)(gCol * (cellW + Gap));
            int gearY = th + Pad + (int)(gRow * (cellH + Gap));
            int gearW = Math.Max(8, (int)(cellW));
            int gearH = Math.Max(8, (int)(cellH));
            _gearBtn.SetBounds(gearX, gearY, gearW, gearH);
            int gfs = Math.Max(7, Math.Min(16, (int)(gearH * 0.42)));
            _gearBtn.Font = GetGearFont(gfs);
            // Override WP key tags with predictions (after all UpdateCornerTag calls)
            ApplyWPTags();
            // Final guarantee: gear is always the topmost control.
            _gearBtn.BringToFront();
        }

        /// <summary>
        /// Picks the right font size for a key button and applies it.
        /// <para>
        /// Priority: per-key font size → group font size → auto-calculated size.
        /// Auto-calculation uses the button height as a starting point, applies a correction
        /// factor for upper-case letters, then measures the actual label width with
        /// <see cref="TextRenderer.MeasureText"/> and scales down if it would overflow.
        /// This avoids per-font empirical fudge factors — it works correctly for any font.
        /// </para>
        /// </summary>
        /// <param name="btn">The button whose font is being set.</param>
        /// <param name="p">The key's properties (font name, size, label).</param>
        /// <param name="btnH">Current button height in pixels.</param>
        /// <param name="btnW">Current button width in pixels.</param>
        /// <param name="shifted">Whether Shift is active (affects which label to measure).</param>
        /// <param name="altGr">Whether AltGr is active.</param>
        private void SetButtonFont(Button btn, KeyProps p, int btnH, int btnW,
                                   bool shifted, bool altGr)
        {
            string label = p.GetDisplayLabel(shifted, altGr);
            if (string.IsNullOrEmpty(label)) label = p.Label ?? "";

            var grpFont = FindGroup(p.GroupName);
            string fn   = ResolveFontName(p.FontName, grpFont?.FontName);

            int fs;
            if (p.FontSize > 0)
                fs = p.FontSize;
            else if (grpFont?.FontSize > 0)
                fs = grpFont.FontSize;
            else
            {
                // Step 1: height-based upper bound
                const float charH   = 1.35f;   // em + internal leading factor
                const int   vMargin =  8;       // button vertical padding (px)
                const int   hMargin = 14;       // button horizontal padding (px) — generous for any font

                float maxFsByHeight = (btnH - vMargin) / charH;
                float baseFs = Math.Min(btnH * 0.36f, maxFsByHeight);

                // Large symbol keys (⌫ ↵) may use more vertical room
                bool big = KeyLayout.LargeSymbolLabels.Contains(p.Label)
                        || KeyLayout.LargeSymbolLabels.Contains(p.ShiftLabel ?? "");
                if (big) baseFs = Math.Min(baseFs * 1.25f, maxFsByHeight);

                fs = Math.Max(6, (int)baseFs);

                // Step 2: measure the actual rendered label at that size and scale
                // down proportionally if it overflows the button width.
                // This works correctly for any font (Verdana, Arial, etc.) and any
                // label length — no per-font empirical constants needed.
                if (btnW > 0 && label.Length > 0 && fs > 6)
                {
                    try
                    {
                        using var probe = new Font(fn, fs, FontStyle.Bold);
                        int measuredW = TextRenderer.MeasureText(
                            label, probe,
                            new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                        int avail = btnW - hMargin;
                        if (measuredW > avail && measuredW > 0)
                            fs = Math.Max(6, (int)(fs * avail / (float)measuredW));
                    }
                    catch { }
                }
            }

            try { btn.Font = GetButtonFont(fn, fs); }
            catch { btn.Font = GetButtonFont("Arial", fs); }
            // Main label and corners are painted by OnButtonPaint via UpdateCornerTag
            UpdateCornerTag(btn, p, shifted, altGr);
        }

        /// <summary>
        /// Stores the main label, corner labels (Shift top-right, AltGr top-left), and font
        /// colour into the button's <c>Tag</c> property so <see cref="OnButtonPaint"/> can
        /// draw them without recalculating on every paint event.
        /// <para>
        /// Skips <c>Invalidate()</c> when nothing has changed to avoid redundant GDI paint
        /// passes — this matters because ~40 buttons are refreshed per LayoutButtons call.
        /// </para>
        /// </summary>
        private void UpdateCornerTag(Button btn, KeyProps p, bool shifted, bool altGr)
        {
            bool isMod = KeyLayout.ModifierLabels.Contains(p.Label);
            string sl = (!shifted && !altGr && !isMod) ? (p.ShiftLabel  ?? "") : "";
            string al = (!altGr             && !isMod) ? (p.AltGrLabel  ?? "") : "";
            // Use the raw label — Graphics.DrawString does NOT interpret & as an
            // accelerator (only WinForms text rendering does). BtnText's &&-escaping
            // is only needed when assigning to btn.Text, which we always set to "".
            string ml = p.GetDisplayLabel(shifted, altGr) ?? "";
            var grpTag = FindGroup(p.GroupName);
            Color fc = ResolveColor(p.FontColor, grpTag?.FontColor ?? Color.Empty, _theme.FontColor);

            // Skip Invalidate when nothing visible changed — avoids redundant GDI paint
            // passes on the ~40 buttons whose labels/colours are unchanged (e.g. selection
            // border moves, mode indicator updates, paired LayoutButtons+RefreshAllButtons).
            // The 5th element (isWP) is ignored for the equality check because UpdateCornerTag
            // is never called on WP buttons in the normal code path.
            if (btn.Tag is (string oml, string osl, string oal, Color ofc, bool _, int _) &&
                oml == ml && osl == sl && oal == al && ofc == fc)
                return;

            btn.Tag = (ml, sl, al, fc, false, 0);   // isWP=false, typedLen=0 for normal keys
            // Clear the button's own text so WinForms draws nothing — we paint it.
            if (btn.Text != "") btn.Text = "";
            btn.Invalidate();
        }

        // ── Owner-draw: main label + corner labels ───────────────────
        // TextRenderer.DrawText is used throughout because it does not
        // interpret "&" as a hotkey prefix unless explicitly asked — unlike
        // Graphics.DrawString which requires a StringFormat with HotkeyPrefix.None.
        // TextFormatFlags.NoPrefix suppresses all "&" interpretation entirely.

        /// <summary>
        /// Custom paint handler for every key button.  WinForms' default text rendering
        /// cannot draw corner labels, so we set <c>btn.Text = ""</c> and paint everything
        /// ourselves here.
        /// <para>
        /// Draws three strings per key:
        /// <list type="bullet">
        ///   <item><b>Main label</b> — centred (or right-aligned for word-prediction keys).</item>
        ///   <item><b>Shift label</b> — small, top-right corner, 60% opacity.</item>
        ///   <item><b>AltGr label</b> — small, top-left corner, 50% opacity.</item>
        /// </list>
        /// For word-prediction keys it also draws an underline beneath the part of the
        /// word the user has already typed so they can see how the prediction relates to
        /// their input.
        /// </para>
        /// </summary>
        private void OnButtonPaint(object sender, PaintEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not (string ml, string sl, string al, Color fc, bool isWP, int typedLen)) return;

            // ── Main label ────────────────────────────────────────────
            if (!string.IsNullOrEmpty(ml))
            {
                // WP keys: full-width rect — text is right-aligned so the left margin is
                // wasted space; the "…" may bleed into the left edge, which is acceptable.
                // Regular keys: keep a small inset so EndEllipsis has room to work.
                var mainRect = isWP
                    ? new Rectangle(0, 2, btn.Width,     btn.Height - 4)
                    : new Rectangle(2, 2, btn.Width - 4, btn.Height - 4);
                TextFormatFlags mainFlags;
                if (isWP)
                {
                    // Word-prediction keys: right-aligned; text is already tail-truncated
                    // by ApplyWPTags so no EndEllipsis needed.
                    mainFlags = TextFormatFlags.Right            |
                                TextFormatFlags.VerticalCenter   |
                                TextFormatFlags.SingleLine       |
                                TextFormatFlags.NoPrefix;
                }
                else
                {
                    mainFlags = TextFormatFlags.HorizontalCenter |
                                TextFormatFlags.VerticalCenter   |
                                TextFormatFlags.SingleLine       |
                                TextFormatFlags.EndEllipsis      |
                                TextFormatFlags.NoPrefix;         // NoPrefix: & is never a hotkey
                }
                TextRenderer.DrawText(e.Graphics, ml, btn.Font, mainRect, fc, mainFlags);

                // ── Underline for the still-visible typed prefix (WP keys only) ───────
                if (isWP && typedLen > 0)
                {
                    int typedStart = (ml.Length > 0 && ml[0] == '…') ? 1 : 0;
                    if (typedStart + typedLen <= ml.Length)
                    {
                        var   mf      = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                        var   inf     = new Size(int.MaxValue, int.MaxValue);
                        string afterTyped    = ml.Substring(typedStart + typedLen);
                        string typedAndAfter = ml.Substring(typedStart);   // typedPart + afterTyped

                        // Compute typedW as the DIFFERENCE of two measurements so the
                        // per-call MeasureText overhead (~8 px) cancels out, giving an
                        // accurate line length.  afterW uses a single call; a small
                        // absolute offset there only shifts the line, not its length.
                        int afterW      = Math.Max(0, TextRenderer.MeasureText(afterTyped,    btn.Font, inf, mf).Width - 8);
                        int combinedW   = Math.Max(0, TextRenderer.MeasureText(typedAndAfter, btn.Font, inf, mf).Width - 8);
                        int typedW      = Math.Max(0, combinedW - afterW);

                        // Text is right-aligned in mainRect — work backwards from right edge
                        int ulRight = mainRect.Right  - afterW;
                        int ulLeft  = ulRight - typedW;

                        // Underline at text baseline: vertically centred text, baseline ≈ centre + ½ fontH
                        int ulY = mainRect.Top + (mainRect.Height + btn.Font.Height) / 2;

                        using var pen = new Pen(Color.FromArgb(210, fc), 1f);
                        e.Graphics.DrawLine(pen, ulLeft, ulY, ulRight, ulY);
                    }
                }
            }

            // ── Corner labels (shift top-right, AltGr top-left) ──────
            const int margin     = 2;
            float     cornerSize = Math.Max(6f, btn.Font.Size * 0.55f);
            using var cf = new Font(btn.Font.FontFamily, cornerSize);

            if (!string.IsNullOrEmpty(sl))
            {
                var szS = TextRenderer.MeasureText(e.Graphics, sl, cf,
                              new Size(btn.Width, btn.Height), TextFormatFlags.NoPrefix);
                var rectS = new Rectangle(
                    btn.Width - szS.Width - margin, margin, szS.Width, szS.Height);
                TextRenderer.DrawText(e.Graphics, sl, cf, rectS,
                    Color.FromArgb(160, fc),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            if (!string.IsNullOrEmpty(al) && al != sl)
            {
                var szA = TextRenderer.MeasureText(e.Graphics, al, cf,
                              new Size(btn.Width, btn.Height), TextFormatFlags.NoPrefix);
                var rectA = new Rectangle(margin, margin, szA.Width, szA.Height);
                TextRenderer.DrawText(e.Graphics, al, cf, rectA,
                    Color.FromArgb(130, fc),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        // ── Font cache ────────────────────────────────────────────────
        // Creating a new Font object on every paint or resize call is expensive and
        // causes memory pressure. These caches ensure each (name, size) combination is
        // only allocated once and reused.

        /// <summary>
        /// Returns a cached <see cref="Font"/> for the gear button at the given size.
        /// Keeps only one font at a time since the gear button always uses the same family.
        /// </summary>
        private Font _lastGearFont; private int _lastGearFontSize = -1;
        private Font GetGearFont(int size)
        {
            if (_lastGearFont == null || _lastGearFontSize != size)
            { _lastGearFont?.Dispose(); _lastGearFont = new Font("Segoe UI", size); _lastGearFontSize = size; }
            return _lastGearFont;
        }
        private readonly Dictionary<(string,int),Font> _fontCache = new();

        /// <summary>
        /// Returns a cached bold <see cref="Font"/> for key buttons at the given family name
        /// and point size. Falls back to Arial if the requested family cannot be loaded.
        /// </summary>
        private Font GetButtonFont(string name, int size)
        {
            var key = (name ?? "Arial", size);
            if (!_fontCache.TryGetValue(key, out var f))
            {
                try { f = new Font(key.Item1, size, FontStyle.Bold); }
                catch { f = new Font("Arial", size, FontStyle.Bold); }
                _fontCache[key] = f;
            }
            return f;
        }

        // ── Refresh ───────────────────────────────────────────────────

        /// <summary>
        /// Refreshes every key button's colours, labels, and (optionally) font sizes.
        /// <para>
        /// Use <paramref name="skipFontCalc"/><c> = true</c> when <see cref="LayoutButtons"/>
        /// was just called — fonts were already computed there, so skipping the
        /// <see cref="TextRenderer"/> measurement pass avoids doing it twice.
        /// </para>
        /// </summary>
        /// <param name="skipFontCalc">
        /// When <c>true</c>, only colours and labels are updated (faster).
        /// When <c>false</c> (default), font sizes are also recalculated.
        /// </param>
        private void RefreshAllButtons(bool skipFontCalc = false)
        {
            bool shifted = ShiftActive, altGr = AltGrActive;
            SuspendLayout();
            try
            {
                foreach (var (cell, btn) in _buttons)
                {
                    var p = cell.Props;
                    bool isWP = p.Send != null &&
                                p.Send.StartsWith("wp:", StringComparison.Ordinal);
                    bool latched = _latchedMods.Contains(cell);
                    if (_mode == Mode.GearPlacement)
                    {
                        // Highlight all keys — user picks the new gear location
                        btn.BackColor = Color.FromArgb(50, 60, 160);
                        btn.ForeColor = Color.White;
                        btn.FlatAppearance.BorderColor = Color.FromArgb(120, 140, 255);
                        btn.FlatAppearance.BorderSize  = 2;
                        // SetButtonFont is skipped in this mode, so update tags directly
                        if (!isWP)
                            UpdateCornerTag(btn, p, shifted, altGr);
                    }
                    else
                    {
                        ApplyPropsToButton(btn, p, latched, _lockedMods.Contains(cell));
                        if (skipFontCalc)
                        {
                            // Font already set by the preceding LayoutButtons() call;
                            // just refresh the tag/label without re-measuring text.
                            // WP buttons are skipped — LayoutButtons() already ran ApplyWPTags().
                            if (!isWP)
                                UpdateCornerTag(btn, p, shifted, altGr);
                        }
                        else
                        {
                            // SetButtonFont calculates size AND calls UpdateCornerTag internally.
                            SetButtonFont(btn, p, btn.Height, btn.Width, shifted, altGr);
                        }
                        ApplyEmptyKeyStyle(btn, p);
                        // Selection highlight — white border over whatever style is applied
                        if (_mode == Mode.Edit && cell == _selectedCell)
                        {
                            btn.FlatAppearance.BorderColor = Color.White;
                            btn.FlatAppearance.BorderSize  = 2;
                        }
                    }
                }
            }
            finally { ResumeLayout(false); }
            // When skipFontCalc is true, LayoutButtons() already ran ApplyWPTags() —
            // no need to run it a second time.
            if (!skipFontCalc) ApplyWPTags();
        }

        /// <summary>Returns the gear button's grid row and column.</summary>
        private (int row, int col) GearCell(int cols)
        {
            int rows = _layout?.Rows ?? 1;
            int row = Math.Clamp(_meta.GearRow, 0, rows - 1);
            int col = _meta.GearCol < 0 ? cols - 1 : Math.Min(_meta.GearCol, cols - 1);
            return (row, col);
        }

        /// <summary>Resolve per-key properties against global defaults.</summary>
        private int ResolveThickness(int keyThickness) =>
            keyThickness == -1 ? _theme.BorderThickness : keyThickness;
        private int ResolveThickness(int keyThickness, int groupThickness) =>
            keyThickness  != -1 ? keyThickness  :
            groupThickness != -1 ? groupThickness :
            _theme.BorderThickness;
        private Color ResolveColor(Color keyColor, Color globalColor) =>
            keyColor.IsEmpty ? globalColor : keyColor;
        private Color ResolveColor(Color keyColor, Color groupColor, Color globalColor) =>
            !keyColor.IsEmpty  ? keyColor  :
            !groupColor.IsEmpty ? groupColor :
            globalColor;
        private string ResolveFontName(string keyFont, string groupFont = null) =>
            !string.IsNullOrEmpty(keyFont)  ? keyFont  :
            !string.IsNullOrEmpty(groupFont) ? groupFont :
            _theme.FontName;

        private KeyGroup FindGroup(string groupName) =>
            string.IsNullOrEmpty(groupName) ? null :
            _layout.Groups.Find(g => g.Name == groupName);

        /// <summary>
        /// Sets a button's background colour, foreground colour, and border colour/thickness
        /// by resolving the key's per-key properties against its group properties and the
        /// global theme.
        /// <para>
        /// Three visual states:
        /// <list type="bullet">
        ///   <item><b>Locked modifier</b> — strong amber; stays until clicked again.</item>
        ///   <item><b>Latched modifier</b> — moderate highlight; clears after the next regular key.</item>
        ///   <item><b>Normal</b> — colours from key/group/theme inheritance chain.</item>
        /// </list>
        /// </para>
        /// </summary>
        private void ApplyPropsToButton(Button btn, KeyProps p, bool latched, bool locked = false)
        {
            // Non-empty keys are always visible; a key may have transitioned from
            // empty (hidden in Normal mode) to having content, so restore explicitly.
            if (!IsEmptyKey(p)) btn.Visible = true;

            var grp = FindGroup(p.GroupName);
            Color gKc = grp?.KeyColor     ?? Color.Empty;
            Color gFc = grp?.FontColor    ?? Color.Empty;
            Color gBc = grp?.BorderColor  ?? Color.Empty;
            int   gBt = grp?.BorderThickness ?? -1;

            if (locked)
            {
                // Locked: strong amber — stays until clicked again
                btn.BackColor = Color.FromArgb(220, 140, 0);
                btn.ForeColor = Color.FromArgb(30, 30, 30);
                btn.FlatAppearance.BorderColor = Color.FromArgb(255, 200, 0);
                btn.FlatAppearance.BorderSize  = Math.Max(3, ResolveThickness(p.BorderThickness, gBt));
            }
            else if (latched)
            {
                // Latched: moderate highlight — clears after next key
                Color rKc = ResolveColor(p.KeyColor, gKc, _theme.KeyColor);
                Color rFc = ResolveColor(p.FontColor, gFc, _theme.FontColor);
                btn.BackColor = AdjustBrightness(rKc, IsLight(rKc) ? -60 : 60);
                btn.ForeColor = AdjustBrightness(rFc, IsLight(rFc) ? -40 : 40);
                btn.FlatAppearance.BorderColor = IsLight(rKc)
                    ? Color.FromArgb(220,180,40) : Color.FromArgb(255,220,80);
                btn.FlatAppearance.BorderSize = Math.Max(2, ResolveThickness(p.BorderThickness, gBt));
            }
            else
            {
                btn.BackColor = ResolveColor(p.KeyColor,  gKc, _theme.KeyColor);
                btn.ForeColor = ResolveColor(p.FontColor, gFc, _theme.FontColor);
                btn.FlatAppearance.BorderColor = ResolveColor(p.BorderColor, gBc, _theme.BorderColor);
                btn.FlatAppearance.BorderSize  = ResolveThickness(p.BorderThickness, gBt);
            }
        }

        /// <summary>Returns true if the colour is perceptually light (luminance &gt; 50%).</summary>
        private static bool IsLight(Color c) => (c.R*299+c.G*587+c.B*114)/1000 > 128;

        /// <summary>Shifts each RGB channel by <paramref name="d"/>, clamped to [0,255].</summary>
        private static Color AdjustBrightness(Color c, int d) =>
            Color.FromArgb(Math.Clamp(c.R+d,0,255),Math.Clamp(c.G+d,0,255),Math.Clamp(c.B+d,0,255));

        /// <summary>
        /// Escapes a string for use as a WinForms button label: doubles every "&amp;" so
        /// it is rendered as a literal ampersand rather than treated as an accelerator prefix.
        /// </summary>
        private static string BtnText(string s) => (s ?? "").Replace("&","&&");

        // ── Gear menu ─────────────────────────────────────────────────
        // The gear button's right-click context menu. Currently it has one entry:
        // "Move gear button…" which enters GearPlacement mode.

        private static readonly Font   MenuFont = new Font("Segoe UI", 11.5f);
        private static readonly Color  MenuBg   = ColorTranslator.FromHtml("#1A1A2E");
        private static readonly Color  MenuFg   = ColorTranslator.FromHtml("#E0E0FF");

        /// <summary>Shows the gear button's right-click context menu below the gear button.</summary>
        private void ShowGearMenu()
        {
            var menu = StyledMenu();
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            MI(menu, Lang.T("📌 Move gear button…"), StartGearPlacement);
            menu.Show(_gearBtn, new Point(0, _gearBtn.Height));
        }

        /// <summary>Creates a dark-themed <see cref="ContextMenuStrip"/> styled to match the toolbar.</summary>
        private ContextMenuStrip StyledMenu()
        {
            var m = new ContextMenuStrip();
            m.BackColor = MenuBg; m.ForeColor = MenuFg; m.Font = MenuFont;
            return m;
        }

        /// <summary>Adds a styled menu item to the given context menu strip.</summary>
        private void MI(ContextMenuStrip m, string text, Action action)
        {
            var item = new ToolStripMenuItem(text)
            { BackColor = MenuBg, ForeColor = MenuFg, Font = MenuFont };
            item.Click += (s, e) => action();
            m.Items.Add(item);
        }

        // ── Key click ─────────────────────────────────────────────────

        /// <summary>
        /// Dispatches a key-button click to the appropriate handler based on the current mode.
        /// In Edit mode, clicks are handled by <see cref="CreateButton"/>'s MouseDown handler
        /// (selection / format painter) and never reach this method.
        /// </summary>
        private void OnKeyClick(GridCell cell)
        {
            switch (_mode)
            {
                case Mode.Normal:        HandleNormalClick(cell);   break;
                case Mode.GearPlacement: FinishGearPlacement(cell); break;
            }
        }

        /// <summary>
        /// Handles a key press in Normal mode.
        /// <list type="bullet">
        ///   <item>Modifier keys (Shift, Ctrl, etc.) are toggled via <see cref="ToggleModifier"/>.</item>
        ///   <item>Word-prediction keys (Send = "wp:N") trigger <see cref="WordPredictor.OnWPClick"/>.</item>
        ///   <item>Layout-switch keys (Send starts with "layout:") load a different layout file.</item>
        ///   <item>All other keys resolve the correct send string (normal / shift / altgr),
        ///   clear active modifiers, and call <see cref="SendKeysHelper.Send"/>.</item>
        /// </list>
        /// </summary>
        private void HandleNormalClick(GridCell cell)
        {
            if (IsModifier(cell)) { ToggleModifier(cell); return; }

            // ── Word prediction key ───────────────────────────────────
            if (cell.Props.Send != null &&
                cell.Props.Send.StartsWith("wp:", StringComparison.Ordinal) &&
                int.TryParse(cell.Props.Send.Substring(3), out int wpSlot) &&
                wpSlot >= 0 && wpSlot < 10)
            {
                var wpResult = _predictor?.OnWPClick(wpSlot);
                if (wpResult != null)
                {
                    for (int i = 0; i < wpResult.Backspaces; i++)
                        SendKeysHelper.Send("{BACKSPACE}");
                    SendKeysHelper.Send(wpResult.Word + wpResult.Suffix);
                }
                return;
            }

            bool shift = AnyModifier("Shift"), ctrl = AnyModifier("Ctrl"),
                 alt   = AnyModifier("Alt"),   altGr= AnyModifier("AltGr");

            // Resolve raw send value (no SendKeys modifier prefixes) to detect layout: keys
            string rawChar;
            if (altGr && !string.IsNullOrEmpty(cell.Props.AltGrSend))
                rawChar = cell.Props.AltGrSend;
            else if (shift && !string.IsNullOrEmpty(cell.Props.ShiftSend))
                rawChar = cell.Props.ShiftSend;
            else
                rawChar = cell.Props.Send;

            // ── Layout switch ─────────────────────────────────────────
            if (rawChar != null && rawChar.StartsWith("layout:", StringComparison.Ordinal))
            {
                ClearModifiers();
                string filePath = ResolveLayoutPath(rawChar.Substring(7).Trim());
                if (!File.Exists(filePath ?? ""))
                {
                    FlashKeyError(cell);
                    return;
                }
                SetMode(Mode.Normal);
                _undoStack.Clear();
                _redoStack.Clear();
                RefreshUndoRedoState();
                ApplyLoadedSettings(filePath);
                return;
            }

            string send;
            if (altGr && !string.IsNullOrEmpty(cell.Props.AltGrSend))
                send = SendKeysHelper.ApplyModifiers(cell.Props.AltGrSend, false, ctrl, false);
            else if (shift && !string.IsNullOrEmpty(cell.Props.ShiftSend))
                send = SendKeysHelper.ApplyModifiers(cell.Props.ShiftSend, false, ctrl, alt || altGr);
            else
                send = SendKeysHelper.ApplyModifiers(cell.Props.Send, shift, ctrl, alt || altGr);

            ClearModifiers();

            // For punctuation after a predicted word, the predictor needs to
            // send {BACKSPACE} BEFORE the punctuation (to remove the trailing
            // prediction space). We check this condition here and inject the
            // backspace before sending the punctuation character.
            bool isPunctuation = rawChar != null && rawChar.Length == 1 &&
                                 (char.IsPunctuation(rawChar[0]) || char.IsSymbol(rawChar[0]));
            if (isPunctuation && (_predictor?.LastActionWasPrediction == true))
                SendKeysHelper.Send("{BACKSPACE}");

            if (!string.IsNullOrEmpty(send)) SendKeysHelper.Send(send);

            // Track AFTER sending. The predictor will NOT send another backspace
            // for punctuation because we already handled it above — suppress it
            // by temporarily clearing the flag via a pre-notify.
            _predictor?.OnKeySent(rawChar, shift || AnyModifier("Caps"));
        }

        // ── Word prediction methods ──────────────────────────────────

        /// <summary>
        /// Push the current _wpPredictions onto the button Tags.
        /// Called after every UpdateCornerTag pass so predictions are never
        /// overwritten by the static placeholder labels.
        /// </summary>
        private void ApplyWPTags()
        {
            if (!WordDatabase.IsLoaded) return;
            foreach (var cell in _layout.Cells)
            {
                if (cell.Props.Send == null) continue;
                if (!cell.Props.Send.StartsWith("wp:")) continue;
                if (!int.TryParse(cell.Props.Send.Substring(3), out int slot)) continue;
                if (slot < 0 || slot >= 10) continue;
                if (!_buttons.TryGetValue(cell, out var btn)) continue;

                string pred = (_predictor != null && slot < _predictor.Predictions.Count) ? _predictor.Predictions[slot] : "";
                var grpWP = FindGroup(cell.Props.GroupName);
                Color fc = ResolveColor(cell.Props.FontColor, grpWP?.FontColor ?? Color.Empty, _theme.FontColor);

                // ── Font: 80 % of auto-sized height, idempotent (from btn.Height) ─────
                const float charH   = 1.35f;
                const int   vMargin = 8;
                int baseFs = Math.Max(6, (int)Math.Min(btn.Height * 0.36f,
                                                       (btn.Height - vMargin) / charH));
                int    wpFs = Math.Max(6, (int)(baseFs * 0.80f));
                string fn   = btn.Font.FontFamily.Name;
                Font wpFont = GetButtonFont(fn, wpFs);

                // ── Measure helper: subtract TextRenderer's ~8 px overhead ───────────
                var   mf  = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                var   inf = new Size(int.MaxValue, int.MaxValue);
                int MeasureW(string t, Font f) =>
                    Math.Max(0, TextRenderer.MeasureText(t, f, inf, mf).Width - 8);

                // ── How many prefix characters may be stripped ───────────────────────
                // The typed buffer is the MAXIMUM strip, not the minimum.
                // If the prediction doesn't start with the buffer (e.g. proper nouns),
                // no stripping is possible.
                string typed     = _predictor?.WordBuffer ?? "";
                int    maxStrip  = (typed.Length > 0 &&
                                    pred.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                                   ? typed.Length : 0;

                // ── Display strategy ──────────────────────────────────────────────────
                // Phase 1: at 80 % font, try strip = 0, 1, 2 … maxStrip.
                //   strip = 0 → full word (no "…")
                //   strip > 0 → "…" + pred.Substring(strip)
                // Take the first (least aggressive) candidate that fits.
                string display    = null;
                Font   displayFont = wpFont;
                int    usedStrip  = 0;

                for (int strip = 0; strip <= maxStrip; strip++)
                {
                    string candidate = strip == 0 ? pred : "…" + pred.Substring(strip);
                    if (MeasureW(candidate, wpFont) <= btn.Width)
                    {
                        display   = candidate;
                        usedStrip = strip;
                        break;
                    }
                }

                // Phase 2: nothing fit at 80 % → auto-shrink font for the most-stripped
                // candidate ("…" + full suffix, or full word when no prefix was typed).
                if (display == null)
                {
                    string textToFit = maxStrip > 0 ? "…" + pred.Substring(maxStrip) : pred;
                    int fitW = MeasureW(textToFit, wpFont);
                    // Proportional first estimate, then fine-tune down 1 pt at a time
                    int  shrFs   = fitW > 0 ? Math.Max(6, (int)(wpFs * (float)btn.Width / fitW)) : 6;
                    Font shrFont = GetButtonFont(fn, shrFs);
                    while (shrFs > 6 && MeasureW(textToFit, shrFont) > btn.Width)
                    { shrFs--;  shrFont = GetButtonFont(fn, shrFs); }
                    // Last resort: TailFit if even 6 pt is still too wide
                    display     = MeasureW(textToFit, shrFont) <= btn.Width
                                  ? textToFit
                                  : TailFit(textToFit, shrFont, btn.Width);
                    displayFont = shrFont;
                    usedStrip   = maxStrip;   // all prefix chars stripped in phase 2
                }

                // Typed chars still visible = prefix length minus how many were stripped
                int visibleTypedLen = Math.Max(0, typed.Length - usedStrip);

                btn.Font = displayFont;
                string label = display;
                var newTag = (label, "", "", fc, true, visibleTypedLen);   // isWP=true
                if (btn.Tag is (string ol, string _, string _, Color ofc, bool owp, int otl) &&
                    owp && ol == label && ofc == fc && otl == visibleTypedLen)
                    continue;   // nothing changed — skip Invalidate
                btn.Tag = newTag;
                btn.Invalidate();
            }
        }

        /// <summary>
        /// Returns the longest suffix of <paramref name="text"/> that fits within
        /// <paramref name="availPx"/> pixels, prepending "…" when the full string is too wide.
        /// Called by ApplyWPTags so measurement happens once per prediction update,
        /// not on every paint.
        /// </summary>
        private static string TailFit(string text, Font font, int availPx)
        {
            if (string.IsNullOrEmpty(text) || availPx <= 0 || font == null)
                return text ?? "";

            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var inf   = new Size(int.MaxValue, int.MaxValue);

            // TextRenderer.MeasureText with Size(MaxValue,MaxValue) adds ~8 px of internal
            // horizontal padding to every result.  Subtract it so the budget comparison
            // reflects actual rendered text width rather than the padded measurement.
            const int pad = 8;

            if (TextRenderer.MeasureText(text, font, inf, flags).Width - pad <= availPx)
                return text;    // fits without truncation

            const string ell  = "…";
            int           ellW = TextRenderer.MeasureText(ell, font, inf, flags).Width - pad;
            int           budget = availPx - ellW;

            for (int i = 1; i < text.Length; i++)
            {
                string suffix = text.Substring(i);
                if (TextRenderer.MeasureText(suffix, font, inf, flags).Width - pad <= budget)
                    return ell + suffix;
            }
            return ell;
        }

        /// <summary>
        /// Toggles a modifier key's latched/locked state.
        /// <para>
        /// With <b>StickyModifiers off</b>: one click latches (active for next key),
        /// another click un-latches.
        /// </para>
        /// <para>
        /// With <b>StickyModifiers on</b>: click 1 = latched, click 2 = locked (stays
        /// active until clicked a third time). This matches the Windows Sticky Keys behaviour.
        /// </para>
        /// <para>
        /// All keys with the same label (e.g. both Shift keys) are toggled together so
        /// either Shift key activates the modifier.
        /// </para>
        /// </summary>
        private void ToggleModifier(GridCell cell)
        {
            // When shift is manually toggled with empty buffer:
            // turning ON  → capitalise predictions (sentence start)
            // turning OFF → reset sentence-start state
            if (cell.Props.Label == "Shift" &&
                string.IsNullOrEmpty(_predictor?.WordBuffer))
            {
                bool turningOn = !_latchedMods.Contains(cell) && !_lockedMods.Contains(cell);
                _predictor?.SetNextWordUpper(turningOn);
            }

            if (!_meta.StickyModifiers)
            {
                bool wasLatched = _latchedMods.Contains(cell);
                foreach (var c in _layout.Cells)
                    if (c.Props.Label == cell.Props.Label)
                    {
                        if (wasLatched) _latchedMods.Remove(c);
                        else            _latchedMods.Add(c);
                    }
            }
            else
            {
                bool wasLocked  = _lockedMods.Contains(cell);
                bool wasLatched = _latchedMods.Contains(cell);
                foreach (var c in _layout.Cells)
                {
                    if (c.Props.Label != cell.Props.Label) continue;
                    if (wasLocked)       { _lockedMods.Remove(c);  _latchedMods.Remove(c); }
                    else if (wasLatched) { _lockedMods.Add(c); }
                    else                 { _latchedMods.Add(c); }
                }
            }
            RefreshAllButtons();
        }

        /// <summary>
        /// Clears all one-shot latched modifiers after a regular key is pressed.
        /// Locked modifiers (click-2 state) and Caps Lock are intentionally preserved.
        /// </summary>
        private void ClearModifiers()
        {
            // Remove one-shot latched mods; keep locked mods and Caps
            _latchedMods.RemoveWhere(c =>
                c.Props.Label != "Caps" && !_lockedMods.Contains(c));
            RefreshAllButtons();
        }



        /// <summary>
        /// Opens the <see cref="KeyEditorForm"/> dialog for the given cell.
        /// If the user confirms, applies the resulting properties and any span change to the
        /// layout, filling or absorbing neighbouring cells as needed.
        /// </summary>
        private void OpenEditor(GridCell cell)
        {
            // Collect WP slots used by other keys so the editor can warn about duplicates
            var usedWpSlots = new HashSet<int>();
            foreach (var c in _layout.Cells)
            {
                if (c == cell) continue;  // exclude the key being edited
                if (c.Props.Send != null &&
                    c.Props.Send.StartsWith("wp:") &&
                    int.TryParse(c.Props.Send.Substring(3), out int s))
                    usedWpSlots.Add(s);
            }

            using var dlg = new KeyEditorForm(
                cell.Props, this,
                colSpan:      cell.ColSpan,
                rowSpan:      cell.RowSpan,
                maxCols:      _layout.Cols,
                maxRows:      _layout.Rows,
                usedWpSlots:  usedWpSlots,
                groups:       _layout.Groups,
                layoutDir:    _currentFilePath != null
                              ? System.IO.Path.GetDirectoryName(_currentFilePath) : null);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            PushUndo();
            cell.Props = dlg.Result;
            NormaliseWPSlots();

            int oldColSpan = cell.ColSpan;
            int oldRowSpan = cell.RowSpan;

            if (dlg.ResultColSpan < oldColSpan || dlg.ResultRowSpan < oldRowSpan)
            {
                // Span shrank: create empty cells for freed positions BEFORE applying.
                FillFreedSpanCells(cell, dlg.ResultColSpan, dlg.ResultRowSpan);
            }

            cell.ColSpan = dlg.ResultColSpan;
            cell.RowSpan = dlg.ResultRowSpan;

            if (dlg.ResultColSpan > oldColSpan || dlg.ResultRowSpan > oldRowSpan)
            {
                // Span grew: remove any cells now covered by the expanded span AFTER applying.
                AbsorbCoveredCells(cell);
            }

            if (_buttons.TryGetValue(cell, out var btn))
            {
                ApplyPropsToButton(btn, cell.Props, false);
                UpdateCornerTag(btn, cell.Props, ShiftActive, AltGrActive);
            }
            LayoutButtons();
            SyncPredictorSlotCount();
            AutoSave();
        }

        /// <summary>
        /// Transitions to a new application mode, updating all visual indicators and
        /// toolbar visibility, then reflowing the button layout.
        /// </summary>
        private void SetMode(Mode newMode)
        {
            if (newMode != Mode.Edit) { _selectedCell = null; UpdateSelectedKeyLabel(); }
            if (newMode != Mode.Edit && _fmtPaintMode)  { _fmtPaintMode  = false; UpdatePaintModeCursors();    }
            if (newMode != Mode.Edit && _keyPaintMode)  { _keyPaintMode  = false; UpdateKeyPaintModeCursors(); }
            _mode = newMode;
            ApplyModeIndicators();          // ends with LayoutButtons()
            RefreshAllButtons(skipFontCalc: true);
        }

        /// <summary>
        /// Enters GearPlacement mode: all keys are highlighted in blue so the user can
        /// click one to become the new gear button home cell.
        /// </summary>
        private void StartGearPlacement()
        {
            _mode = Mode.GearPlacement;
            ApplyModeIndicators();          // ends with LayoutButtons()
            RefreshAllButtons(skipFontCalc: true);
        }

        /// <summary>
        /// Completes GearPlacement mode by recording the chosen cell's row/column as the
        /// gear button's new home, then returning to Normal mode.
        /// </summary>
        private void FinishGearPlacement(GridCell cell)
        {
            _meta.GearRow = cell.Row;
            _meta.GearCol = cell.Col;
            _mode = Mode.Normal;
            ApplyModeIndicators();          // ends with LayoutButtons()
            RefreshAllButtons(skipFontCalc: true);
            LayoutButtons();   // reposition gear button immediately
            AutoSave();
        }

        /// <summary>
        /// Updates all visual indicators that reflect the current mode: edit strip colour,
        /// toolbar visibility, gear button glyph and colour, and the enabled state of toolbar
        /// buttons. Always ends with <see cref="LayoutButtons"/> to reflow the key grid.
        /// </summary>
        private void ApplyModeIndicators()
        {
            bool inEdit = _mode == Mode.Edit;

            switch (_mode)
            {
                case Mode.Edit:
                    _editStrip.BackColor = _stripEditColor;
                    _editStrip.Visible   = true;
                    _toolbar.Visible     = true;
                    _gearBtn.Text        = "✏";
                    _gearBtn.BackColor   = _gearEditBg;
                    _gearBtn.ForeColor   = Color.White;
                    break;
                case Mode.GearPlacement:
                    _editStrip.BackColor = Color.FromArgb(80, 80, 200);
                    _editStrip.Visible   = true;
                    _toolbar.Visible     = false;
                    _gearBtn.Text        = "📌";
                    _gearBtn.BackColor   = Color.FromArgb(60, 60, 180);
                    _gearBtn.ForeColor   = Color.White;
                    break;
                default:
                    _editStrip.Visible   = false;
                    _toolbar.Visible     = false;
                    _gearBtn.Text        = "⚙";
                    _gearBtn.BackColor   = _gearNormalBg;
                    _gearBtn.ForeColor   = _gearNormalFg;
                    break;
            }

            // Row 2 toolbar: only in Edit mode
            if (_toolbarEdit != null)
                _toolbarEdit.Visible = inEdit;

            // Hide Edit button when already in Edit mode
            _btnEdit    .Visible = _mode != Mode.Edit;
            _btnEdit    .Invalidate();
            _btnExitEdit.Invalidate();

            RefreshToolbarEditState();
            RefreshUndoRedoState();
            LayoutButtons();   // reflow keys to match toolbar visibility
        }

        // ── Destructive-action helpers ────────────────────────────────

        /// <summary>True when a key has any per-key style override (font, color, border).</summary>
        private static bool HasCustomFormatting(KeyProps p) =>
            !string.IsNullOrEmpty(p.FontName) || p.FontSize > 0 ||
            !p.FontColor.IsEmpty || !p.KeyColor.IsEmpty ||
            !p.BorderColor.IsEmpty || p.BorderThickness != -1;

        /// <summary>Finds the single cell whose top-left corner is at (row, col).</summary>
        private GridCell FindCellAt(int row, int col) =>
            _layout.Cells.Find(c => c.Row == row && c.Col == col);

        /// <summary>
        /// Shows a Yes/No warning dialog. "No" is the default button so an
        /// accidental Enter press does not confirm the destructive action.
        /// Returns true only when the user explicitly chooses Yes.
        /// </summary>

        // ── Toolbar state helpers ─────────────────────────────────────

        /// <summary>
        /// Updates the filename label in the toolbar to show the current layout file's
        /// base name, or "default" when no file has been explicitly loaded.
        /// </summary>
        private void UpdateFilenameLabel()
        {
            if (_lblFilename == null) return;
            string name = _currentFilePath != null
                ? Path.GetFileName(_currentFilePath)
                : "default";
            _lblFilename.Text = name;
        }

        /// <summary>
        /// Updates the selected-key info label in the edit toolbar to show the current
        /// cell's label and send value. Shows "—" when nothing is selected.
        /// </summary>
        private void UpdateSelectedKeyLabel()
        {
            if (_lblSelectedKey == null) return;
            if (_selectedCell == null) { _lblSelectedKey.Text = "—"; return; }
            var p = _selectedCell.Props;

            // Main line: label  →  send
            string lbl  = string.IsNullOrEmpty(p.Label) ? "(empty)" : p.Label;
            string send = string.IsNullOrEmpty(p.Send)  ? ""        : $"  →  {p.Send}";
            string main = lbl + send;

            // Shift line (shown above main when either shift field is set)
            bool hasShift = !string.IsNullOrEmpty(p.ShiftLabel) || !string.IsNullOrEmpty(p.ShiftSend);
            if (hasShift)
            {
                string shiftLbl  = p.ShiftLabel ?? "";
                string shiftSend = string.IsNullOrEmpty(p.ShiftSend) ? "" : $"  →  {p.ShiftSend}";
                _lblSelectedKey.Text = $"⇧  {shiftLbl}{shiftSend}\n{main}";
            }
            else
            {
                _lblSelectedKey.Text = main;
            }
        }

        /// <summary>
        /// Updates the enabled state of all toolbar2 buttons based on whether
        /// a cell is selected and what its span is.
        /// </summary>
        private void RefreshToolbarEditState()
        {
            if (_btnKeyEdit == null) return;

            bool hasSel    = _selectedCell != null && _mode == Mode.Edit;
            bool canMergeR = hasSel && _selectedCell.ColSpan == 1;
            bool canMergeD = hasSel && _selectedCell.RowSpan == 1;
            bool canSplit  = hasSel && (_selectedCell.ColSpan > 1 || _selectedCell.RowSpan > 1);
            bool hasFmt    = _copiedFormatting != null;
            bool hasKey    = _copiedKey        != null;

            void SetBtn(ToolbarButton b, bool enabled, bool active = false)
            {
                b.Enabled  = enabled;
                b.IsActive = active;
                b.Invalidate();
            }

            SetBtn(_btnKeyEdit,     hasSel);
            SetBtn(_btnKeyRemove,   hasSel);
            SetBtn(_btnCopyFmt,     hasSel || _fmtPaintMode, _fmtPaintMode);
            SetBtn(_btnCopyKey,     hasSel || _keyPaintMode, _keyPaintMode);
            SetBtn(_btnAddRowAbove, hasSel);
            SetBtn(_btnAddRowBelow, hasSel);
            SetBtn(_btnAddColLeft,  hasSel);
            SetBtn(_btnAddColRight, hasSel);
            SetBtn(_btnRemoveRow,   hasSel);
            SetBtn(_btnRemoveCol,   hasSel);
            SetBtn(_btnMergeRight,  canMergeR);
            SetBtn(_btnMergeDown,   canMergeD);
            SetBtn(_btnSplitCell,   canSplit);
        }

        /// <summary>
        /// Applies the previously copied formatting (font, colours, border, group) to the
        /// given cell. Does not affect the key's content (label, send strings).
        /// </summary>
        private void ApplyFormatPainter(GridCell cell)
        {
            if (_copiedFormatting == null) return;
            PushUndo();
            var p = cell.Props;
            p.FontName        = _copiedFormatting.FontName;
            p.FontSize        = _copiedFormatting.FontSize;
            p.FontColor       = _copiedFormatting.FontColor;
            p.KeyColor        = _copiedFormatting.KeyColor;
            p.BorderColor     = _copiedFormatting.BorderColor;
            p.BorderThickness = _copiedFormatting.BorderThickness;
            p.GroupName       = _copiedFormatting.GroupName;
            AutoSave();
        }

        /// <summary>
        /// Sets the mouse cursor to a hand icon for all key buttons when format-paint mode
        /// is active, or restores the default cursor when it is cancelled.
        /// </summary>
        private void UpdatePaintModeCursors()
        {
            var cur = _fmtPaintMode ? Cursors.Hand : Cursors.Default;
            foreach (var btn in _buttons.Values) btn.Cursor = cur;
        }

        /// <summary>
        /// Sets the mouse cursor to a hand icon for all key buttons when key-paint mode
        /// is active, or restores the default cursor when it is cancelled.
        /// </summary>
        private void UpdateKeyPaintModeCursors()
        {
            var cur = _keyPaintMode ? Cursors.Hand : Cursors.Default;
            foreach (var btn in _buttons.Values) btn.Cursor = cur;
        }

        /// <summary>
        /// Handles keyboard shortcuts for the keyboard form itself (not for typing through).
        /// Currently: Escape cancels format-paint mode, key-paint mode, and gear-placement mode.
        /// </summary>
        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if ((_fmtPaintMode || _keyPaintMode) && e.KeyCode == Keys.Escape)
            {
                _fmtPaintMode = false; UpdatePaintModeCursors();
                _keyPaintMode = false; UpdateKeyPaintModeCursors();
                RefreshToolbarEditState();
                e.Handled = true;
                return;
            }
            if (_mode == Mode.GearPlacement && e.KeyCode == Keys.Escape)
            {
                _mode = Mode.Normal;
                ApplyModeIndicators();          // ends with LayoutButtons()
                RefreshAllButtons(skipFontCalc: true);
                e.Handled = true;
                return;
            }
        }

        // ── Keyboard editor ───────────────────────────────────────────

        /// <summary>
        /// Opens the <see cref="KeyboardEditorForm"/> dialog for global keyboard settings
        /// (theme, window, language, groups).
        /// <para>
        /// If the user did NOT tick "Apply to all keys", currently-inheriting keys are frozen
        /// at the old global values so the global change doesn't silently restyle them.
        /// If the user DID tick it, per-key overrides for changed fields are cleared so all
        /// keys immediately adopt the new global style.
        /// </para>
        /// </summary>
        private void OpenKeyboardEditor()
        {
            using var dlg = new KeyboardEditorForm(_theme, _window, _meta, this,
                groups:    _layout.Groups,
                onSave:    () => SaveSettings(false),
                onSaveAs:  () => SaveSettings(true),
                onLoad:    () => LoadSettings(),
                getGroups: () => _layout.Groups);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            PushUndo();

            var chg = dlg.ChangedFields;

            if (!dlg.ApplyToKeys)
            {
                // "Apply style to all keys" was NOT ticked.
                // Freeze every currently-inheriting key at the current (old) global value so the
                // global change doesn't silently affect keys the user hasn't explicitly styled.
                // Explicitly-set per-key values are untouched.
                // (Empty spacer keys are excluded: they have no visible appearance in Normal mode
                //  and the key editor will always show global colours for them regardless.)
                foreach (var cell in _layout.Cells)
                {
                    if (IsEmptyKey(cell.Props)) continue;  // spacers: skip bake-in
                    var p = cell.Props;
                    if (chg.FontName   && string.IsNullOrEmpty(p.FontName))  p.FontName   = _theme.FontName;
                    if (chg.FontColor  && p.FontColor.IsEmpty)                p.FontColor  = _theme.FontColor;
                    if (chg.KeyColor   && p.KeyColor.IsEmpty)                 p.KeyColor   = _theme.KeyColor;
                    if (chg.BorderColor && p.BorderColor.IsEmpty)             p.BorderColor = _theme.BorderColor;
                    if (chg.BorderThickness && p.BorderThickness == -1)       p.BorderThickness = _theme.BorderThickness;
                }
            }

            // Copy new values into the existing objects (keeps _theme/_window/_meta as stable
            // references so any code that captured them — e.g. KeyEditorForm._ownerGlobal —
            // always sees the latest values without needing a fresh reference).
            _layout.Groups = dlg.ResultGroups;
            _theme .CopyFrom(dlg.ResultTheme);
            _window.CopyFrom(dlg.ResultWindow);
            _meta  .CopyFrom(dlg.ResultMeta);
            ApplyTitlebarState();
            ForceTopMost();  // re-apply always-on-top setting immediately
            BackColor = _theme.BackgroundColor; Opacity = _theme.Opacity;
            if (dlg.ApplyToKeys)
            {
                // "Apply style to all keys" WAS ticked: clear per-key style overrides for every
                // changed field so all keys immediately adopt the new global values.
                foreach (var cell in _layout.Cells)
                {
                    var p = cell.Props;
                    if (chg.FontName)        p.FontName        = "";
                    if (chg.FontSize)        p.FontSize        = _theme.FontSize;
                    if (chg.FontColor)       p.FontColor       = Color.Empty;
                    if (chg.KeyColor)        p.KeyColor        = Color.Empty;
                    if (chg.BorderColor)     p.BorderColor     = Color.Empty;
                    if (chg.BorderThickness) p.BorderThickness = -1;
                }
            }
            RefreshAllButtons();
            AutoSave();
        }

        // ── Save / Load ───────────────────────────────────────────────

        /// <summary>
        /// Saves the current layout to an XML file.
        /// <para>
        /// When <paramref name="saveAs"/> is <c>true</c>, always shows a Save dialog even if
        /// a file path is already known. After saving, also saves a copy to the default
        /// auto-save path so settings persist across restarts even if the user's file is on
        /// a different drive.
        /// </para>
        /// <para>
        /// The save is blocked when the layout is structurally invalid (overlapping or missing
        /// cells) to prevent writing a file that cannot be loaded back.
        /// </para>
        /// </summary>
        private void SaveSettings(bool saveAs)
        {
            // Guard: refuse to save a structurally broken layout so the file on disk
            // always stays loadable.  AutoSave bypasses this check deliberately —
            // it preserves the in-memory state even when invalid so data is never lost.
            if (!_layout.IsValid())
            {
                MessageBox.Show(Lang.T("Save invalid msg"),
                    Lang.T("Save invalid title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string path = _currentFilePath ?? SettingsManager.DefaultPath;
            if (saveAs || _currentFilePath == null)
            {
                using var dlg = new SaveFileDialog
                { Title="Save",Filter="XML files (*.xml)|*.xml|All files (*.*)|*.*",
                  DefaultExt="xml",FileName=path };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                path = dlg.FileName;
            }
            try
            {
                _meta.LastFile = path;
                SettingsManager.SaveSettings(_layout, _theme, _window, _meta, path);
                _currentFilePath = path;
                UpdateFilenameLabel();
                if (path != SettingsManager.DefaultPath)
                    SettingsManager.SaveSettings(_layout, _theme, _window, _meta, SettingsManager.DefaultPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Lang.T("Save failed")}\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Shows an Open dialog and loads the selected layout file via
        /// <see cref="ApplyLoadedSettings"/>.
        /// </summary>
        private void LoadSettings()
        {
            using var dlg = new OpenFileDialog
            { Title="Load",Filter="XML files (*.xml)|*.xml|All files (*.*)|*.*",
              FileName=_currentFilePath ?? SettingsManager.DefaultPath };
            if (dlg.ShowDialog() == DialogResult.OK) ApplyLoadedSettings(dlg.FileName);
        }

        /// <summary>
        /// Called at startup. Reads the default auto-save file to find the path of the last
        /// explicitly opened layout, then loads that file if it still exists.
        /// <para>
        /// Falls back to the auto-save file itself when the last-used file cannot be found.
        /// Does nothing if no auto-save file exists (first run).
        /// </para>
        /// </summary>
        private void TryAutoLoad()
        {
            string settingsPath = SettingsManager.DefaultPath;
            string pathToLoad   = settingsPath;
            if (File.Exists(settingsPath))
            {
                try
                {
                    var peekTheme = new VisualTheme();
                    var peekWindow = new WindowState();
                    var peekMeta = new LayoutMeta();
                    SettingsManager.LoadSettings(peekTheme, peekWindow, peekMeta, settingsPath);
                    string last = peekMeta.LastFile ?? "";
                    if (!string.IsNullOrEmpty(last) && last != settingsPath && File.Exists(last))
                        pathToLoad = last;
                }
                catch { }
            }
            if (!File.Exists(pathToLoad)) return;
            try
            {
                var loaded = SettingsManager.LoadSettings(_theme, _window, _meta, pathToLoad);
                if (loaded == null || !loaded.IsValid()) return;
                _layout = loaded; _latchedMods.Clear(); _lockedMods.Clear();
                _currentFilePath = pathToLoad;
                UpdateFilenameLabel();
                BackColor = _theme.BackgroundColor; Opacity = _theme.Opacity;
                ApplyTitlebarState();
                if (!string.IsNullOrEmpty(_meta.Language)) Lang.Load(_meta.Language);
                RebuildAllButtons();
                Size = new Size(_window.WindowWidth, _window.WindowHeight);
            }
            catch { }
        }

        // ── Undo / Redo ───────────────────────────────────────────────

        /// <summary>
        /// Captures the current layout + global settings onto the undo stack and
        /// clears the redo stack. Call this immediately before any destructive edit.
        /// The stack is capped at 50 snapshots; the oldest entry is discarded when full.
        /// </summary>
        private void PushUndo()
        {
            _undoStack.Push((_layout.Clone(), _theme.Clone(), _window.Clone(), _meta.Clone()));
            _redoStack.Clear();
            if (_undoStack.Count > 50)
            {
                // Discard the oldest (bottom) entry — rebuild without it.
                var arr = _undoStack.ToArray(); // [0] = top/newest
                _undoStack.Clear();
                for (int i = arr.Length - 2; i >= 0; i--) _undoStack.Push(arr[i]);
            }
            RefreshUndoRedoState();
        }

        /// <summary>
        /// Pops the most recent snapshot from the undo stack, saves the current state to the
        /// redo stack, and restores the popped snapshot.
        /// </summary>
        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push((_layout.Clone(), _theme.Clone(), _window.Clone(), _meta.Clone()));
            var (layout, theme, window, meta) = _undoStack.Pop();
            ApplySnapshot(layout, theme, window, meta);
            // RefreshUndoRedoState() is called inside ApplySnapshot via RebuildAllButtons chain,
            // but call it again here to be safe after the stack mutates.
            RefreshUndoRedoState();
        }

        /// <summary>
        /// Pops the most recent redo snapshot, saves the current state to the undo stack,
        /// and restores the popped snapshot.
        /// </summary>
        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push((_layout.Clone(), _theme.Clone(), _window.Clone(), _meta.Clone()));
            var (layout, theme, window, meta) = _redoStack.Pop();
            ApplySnapshot(layout, theme, window, meta);
            RefreshUndoRedoState();
        }

        /// <summary>Updates the enabled/colour state of the Undo and Redo toolbar buttons.</summary>
        private void RefreshUndoRedoState()
        {
            if (_btnUndo == null) return;
            _btnUndo.Enabled = _undoStack.Count > 0; _btnUndo.Invalidate();
            _btnRedo.Enabled = _redoStack.Count > 0; _btnRedo.Invalidate();
        }

        /// <summary>
        /// Restores layout + settings from a snapshot, then rebuilds the UI.
        /// Called by Undo() and Redo().
        /// </summary>
        private void ApplySnapshot(GridLayout layout, VisualTheme theme, WindowState window, LayoutMeta meta)
        {
            _layout = layout;
            _theme  = theme;
            _window = window;
            _meta   = meta;
            // Reset edit state
            _selectedCell = null;
            UpdateSelectedKeyLabel();
            _latchedMods.Clear();
            _lockedMods.Clear();
            // Re-apply visual state derived from settings
            BackColor = _theme.BackgroundColor;
            Opacity   = _theme.Opacity;
            ApplyTitlebarState();
            ForceTopMost();
            RebuildAllButtons();
            RefreshUndoRedoState();
            AutoSave();
        }

        /// <summary>
        /// Silently saves the current layout to the default auto-save path (and to the
        /// current file if one is open). Errors are swallowed — auto-save should never
        /// interrupt the user.
        /// </summary>
        private void AutoSave()
        {
            string path = _currentFilePath ?? SettingsManager.DefaultPath;
            try
            {
                _meta.LastFile = path;
                SettingsManager.SaveSettings(_layout, _theme, _window, _meta, path);
                if (path != SettingsManager.DefaultPath)
                    SettingsManager.SaveSettings(_layout, _theme, _window, _meta, SettingsManager.DefaultPath);
            }
            catch { }
        }

        /// <summary>
        /// Resolves a (possibly relative) path from a layout: key.
        /// Tries: (1) absolute as-is, (2) relative to current layout dir, (3) relative to app dir.
        /// </summary>
        private string ResolveLayoutPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Path.IsPathRooted(path)) return path;
            if (_currentFilePath != null)
            {
                string candidate = Path.Combine(Path.GetDirectoryName(_currentFilePath), path);
                if (File.Exists(candidate)) return candidate;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        /// <summary>
        /// Briefly flashes a key button red (or white if the key is already red-ish)
        /// to signal that a layout: path could not be resolved.
        /// </summary>
        private void FlashKeyError(GridCell cell)
        {
            if (!_buttons.TryGetValue(cell, out var btn)) return;
            var original  = btn.BackColor;
            bool isRedish = original.R > 150 && original.G < 80 && original.B < 80;
            var flash     = isRedish ? Color.White : Color.FromArgb(220, 50, 50);
            btn.BackColor = flash;
            var t = new System.Windows.Forms.Timer { Interval = 400 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); btn.BackColor = original; };
            t.Start();
        }

        /// <summary>
        /// Loads a layout file and applies it to the keyboard.
        /// Shows an error dialog if the file cannot be read or is not a valid layout.
        /// On success, rebuilds all buttons and resizes the window to match saved dimensions.
        /// </summary>
        private void ApplyLoadedSettings(string path)
        {
            GridLayout loaded = null; Exception loadEx = null;
            try { loaded = SettingsManager.LoadSettings(_theme, _window, _meta, path); }
            catch (Exception ex) { loadEx = ex; }

            if (loadEx != null)
            { ShowFileError(Path.GetFileName(path), $"{Lang.T("Invalid file detail")}\n{loadEx.Message}"); return; }
            if (loaded == null || !loaded.IsValid())
            { ShowFileError(Path.GetFileName(path), null); return; }

            _layout = loaded; _latchedMods.Clear(); _lockedMods.Clear();
            _currentFilePath = path;
            UpdateFilenameLabel();
            BackColor = _theme.BackgroundColor; Opacity = _theme.Opacity;
            ApplyTitlebarState();
            if (!string.IsNullOrEmpty(_meta.Language)) Lang.Load(_meta.Language);
            RebuildAllButtons();
            Size = new Size(_window.WindowWidth, _window.WindowHeight);
            AutoSave();
        }

        /// <summary>
        /// Shows a translated error dialog explaining that a file could not be opened.
        /// Optionally appends a technical detail string (exception message) for diagnostics.
        /// </summary>
        private void ShowFileError(string fileName, string detail)
        {
            string msg = Lang.T("Invalid file msg");
            if (!string.IsNullOrEmpty(detail)) msg += $"\n\n{detail}";
            MessageBox.Show(msg, $"{Lang.T("Invalid file title")} — {fileName}",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
