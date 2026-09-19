using System;
using System.Collections.Generic;
using System.Drawing;
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
        protected readonly List<(Panel   Pnl,   Func<string> GetTitle)> _transGroups
            = new List<(Panel,   Func<string>)>();
        protected readonly List<(Control Ctrl,  Func<string> GetTip)>   _transTooltips
            = new List<(Control, Func<string>)>();

        // Text of buttons / check boxes / any control whose Text is a translated string.
        protected readonly List<(Control Ctrl, Func<string> GetText)> _transTexts
            = new List<(Control, Func<string>)>();

        protected string _pendingAccessibleName;

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
                if (_contentSized && _sizingRoot != null) FitToContent();
                var wa = Screen.FromControl(this).WorkingArea;
                if (Width > wa.Width - 10 || Height > wa.Height - 10)
                {
                    Width  = Math.Min(Width,  wa.Width  - 10);
                    Height = Math.Min(Height, wa.Height - 10);
                }
                MinimumSize = new Size(Math.Min(Width, 480), Math.Min(Height, 320));
                ApplyTheme();
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
            foreach (var (pnl,  _)       in _transGroups)   pnl.Invalidate();
            foreach (var (ctrl, getTip)  in _transTooltips) _tip.SetToolTip(ctrl, getTip());
            Sections?.RefreshTitles();
            Invalidate(true);
            // A longer translation may need more room: grow (never shrink) once layout has settled.
            if (_contentSized && _sizingRoot != null && IsHandleCreated)
                BeginInvoke((Action)GrowToContent);
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

        /// <summary>
        /// Adds a field label to a card panel, registers it for language-change refresh,
        /// and sets <see cref="_pendingAccessibleName"/> so the next <see cref="AddColorRow"/>
        /// or sibling input can automatically inherit the label text as its accessible name.
        /// </summary>
        protected Label AddFieldLabel(Panel parent, Func<string> getText, int x, int y)
        {
            var lbl = new Label
            {
                Text = getText(), Left = x, Top = y + 6, AutoSize = true,
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = Fluent.FontLabel,
            };
            parent.Controls.Add(lbl);
            _transLabels.Add((lbl, getText));
            _pendingAccessibleName = Lang.StripMnemonic(getText());
            return lbl;
        }

        /// <summary>
        /// Creates a card panel with a coloured header bar, adds it to the form, and registers it
        /// for language-change repaints (header text re-evaluated from <paramref name="getTitle"/>).
        /// </summary>
        /// <param name="hdrH">Height of the painted header strip in pixels (default 42).</param>
        protected Panel AddGroup(Func<string> getTitle, int x, int y, int w, int h, Color accentColor, int hdrH = 42)
        {
            Color bg = _dark ? Fluent.DialogDarkCard : Fluent.BgCard;
            var pnl = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = bg };
            bool dark = _dark;
            pnl.Paint += (s, e) =>
                FluentPainter.PaintCard(e.Graphics, pnl.Width, pnl.Height, getTitle(), accentColor, hdrH, dark);
            Controls.Add(pnl);
            _transGroups.Add((pnl, getTitle));
            return pnl;
        }

        /// <summary>
        /// Adds a colour-picker row (hex <see cref="TextBox"/> + <see cref="ColorSwatchButton"/>)
        /// to a parent panel.  The swatch <see cref="Control.Tag"/> holds the TextBox reference so
        /// <see cref="GetSwatchHex"/> / <see cref="SetSwatchHex"/> can reach it.
        /// </summary>
        /// <param name="onChanged">
        /// Optional callback invoked inside the TextChanged handler — use to trigger a live
        /// preview refresh (e.g. pass <c>Refresh2</c> from <see cref="KeyEditorForm"/>).
        /// </param>
        protected Button AddColorRow(Panel parent, int x, int y, int totalW, ref int ti, Action onChanged = null)
        {
            int sw = 32;
            var txtHex = new TextBox
            {
                Left = x, Top = y, Width = totalW - sw - 5,
                BackColor = Fluent.BgInput, ForeColor = Fluent.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle, Font = Fluent.FontCourier,
                TabIndex = ti++,
            };
            var swatch = new ColorSwatchButton
            {
                Left = x + totalW - sw, Top = y, Width = sw, Height = 26,
                BackColor = Color.Gray,
                TabIndex = ti++,
            };
            string colorName = _pendingAccessibleName;
            _pendingAccessibleName = null;
            if (colorName != null)
            {
                txtHex.AccessibleName  = colorName + " hex";
                swatch.AccessibleName  = colorName + " swatch";
            }
            SetTip(txtHex,  () => Lang.T("tip: Hex color"));
            SetTip(swatch,  () => Lang.T("tip: Color swatch"));
            txtHex.TextChanged += (s, e) =>
            {
                var parsed = ParseColor(txtHex.Text, Color.Empty);
                swatch.BackColor = parsed.IsEmpty ? swatch.BackColor : parsed;
                if (!_suppressOnChanged) onChanged?.Invoke();
                bool bad = !string.IsNullOrWhiteSpace(txtHex.Text) && parsed.IsEmpty;
                if (!_suppressOnChanged) _err.SetError(txtHex, bad ? Lang.T("err: invalid hex") : "");
            };
            swatch.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { Color = swatch.BackColor };
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtHex.Text = SettingsManager.Hex(dlg.Color);
            };
            parent.Controls.Add(txtHex);
            parent.Controls.Add(swatch);
            swatch.Tag = txtHex;
            return swatch;
        }

        /// <summary>Returns the hex string from the TextBox paired with this swatch button.</summary>
        protected string GetSwatchHex(Button s) => s.Tag is TextBox t ? t.Text : "";

        /// <summary>Writes a hex string into the TextBox paired with this swatch and updates its background.
        /// Suppresses the <c>onChanged</c> callback and error-provider update for the duration so
        /// bulk population (e.g. loading a key's three colour fields) does not trigger spurious
        /// preview redraws or validation errors mid-load.</summary>
        protected void SetSwatchHex(Button s, string hex)
        {
            if (s.Tag is TextBox t)
            {
                _suppressOnChanged = true;
                t.Text = hex;
                _suppressOnChanged = false;
                s.BackColor = ParseColor(hex, s.BackColor);
            }
        }

        // Flag set by SetSwatchHex to suppress onChanged/error-provider during bulk population.
        private bool _suppressOnChanged;

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
        /// Shows (or clears) a warning-triangle icon on <paramref name="combo"/> when
        /// <paramref name="fontName"/> is set but isn't installed on this machine. Purely
        /// informational — uses <see cref="_fontWarn"/>, never <see cref="_err"/>, so it can
        /// never block Apply/OK via <see cref="HasPendingErrors"/>. Pass <c>""</c> for
        /// <paramref name="fontName"/> when the combo is showing a non-font placeholder like
        /// "(inherit standard)" so it isn't itself flagged as a missing font.
        /// </summary>
        protected void UpdateFontAvailabilityWarning(ComboBox combo, string fontName)
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

        /// <summary>
        /// Creates an action button (Apply / Cancel) and adds it directly to the form.
        /// </summary>
        protected Button MakeActionBtn(string text, int x, int y, int w, int h,
                                       FluentButton.Variant style = FluentButton.Variant.Neutral)
        {
            var btn = new FluentButton
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                Style = style, TabStop = true,
            };
            Controls.Add(btn);
            return btn;
        }

        /// <summary>
        /// Moves <paramref name="controls"/> from the form into a DockStyle.Fill scroll panel,
        /// then adds that panel to the form.  The scroll panel's
        /// <c>AutoScrollMinSize</c> is set to the current client size so scrollbars appear the
        /// moment the form is smaller than its designed layout in either dimension.
        /// </summary>
        protected Panel WrapInScrollPanel(params Control[] controls)
        {
            var sp = new Panel
            {
                Dock              = DockStyle.Fill,
                BackColor         = _dark ? Fluent.DarkBg : Fluent.BgPage,
                AutoScroll        = true,
                AutoScrollMinSize = new Size(ClientSize.Width, ClientSize.Height),
            };
            foreach (var c in controls)
            {
                Controls.Remove(c);
                sp.Controls.Add(c);
            }
            Controls.Add(sp);
            return sp;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Content-sized, sectioned, touch-friendly layout
        // ══════════════════════════════════════════════════════════════════

        /// <summary>The section buttons (null for a dialog built without sections).</summary>
        protected SectionBar Sections { get; private set; }

        /// <summary>Widest the content may grow before it is limited, in design pixels.</summary>
        protected const int MaxContentWidth = 760;

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
                Sections.SelectedIndexChanged += (s, e) => _host.ShowSection(Sections.SelectedIndex);
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
            var lbl = new Label
            {
                Text = label(), AutoSize = true, UseMnemonic = true,
                Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, Fluent.Pad, 4),
                MaximumSize = new Size(Touch.LabelMaxWidth, 0),
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = Fluent.FontLabel,
                TabIndex = _nextTab++,
            };
            _transLabels.Add((lbl, label));
            NameInput(input, Lang.StripMnemonic(label()));
            _pendingAccessibleName = Lang.StripMnemonic(label());

            PrepareInput(input, fill);
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
            c.TabIndex = _nextTab++;
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
        }

        /// <summary>A touch-sized check box whose text follows language changes; add it with <see cref="AddWideRow"/>.</summary>
        protected TouchCheckBox NewCheck(Func<string> text)
        {
            var c = new TouchCheckBox { Text = text() };
            _transTexts.Add((c, text));
            return c;
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
            var f = new TableLayoutPanel
            {
                ColumnCount = 2 + buttons.Length, RowCount = 1, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Touch.Gap, 0, 0),
            };
            f.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < buttons.Length; i++) f.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            if (leftSlot != null) { leftSlot.Anchor = AnchorStyles.Left; f.Controls.Add(leftSlot, 0, 0); }
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Anchor = AnchorStyles.Right;
                f.Controls.Add(buttons[i], 2 + i, 0);
            }
            return f;
        }

        /// <summary>
        /// A colour row for a section table: a 44 px hex box that fills the row plus a 44 x 44
        /// swatch. Same behaviour and error handling as <see cref="AddColorRow"/>; the returned
        /// row is placed with <see cref="AddRow"/>, the swatch works with <see cref="GetSwatchHex"/> /
        /// <see cref="SetSwatchHex"/>.
        /// </summary>
        protected Control MakeColorRow(out Button swatch, Action onChanged = null)
        {
            var txtHex = new TouchTextBox { Font = Fluent.FontCourier };
            var sw = new ColorSwatchButton
            {
                BackColor = Color.Gray, Size = new Size(Touch.Target, Touch.Target),
                MinimumSize = new Size(Touch.Target, Touch.Target), TabStop = true,
            };
            string colorName = _pendingAccessibleName;
            _pendingAccessibleName = null;
            if (colorName != null)
            {
                txtHex.AccessibleName = colorName + " hex";
                sw.AccessibleName     = colorName + " swatch";
            }
            SetTip(txtHex, () => Lang.T("tip: Hex color"));
            SetTip(sw,     () => Lang.T("tip: Color swatch"));
            // The error icon sits inside the hex box's right end instead of outside the row.
            _err.SetIconAlignment(txtHex, ErrorIconAlignment.MiddleRight);
            _err.SetIconPadding(txtHex, -24);
            txtHex.TextChanged += (s, e) =>
            {
                var parsed = ParseColor(txtHex.Text, Color.Empty);
                sw.BackColor = parsed.IsEmpty ? sw.BackColor : parsed;
                if (!_suppressOnChanged) onChanged?.Invoke();
                bool bad = !string.IsNullOrWhiteSpace(txtHex.Text) && parsed.IsEmpty;
                if (!_suppressOnChanged) _err.SetError(txtHex, bad ? Lang.T("err: invalid hex") : "");
            };
            sw.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { Color = sw.BackColor };
                if (dlg.ShowDialog() == DialogResult.OK) txtHex.Text = SettingsManager.Hex(dlg.Color);
            };
            sw.Tag = txtHex;

            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtHex.Dock   = DockStyle.Fill;
            txtHex.Margin = new Padding(0, 0, Touch.Gap, 0);
            sw.Margin     = Padding.Empty;
            txtHex.MinimumSize = new Size(140, Touch.Target);
            row.Controls.Add(txtHex, 0, 0);
            row.Controls.Add(sw, 1, 0);
            swatch = sw;
            return row;
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
            int w = maxWidth ?? (int)Math.Round(MaxContentWidth * DeviceDpi / 96.0);

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

        /// <summary>Sizes the window to its content, limited to the screen's working area, and centres it on its parent.</summary>
        protected void FitToContent()
        {
            if (_sizingRoot == null) return;
            var wa   = Screen.FromControl(this).WorkingArea;
            var nonC = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
            int maxW = wa.Width  - 10 - nonC.Width;
            int maxH = wa.Height - 10 - nonC.Height;
            var pref = MeasureContent(Math.Min((int)Math.Round(MaxContentWidth * DeviceDpi / 96.0), maxW));
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
