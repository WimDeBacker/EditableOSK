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

        private UserPreferenceChangedEventHandler _onPrefChanged;

        protected readonly List<(Label   Ctrl,  Func<string> GetText)>  _transLabels
            = new List<(Label,   Func<string>)>();
        protected readonly List<(Panel   Pnl,   Func<string> GetTitle)> _transGroups
            = new List<(Panel,   Func<string>)>();
        protected readonly List<(Control Ctrl,  Func<string> GetTip)>   _transTooltips
            = new List<(Control, Func<string>)>();

        protected string _pendingAccessibleName;

        // ── Constructor ──────────────────────────────────────────────────

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

            Load += (s, e) =>
            {
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

            FormClosed += (s, e) =>
            {
                Lang.LanguageChanged               -= OnLanguageChanged;
                SystemEvents.UserPreferenceChanged -= _onPrefChanged;
                _tip?.Dispose();
                _err?.Dispose();
            };
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
            foreach (var (pnl,  _)       in _transGroups)   pnl.Invalidate();
            foreach (var (ctrl, getTip)  in _transTooltips) _tip.SetToolTip(ctrl, getTip());
            Invalidate(true);
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
            Color bg = _dark ? Color.FromArgb(48, 48, 48) : Fluent.BgCard;
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
    }
}
