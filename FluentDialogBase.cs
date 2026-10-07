using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnScreenKeyboard
{
    /// <summary>
    /// Shared infrastructure for the three editor dialogs (KeyEditorForm, GroupEditorForm,
    /// KeyboardEditorForm). Centralises: dark/light theme detection, DPI scaling, screen-clamp
    /// on Load, live high-contrast updates, language-change refresh, and common UI-builder helpers.
    /// </summary>
    public abstract class FluentDialogBase : Form
    {
        // ── Shared infrastructure fields ─────────────────────────────────

        protected readonly bool          _dark;
        protected readonly ToolTip       _tip;
        protected readonly ErrorProvider _err;
        // Separate from _err on purpose: "this font isn't installed" is informational, not a
        // reason to block Apply/OK, so it must never be seen by HasPendingErrors().
        protected readonly ErrorProvider _fontWarn;
        // A resized copy of SystemIcons.Warning — the stock icon renders too large next to a
        // combo box. Owned by this class (unlike the shared SystemIcons.Warning), so it must
        // be disposed on FormClosed.
        private readonly Icon _fontWarnIcon;

        private UserPreferenceChangedEventHandler _onPrefChanged;

        protected readonly List<(Label   Ctrl,  Func<string> GetText)>  _transLabels
            = new List<(Label,   Func<string>)>();
        protected readonly List<(Control Ctrl,  Func<string> GetTip)>   _transTooltips
            = new List<(Control, Func<string>)>();

        // Text of buttons / check boxes / any control whose Text is a translated string.
        protected readonly List<(Control Ctrl, Func<string> GetText)> _transTexts
            = new List<(Control, Func<string>)>();

        // ── Content-sized dialogs (touch-friendly editors) ───────────────
        // A dialog built with the parameterless constructor and BuildFrame() sizes itself from its
        // content when it opens (FitToContent), instead of taking a constant size.
        private bool         _contentSized;
        private Control      _sizingRoot;
        private SectionHost  _host;
        private int          _nextTab;

        // ── Constructor ──────────────────────────────────────────────────

        /// <summary>
        /// Content-sized dialog: build the body with <see cref="BuildFrame"/>; the window opens at
        /// the size its content needs (limited to the screen) rather than at a fixed size.
        /// </summary>
        protected FluentDialogBase() : this(new Size(480, 320)) { _contentSized = true; }

        protected FluentDialogBase(Size size)
        {
            _dark = !ToolbarButton.IsLightTheme;

            AutoScaleMode       = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            BackColor       = _dark ? Fluent.DarkBg : Fluent.BgPage;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = MinimizeBox = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;
            Size            = size;
            TopMost         = true;
            Font            = Fluent.FontLabel;

            // The dialog is always-on-top like the keyboard under it. Two always-on-top windows are ordered by when each was last
            // raised, so the dialog raises itself once it is shown: it must open in front of the keyboard, never behind it.
            Shown += (s, e) => { if (TopMost) BringToFront(); ForceForeground(this); };

            _tip = new ToolTip { InitialDelay = 400, AutoPopDelay = 10000, ShowAlways = true };
            _err = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.BlinkIfDifferentError };
            // Warning-triangle icon (vs. _err's default) so the two read as different severities.
            // Sized down 10% from the stock SystemIcons.Warning, which renders too large to sit
            // comfortably to the right of a combo box.
            _fontWarnIcon = new Icon(SystemIcons.Warning,
                (int)(SystemIcons.Warning.Width * 0.9), (int)(SystemIcons.Warning.Height * 0.9));
            _fontWarn = new ErrorProvider
            { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.BlinkIfDifferentError, Icon = _fontWarnIcon };

            Load += (s, e) =>
            {
                RefreshAccelerators();
                if (_contentSized && _sizingRoot != null)
                {
                    FitToContent();
                    // The responsive tables choose their arrangement from the window they are in: once for the first size, then the
                    // height is measured again for the arrangement they chose (a narrow screen: the same content, taller).
                    PerformLayout();
                    FitToContent();
                }
                var wa = Screen.FromControl(this).WorkingArea;
                if (Width > wa.Width - 10 || Height > wa.Height - 10)
                {
                    Width  = Math.Min(Width,  wa.Width  - 10);
                    Height = Math.Min(Height, wa.Height - 10);
                }
                // The narrowest window the layout is built for: 480 design pixels (responsive_spec.md), whatever the display scaling.
                MinimumSize = new Size(Math.Min(Width, (int)Math.Round(480 * DeviceDpi / 96.0)), Math.Min(Height, 320));
                ApplyTheme();
                UpdateWrapWidths();
            };

            _onPrefChanged = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.Accessibility && IsHandleCreated && !IsDisposed)
                    BeginInvoke((Action)ApplyTheme);
            };
            SystemEvents.UserPreferenceChanged += _onPrefChanged;

            Lang.LanguageChanged += OnLanguageChanged;

            // Also released from Dispose(bool) below — a form that's constructed and disposed
            // without ever being shown (ShowDialog()/Show()) never raises FormClosed, so relying
            // on FormClosed alone leaked the static event subscriptions and the ToolTip/
            // ErrorProvider/Icon every time (the test suite's `using var f = new ...Form()`
            // pattern hits this on every run).
            FormClosed += (s, e) => ReleaseSharedResources();
        }

        // ── Cleanup ──────────────────────────────────────────────────────

        private bool _resourcesReleased;

        /// <summary>
        /// Unsubscribes the static event handlers and disposes the shared ToolTip/ErrorProvider/
        /// Icon fields. Idempotent — safe to call from both <see cref="FormClosed"/> (the normal
        /// ShowDialog() path) and <see cref="Dispose(bool)"/> (a form disposed without ever being
        /// shown), whichever happens first.
        /// </summary>
        private void ReleaseSharedResources()
        {
            if (_resourcesReleased) return;
            _resourcesReleased = true;
            Lang.LanguageChanged               -= OnLanguageChanged;
            SystemEvents.UserPreferenceChanged -= _onPrefChanged;
            _tip?.Dispose();
            _err?.Dispose();
            _fontWarn?.Dispose();
            _fontWarnIcon?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseSharedResources();
            base.Dispose(disposing);
        }

        // ── Virtual hooks ────────────────────────────────────────────────

        /// <summary>
        /// Re-applies the Fluent / high-contrast theme to the whole form.
        /// Called automatically when the user toggles high-contrast mode while the dialog is open.
        /// Override to pass form-specific exclusions to <see cref="FluentPainter.ApplyDialogTheme"/>.
        /// </summary>
        protected virtual void ApplyTheme() =>
            FluentPainter.ApplyDialogTheme(this, _dark);

        /// <summary>
        /// Called when the application language changes.
        /// Refreshes every label, group-panel header, and tooltip registered via
        /// <see cref="AddFieldLabel"/> and <see cref="SetTip"/>, then invalidates the form.
        /// Override to also update form-specific controls (button text, title, etc.);
        /// call <c>base.OnLanguageChanged()</c> first.
        /// </summary>
        protected virtual void OnLanguageChanged()
        {
            foreach (var (ctrl, getText) in _transLabels)   ctrl.Text = getText();
            foreach (var (ctrl, getText) in _transTexts)    ctrl.Text = getText();
            foreach (var (ctrl, getTip)  in _transTooltips) _tip.SetToolTip(ctrl, getTip());
            Sections?.RefreshTitles();
            if (_accelReady) RefreshAccelerators();          // the letters depend on the words
            Invalidate(true);
            // A longer translation may need more room: grow (never shrink) once layout has settled.
            if (_contentSized && _sizingRoot != null && IsHandleCreated)
                BeginInvoke((Action)GrowToContent);
        }

        // ── Alt+letter accelerators (see Accelerators.cs) ────────────────

        private bool _accelReady, _accelBusy, _accelQueued;
        private readonly HashSet<Control> _accelWatched = new HashSet<Control>();

        /// <summary>One control that carries an accelerator: where it is (scope -1 = the frame, else the section or page), and its letter ('\0': none left).</summary>
        internal sealed class AccelEntry
        {
            public Control Control; public int Scope; public char Letter; public string Plain; public char Preferred;
        }

        /// <summary>Whether a control takes part in the accelerators: it is reached by its text, and the text has a letter to mark.</summary>
        private static bool TakesAccelerator(Control c, out string plain)
        {
            plain = null;
            if (c.IsDisposed) return false;
            if (c is AccelLabel al) { if (al.Targets.Count == 0 || !al.HasReachableTarget()) return false; }
            else if (c is TouchChoiceButton) return false;                 // its text is the chosen item; its label carries the letter
            else if (!(c is FluentButton || c is ColorChip || c is TouchCheckBox || c is TouchRadioButton)) return false;
            plain = Accel.Plain(c.Text);
            return plain.Any(char.IsLetterOrDigit);
        }

        private void CollectAccelerators(Control parent, List<AccelEntry> into)
        {
            foreach (Control c in parent.Controls)
            {
                if (TakesAccelerator(c, out string plain))
                    into.Add(new AccelEntry { Control = c, Scope = ScopeOf(c), Plain = plain, Preferred = Accel.Marked(c.Text) });
                CollectAccelerators(c, into);
            }
        }

        /// <summary>
        /// Gives every control that is reached by its name an Alt+letter, in the language now on screen, and writes the "&amp;" into its text.
        /// Letters are unique within the frame together with one section (or page): a section's letters may be used again in another section.
        /// A "&amp;" already in a text is a preference, kept when the letter is free. Runs when the dialog opens, when the language changes and
        /// when the text of one of these controls changes. Returns what was assigned (for the tests).
        /// </summary>
        internal List<AccelEntry> RefreshAccelerators()
        {
            _accelReady = true;
            var all = new List<AccelEntry>();
            CollectAccelerators(this, all);          // Scope: the section the control sits in (-1 = outside the sections)

            var frameTaken = new HashSet<char>();
            AssignLetters(all.Where(e => e.Scope < 0).ToList(), frameTaken);
            foreach (var group in all.Where(e => e.Scope >= 0).GroupBy(e => e.Scope))
                AssignLetters(group.ToList(), new HashSet<char>(frameTaken));

            _accelBusy = true;
            try
            {
                foreach (var e in all)
                {
                    string text = e.Letter == '\0' ? e.Plain : Accel.Mark(e.Plain, e.Letter);
                    if (e.Control.Text != text) e.Control.Text = text;
                    if (_accelWatched.Add(e.Control)) e.Control.TextChanged += OnAccelTextChanged;
                }
            }
            finally { _accelBusy = false; }
            return all;
        }

        private int ScopeOf(Control c)
        {
            if (_host == null) return -1;
            for (Control p = c; p != null && p.Parent != null; p = p.Parent)
                if (p.Parent == _host)
                    for (int i = 0; i < _host.SectionCount; i++) if (_host.SectionAt(i) == p) return i;
            return -1;
        }

        private static void AssignLetters(List<AccelEntry> list, HashSet<char> taken)
        {
            // 1. A letter that was asked for (written into a string) and is free.
            // Each takes the first of its letters that is free: the one asked for in its string, then the word initials, then any other; when none is
            // free, a control that already holds one of them moves to another of its own letters if it can (augmenting paths), so nobody is left
            // without while a letter remains.
            var open = list.ToList();
            var holder = new Dictionary<char, AccelEntry>();
            foreach (var e in open)
                TryPlace(e, open, taken, holder, new HashSet<AccelEntry>());
            foreach (var kv in holder) { kv.Value.Letter = kv.Key; taken.Add(kv.Key); }
        }

        private static bool TryPlace(AccelEntry e, List<AccelEntry> open, HashSet<char> taken, Dictionary<char, AccelEntry> holder, HashSet<AccelEntry> seen)
        {
            if (!seen.Add(e)) return false;
            var options = Accel.Preferred(e.Plain).Where(c => !taken.Contains(c)).ToList();
            if (e.Preferred != '\0' && options.Remove(e.Preferred)) options.Insert(0, e.Preferred);
            foreach (char c in options)
                if (!holder.ContainsKey(c)) { holder[c] = e; return true; }
            foreach (char c in options)
            {
                var other = holder[c];
                if (TryPlace(other, open, taken, holder, seen)) { holder[c] = e; return true; }
            }
            return false;
        }

        private void OnAccelTextChanged(object sender, EventArgs e)
        {
            if (_accelBusy || IsDisposed) return;
            if (!IsHandleCreated) { RefreshAccelerators(); return; }
            if (_accelQueued) return;
            _accelQueued = true;
            BeginInvoke((Action)(() => { _accelQueued = false; if (!IsDisposed) RefreshAccelerators(); }));
        }

        // ── Shared UI-builder helpers ────────────────────────────────────

        /// <summary>
        /// Registers a tooltip on <paramref name="ctrl"/> and adds the factory to
        /// <see cref="_transTooltips"/> so it refreshes automatically on language change.
        /// </summary>
        protected void SetTip(Control ctrl, Func<string> getTip)
        {
            _tip.SetToolTip(ctrl, getTip());
            _transTooltips.Add((ctrl, getTip));
        }

        /// <summary>Parses a hex colour string; returns <paramref name="fallback"/> on failure.</summary>
        protected static Color ParseColor(string hex, Color fallback) =>
            SettingsManager.ParseColor(hex, fallback);

        /// <summary>
        /// Selects <paramref name="fontName"/> in <paramref name="combo"/>. If the name isn't
        /// installed (so it's not already one of the combo's items), it's inserted rather than
        /// silently substituted for whatever happens to be first in the list — a layout authored
        /// on a machine that had this font must keep saying so on one that doesn't, or the real
        /// font name gets permanently overwritten the next time the form is applied. Inserted at
        /// index 1, not 0, so it never collides with a reserved "(inherit standard)" / "(none)"
        /// placeholder some callers keep at index 0 — harmless for callers with no such
        /// placeholder, since the list is otherwise just alphabetically sorted installed fonts.
        /// </summary>
        protected static void SelectOrInsertFont(ComboBox combo, string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
                return;
            }
            int idx = combo.Items.IndexOf(fontName);
            if (idx < 0)
            {
                idx = Math.Min(1, combo.Items.Count);
                combo.Items.Insert(idx, fontName);
            }
            combo.SelectedIndex = idx;
        }

        /// <summary>
        /// The <see cref="TouchChoiceButton"/> version of <see cref="SelectOrInsertFont(ComboBox, string)"/>: selects
        /// <paramref name="name"/> without raising the change event; a font that isn't installed is inserted (at index 1,
        /// after a possible "(inherit)" placeholder at index 0), not substituted. An empty name selects index 0.
        /// </summary>
        internal static void SelectOrInsertFont(TouchChoiceButton chooser, string name)
        {
            if (string.IsNullOrEmpty(name)) { chooser.SelectSilently(0); return; }
            int idx = chooser.Items.FindIndex(i => i.Text == name);
            if (idx < 0)
            {
                idx = Math.Min(1, chooser.Items.Count);
                chooser.Items.Insert(idx, new TouchChoice { Text = name });
            }
            chooser.SelectSilently(idx);
        }

        /// <summary>
        /// Shows (or clears) a warning-triangle icon on <paramref name="combo"/> when
        /// <paramref name="fontName"/> is set but isn't installed on this machine. Purely
        /// informational — uses <see cref="_fontWarn"/>, never <see cref="_err"/>, so it can
        /// never block Apply/OK via <see cref="HasPendingErrors"/>. Pass <c>""</c> for
        /// <paramref name="fontName"/> when the combo is showing a non-font placeholder like
        /// "(inherit standard)" so it isn't itself flagged as a missing font.
        /// </summary>
        protected void UpdateFontAvailabilityWarning(Control combo, string fontName)
        {
            bool missing = !string.IsNullOrEmpty(fontName) && !Fluent.IsFontAvailable(fontName);
            _fontWarn.SetError(combo, missing
                ? string.Format(Lang.T("warn: font not installed"), fontName)
                : "");
        }

        /// <summary>
        /// Returns <c>true</c> if any control under <paramref name="root"/> (the whole form, by
        /// default) currently has a non-empty <see cref="_err"/> message set on it — e.g. an
        /// invalid hex colour flagged by <see cref="AddColorRow"/>. Call this from Apply/OK
        /// handlers so the ErrorProvider icon actually blocks committing invalid data instead of
        /// being purely decorative.
        /// </summary>
        protected bool HasPendingErrors(Control root = null)
        {
            foreach (Control c in (root ?? this).Controls)
            {
                if (!string.IsNullOrEmpty(_err.GetError(c))) return true;
                if (HasPendingErrors(c)) return true;
            }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Content-sized, sectioned, touch-friendly layout
        // ══════════════════════════════════════════════════════════════════

        /// <summary>The section buttons (null for a dialog built without sections).</summary>
        protected SectionBar Sections { get; private set; }

        /// <summary>Widest the content may grow before it is limited, in design pixels (the default for <see cref="ContentMaxWidth"/>).</summary>
        protected const int MaxContentWidth = 760;

        /// <summary>The widest this dialog may grow in design pixels; a dialog with two columns (the Group Editor) allows more.</summary>
        protected virtual int ContentMaxWidth => MaxContentWidth;

        /// <summary>
        /// Builds the standard frame of a content-sized dialog and adds it to the form:
        /// [section buttons] / [body that scrolls only if the screen is too small] / [footer].
        /// Add the body with <see cref="AddSection"/>; the footer comes from <see cref="MakeFooter"/>.
        /// </summary>
        protected void BuildFrame(Control footer, bool withSections, Control headerRight = null)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = withSections ? 3 : 2,
                Padding = new Padding(Fluent.Pad), AutoSize = false,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;
            if (withSections)
            {
                Sections = new SectionBar { Anchor = AnchorStyles.Left | AnchorStyles.Right };
                Sections.SelectedIndexChanged += (s, e) => { _host.ShowSection(Sections.SelectedIndex); UpdateWrapWidths(); };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Control top = Sections;
                if (headerRight != null)
                {
                    // Section buttons on the left, a fixed control (e.g. the preview key) on the right.
                    var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left | AnchorStyles.Right };
                    t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    Sections.Dock = DockStyle.Fill;
                    headerRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    t.Controls.Add(Sections, 0, 0);
                    t.Controls.Add(headerRight, 1, 0);
                    top = t;
                }
                root.Controls.Add(top, 0, row++);
            }
            _host = new SectionHost { Dock = DockStyle.Fill };
            _host.SizeChanged += (s, e) => { UpdateWrapWidths(); AdaptiveTable.ReselectAll(_host); };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(_host, 0, row++);
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(footer, 0, row);
            Controls.Add(root);
            _sizingRoot = root;
        }

        /// <summary>
        /// Adds a section (a tab of the dialog, or the whole body when there is no section bar) and
        /// returns its two-column table: label column sized to the longest label, input column filling.
        /// </summary>
        protected TableLayoutPanel AddSection(Func<string> title)
        {
            var t = NewTable();
            _host.Add(t);
            Sections?.Add(title);
            return t;
        }

        /// <summary>The section buttons, for the UI guard tests (which switch section on any dialog).</summary>
        internal SectionBar SectionBarAccess => Sections;

        /// <summary>Number of sections or pages added with <see cref="AddSection"/>.</summary>
        internal int HostSectionCount => _host?.SectionCount ?? 0;

        /// <summary>Shows one section or page by index (for a dialog without a section bar, such as a wizard).</summary>
        protected void ShowHostSection(int index) { _host.ShowSection(index); UpdateWrapWidths(); }

        // ── Text that wraps at the width the window allows ───────────────
        // A label, check box or radio button added with Wrap() is never wider than what is left of the window to its right, so a long text
        // takes more lines instead of being cut off. The limit is kept up to date when the window is resized, a section is shown or the
        // language changes; before the window exists it is the dialog's widest allowed content (design pixels).

        private readonly List<Control> _wrapControls = new List<Control>();
        private readonly Dictionary<Control, int> _wrapCaps = new Dictionary<Control, int>();      // a width the control was given on purpose (0: none)
        private readonly Dictionary<Control, int> _wrapReserve = new Dictionary<Control, int>();   // room kept free to the right (design pixels)

        /// <summary>
        /// Registers a control whose width is limited to the room the window leaves it (text, and anything that keeps its own width);
        /// returns the control. A <see cref="Control.MaximumSize"/> set before is kept as the upper limit.
        /// </summary>
        /// <param name="control">The control to limit.</param>
        /// <param name="reserveRight">Room to keep free to its right (a button standing beside it), in design pixels.</param>
        protected T Wrap<T>(T control, int reserveRight = 0) where T : Control
        {
            if (_wrapCaps.ContainsKey(control)) return control;
            int cap = control.MaximumSize.Width;
            _wrapCaps[control] = cap;
            _wrapReserve[control] = reserveRight;
            control.MaximumSize = new Size(cap > 0 ? cap : ContentMaxWidth - 4 * Fluent.Pad - reserveRight, 0);
            _wrapControls.Add(control);
            return control;
        }

        /// <summary>Sets the width limit of every registered text control from the window as it is now.</summary>
        internal void UpdateWrapWidths()
        {
            if (_host == null || !_host.IsHandleCreated || _wrapControls.Count == 0) return;            // The body scrolls vertically; the room for its scroll bar is kept free (it appears when the content gets taller than the window).
            int hostRight = _host.RectangleToScreen(_host.ClientRectangle).Right - SystemInformation.VerticalScrollBarWidth;
            foreach (var c in _wrapControls)
            {
                if (c.IsDisposed || c.Parent == null || !c.Visible) continue;
                int chrome = c.Margin.Right;
                for (Control a = c.Parent; a != null && a != _host; a = a.Parent)
                    chrome += a.Padding.Right + (a is TouchGroup ? 3 : 0);
                int left = c.Parent.RectangleToScreen(new Rectangle(c.Left, 0, 0, 0)).X;
                int avail = Math.Max(80, hostRight - left - chrome - (int)Math.Round(_wrapReserve[c] * DeviceDpi / 96.0));
                int cap = _wrapCaps[c];
                if (cap > 0) avail = Math.Min(avail, (int)Math.Round(cap * DeviceDpi / 96.0));       // the cap was given in design pixels
                if (Math.Abs(c.MaximumSize.Width - avail) > 1) c.MaximumSize = new Size(avail, 0);
            }
        }

        /// <summary>For the UI guard tests: shows section (or wizard page) <paramref name="index"/> the way the user would reach it.</summary>
        internal virtual void ShowSectionForGuard(int index)
        {
            if (Sections != null) Sections.Select(index, focus: false);
            else if (_host != null && _host.SectionCount > 1) _host.ShowSection(index);
        }

        /// <summary>Index of the visible section.</summary>
        protected int SelectedSection => Sections?.SelectedIndex ?? 0;

        protected void ShowSection(int index) => Sections?.Select(index, focus: false);

        /// <summary>
        /// Puts a warning marker on every section that holds a validation error, and switches to
        /// the first of them. Returns false (and does nothing) when no section has an error. Call
        /// from Apply so an error on a hidden section is never overlooked.
        /// </summary>
        protected bool ShowFirstSectionWithError()
        {
            if (Sections == null) return HasPendingErrors();
            int first = -1;
            for (int i = 0; i < _host.SectionCount; i++)
            {
                bool bad = HasPendingErrors(_host.SectionAt(i));
                Sections.SetError(i, bad);
                if (bad && first < 0) first = i;
            }
            if (first >= 0 && first != Sections.SelectedIndex) Sections.Select(first, focus: false);
            return first >= 0;
        }

        /// <summary>A section table: labels in an auto-sized column, inputs in the rest, rows sized by content.</summary>
        protected TableLayoutPanel NewTable()
        {
            var t = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Fluent.Pad), Dock = DockStyle.Top,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        /// <summary>
        /// Adds "label | input" as a new row. The label sizes to its text (wrapping past
        /// <see cref="Touch.LabelMaxWidth"/>); the input is at least 44 px tall and fills the column,
        /// or keeps its own width when <paramref name="fill"/> is false (steppers, short fields).
        /// </summary>
        protected Label AddRow(TableLayoutPanel t, Func<string> label, Control input, bool fill = true)
        {
            var lbl = new AccelLabel
            {
                Text = label(), AutoSize = true,
                Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, Fluent.Pad, 4),
                MaximumSize = new Size(Touch.LabelMaxWidth, 0),
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = Fluent.FontLabel,
                TabIndex = _nextTab++,
            };
            _transLabels.Add((lbl, label));
            lbl.SetTargets(input);
            NameInput(input, Lang.StripMnemonic(label()));

            PrepareInput(input, fill);
            // A container that keeps its own width (a group, a stack of options, a row) is never wider than the window allows. Not a
            // single control such as a stepper: a MaximumSize on one of those left it with no height while the row was measured.
            if (!fill && input is Panel) Wrap(input);
            input.TabIndex = _nextTab++;
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(lbl,   0, r);
            t.Controls.Add(input, 1, r);
            return lbl;
        }

        /// <summary>Adds a control spanning both columns (a check box, a button row, a heading).</summary>
        protected void AddWideRow(TableLayoutPanel t, Control c, bool fill = true)
        {
            PrepareInput(c, fill);
            if (!fill && c is Panel) Wrap(c);        // see AddRow
            c.TabIndex = _nextTab++;
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
        }

        /// <summary>A touch-sized check box whose text follows language changes; add it with <see cref="AddWideRow"/>.</summary>
        protected TouchCheckBox NewCheck(Func<string> text)
        {
            var c = Wrap(new TouchCheckBox { Text = text() });
            _transTexts.Add((c, text));
            return c;
        }

        /// <summary>A touch-sized radio button whose text follows language changes. Radio buttons with the same parent are one group.</summary>
        protected TouchRadioButton NewRadio(Func<string> text)
        {
            var r = Wrap(new TouchRadioButton { Text = text() });
            _transTexts.Add((r, text));
            return r;
        }

        /// <summary>
        /// Controls side by side on one line, each in a column that sizes to it; when they do not fit the width the window leaves
        /// them, they go one per line (an <see cref="AdaptiveTable"/>). Used instead of a wrapping FlowLayoutPanel: inside a table
        /// a flow panel is measured at a narrow width first, which inflated the table's height and opened a gap in its last row.
        /// </summary>
        protected static TableLayoutPanel InlineRow(params Control[] items)
        {
            foreach (var c in items) c.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            var row = ReflowRows.Rows(items, Enumerable.Range(1, items.Length).Reverse().ToArray());       // all on one line, else one fewer, … one per line
            // A little room on the right: an auto-sized column can come out a pixel or two narrower than the long caption it holds.
            row.Padding = new Padding(0, 0, Touch.Gap, 0);
            return row;
        }

        /// <summary>
        /// Alignment rule (spec D23): controls that stand under each other get <b>one width, the widest of them</b>, so their right
        /// edges line up. A one-column table whose column sizes to its widest member; every member fills it.
        /// </summary>
        protected static TableLayoutPanel OptionStack(params Control[] items)
        {
            var t = new OptionStackPanel { ColumnCount = 1, RowCount = items.Length, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < items.Length; i++)
            {
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                items[i].Dock = DockStyle.Fill;
                items[i].Margin = new Padding(0, 4, 0, 4);
                t.Controls.Add(items[i], 0, i);
            }
            return t;
        }

        /// <summary>
        /// Alignment rule (spec D23): buttons side by side get <b>one common width, the widest</b>. Re-measured when a button's text
        /// changes (language), so the buttons stay equal.
        /// </summary>
        protected static TableLayoutPanel ButtonRow(params Control[] buttons) => ReflowRows.Buttons(buttons, buttons.Length, 1);

        /// <summary>A bold heading in the label column of a section table, spanning both columns; its text follows language changes.</summary>
        protected AccelLabel Heading(Func<string> text)
        {
            var lbl = new AccelLabel
            {
                Text = text(), AutoSize = true, Anchor = AnchorStyles.Left, Font = Fluent.FontBtnLg,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Touch.Gap),
            };
            _transLabels.Add((lbl, text));
            return lbl;
        }

        /// <summary>A framed group with a caption (see <see cref="TouchGroup"/>); the caption follows language changes.</summary>
        internal TouchGroup MakeGroup(Func<string> caption, Control content)
        {
            var g = new TouchGroup(caption());
            g.SetContent(content);
            _transLabels.Add((g.CaptionLabel, () => { string s = caption(); g.AccessibleName = Lang.StripMnemonic(s); return s; }));
            return g;
        }

        private static void NameInput(Control input, string name)
        {
            if (input is TouchStepper st) { if (string.IsNullOrEmpty(st.AccessibleName)) st.AccessibleName = name; }
            else if (string.IsNullOrEmpty(input.AccessibleName)) input.AccessibleName = name;
        }

        private static void PrepareInput(Control input, bool fill)
        {
            input.Margin = new Padding(0, 4, 0, 4);
            if (fill)
            {
                if (input is TextBoxBase || input is ComboBox)
                    input.MinimumSize = new Size(Math.Max(input.MinimumSize.Width, Touch.InputMinWidth), Touch.Target);
                input.Dock = DockStyle.Fill;
            }
            else input.Anchor = AnchorStyles.Left;
        }

        /// <summary>A 44 px button for a footer or button row; the text follows language changes.</summary>
        protected FluentButton MakeTouchButton(Func<string> text, FluentButton.Variant style = FluentButton.Variant.Neutral)
        {
            var b = new FluentButton
            {
                Text = text(), Style = style, TabStop = true, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 0, 16, 0),
                MinimumSize = new Size(120, Touch.Target), Margin = new Padding(Touch.Gap, 0, 0, 0),
            };
            _transTexts.Add((b, text));
            return b;
        }

        /// <summary>
        /// Footer row: an optional <paramref name="leftSlot"/> (e.g. the live preview), flexible
        /// space, then the buttons right-aligned in the order given.
        /// </summary>
        protected TableLayoutPanel MakeFooter(Control leftSlot, params Control[] buttons)
        {
            var f = new AdaptiveTable
            {
                ColumnCount = 2 + buttons.Length,
                Padding = new Padding(0, Touch.Gap, 0, 0),
            };
            f.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < buttons.Length; i++) f.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            if (leftSlot != null) leftSlot.Anchor = AnchorStyles.Left;
            foreach (var b in buttons) b.Anchor = AnchorStyles.Right;
            // One line: the left slot, then the buttons at the right. When they do not fit together the buttons take a line of their own
            // under the left slot (the wizard's "Step 2 of 4").
            f.AddVariant(() =>
            {
                var cells = new List<AdaptiveTable.Cell>();
                if (leftSlot != null) cells.Add(new AdaptiveTable.Cell(leftSlot, 0, 0));
                for (int i = 0; i < buttons.Length; i++) cells.Add(new AdaptiveTable.Cell(buttons[i], 2 + i, 0));
                return cells;
            });
            if (leftSlot != null)
                f.AddVariant(() =>
                {
                    var cells = new List<AdaptiveTable.Cell> { new AdaptiveTable.Cell(leftSlot, 0, 0, 2 + buttons.Length) };
                    for (int i = 0; i < buttons.Length; i++) cells.Add(new AdaptiveTable.Cell(buttons[i], 2 + i, 1));
                    return cells;
                });
            return f;
        }

        // ── Sizing ───────────────────────────────────────────────────────

        /// <summary>
        /// The client size the content wants, for a given available width, in the pixels of the
        /// current display scaling. <paramref name="maxWidth"/> defaults to
        /// <see cref="MaxContentWidth"/> scaled to this form.
        /// </summary>
        internal Size MeasureContent(int? maxWidth = null)
        {
            if (_sizingRoot == null) return ClientSize;
            int w = maxWidth ?? (int)Math.Round(ContentMaxWidth * DeviceDpi / 96.0);

            // A TableLayoutPanel ignores the content of a percent-sized row when asked for its
            // preferred size (the body row is percent-sized so it can shrink and scroll), so the
            // frame is measured explicitly: padding + every stacked child + its margins.
            var pad   = _sizingRoot.Padding;
            int avail = Math.Max(0, w - pad.Horizontal);
            int width = 0, height = pad.Vertical;
            foreach (Control c in _sizingRoot.Controls)
            {
                var p = c.GetPreferredSize(new Size(Math.Max(0, avail - c.Margin.Horizontal), 0));
                width   = Math.Max(width, p.Width + c.Margin.Horizontal);
                height += p.Height + c.Margin.Vertical;
            }
            return new Size(width + pad.Horizontal, height);
        }

        // ── Coming to the front ──────────────────────────────────────────
        // The keys of the keyboard answer a click with MA_NOACTIVATE (the program being typed into keeps the focus), so when a dialog
        // is opened from a key the keyboard's process is not the foreground process. Windows then puts a window of that process
        // behind the foreground program, or where it happens to fit: in front of the keyboard one time and behind another program
        // the next. A dialog therefore takes the foreground itself once it is shown.

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        /// <summary>Makes <paramref name="form"/> the foreground window although its process was not (briefly sharing the input queue of the current foreground window, the usual way to be allowed).</summary>
        internal static void ForceForeground(Form form)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            // Not for the invisible windows of the tests and the gallery: they must not take the focus from whatever the user is doing.
            if (SendKeysHelper.TestMode || form.Opacity < 0.1) return;
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == form.Handle) return;
                uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
                uint me = GetCurrentThreadId();
                bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
                try { SetForegroundWindow(form.Handle); }
                finally { if (attached) AttachThreadInput(me, fgThread, false); }
            }
            catch (Exception) { /* a dialog that cannot take the foreground still works: it is just not raised */ }
        }

        /// <summary>Sizes the window to its content, limited to the screen's working area, and centres it on its parent.</summary>
        protected void FitToContent()
        {
            if (_sizingRoot == null) return;
            var wa   = Screen.FromControl(this).WorkingArea;
            var nonC = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
            int maxW = wa.Width  - 10 - nonC.Width;
            int maxH = wa.Height - 10 - nonC.Height;
            int widest = Math.Min((int)Math.Round(ContentMaxWidth * DeviceDpi / 96.0), maxW);
            // The responsive tables choose their arrangement from the window they are in. The window has its small starting size at this
            // point, so it is first made as wide as it may become: the tables then take their widest arrangement, and the width and height
            // measured are those of the dialog at its best. (Otherwise it opened narrow and tall.)
            ClientSize = new Size(widest, ClientSize.Height);
            PerformLayout();
            var pref = MeasureContent(widest);
            ClientSize = new Size(Math.Min(pref.Width, maxW), Math.Min(pref.Height, maxH));
            if (Owner != null || StartPosition == FormStartPosition.CenterParent)
            {
                var anchor = Owner != null ? Owner.Bounds : wa;
                Location = new Point(
                    Math.Max(wa.Left, Math.Min(anchor.Left + (anchor.Width  - Width)  / 2, wa.Right  - Width)),
                    Math.Max(wa.Top,  Math.Min(anchor.Top  + (anchor.Height - Height) / 2, wa.Bottom - Height)));
            }
        }

        /// <summary>After a content change (language, font): grows the window if the content no longer fits; never shrinks it.</summary>
        private void GrowToContent()
        {
            if (IsDisposed || _sizingRoot == null) return;
            var wa   = Screen.FromControl(this).WorkingArea;
            var nonC = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
            var pref = MeasureContent(ClientSize.Width);
            int w = Math.Min(Math.Max(ClientSize.Width,  pref.Width),  wa.Width  - 10 - nonC.Width);
            int h = Math.Min(Math.Max(ClientSize.Height, pref.Height), wa.Height - 10 - nonC.Height);
            if (w != ClientSize.Width || h != ClientSize.Height) ClientSize = new Size(w, h);
        }

        /// <summary>Ctrl+Tab / Ctrl+Shift+Tab (and Ctrl+PageDown / PageUp) switch section from anywhere in the dialog.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Sections != null && Sections.Count > 1)
            {
                if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown))
                { Sections.SelectNext(+1, focus: false); return true; }
                if (keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.PageUp))
                { Sections.SelectNext(-1, focus: false); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
