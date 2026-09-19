// KeyEditorMockups.cs — throw-away mock-ups for choosing the Key Editor layout.
//
// The question: how can the mode buttons (Text, Key/Shortcut, Modifier, Word prediction, Layout)
// be used for the Shift and AltGr layers too? Three options, built from the real touch controls so
// the screenshots ("--gallery") show true sizes:
//
//   A  a layer bar (Normal | Shift | AltGr) above ONE shared editor
//   B  an accordion: one row per layer, the selected one expands into the full editor
//   C  everything on one page: per layer a label row and an action row with a type menu
//
// Nothing here is wired to real data. Delete this file once the layout is decided.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>The parts the three mock-ups share: footer with preview, Appearance section, width/height row.</summary>
    internal abstract class KeyEditorMockup : FluentDialogBase
    {
        protected static readonly string[] ModeNames = { "Text", "Key/Shortcut", "Modifier", "Word prediction", "Layout" };
        protected Label PreviewLabel;

        /// <summary>The labelled preview key at the top right (used instead of the small footer preview).</summary>
        protected KeyPreviewCard PreviewCard;

        /// <summary>True when the preview belongs top right, next to the section buttons, instead of in the footer.</summary>
        protected virtual bool PreviewTopRight => false;

        /// <summary>Builds the frame; <paramref name="buildKey"/> fills the "Key" section, Appearance follows.</summary>
        protected void Build(Action<TableLayoutPanel> buildKey)
        {
            var preview = new Panel { Size = new Size(88, Touch.Target), Tag = "notheme", BackColor = Color.FromArgb(30, 30, 50) };
            PreviewLabel = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(224, 224, 255), BackColor = Color.FromArgb(45, 45, 74),
                Font = Fluent.FontPreviewKey, Text = "a",
            };
            preview.Controls.Add(PreviewLabel);
            var cancel = MakeTouchButton(() => Lang.T("Cancel"));
            var apply  = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            if (PreviewTopRight)
            {
                PreviewCard = new KeyPreviewCard();
                BuildFrame(MakeFooter(null, cancel, apply), withSections: true, headerRight: PreviewCard);
            }
            else BuildFrame(MakeFooter(preview, cancel, apply), withSections: true);

            var key = AddSection(() => Lang.T("Key Content"));
            buildKey(key);
            BuildAppearance(AddSection(() => Lang.T("Appearance")));

            AcceptButton = apply;
            CancelButton = cancel;
        }

        /// <summary>Re-fits the window to its content after a state change (used by the gallery).</summary>
        internal void Refit() => FitToContent();

        internal void ShowKeySection() => ShowSection(0);

        // ── Shared building blocks ───────────────────────────────────────

        /// <summary>The five mode buttons in a wrapping row; <paramref name="selected"/> looks pressed.</summary>
        protected FlowLayoutPanel ModeRow(int selected, bool restricted, out FluentButton[] buttons)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            buttons = new FluentButton[ModeNames.Length];
            for (int i = 0; i < ModeNames.Length; i++)
            {
                string name = ModeNames[i];
                var b = MakeTouchButton(() => Lang.T(name), i == selected ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral);
                b.Margin = new Padding(0, 0, Touch.Gap, Touch.Gap);
                b.Enabled = !(restricted && (i == 2 || i == 3));   // Modifier / Word prediction belong to the whole key
                flow.Controls.Add(b);
                buttons[i] = b;
            }
            return flow;
        }

        /// <summary>Restyles a mode-button row for another layer.</summary>
        protected static void ShowMode(FluentButton[] buttons, int selected, bool restricted)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Style   = i == selected ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
                buttons[i].Enabled = !(restricted && (i == 2 || i == 3));
                buttons[i].Invalidate();
            }
        }

        /// <summary>A text field with the contextual picker button (Browse / Record) to its right.</summary>
        protected Control SendWithPicker(TouchTextBox box, out FluentButton picker, string pickerText)
        {
            var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 0, Touch.Gap, 0);
            picker = MakeTouchButton(() => pickerText);
            picker.Margin = Padding.Empty;
            t.Controls.Add(box, 0, 0);
            t.Controls.Add(picker, 1, 0);
            return t;
        }

        protected Label Hint(string text, int maxWidth = 680) => new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(maxWidth, 0),
            ForeColor = Fluent.TextHint, BackColor = Color.Transparent, Font = Fluent.FontHint,
        };

        /// <summary>Key width and Key height side by side; they wrap onto two lines when the window is narrow.</summary>
        protected Control SpanRow()
        {
            var span = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            foreach (var label in new Func<string>[] { () => Lang.T("Key width"), () => Lang.T("Key height") })
            {
                var pair = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, Touch.Gap * 2, 0) };
                var lbl = new Label
                {
                    Text = label(), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = true,
                    Margin = new Padding(0, 0, Touch.Gap, 0), MinimumSize = new Size(0, Touch.Target),
                    ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = Fluent.FontLabel,
                };
                _transLabels.Add((lbl, label));
                pair.Controls.Add(lbl);
                pair.Controls.Add(new TouchStepper { Minimum = 1, Maximum = 14, Value = 1, AccessibleName = Lang.StripMnemonic(label()), Margin = Padding.Empty });
                span.Controls.Add(pair);
            }
            return span;
        }

        protected virtual void BuildAppearance(TableLayoutPanel look)
        {
            var group = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var groupCombo = new TouchComboBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Touch.Gap, 0) };
            groupCombo.Items.AddRange(new object[] { "(no group)", "standard", "Klinkers", "Medeklinkers" });
            groupCombo.SelectedIndex = 0;
            var manage = MakeTouchButton(() => Lang.T("Manage Groups…"));
            manage.Margin = Padding.Empty;
            group.Controls.Add(groupCombo, 0, 0);
            group.Controls.Add(manage, 1, 0);
            AddRow(look, () => Lang.T("Group"), group);

            var font = new TouchComboBox();
            font.Items.AddRange(new object[] { "Segoe UI", "Arial", "Courier New", "Verdana" });
            font.SelectedIndex = 0;
            AddRow(look, () => Lang.T("Font"), font);

            var size = new TouchStepper { Minimum = 0, Maximum = 72, Value = 14, Margin = new Padding(0, 0, Touch.Gap, 0) };
            var sizeRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            sizeRow.Controls.Add(size);
            sizeRow.Controls.Add(NewCheck(() => Lang.T("Auto")));
            AddRow(look, () => Lang.T("Font size"), sizeRow, fill: false);

            AddRow(look, () => Lang.T("Font color"),   MakeColorRow(out _));
            AddRow(look, () => Lang.T("Key color"),    MakeColorRow(out _));
            AddRow(look, () => Lang.T("Border color"), MakeColorRow(out _));
            AddRow(look, () => Lang.T("Border thickness"), new TouchStepper { Minimum = -1, Maximum = 10, Value = 1 }, fill: false);
            if (SizeOnAppearance) AddWideRow(look, SpanRow());
        }

        /// <summary>True when Key width / Key height belong on the Appearance section instead of the Key section.</summary>
        protected virtual bool SizeOnAppearance => false;

        internal void ShowAppearanceSection() => ShowSection(1);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Option A — layer bar + one shared editor
    // ════════════════════════════════════════════════════════════════════
    internal sealed class KeyEditorMockupA : KeyEditorMockup
    {
        private readonly SectionBar _layers = new SectionBar();
        private readonly (string Label, int Mode, string Send)[] _data =
            { ("a", 0, "a"), ("A", 4, "standard.kbl"), ("", 0, "") };
        private TouchTextBox _label, _send;
        private FluentButton[] _modes;
        private FluentButton _picker;
        private Label _hint;

        public KeyEditorMockupA()
        {
            Text = "Option A — layer bar + one shared editor";
            Build(BuildKey);
        }

        private void BuildKey(TableLayoutPanel key)
        {
            _layers.Add(() => "Normal · a");
            _layers.Add(() => "Shift · ↪ standard.kbl");
            _layers.Add(() => "AltGr · –");
            AddWideRow(key, _layers);

            _label = new TouchTextBox();
            AddRow(key, () => Lang.T("Label"), _label);
            AddWideRow(key, ModeRow(0, false, out _modes));
            _hint = Hint("Modifier and Word prediction apply to the whole key, so they are only available on the Normal layer.");
            AddWideRow(key, _hint, fill: false);
            _send = new TouchTextBox();
            AddRow(key, () => Lang.T("Send"), SendWithPicker(_send, out _picker, "Browse layout…"));
            AddWideRow(key, SpanRow());

            _layers.SelectedIndexChanged += (s, e) => ShowLayer(_layers.SelectedIndex);
            ShowLayer(0);
        }

        private void ShowLayer(int i)
        {
            var d = _data[i];
            _label.Text = d.Label;
            _send.Text  = d.Send;
            ShowMode(_modes, d.Mode, restricted: i > 0);
            _hint.Visible   = i > 0;
            _picker.Visible = d.Mode == 4 || d.Mode == 1;
            _picker.Text    = d.Mode == 4 ? "Browse layout…" : "Record shortcut…";
            PreviewLabel.Text = d.Label;
        }

        internal void SelectLayer(int i)
        {
            _layers.Select(i, focus: false);
            Application.DoEvents();
            Refit();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Option B — accordion, one row per layer
    // ════════════════════════════════════════════════════════════════════
    internal sealed class KeyEditorMockupB : KeyEditorMockup
    {
        private static readonly string[] Names   = { "Normal", "Shift", "AltGr" };
        private static readonly string[] Summary = { "Text: a", "Layout: standard.kbl", "(empty)" };
        private static readonly string[] Labels  = { "a", "A", "" };
        private static readonly int[]    Modes   = { 0, 4, 0 };
        private static readonly string[] Sends   = { "a", "standard.kbl", "" };

        private readonly FluentButton[] _headers = new FluentButton[3];
        // The controls that make up each layer's editor. They sit directly in the section's table
        // (a wrapping button row inside a table nested in a table is mis-measured by WinForms) and
        // are shown or hidden together; a row whose controls are all hidden takes no height.
        private readonly System.Collections.Generic.List<Control>[] _parts =
            { new System.Collections.Generic.List<Control>(), new System.Collections.Generic.List<Control>(), new System.Collections.Generic.List<Control>() };
        private int _open = -1;

        public KeyEditorMockupB()
        {
            Text = "Option B — accordion, one row per layer";
            Build(BuildKey);
        }

        private string HeaderText(int i) => (i == _open ? "▾  " : "▸  ") + Names[i] + "     " + Summary[i];

        private void BuildKey(TableLayoutPanel key)
        {
            for (int i = 0; i < 3; i++)
            {
                int layer = i;
                var header = MakeTouchButton(() => HeaderText(layer));
                header.Margin = new Padding(0, 2, 0, 2);
                header.Click += (s, e) => Open(layer);
                _headers[i] = header;
                AddWideRow(key, header);

                var labelBox = new TouchTextBox { Text = Labels[layer] };
                var labelLbl = AddRow(key, () => Lang.T("Label"), labelBox);
                var modes = ModeRow(Modes[layer], layer > 0, out _);
                AddWideRow(key, modes);
                var send = new TouchTextBox { Text = Sends[layer] };
                var sendRow = SendWithPicker(send, out _, Modes[layer] == 4 ? "Browse layout…" : "Record shortcut…");
                var sendLbl = AddRow(key, () => Lang.T("Send"), sendRow);
                _parts[layer].AddRange(new Control[] { labelLbl, labelBox, modes, sendLbl, sendRow });
            }
            AddWideRow(key, SpanRow());
            Open(1);
        }

        /// <summary>Expands one layer and collapses the others.</summary>
        internal void Open(int index)
        {
            _open = index;
            for (int i = 0; i < 3; i++)
            {
                foreach (var c in _parts[i]) c.Visible = i == index;
                _headers[i].Text   = HeaderText(i);
                _headers[i].Style  = i == index ? FluentButton.Variant.Primary : FluentButton.Variant.Neutral;
            }
            PreviewLabel.Text = Labels[index];
            Application.DoEvents();
            if (IsHandleCreated) Refit();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Option C — everything on one page, type menu per layer
    // ════════════════════════════════════════════════════════════════════
    internal sealed class KeyEditorMockupC : KeyEditorMockup
    {
        private static readonly string[] Names  = { "Normal", "Shift", "AltGr" };
        private static readonly string[] Labels = { "a", "A", "" };
        private static readonly int[]    Modes  = { 0, 2, 0 };        // index into the combo's own item list
        private static readonly string[] Sends  = { "a", "standard.kbl", "" };

        public KeyEditorMockupC()
        {
            Text = "Option C — everything on one page, type menu per layer";
            Build(BuildKey);
        }

        private void BuildKey(TableLayoutPanel key)
        {
            for (int i = 0; i < 3; i++)
            {
                int layer = i;                       // a lambda must not capture the loop variable itself
                string name = Names[layer];
                AddRow(key, () => name + " label", new TouchTextBox { Text = Labels[layer] });

                // Normal can be any of the five types; Shift and AltGr only the three that work per layer.
                var type = new TouchComboBox { MinimumSize = new Size(170, Touch.Target) };
                if (layer == 0) type.Items.AddRange(new object[] { "Text", "Key/Shortcut", "Modifier", "Word prediction", "Layout" });
                else            type.Items.AddRange(new object[] { "Text", "Key/Shortcut", "Layout" });
                type.SelectedIndex = layer == 0 ? 0 : Modes[layer];
                type.Margin = new Padding(0, 0, Touch.Gap, 0);

                var send = new TouchTextBox { Text = Sends[layer], Dock = DockStyle.Fill, Margin = new Padding(0, 0, Touch.Gap, 0) };
                var picker = MakeTouchButton(() => layer == 1 ? "Browse layout…" : "Record shortcut…");
                picker.Margin = Padding.Empty;
                picker.Visible = layer == 1;

                var action = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
                action.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                action.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                action.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                action.Controls.Add(type, 0, 0);
                action.Controls.Add(send, 1, 0);
                action.Controls.Add(picker, 2, 0);
                AddRow(key, () => name + " action", action);
            }
            AddWideRow(key, SpanRow());
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Option D — Option C redesigned
    //   • one compact row per layer instead of two rows: a NARROW label field (a key label is
    //     short), an action chooser, the action's value, and a contextual picker button
    //   • the five mode buttons are gone: the action type is a TouchChoiceButton, a big button
    //     that opens a flyout with large rows (name + description + why a choice is unavailable)
    //   • "Action" instead of "Send"
    //   • Key width / Key height moved to the Appearance section
    // ════════════════════════════════════════════════════════════════════
    internal class KeyEditorMockupD : KeyEditorMockup
    {
        private static readonly string[] Names  = { "Normal", "Shift", "AltGr" };
        private static readonly string[] Labels = { "a", "A", "" };
        private static readonly int[]    Modes  = { 0, 4, 0 };
        private static readonly string[] Values = { "a", "standard.kbl", "" };

        /// <summary>Width of the label column in design pixels: room for about 11 characters.</summary>
        private const int LabelColumnWidth = 124;

        internal readonly TouchChoiceButton[] Choosers = new TouchChoiceButton[3];

        protected override bool SizeOnAppearance => true;

        public KeyEditorMockupD()
        {
            Text = "Option D — one row per layer, action chooser";
            Build(BuildKey);
        }

        private static TouchChoiceButton NewChooser(bool restricted, int selected)
        {
            const string wholeKey = "Whole key only: set it on the Normal layer";
            var c = new TouchChoiceButton();
            c.Items.Add(new TouchChoice { Text = "Text",            Description = "Types these characters" });
            c.Items.Add(new TouchChoice { Text = "Key / Shortcut",  Description = "Presses a key or a shortcut, e.g. Ctrl+C" });
            c.Items.Add(new TouchChoice { Text = "Modifier",        Description = "Holds Shift, Ctrl or Alt for the next key", Enabled = !restricted, DisabledReason = wholeKey });
            c.Items.Add(new TouchChoice { Text = "Word prediction", Description = "Shows a word suggestion to tap",           Enabled = !restricted, DisabledReason = wholeKey });
            c.Items.Add(new TouchChoice { Text = "Layout",          Description = "Jumps to another layout file" });
            c.SelectedIndex = selected;
            return c;
        }

        private static Label Header(string text) => new Label
        {
            Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Font = Fluent.FontHint,
            ForeColor = Fluent.TextHint, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 0),
        };

        protected virtual void BuildKey(TableLayoutPanel key)
        {
            var grid = new TableLayoutPanel { ColumnCount = 5, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                       // layer name
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));      // label: short, so narrow
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                       // action type chooser
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));                   // action value
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                       // Browse / Record

            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(Header("Label"), 1, 0);
            var action = Header("Action");
            grid.Controls.Add(action, 2, 0);
            grid.SetColumnSpan(action, 3);

            for (int i = 0; i < 3; i++)
            {
                int row = i + 1;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                grid.Controls.Add(new Label
                {
                    Text = Names[i], AutoSize = true, Anchor = AnchorStyles.Left, Font = Fluent.FontBtnLg,
                    ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Margin = new Padding(0, 4, Fluent.Pad, 4),
                }, 0, row);

                grid.Controls.Add(new TouchTextBox { Text = Labels[i], Dock = DockStyle.Fill, Margin = new Padding(0, 4, Touch.Gap, 4) }, 1, row);

                var chooser = NewChooser(restricted: i > 0, selected: Modes[i]);
                chooser.Dock = DockStyle.Fill;
                chooser.Margin = new Padding(0, 4, Touch.Gap, 4);
                Choosers[i] = chooser;
                grid.Controls.Add(chooser, 2, row);

                var value = new TouchTextBox { Text = Values[i], Dock = DockStyle.Fill, Margin = new Padding(0, 4, Touch.Gap, 4) };
                grid.Controls.Add(value, 3, row);

                // Short text keeps the value field wide; the tooltip / accessible name would say what is browsed.
                var picker = MakeTouchButton(() => "Browse…");
                picker.Margin = new Padding(0, 4, 0, 4);
                picker.Visible = Modes[i] == 4;
                grid.Controls.Add(picker, 4, row);
                // Without a picker the value field takes over its column instead of leaving a gap.
                grid.SetColumnSpan(value, picker.Visible ? 1 : 2);

                chooser.SelectedIndexChanged += (s, e) =>
                {
                    int mode = chooser.SelectedIndex;
                    picker.Visible = mode == 4 || mode == 1;
                    picker.Text = mode == 4 ? "Browse…" : "Record…";
                    grid.SetColumnSpan(value, picker.Visible ? 1 : 2);
                };
            }
            AddWideRow(key, grid);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Option E — Option D with the second round of feedback
    //   • Key width / Key height are back on the Key section (there is room now)
    //   • the preview is a labelled card at the top right of BOTH sections
    //   • Group and Font are TouchChoiceButtons (44 px); a long list scrolls, is capped to the
    //     room on screen and has a search box
    //   • the three colours are one row of labelled chips; a flyout offers a palette, a hex box
    //     and the standard Windows colour dialog
    // ════════════════════════════════════════════════════════════════════
    internal sealed class KeyEditorMockupE : KeyEditorMockupD
    {
        internal TouchChoiceButton GroupChooser, FontChooser;
        internal ColorChip FontChip, KeyChip, BorderChip;

        protected override bool PreviewTopRight => true;
        protected override bool SizeOnAppearance => false;

        public KeyEditorMockupE() { Text = "Option E — round two"; }

        protected override void BuildKey(TableLayoutPanel key)
        {
            base.BuildKey(key);
            AddWideRow(key, SpanRow());
        }

        protected override void BuildAppearance(TableLayoutPanel look)
        {
            // Group: a chooser with plain 44 px rows, and the button that manages the groups
            var group = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            GroupChooser = new TouchChoiceButton { RowHeight = 44, Dock = DockStyle.Fill, Margin = new Padding(0, 0, Touch.Gap, 0) };
            foreach (var name in new[] { "(no group)", "standard", "Klinkers", "Medeklinkers", "Cijfers", "Besturing", "Leestekens", "Woord" })
                GroupChooser.Items.Add(new TouchChoice { Text = name });
            GroupChooser.SelectedIndex = 0;
            var manage = MakeTouchButton(() => Lang.T("Manage Groups…"));
            manage.Margin = Padding.Empty;
            group.Controls.Add(GroupChooser, 0, 0);
            group.Controls.Add(manage, 1, 0);
            AddRow(look, () => Lang.T("Group"), group);

            // Font: every installed font, so the flyout scrolls and can be searched
            FontChooser = new TouchChoiceButton { RowHeight = 44, Searchable = true, MaxVisibleRows = 7 };
            foreach (var f in Fluent.InstalledFontNames()) FontChooser.Items.Add(new TouchChoice { Text = f });
            int seg = FontChooser.Items.FindIndex(i => i.Text == "Segoe UI");
            FontChooser.SelectedIndex = seg >= 0 ? seg : 0;
            AddRow(look, () => Lang.T("Font"), FontChooser);

            var size = new TouchStepper { Minimum = 0, Maximum = 72, Value = 14, Margin = new Padding(0, 0, Touch.Gap, 0) };
            var sizeRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            sizeRow.Controls.Add(size);
            sizeRow.Controls.Add(NewCheck(() => Lang.T("Auto")));
            AddRow(look, () => Lang.T("Font size"), sizeRow, fill: false);

            // Colours: one row, three labelled chips; the hex code is not shown, the flyout has it
            FontChip   = new ColorChip("Font",   Color.FromArgb(224, 224, 255));
            KeyChip    = new ColorChip("Key",    Color.FromArgb(45, 45, 74));
            BorderChip = new ColorChip("Border", Color.FromArgb(120, 120, 140));
            var chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            chips.Controls.AddRange(new Control[] { FontChip, KeyChip, BorderChip });
            AddRow(look, () => "Colours", chips, fill: false);
            EventHandler refreshPreview = (s, e) => PreviewCard.Set("a", KeyChip.Value, FontChip.Value, BorderChip.Value);
            FontChip.ValueChanged += refreshPreview; KeyChip.ValueChanged += refreshPreview; BorderChip.ValueChanged += refreshPreview;
            refreshPreview(this, EventArgs.Empty);

            AddRow(look, () => Lang.T("Border thickness"), new TouchStepper { Minimum = -1, Maximum = 10, Value = 1 }, fill: false);
        }
    }
}
