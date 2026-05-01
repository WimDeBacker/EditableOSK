using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public class KeyboardForm : Form
    {
        // ── Win32 ────────────────────────────────────────────────────
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        private void ForceTopMost()
        {
            if (!IsHandleCreated) return;
            var target = _global.AlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST;
            SetWindowPos(Handle, target, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override CreateParams CreateParams
        {
            // WS_EX_NOACTIVATE: clicking keyboard never steals focus from target app.
            // WS_EX_TOOLWINDOW removed — it caused a small non-standard title bar and
            // suppressed minimize/maximize buttons. ShowInTaskbar=false hides from taskbar instead.
            get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_NOACTIVATE; return cp; }
        }

        // ── State ────────────────────────────────────────────────────
        private GridLayout          _layout;
        private readonly Dictionary<GridCell, Button> _buttons = new();
        internal readonly GlobalSettings _global = new GlobalSettings();

        private enum Mode { Normal, Edit, QuickEdit, GearPlacement }

        // Gear button appearance per mode
        private static readonly Color _gearNormalBg   = ColorTranslator.FromHtml("#2A2A4A");
        private static readonly Color _gearNormalFg   = ColorTranslator.FromHtml("#CCCCFF");
        private static readonly Color _gearEditBg     = Color.FromArgb(200, 100, 0);   // amber
        private static readonly Color _gearQuickBg    = Color.FromArgb(0,  130, 130);  // teal
        private static readonly Color _stripEditColor  = Color.FromArgb(220, 120, 0);  // orange
        private static readonly Color _stripQuickColor = Color.FromArgb(0,  160, 160); // teal
        private Mode _mode = Mode.Normal;

        private GridCell _quickEditCell;
        // Double-click detection for Edit mode: a timer defers the context menu
        // so a second click within the double-click interval cancels it.
        private GridCell _pendingEditCell  = null;
        private System.Windows.Forms.Timer _editClickTimer;
        private string   _quickEditText = "";

        private Button _gearBtn;
        private Panel  _editStrip;   // thin colored bar along bottom — signals edit/quickedit mode
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

        private bool IsModifier(GridCell cell) =>
            cell != null && KeyLayout.ModifierLabels.Contains(cell.Props.Label);

        private bool AnyModifier(string label)
        {
            foreach (var cell in _latchedMods)
                if (cell.Props.Label == label) return true;
            return false;
        }

        private bool ShiftActive => AnyModifier("Shift") || AnyModifier("Caps");
        private bool AltGrActive => AnyModifier("AltGr");

        private void LatchShiftForSentence()
        {
            foreach (var cell in _layout.Cells)
                if (cell.Props.Label == "Shift" && !_latchedMods.Contains(cell))
                    _latchedMods.Add(cell);
            RefreshAllButtons();
            ApplyWPTags();
        }

        private void UnlatchShift()
        {
            bool removed = _latchedMods.RemoveWhere(c =>
                c.Props.Label == "Shift" && !_lockedMods.Contains(c)) > 0;
            if (removed) RefreshAllButtons();
            ApplyWPTags();
        }

        // ── Constructor ──────────────────────────────────────────────
        public KeyboardForm()
        {
            Text            = "On-Screen Keyboard";
            BackColor       = _global.BackgroundColor;
            Opacity         = _global.Opacity;
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
            // Timer for deferred single-click in Edit mode
            _editClickTimer = new System.Windows.Forms.Timer
                { Interval = SystemInformation.DoubleClickTime };
            _editClickTimer.Tick += (s, e) =>
            {
                _editClickTimer.Stop();
                if (_pendingEditCell != null)
                {
                    var cell = _pendingEditCell;
                    _pendingEditCell = null;
                    ShowKeyEditMenu(cell);
                }
            };
            RebuildAllButtons();
            TryAutoLoad();

            ResizeEnd   += (s, e) =>
            {
                _global.WindowWidth  = Width;
                _global.WindowHeight = Height;
                AutoSave();
            };
            SizeChanged += (s, e) => LayoutButtons();
            Shown       += (s, e) => { LayoutButtons(); ForceTopMost(); _gearBtn.BringToFront(); LatchShiftForSentence(); };
            Activated   += (s, e) => ForceTopMost();
            KeyDown     += OnFormKeyDown;
            RegisterFocusHook();

            void onLangChanged() => _global.Language = Lang.CurrentCode;
            Lang.LanguageChanged += onLangChanged;
            FormClosing += (s, e) =>
            {
                Lang.LanguageChanged -= onLangChanged;
                if (_hookHandle != IntPtr.Zero) { UnhookWinEvent(_hookHandle); _hookHandle = IntPtr.Zero; }
                _editClickTimer.Stop();
                _editClickTimer.Dispose();
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

        private void ApplyTitlebarState()
        {
            bool hide = _global.HideTitlebar;
            FormBorderStyle = hide ? FormBorderStyle.None : FormBorderStyle.Sizable;
            // Both MaximizeBox and MinimizeBox must be true for the minimize
            // button to appear in the title bar. We allow maximize here —
            // the user can drag the keyboard back to their preferred size.
            MaximizeBox = !hide;
            MinimizeBox = !hide;
            ForceTopMost();
        }

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
            _gearBtn.Click += (s, e) => ShowGearMenu();
            ForwardMouseEvents(_gearBtn);
            // NOTE: _gearBtn is NOT added to Controls here.
            // It is re-added at the end of RebuildAllButtons so it is always
            // the last control added — guaranteeing it sits on top in z-order.
        }

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

        // ── Rebuild all buttons ──────────────────────────────────────
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
            // Populate WP keys with initial predictions
            UpdateWPKeys();
        }

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
            };
            btn.FlatAppearance.BorderSize = 1;
            ApplyPropsToButton(btn, p, false);
            ApplyEmptyKeyStyle(btn, p);
            btn.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (_mode == Mode.Edit)
                {
                    if (e.Clicks == 2)
                    {
                        // Second click arrived — cancel pending menu, open editor
                        _editClickTimer.Stop();
                        _pendingEditCell = null;
                        OpenEditor(cell);
                    }
                    else
                    {
                        // First click — defer menu until timer fires
                        // If a second click arrives first, the timer is cancelled above
                        _pendingEditCell = cell;
                        _editClickTimer.Stop();
                        _editClickTimer.Start();
                    }
                }
                else
                {
                    OnKeyClick(cell);
                }
            };
            btn.Paint += OnButtonPaint;
            return btn;
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
                btn.Enabled   = true;
                btn.BackColor = Color.FromArgb(55, 55, 75);
                btn.ForeColor = Color.FromArgb(110, 110, 130);
                btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 100);
                btn.FlatAppearance.BorderSize  = 1;
                btn.Text = "";
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
        // The gear button is positioned over the cell at row 0, last column.
        // That cell is kept blank in the XML (Label="" Send="") so it acts
        // as a reserved slot — invisible in normal mode, the gear sits on top.
        private void LayoutButtons()
        {
            int rows = _layout.Rows, cols = _layout.Cols;
            if (rows == 0 || cols == 0) return;
            if (ClientSize.Width < 50 || ClientSize.Height < 50) return;

            // No extra column reserved — gear overlays the top-right grid cell
            int usableW = ClientSize.Width  - Pad * 2 - Gap * (cols - 1);
            int usableH = ClientSize.Height - Pad * 2 - Gap * (rows - 1);
            float cellW = Math.Max(8f, (float)usableW / cols);
            float cellH = Math.Max(8f, (float)usableH / rows);

            bool shifted = ShiftActive, altGr = AltGrActive;
            var placed = new HashSet<GridCell>();

            foreach (var cell in _layout.Cells)
            {
                if (!_buttons.TryGetValue(cell, out var btn)) continue;
                if (placed.Contains(cell)) continue;
                placed.Add(cell);

                int x = Pad + (int)(cell.Col * (cellW + Gap));
                int y = Pad + (int)(cell.Row * (cellH + Gap));
                int w = Math.Max(8, (int)(cell.ColSpan * cellW + (cell.ColSpan - 1) * Gap));
                int h = Math.Max(8, (int)(cell.RowSpan * cellH + (cell.RowSpan - 1) * Gap));

                btn.SetBounds(x, y, w, h);
                SetButtonFont(btn, cell.Props, h, w, shifted, altGr);
                ApplyEmptyKeyStyle(btn, cell.Props);
            }

            // Gear button: overlays the designated gear cell (default: row 0, last column)
            var (gRow, gCol) = GearCell(cols);
            int gearX = Pad + (int)(gCol * (cellW + Gap));
            int gearY = Pad + (int)(gRow * (cellH + Gap));
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

        private void SetButtonFont(Button btn, KeyProps p, int btnH, int btnW,
                                   bool shifted, bool altGr)
        {
            string label = p.GetDisplayLabel(shifted, altGr);
            if (string.IsNullOrEmpty(label)) label = p.Label ?? "";

            int fs;
            if (p.FontSize > 0)
            {
                fs = p.FontSize;
            }
            else
            {
                // Measured Bold Arial metrics inside a WinForms Flat button:
                //   charW  ≈ 0.72× pt size  (average for caps + digits + lower)
                //   charH  ≈ 1.35× pt size  (em + internal leading)
                //   hMargin = 10px total     (button internal padding 4+4 + 2 safety)
                //   vMargin =  8px total     (button internal padding 2+2 + 2+2 safety)
                const float charW   = 0.72f;
                const float charH   = 1.35f;
                const int   hMargin = 10;
                const int   vMargin =  8;

                float maxFsByHeight = (btnH - vMargin) / charH;
                float maxFsByWidth  = btnW > 0 && label.Length > 0
                    ? (btnW - hMargin) / (Math.Max(1f, label.Length) * charW)
                    : maxFsByHeight;

                // Base: 36% of height, never exceeding either dimension cap
                float baseFs = Math.Min(btnH * 0.36f,
                               Math.Min(maxFsByHeight, maxFsByWidth));

                // Large symbol keys (⌫ ↵) may use more height
                bool big = KeyLayout.LargeSymbolLabels.Contains(p.Label)
                        || KeyLayout.LargeSymbolLabels.Contains(p.ShiftLabel ?? "");
                if (big) baseFs = Math.Min(baseFs * 1.25f, maxFsByHeight);

                fs = Math.Max(6, (int)baseFs);
            }

            string fn = ResolveFontName(p.FontName);
            try { btn.Font = GetButtonFont(fn, fs); }
            catch { btn.Font = GetButtonFont("Arial", fs); }
            // Main label and corners are painted by OnButtonPaint via UpdateCornerTag
            UpdateCornerTag(btn, p, shifted, altGr);
        }

        // After all buttons have been laid out and tagged, apply WP predictions.
        // This is called once per LayoutButtons pass via the post-loop section below.
        private void UpdateCornerTag(Button btn, KeyProps p, bool shifted, bool altGr)
        {
            bool isMod = KeyLayout.ModifierLabels.Contains(p.Label);
            string sl = (!shifted && !altGr && !isMod) ? (p.ShiftLabel  ?? "") : "";
            string al = (!altGr             && !isMod) ? (p.AltGrLabel  ?? "") : "";
            // Use the raw label — Graphics.DrawString does NOT interpret & as an
            // accelerator (only WinForms text rendering does). BtnText's &&-escaping
            // is only needed when assigning to btn.Text, which we always set to "".
            string ml = p.GetDisplayLabel(shifted, altGr) ?? "";
            btn.Tag = (ml, sl, al, ResolveColor(p.FontColor, _global.FontColor));
            // Clear the button's own text so WinForms draws nothing — we paint it.
            if (btn.Text != "") btn.Text = "";
            btn.Invalidate();
        }

        // ── Owner-draw: main label + corner labels ───────────────────
        // TextRenderer.DrawText is used throughout because it does not
        // interpret "&" as a hotkey prefix unless explicitly asked — unlike
        // Graphics.DrawString which requires a StringFormat with HotkeyPrefix.None.
        // TextFormatFlags.NoPrefix suppresses all "&" interpretation entirely.

        private void OnButtonPaint(object sender, PaintEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not (string ml, string sl, string al, Color fc)) return;

            // ── Main label — centred, no hotkey interpretation ────────
            if (!string.IsNullOrEmpty(ml))
            {
                var mainRect = new Rectangle(2, 2, btn.Width - 4, btn.Height - 4);
                TextRenderer.DrawText(e.Graphics, ml, btn.Font, mainRect, fc,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter   |
                    TextFormatFlags.SingleLine       |
                    TextFormatFlags.EndEllipsis      |
                    TextFormatFlags.NoPrefix);        // NoPrefix: & is never a hotkey
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
        private Font _lastGearFont; private int _lastGearFontSize = -1;
        private Font GetGearFont(int size)
        {
            if (_lastGearFont == null || _lastGearFontSize != size)
            { _lastGearFont?.Dispose(); _lastGearFont = new Font("Segoe UI", size); _lastGearFontSize = size; }
            return _lastGearFont;
        }
        private readonly Dictionary<(string,int),Font> _fontCache = new();
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
        private void RefreshAllButtons()
        {
            bool shifted = ShiftActive, altGr = AltGrActive;
            foreach (var (cell, btn) in _buttons)
            {
                var p = cell.Props;
                bool latched = _latchedMods.Contains(cell);
                if (_mode == Mode.GearPlacement)
                {
                    // Highlight all keys — user picks the new gear location
                    btn.BackColor = Color.FromArgb(50, 60, 160);
                    btn.ForeColor = Color.White;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(120, 140, 255);
                    btn.FlatAppearance.BorderSize  = 2;
                }
                else
                {
                    ApplyPropsToButton(btn, p, latched, _lockedMods.Contains(cell));
                    ApplyEmptyKeyStyle(btn, p);
                }
                UpdateCornerTag(btn, p, shifted, altGr);
            }
            // Override WP key tags with current predictions (must come AFTER
            // UpdateCornerTag which would otherwise reset them to static labels)
            ApplyWPTags();
        }

        /// <summary>Returns the gear button's grid row and column.</summary>
        private (int row, int col) GearCell(int cols)
        {
            int rows = _layout?.Rows ?? 1;
            int row = Math.Clamp(_global.GearRow, 0, rows - 1);
            int col = _global.GearCol < 0 ? cols - 1 : Math.Min(_global.GearCol, cols - 1);
            return (row, col);
        }

        /// <summary>Resolve per-key properties against global defaults.</summary>
        private int    ResolveThickness(int keyThickness) =>
            keyThickness == -1 ? _global.BorderThickness : keyThickness;
        private Color  ResolveColor(Color keyColor, Color globalColor) =>
            keyColor.IsEmpty ? globalColor : keyColor;
        private string ResolveFontName(string keyFont) =>
            string.IsNullOrEmpty(keyFont) ? _global.FontName : keyFont;

        private void ApplyPropsToButton(Button btn, KeyProps p, bool latched, bool locked = false)
        {
            if (locked)
            {
                // Locked: strong amber — stays until clicked again
                btn.BackColor = Color.FromArgb(220, 140, 0);
                btn.ForeColor = Color.FromArgb(30, 30, 30);
                btn.FlatAppearance.BorderColor = Color.FromArgb(255, 200, 0);
                btn.FlatAppearance.BorderSize  = Math.Max(3, ResolveThickness(p.BorderThickness));
            }
            else if (latched)
            {
                // Latched: moderate highlight — clears after next key
                Color rKc = ResolveColor(p.KeyColor,  _global.KeyColor);
                Color rFc = ResolveColor(p.FontColor, _global.FontColor);
                btn.BackColor = AdjustBrightness(rKc, IsLight(rKc) ? -60 : 60);
                btn.ForeColor = AdjustBrightness(rFc, IsLight(rFc) ? -40 : 40);
                btn.FlatAppearance.BorderColor = IsLight(rKc)
                    ? Color.FromArgb(220,180,40) : Color.FromArgb(255,220,80);
                btn.FlatAppearance.BorderSize = Math.Max(2, ResolveThickness(p.BorderThickness));
            }
            else
            {
                btn.BackColor = ResolveColor(p.KeyColor,  _global.KeyColor);
                btn.ForeColor = ResolveColor(p.FontColor, _global.FontColor);
                btn.FlatAppearance.BorderColor = ResolveColor(p.BorderColor, _global.BorderColor);
                btn.FlatAppearance.BorderSize = ResolveThickness(p.BorderThickness);
            }
        }

        private static bool IsLight(Color c) => (c.R*299+c.G*587+c.B*114)/1000 > 128;
        private static Color AdjustBrightness(Color c, int d) =>
            Color.FromArgb(Math.Clamp(c.R+d,0,255),Math.Clamp(c.G+d,0,255),Math.Clamp(c.B+d,0,255));
        private static string BtnText(string s) => (s ?? "").Replace("&","&&");

        // ── Gear menu ─────────────────────────────────────────────────
        private static readonly Font   MenuFont = new Font("Segoe UI", 11.5f);
        private static readonly Color  MenuBg   = ColorTranslator.FromHtml("#1A1A2E");
        private static readonly Color  MenuFg   = ColorTranslator.FromHtml("#E0E0FF");

        private void ShowGearMenu()
        {
            var menu = StyledMenu();
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            MI(menu, Lang.T("✏ Edit Mode")  + (_mode == Mode.Edit      ? "  ✔" : ""),
               () => SetMode(_mode == Mode.Edit      ? Mode.Normal : Mode.Edit));
            MI(menu, Lang.T("⚡ Quick Edit") + (_mode == Mode.QuickEdit ? "  ✔" : ""),
               () => SetMode(_mode == Mode.QuickEdit ? Mode.Normal : Mode.QuickEdit));
            menu.Items.Add(new ToolStripSeparator());
            MI(menu, Lang.T("🖥 Edit Keyboard…"), OpenKeyboardEditor);
            menu.Items.Add(new ToolStripSeparator());
            MI(menu, Lang.T("📌 Move gear button…"), StartGearPlacement);
            menu.Show(_gearBtn, new Point(0, _gearBtn.Height));
        }

        private ContextMenuStrip StyledMenu()
        {
            var m = new ContextMenuStrip();
            m.BackColor = MenuBg; m.ForeColor = MenuFg; m.Font = MenuFont;
            return m;
        }

        private void MI(ContextMenuStrip m, string text, Action action)
        {
            var item = new ToolStripMenuItem(text)
            { BackColor = MenuBg, ForeColor = MenuFg, Font = MenuFont };
            item.Click += (s, e) => action();
            m.Items.Add(item);
        }

        // ── Key click ─────────────────────────────────────────────────
        private void OnKeyClick(GridCell cell)
        {
            switch (_mode)
            {
                case Mode.Normal:        HandleNormalClick(cell);    break;
                case Mode.Edit:          ShowKeyEditMenu(cell);      break;
                case Mode.QuickEdit:     StartQuickEdit(cell);       break;
                case Mode.GearPlacement: FinishGearPlacement(cell);  break;
            }
        }

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
            string send;
            if (altGr && !string.IsNullOrEmpty(cell.Props.AltGrSend))
                send = SendKeysHelper.ApplyModifiers(cell.Props.AltGrSend, false, ctrl, false);
            else if (shift && !string.IsNullOrEmpty(cell.Props.ShiftSend))
                send = SendKeysHelper.ApplyModifiers(cell.Props.ShiftSend, false, ctrl, alt || altGr);
            else
                send = SendKeysHelper.ApplyModifiers(cell.Props.Send, shift, ctrl, alt || altGr);

            // ── Track typed characters for word prediction ────────────
            // Use the raw resolved character (before ApplyModifiers adds SendKeys
            // prefixes like + ^ %) so TrackSendForPrediction sees "a" not "+a".
            string rawChar;
            if (altGr && !string.IsNullOrEmpty(cell.Props.AltGrSend))
                rawChar = cell.Props.AltGrSend;
            else if (shift && !string.IsNullOrEmpty(cell.Props.ShiftSend))
                rawChar = cell.Props.ShiftSend;
            else
                rawChar = cell.Props.Send;
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
        /// Called after each key send to update the word buffer and
        /// refresh WP key predictions when a word boundary is reached.
        /// </summary>
        /// <summary>
        /// Recompute predictions and refresh all wp: key labels.
        /// Call this when the word context changes (typing, space, backspace).
        /// </summary>
        private void UpdateWPKeys()
        {
            _predictor?.SetNextWordUpper(_predictor.NextWordUpper);
        }

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
                Color  fc   = ResolveColor(cell.Props.FontColor, _global.FontColor);
                // Show prediction if available, else show slot placeholder
                // Capitalise display label at sentence start / Caps Lock
                string label = pred ?? "";
                if (!string.IsNullOrEmpty(label) && (_predictor?.NextWordUpper ?? false))
                    label = char.ToUpper(label[0]) + label.Substring(1);
                btn.Tag = (label, "", "", fc);
                btn.Invalidate();
            }
        }

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

            if (!_global.StickyModifiers)
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

        private void ClearModifiers()
        {
            // Remove one-shot latched mods; keep locked mods and Caps
            _latchedMods.RemoveWhere(c =>
                c.Props.Label != "Caps" && !_lockedMods.Contains(c));
            RefreshAllButtons();
        }

        // ── Edit mode: key context menu ───────────────────────────────
        private void ShowKeyEditMenu(GridCell cell)
        {
            var menu = StyledMenu();
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            MI(menu, $"{Lang.T("✏ Edit key")}  [{cell.Props.Label}]", () => OpenEditor(cell));
            menu.Items.Add(new ToolStripSeparator());

            // Grid operations
            MI(menu, Lang.T("⬆ Add row above"),    () => { _layout.InsertRow(cell.Row, true, _global);  RebuildAllButtons(); AutoSave(); });
            MI(menu, Lang.T("⬇ Add row below"),    () => { _layout.InsertRow(cell.Row, false, _global); RebuildAllButtons(); AutoSave(); });
            MI(menu, Lang.T("⬅ Add col left"),     () => { _layout.InsertCol(cell.Col, true, _global);  RebuildAllButtons(); AutoSave(); });
            MI(menu, Lang.T("➡ Add col right"),    () => { _layout.InsertCol(cell.Col, false, _global); RebuildAllButtons(); AutoSave(); });
            menu.Items.Add(new ToolStripSeparator());
            MI(menu, Lang.T("🗑 Remove row"),  () => { if(_layout.RemoveRow(cell.Row)) { RebuildAllButtons(); AutoSave(); } });
            MI(menu, Lang.T("🗑 Remove col"),  () => { if(_layout.RemoveCol(cell.Col)) { RebuildAllButtons(); AutoSave(); } });
            menu.Items.Add(new ToolStripSeparator());

            // Merge / split
            if (cell.ColSpan > 1 || cell.RowSpan > 1)
                MI(menu, Lang.T("Split cell"), () => { _layout.SplitCell(cell.Row, cell.Col, _global); RebuildAllButtons(); AutoSave(); });
            else
            {
                MI(menu, Lang.T("Merge right"), () => { if(_layout.MergeRight(cell.Row,cell.Col)){ RebuildAllButtons(); AutoSave(); } });
                MI(menu, Lang.T("Merge down"),  () => { if(_layout.MergeDown(cell.Row,cell.Col)) { RebuildAllButtons(); AutoSave(); } });
            }

            if (_buttons.TryGetValue(cell, out var btn))
                menu.Show(btn, new Point(0, btn.Height));
        }

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
                usedWpSlots:  usedWpSlots);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            cell.Props   = dlg.Result;
            cell.ColSpan = dlg.ResultColSpan;
            cell.RowSpan = dlg.ResultRowSpan;
            if (_buttons.TryGetValue(cell, out var btn))
            {
                ApplyPropsToButton(btn, cell.Props, false);
                UpdateCornerTag(btn, cell.Props, ShiftActive, AltGrActive);
            }
            LayoutButtons();
            AutoSave();
        }

        // ── Quick Edit ────────────────────────────────────────────────
        private void SetMode(Mode newMode)
        {
            if (_mode == Mode.QuickEdit && _quickEditCell != null) CancelQuickEdit();
            _mode = newMode;
            ApplyModeIndicators();
            RefreshAllButtons();
        }

        private void StartGearPlacement()
        {
            if (_mode == Mode.QuickEdit && _quickEditCell != null) CancelQuickEdit();
            _mode = Mode.GearPlacement;
            ApplyModeIndicators();
            RefreshAllButtons();
        }

        private void FinishGearPlacement(GridCell cell)
        {
            _global.GearRow = cell.Row;
            _global.GearCol = cell.Col;
            _mode = Mode.Normal;
            ApplyModeIndicators();
            RefreshAllButtons();
            LayoutButtons();   // reposition gear button immediately
            AutoSave();
        }

        private void ApplyModeIndicators()
        {
            switch (_mode)
            {
                case Mode.Edit:
                    _editStrip.BackColor = _stripEditColor;
                    _editStrip.Visible   = true;
                    _gearBtn.Text        = "✏";
                    _gearBtn.BackColor   = _gearEditBg;
                    _gearBtn.ForeColor   = Color.White;
                    break;
                case Mode.QuickEdit:
                    _editStrip.BackColor = _stripQuickColor;
                    _editStrip.Visible   = true;
                    _gearBtn.Text        = "⚡";
                    _gearBtn.BackColor   = _gearQuickBg;
                    _gearBtn.ForeColor   = Color.White;
                    break;
                case Mode.GearPlacement:
                    _editStrip.BackColor = Color.FromArgb(80, 80, 200);  // blue
                    _editStrip.Visible   = true;
                    _gearBtn.Text        = "📌";
                    _gearBtn.BackColor   = Color.FromArgb(60, 60, 180);
                    _gearBtn.ForeColor   = Color.White;
                    break;
                default:
                    _editStrip.Visible = false;
                    _gearBtn.Text      = "⚙";
                    _gearBtn.BackColor = _gearNormalBg;
                    _gearBtn.ForeColor = _gearNormalFg;
                    break;
            }
        }

        private void StartQuickEdit(GridCell cell)
        {
            if (_quickEditCell != null && _quickEditCell != cell) ConfirmQuickEdit();
            _quickEditCell = cell; _quickEditText = "";
            if (_buttons.TryGetValue(cell, out var btn))
            {
                btn.FlatAppearance.BorderColor = Color.Orange;
                btn.FlatAppearance.BorderSize  = 2;
                btn.Text = "▌";
            }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (_mode == Mode.GearPlacement && e.KeyCode == Keys.Escape)
            {
                _mode = Mode.Normal;
                ApplyModeIndicators();
                RefreshAllButtons();
                e.Handled = true;
                return;
            }
            if (_mode != Mode.QuickEdit || _quickEditCell == null) return;
            if (e.KeyCode == Keys.Return) { ConfirmQuickEdit(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Escape) { CancelQuickEdit();  e.Handled = true; return; }
            if (e.KeyCode == Keys.Back && _quickEditText.Length > 0)
            { _quickEditText = _quickEditText[..^1]; UpdateQuickEditDisplay(); e.Handled = true; return; }
            char ch = KeyEventToChar(e);
            if (ch != '\0')
            { _quickEditText += ch; UpdateQuickEditDisplay(); e.Handled = true; }
        }

        private void UpdateQuickEditDisplay()
        {
            if (_quickEditCell != null && _buttons.TryGetValue(_quickEditCell, out var btn))
                btn.Text = _quickEditText + "▌";
        }

        private void ConfirmQuickEdit()
        {
            if (_quickEditCell == null) return;
            string lbl = _quickEditText.Trim();
            if (lbl.Length > 0)
            {
                _quickEditCell.Props.Label      = lbl;
                _quickEditCell.Props.Send       = SendKeysHelper.EscapeForSend(lbl);
                _quickEditCell.Props.ShiftLabel = "";
                _quickEditCell.Props.ShiftSend  = "";
                _quickEditCell.Props.AltGrLabel = "";
                _quickEditCell.Props.AltGrSend  = "";
                AutoSave();
            }
            RestoreQuickEditButton(_quickEditCell);
        }

        private void CancelQuickEdit()
        {
            if (_quickEditCell != null) RestoreQuickEditButton(_quickEditCell);
        }

        private void RestoreQuickEditButton(GridCell cell)
        {
            if (_buttons.TryGetValue(cell, out var btn))
            {
                ApplyPropsToButton(btn, cell.Props, _latchedMods.Contains(cell), _lockedMods.Contains(cell));
                UpdateCornerTag(btn, cell.Props, ShiftActive, AltGrActive);
            }
            _quickEditCell = null; _quickEditText = "";
        }

        private static char KeyEventToChar(KeyEventArgs e)
        {
            bool shift = e.Shift; int k = (int)e.KeyCode;
            if (k>=(int)Keys.A && k<=(int)Keys.Z) return (char)(shift ? k : k+32);
            if (k>=(int)Keys.D0 && k<=(int)Keys.D9)
            { return shift ? ")!@#$%^&*("[k-(int)Keys.D0] : (char)('0'+k-(int)Keys.D0); }
            if (k>=(int)Keys.NumPad0 && k<=(int)Keys.NumPad9) return (char)('0'+k-(int)Keys.NumPad0);
            return (e.KeyCode, shift) switch
            {
                (Keys.Space,_)               => ' ',
                (Keys.OemMinus,false)        => '-',  (Keys.OemMinus,true)         => '_',
                (Keys.Oemplus,false)         => '=',  (Keys.Oemplus,true)          => '+',
                (Keys.OemOpenBrackets,false) => '[',  (Keys.OemOpenBrackets,true)  => '{',
                (Keys.OemCloseBrackets,false)=> ']',  (Keys.OemCloseBrackets,true) => '}',
                (Keys.OemSemicolon,false)    => ';',  (Keys.OemSemicolon,true)     => ':',
                (Keys.OemQuotes,false)       => '\'', (Keys.OemQuotes,true)        => '"',
                (Keys.Oemcomma,false)        => ',',  (Keys.Oemcomma,true)         => '<',
                (Keys.OemPeriod,false)       => '.',  (Keys.OemPeriod,true)        => '>',
                (Keys.OemQuestion,false)     => '/',  (Keys.OemQuestion,true)      => '?',
                (Keys.OemBackslash,false)    => '\\', (Keys.OemBackslash,true)     => '|',
                (Keys.Oem5,false)            => '\\', (Keys.Oem5,true)             => '|',
                _                            => '\0'
            };
        }

        // ── Keyboard editor ───────────────────────────────────────────
        private void OpenKeyboardEditor()
        {
            using var dlg = new KeyboardEditorForm(_global, this,
                onSave:   () => SaveSettings(false),
                onSaveAs: () => SaveSettings(true),
                onLoad:   () => LoadSettings());
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var g = dlg.ResultGlobal;
            _global.BackgroundColor = g.BackgroundColor; _global.Opacity        = g.Opacity;
            _global.FontName        = g.FontName;         _global.FontSize       = g.FontSize;
            _global.FontColor       = g.FontColor;        _global.KeyColor       = g.KeyColor;
            _global.BorderColor     = g.BorderColor;      _global.BorderThickness = g.BorderThickness;
            _global.HideTitlebar    = g.HideTitlebar;
            _global.StickyModifiers = g.StickyModifiers;
            _global.AlwaysOnTop     = g.AlwaysOnTop;
            ApplyTitlebarState();
            ForceTopMost();  // re-apply always-on-top setting immediately
            BackColor = _global.BackgroundColor; Opacity = _global.Opacity;
            if (dlg.ApplyToKeys)
                foreach (var cell in _layout.Cells)
                {
                    cell.Props.FontName  = "";         cell.Props.FontSize   = g.FontSize;
                    cell.Props.FontColor  = Color.Empty; cell.Props.KeyColor   = Color.Empty;
                    cell.Props.BorderColor = Color.Empty; cell.Props.BorderThickness = -1;
                }
            RefreshAllButtons();
            AutoSave();
        }

        // ── Save / Load ───────────────────────────────────────────────
        private void SaveSettings(bool saveAs)
        {
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
                _global.LastFile = path;
                SettingsManager.SaveSettings(_layout, _global, path);
                _currentFilePath = path;
                if (path != SettingsManager.DefaultPath)
                    SettingsManager.SaveSettings(_layout, _global, SettingsManager.DefaultPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Lang.T("Save failed")}\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadSettings()
        {
            using var dlg = new OpenFileDialog
            { Title="Load",Filter="XML files (*.xml)|*.xml|All files (*.*)|*.*",
              FileName=_currentFilePath ?? SettingsManager.DefaultPath };
            if (dlg.ShowDialog() == DialogResult.OK) ApplyLoadedSettings(dlg.FileName);
        }

        private void TryAutoLoad()
        {
            string settingsPath = SettingsManager.DefaultPath;
            string pathToLoad   = settingsPath;
            if (File.Exists(settingsPath))
            {
                try
                {
                    var peek = new GlobalSettings();
                    SettingsManager.LoadSettings(peek, settingsPath);
                    string last = peek.LastFile ?? "";
                    if (!string.IsNullOrEmpty(last) && last != settingsPath && File.Exists(last))
                        pathToLoad = last;
                }
                catch { }
            }
            if (!File.Exists(pathToLoad)) return;
            try
            {
                var loaded = SettingsManager.LoadSettings(_global, pathToLoad);
                if (loaded == null || !loaded.IsValid()) return;
                _layout = loaded; _latchedMods.Clear(); _lockedMods.Clear();
                _currentFilePath = pathToLoad;
                BackColor = _global.BackgroundColor; Opacity = _global.Opacity;
                ApplyTitlebarState();
                if (!string.IsNullOrEmpty(_global.Language)) Lang.Load(_global.Language);
                RebuildAllButtons();
                Size = new Size(_global.WindowWidth, _global.WindowHeight);
            }
            catch { }
            // Restore predictor sentence-start state after layout load
            if (_buttons.Count > 0 && _predictor?.NextWordUpper == true)
                _predictor?.OnSentenceStart();
        }

        private void AutoSave()
        {
            string path = _currentFilePath ?? SettingsManager.DefaultPath;
            try
            {
                _global.LastFile = path;
                SettingsManager.SaveSettings(_layout, _global, path);
                if (path != SettingsManager.DefaultPath)
                    SettingsManager.SaveSettings(_layout, _global, SettingsManager.DefaultPath);
            }
            catch { }
        }

        private void ApplyLoadedSettings(string path)
        {
            GridLayout loaded = null; Exception loadEx = null;
            try { loaded = SettingsManager.LoadSettings(_global, path); }
            catch (Exception ex) { loadEx = ex; }

            if (loadEx != null)
            { ShowFileError(Path.GetFileName(path), $"{Lang.T("Invalid file detail")}\n{loadEx.Message}"); return; }
            if (loaded == null || !loaded.IsValid())
            { ShowFileError(Path.GetFileName(path), null); return; }

            _layout = loaded; _latchedMods.Clear(); _lockedMods.Clear();
            _currentFilePath = path;
            BackColor = _global.BackgroundColor; Opacity = _global.Opacity;
            ApplyTitlebarState();
            if (!string.IsNullOrEmpty(_global.Language)) Lang.Load(_global.Language);
            RebuildAllButtons();
            Size = new Size(_global.WindowWidth, _global.WindowHeight);
            AutoSave();
        }

        private void ShowFileError(string fileName, string detail)
        {
            string msg = Lang.T("Invalid file msg");
            if (!string.IsNullOrEmpty(detail)) msg += $"\n\n{detail}";
            MessageBox.Show(msg, $"{Lang.T("Invalid file title")} — {fileName}",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
