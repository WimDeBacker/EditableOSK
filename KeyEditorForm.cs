using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>
    /// A modal dialog that lets the user edit all properties of a single keyboard key: for each layer
    /// (Normal, Shift, AltGr) its label and its action (type text, press a shortcut, act as a modifier,
    /// show a word prediction, or jump to another layout), the key's size, and its appearance
    /// (group, font, colours, border).
    ///
    /// Layout: two sections (Key Content, Appearance) with a labelled preview key at the top right, all
    /// controls at least 44 px, sized from its content. After the dialog closes with OK, the caller reads
    /// <see cref="Result"/>, <see cref="ResultColSpan"/>, <see cref="ResultRowSpan"/> and
    /// <see cref="ResultGroupsChanged"/>.
    /// </summary>
    public class KeyEditorForm : FluentDialogBase
    {
        /// <summary>The edited key properties after the user clicks Apply. If cancelled this still holds the original values.</summary>
        public KeyProps Result        { get; private set; }

        /// <summary>How many grid columns wide the key should be (minimum 1).</summary>
        public int     ResultColSpan { get; private set; } = 1;

        /// <summary>How many grid rows tall the key should be (minimum 1).</summary>
        public int     ResultRowSpan { get; private set; } = 1;

        /// <summary>
        /// True when the user modified one or more groups via "Manage Groups". The caller should refresh all
        /// key buttons so group colour changes are reflected across every key that belongs to a modified group.
        /// </summary>
        public bool ResultGroupsChanged { get; private set; }

        // ── What a layer's action can be ──────────────────────────────
        private enum SendMode { Text, KeySequence, Modifier, WordPrediction, Layout }

        // The types each layer's chooser offers, in the order of its rows (the row index is NOT the enum value
        // on Shift and AltGr). Word prediction belongs to the whole key, so it exists on the Normal layer only.
        private static readonly SendMode[] NormalModes = { SendMode.Text, SendMode.KeySequence, SendMode.Modifier, SendMode.WordPrediction, SendMode.Layout };
        private static readonly SendMode[] LayerModes  = { SendMode.Text, SendMode.KeySequence, SendMode.Modifier, SendMode.Layout };
        private static SendMode[] ModesOf(int layer) => layer == 0 ? NormalModes : LayerModes;

        private const int Layers = 3;                    // Normal, Shift, AltGr
        private const int LabelColumnWidth = 124;        // a key label is short: room for about 11 characters

        // The most the window may grow when the one-line action row needs it (a long translation or a large font); beyond that
        // the row takes two lines (the AdaptiveTable). Only a cap: the window is as wide as its content needs, not as this.
        protected override int ContentMaxWidth => 1000;

        // ── Controls ──────────────────────────────────────────────────
        // One row per layer: label, action type, action value, contextual picker (Browse / Record).
        private readonly TouchTextBox[]      _labels  = new TouchTextBox[Layers];
        private readonly TouchChoiceButton[] _types   = new TouchChoiceButton[Layers];
        private readonly TouchTextBox[]      _values  = new TouchTextBox[Layers];
        private readonly FluentButton[]      _pickers = new FluentButton[Layers];
        private readonly Label[]             _layerNames = new Label[Layers];
        private AdaptiveTable _grid;
        private Panel            _valueHost0;            // holds the Normal layer's value box and the modifier chooser
        private TouchChoiceButton _modChooser;           // which modifier (Normal layer, Modifier type)
        private Label            _lblHint;               // recording / picker messages, hidden when empty

        // Aliases of the first row's controls (kept for readability and for the tests).
        private TextBox _txtLabel, _txtSend, _txtShiftLabel, _txtShiftSend, _txtAltGrLabel, _txtAltGrSend;

        private TouchStepper  _stpColSpan, _stpRowSpan, _stpFontSize, _stpBorderThickness;
        private TouchCheckBox _chkAutoSize;
        private TouchChoiceButton _cmbGroup, _cmbFont;
        /// <summary>
        /// Test seam: answers the question "take the key out of its group?" (title, text) instead of a window being opened
        /// (a modal window would stop a test that clicks on a shown dialog).
        /// </summary>
        internal Func<string, string, bool> LeaveGroupAnswer;
        private FluentButton  _btnGroupEdit;
        private ColorChip     _chipFont, _chipKey, _chipBorder;
        private KeyPreviewCard _preview;
        private FluentButton  _btnApply, _btnCancel;

        // ── State ─────────────────────────────────────────────────────
        private readonly KeyProps    _original;
        private readonly string[]    _origSend = new string[Layers];       // the sends as stored, per layer
        private readonly bool[]      _layerTouched = new bool[Layers];     // the user changed this layer's action
        private readonly int         _initColSpan, _initRowSpan, _maxCols, _maxRows;
        private readonly HashSet<int> _usedWpSlots;                        // slots used by OTHER keys
        private readonly List<KeyGroup> _groups;
        private readonly string      _layoutDir;
        private readonly Color       _globalBorderColor;
        private readonly VisualTheme _ownerGlobal;
        private int  _wpSlot;
        private bool _valueShowsWp;
        private bool _initialising;

        // Effective (resolved) appearance loaded into the controls; Apply() compares against these.
        private Color  _loadedFontColor, _loadedKeyColor, _loadedBorderColor;
        private string _loadedFontName = "";
        private int    _loadedFontSize, _loadedBorderThickness;
        // What the selected group provides on its own (ignoring per-key overrides).
        private Color  _groupFontColor, _groupKeyColor, _groupBorderColor;
        private string _groupFontName = "";
        private int    _groupFontSize, _groupBorderThickness;
        // True once the user actually changed the font (a font that isn't installed can't be selected,
        // so comparing values would always look like a change and silently detach the key from its group).
        private bool _fontUserChanged;

        /// <summary>The modifier keys the user can assign: (label stored in XML, name shown).</summary>
        private static readonly (string Label, string Display)[] _modifiers =
        {
            ("Shift", "Shift"), ("Caps", "Caps Lock"), ("Ctrl", "Ctrl"),
            ("Alt", "Alt"), ("AltGr", "AltGr"), ("Win", "Win"),
        };

        // ── Key recorder (low-level keyboard hook) ────────────────────
        // A system-wide hook lets us capture Win-key combinations before Windows acts on them.
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        private const int  WH_KEYBOARD_LL = 13;
        private const int  WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        private const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        private bool   _recording;
        private int    _recordLayer;
        private readonly string[] _recordedLabel = new string[Layers];   // the label a recording put in a layer (so a re-recording may replace it)
        private readonly string[] _autoLabelBefore = new string[Layers]; // the label the shortcut that a recording replaces would have got
        private bool   _winHeld;              // tracked separately because the hook suppresses the Win key-up
        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc _hookProc;   // kept in a field so the GC cannot free it while the hook is active

        /// <summary>Test seam: <see cref="StartRecording"/> does not install the system-wide hook.</summary>
        internal bool HookDisabledForTest;

        // ── Title ─────────────────────────────────────────────────────

        /// <summary>A title-bar-safe key label: emoji and symbol blocks are stripped so the title bar shows no replacement boxes.</summary>
        private static string TitleSafeLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            var sb = new System.Text.StringBuilder(label.Length);
            for (int i = 0; i < label.Length; i++)
            {
                char c = label[i];
                if (char.IsHighSurrogate(c)) { i++; continue; }
                if (char.IsLowSurrogate(c))        continue;
                if (c >= 0x2600 && c <= 0x27BF)    continue;
                if (c >= 0x2B00 && c <= 0x2BFF)    continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private string BuildTitle(string label)
        {
            string safe = TitleSafeLabel(label);
            return string.IsNullOrEmpty(safe) ? Lang.T("Edit Key") : $"{Lang.T("Edit Key")}  [{safe}]";
        }

        // ── Constructor ───────────────────────────────────────────────

        /// <summary>Creates and prepopulates the key editor dialog.</summary>
        /// <param name="props">The current properties of the key being edited.</param>
        /// <param name="owner">The parent <see cref="KeyboardForm"/>; its global theme supplies the default font and colours.</param>
        /// <param name="colSpan">Current column span of the key.</param>
        /// <param name="rowSpan">Current row span of the key.</param>
        /// <param name="maxCols">Total columns in the layout — caps the width stepper.</param>
        /// <param name="maxRows">Total rows in the layout — caps the height stepper.</param>
        /// <param name="usedWpSlots">Word-prediction slots already used by other keys.</param>
        /// <param name="groups">Named key groups available in this layout.</param>
        /// <param name="layoutDir">Folder of the current layout file (start folder of Browse, base for relative paths).</param>
        public KeyEditorForm(KeyProps props, Form owner, int colSpan = 1, int rowSpan = 1, int maxCols = 14, int maxRows = 6, HashSet<int> usedWpSlots = null, List<KeyGroup> groups = null, string layoutDir = null)
        {
            _original    = props;
            _layoutDir   = layoutDir;
            _groups      = groups ?? new List<KeyGroup>();
            _maxCols     = Math.Max(1, maxCols);
            _usedWpSlots = usedWpSlots ?? new HashSet<int>();
            _origSend[0] = props.Send ?? ""; _origSend[1] = props.ShiftSend ?? ""; _origSend[2] = props.AltGrSend ?? "";

            // The owner's theme is cached now: Owner is null until ShowDialog().
            _ownerGlobal       = (owner as KeyboardForm)?._theme;
            _globalBorderColor = _ownerGlobal?.BorderColor ?? ColorTranslator.FromHtml("#3C3C5A");

            Result        = props.Clone();     // Apply() writes to Result, never to the original
            ResultColSpan = Math.Max(1, colSpan);
            ResultRowSpan = Math.Max(1, rowSpan);
            _initColSpan  = ResultColSpan;
            _initRowSpan  = ResultRowSpan;
            _maxRows      = Math.Max(1, maxRows);

            Text = BuildTitle(props.Label);
            BuildUI(props);

            // Uninstall the keyboard hook. Also released from Dispose(bool): FormClosed only fires for a form that
            // was shown, and a hook left installed intercepts every keystroke system-wide.
            FormClosed += (s, e) => ReleaseFormResources();
            Deactivate += (s, e) => { if (_recording) StopRecording(cancelled: true); };   // the hook must not outlive focus
        }

        private void ReleaseFormResources()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseFormResources();
            base.Dispose(disposing);
        }

        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            Text = BuildTitle(_original.Label);
            for (int i = 0; i < Layers; i++)
            {
                _types[i].SetItems(TypeItems(restricted: i > 0), _types[i].SelectedIndex);
                _labels[i].AccessibleName = LayerLabelName(i);
                _values[i].AccessibleName = ValueName(i);
            }
            _cmbGroup.Items[0].Text = Lang.T("(no group)");
            _cmbGroup.ShowSelection();
            UpdatePickerTexts();
            if (_valueShowsWp) ShowWpValue();
            if (LayersDisabled) SetHint(WpHint);
            _grid.Reevaluate();          // the translated headers and names change how wide the one-line arrangement is
        }

        // ══════════════════════════════════════════════════════════════
        //  Building the UI
        // ══════════════════════════════════════════════════════════════

        private void BuildUI(KeyProps p)
        {
            _preview   = new KeyPreviewCard();
            _btnCancel = MakeTouchButton(() => Lang.T("Cancel"));
            _btnApply  = MakeTouchButton(() => Lang.T("Apply"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, _btnCancel, _btnApply), withSections: true, headerRight: _preview);
            _btnApply.Click  += (s, e) => Apply();
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;

            BuildKeySection(AddSection(() => Lang.T("Key Content")));
            BuildAppearanceSection(AddSection(() => Lang.T("Appearance")));

            _txtLabel = _labels[0]; _txtShiftLabel = _labels[1]; _txtAltGrLabel = _labels[2];
            _txtSend  = _values[0]; _txtShiftSend  = _values[1]; _txtAltGrSend  = _values[2];

            PopulateFields(p);
        }

        private string LayerName(int i) => i == 0 ? Lang.T("Normal") : i == 1 ? Lang.T("Shift") : Lang.T("AltGr");
        private string LayerLabelName(int i) => Lang.StripMnemonic(Lang.T("Label")) + " " + LayerName(i);

        private string ValueName(int i)
        {
            var mode = ModeOf(i);
            string what = mode == SendMode.WordPrediction ? Lang.T("Prediction cell")
                        : mode == SendMode.Layout         ? Lang.T("Layout file")
                        :                                    Lang.T("Action");
            return Lang.StripMnemonic(what) + " " + LayerName(i);
        }

        private Label GridHeader(Func<string> text)
        {
            var lbl = new Label
            {
                Text = text(), AutoSize = true, Anchor = AnchorStyles.Left, Font = Fluent.FontHint,
                ForeColor = Fluent.TextHint, BackColor = Color.Transparent, UseMnemonic = false,
            };
            _transLabels.Add((lbl, text));
            return lbl;
        }

        /// <summary>The action type chooser's rows for a layer (see <see cref="ModesOf"/>). Modifier belongs to the
        /// whole key, so on Shift and AltGr it is listed but unavailable, and says why. Word prediction is not listed there at all.</summary>
        private List<TouchChoice> TypeItems(bool restricted)
        {
            string whole = Lang.T("Whole key only: set it on the Normal layer");
            TouchChoice Item(SendMode mode) => mode switch
            {
                SendMode.Text           => new TouchChoice { Text = Lang.StripMnemonic(Lang.T("Text")),            Description = Lang.T("Types these characters") },
                SendMode.KeySequence    => new TouchChoice { Text = Lang.StripMnemonic(Lang.T("Key/Shortcut")),    Description = Lang.T("Presses a key or a shortcut, e.g. Ctrl+C") },
                SendMode.Modifier       => new TouchChoice { Text = Lang.StripMnemonic(Lang.T("Modifier")),        Description = Lang.T("Holds Shift, Ctrl or Alt for the next key"), Enabled = !restricted, DisabledReason = whole },
                SendMode.WordPrediction => new TouchChoice { Text = Lang.StripMnemonic(Lang.T("Word prediction")), Description = Lang.T("Shows a word suggestion to tap") },
                _                       => new TouchChoice { Text = Lang.StripMnemonic(Lang.T("Layout")),          Description = Lang.T("Jumps to another layout file") },
            };
            return ModesOf(restricted ? 1 : 0).Select(Item).ToList();
        }

        // Icon only (a record / stop symbol, or a folder), so the button is the same 44 px square in every language and
        // in every state: nothing about it can grow over the value box. The accessible name carries the words.
        private FluentButton NewPicker() => new FluentButton
        {
            Style = FluentButton.Variant.Neutral, TabStop = true, AutoSize = false,
            Size = new Size(Touch.Target, Touch.Target), MinimumSize = new Size(Touch.Target, Touch.Target),
            Margin = new Padding(0, 4, 0, 4),
        };

        // Segoe MDL2 Assets: Record, Stop, FolderOpen.
        private const string GlyphRecord = "", GlyphStop = "", GlyphFolder = "";

        private void BuildKeySection(TableLayoutPanel key)
        {
            // The rows of this grid are arranged by the AdaptiveTable (see the variants below): the five columns on one line when they fit,
            // and the value box with its button on a second line under the label when they do not.
            _grid = new AdaptiveTable { ColumnCount = 5 };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                        // layer name
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));       // label: short, so narrow
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                        // action type
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));                    // action value
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));                        // Browse / Record

            var headerLabel  = GridHeader(() => Lang.StripMnemonic(Lang.T("Label")));
            var headerAction = GridHeader(() => Lang.T("Action"));
            _grid.Controls.Add(headerLabel);
            _grid.Controls.Add(headerAction);
            var valueCells = new Control[Layers];

            int ti = 0;
            for (int i = 0; i < Layers; i++)
            {
                int layer = i;

                var name = new Label
                {
                    Text = LayerName(i), AutoSize = true, Anchor = AnchorStyles.Left, Font = Fluent.FontBtnLg,
                    ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, UseMnemonic = false,
                    Margin = new Padding(0, 4, Fluent.Pad, 4),
                };
                _layerNames[i] = name;
                _transLabels.Add((name, () => LayerName(layer)));
                _grid.Controls.Add(name);

                _labels[i] = new TouchTextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, Touch.Gap, 4), AccessibleName = LayerLabelName(i), TabIndex = ti++ };
                _grid.Controls.Add(_labels[i]);

                _types[i] = new TouchChoiceButton { RowHeight = 56, Dock = DockStyle.Fill, Margin = new Padding(0, 4, Touch.Gap, 4), TabIndex = ti++ };
                _types[i].SetItems(TypeItems(restricted: i > 0), 0);
                _types[i].AccessibleDescription = Lang.T("Action");
                _grid.Controls.Add(_types[i]);

                // A minimum width: with long translations the other columns must not squeeze the value away.
                _values[i] = new TouchTextBox { Dock = DockStyle.Fill, AccessibleName = ValueName(i), MinimumSize = new Size(150, Touch.Target) };
                _err.SetIconAlignment(_values[i], ErrorIconAlignment.MiddleRight);
                _err.SetIconPadding(_values[i], -24);           // the error icon sits inside the box, not outside the row
                Control valueCell = _values[i];
                if (i == 0)
                {
                    // The Normal layer's value cell holds either the text box or, for a modifier key, a chooser.
                    _modChooser = new TouchChoiceButton { RowHeight = 44, Dock = DockStyle.Fill, Visible = false, AccessibleName = Lang.StripMnemonic(Lang.T("Modifier")) };
                    _modChooser.SetItems(_modifiers.Select(m => new TouchChoice { Text = m.Display }), 0);
                    _modChooser.SelectedIndexChanged += (s, e) => ApplyModChoice();
                    _values[0].Dock = DockStyle.Fill;
                    _valueHost0 = new Panel
                    {
                        Dock = DockStyle.Fill, Height = Touch.Target, MinimumSize = new Size(0, Touch.Target),
                        MaximumSize = new Size(0, Touch.Target), Margin = new Padding(0, 4, Touch.Gap, 4), TabIndex = ti++,
                    };
                    _valueHost0.Controls.Add(_values[0]);
                    _valueHost0.Controls.Add(_modChooser);
                    valueCell = _valueHost0;
                }
                else
                {
                    _values[i].Margin = new Padding(0, 4, Touch.Gap, 4);
                    _values[i].TabIndex = ti++;
                }
                valueCells[i] = valueCell;
                _grid.Controls.Add(valueCell);

                _pickers[i] = NewPicker();
                _pickers[i].TabIndex = ti++;
                _pickers[i].Visible = false;
                _grid.Controls.Add(_pickers[i]);
                _pickers[i].VisibleChanged += (s, e) => _grid.Rearrange();      // the narrow arrangement gives the value box the button's room while it is hidden
                SetTip(_pickers[i], () => ModeOf(layer) == SendMode.Layout ? Lang.T("tip: Browse layout")
                                        : _recording && _recordLayer == layer ? Lang.T("tip: Stop recording") : Lang.T("tip: Record"));

                _types[i].SelectedIndexChanged += (s, e) => OnTypeChanged(layer);
                _values[i].TextChanged += (s, e) => { if (!_initialising) _layerTouched[layer] = true; ValidateLayoutField(layer); };
                _labels[i].TextChanged += (s, e) => { if (layer == 0) Refresh2(); };
                _pickers[i].Click += (s, e) => OnPickerClick(layer);
            }

            // Wide: name | label | type | value | button on one line per layer, under the two column headers.
            _grid.AddVariant(() =>
            {
                var cells = new List<AdaptiveTable.Cell> { new AdaptiveTable.Cell(headerLabel, 1, 0), new AdaptiveTable.Cell(headerAction, 2, 0, 3) };
                for (int i = 0; i < Layers; i++)
                {
                    int row = i + 1;
                    cells.Add(new AdaptiveTable.Cell(_layerNames[i], 0, row));
                    cells.Add(new AdaptiveTable.Cell(_labels[i], 1, row));
                    cells.Add(new AdaptiveTable.Cell(_types[i], 2, row));
                    cells.Add(new AdaptiveTable.Cell(valueCells[i], 3, row));
                    cells.Add(new AdaptiveTable.Cell(_pickers[i], 4, row));
                }
                return cells;
            });
            // Narrow: line 1 = name, label, type (the type chooser takes the rest of the line); line 2 = the value box under the label,
            // with the button at the right end (the whole width when there is no button).
            _grid.AddVariant(() =>
            {
                var cells = new List<AdaptiveTable.Cell> { new AdaptiveTable.Cell(headerLabel, 1, 0), new AdaptiveTable.Cell(headerAction, 2, 0, 3) };
                for (int i = 0; i < Layers; i++)
                {
                    int first = 1 + 2 * i, second = first + 1;
                    cells.Add(new AdaptiveTable.Cell(_layerNames[i], 0, first));
                    cells.Add(new AdaptiveTable.Cell(_labels[i], 1, first));
                    cells.Add(new AdaptiveTable.Cell(_types[i], 2, first, 3));
                    cells.Add(new AdaptiveTable.Cell(valueCells[i], 1, second, _pickers[i].Visible ? 3 : 4));
                    cells.Add(new AdaptiveTable.Cell(_pickers[i], 4, second));
                }
                return cells;
            });
            AddWideRow(key, _grid);

            _lblHint = new Label
            {
                AutoSize = true, MaximumSize = new Size(680, 0), Visible = false, UseMnemonic = false,
                ForeColor = Fluent.TextHint, BackColor = Color.Transparent, Font = Fluent.FontHint,
            };
            AddWideRow(key, _lblHint, fill: false);

            // Key width and height side by side (they wrap onto two lines when the window is narrow).
            var pairs = new List<Control>();
            _stpColSpan = NewSpan(pairs, () => Lang.T("Key width"),  "tip: Key width", _maxCols);
            _stpRowSpan = NewSpan(pairs, () => Lang.T("Key height"), "tip: Row span",  _maxRows);
            AddWideRow(key, ReflowRows.Rows(pairs, 2, 1));              // side by side, else one under the other
        }

        private TouchStepper NewSpan(List<Control> pairs, Func<string> label, string tip, int max)
        {
            var pair = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, Touch.Gap * 2, 0) };
            var lbl = new Label
            {
                Text = label(), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = true,
                Margin = new Padding(0, 0, Touch.Gap, 0), MinimumSize = new Size(0, Touch.Target),
                ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent, Font = Fluent.FontLabel, TabIndex = 0,
            };
            _transLabels.Add((lbl, label));
            var st = new TouchStepper { Minimum = 1, Maximum = max, Value = 1, AccessibleName = Lang.StripMnemonic(label()), Margin = Padding.Empty, TabIndex = 1 };
            SetTip(st.ValueBox, () => Lang.T(tip));
            pair.Controls.Add(lbl);
            pair.Controls.Add(st);
            pairs.Add(pair);
            return st;
        }

        private void BuildAppearanceSection(TableLayoutPanel look)
        {
            // Group: a chooser plus the button that manages the groups.
            // Beside each other when they fit, the button under the chooser when they do not (a chooser squeezed to nothing helps nobody).
            var group = new AdaptiveTable { ColumnCount = 2 };
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _cmbGroup = new TouchChoiceButton { RowHeight = 44, Dock = DockStyle.Fill, Margin = new Padding(0, 0, Touch.Gap, 0), TabIndex = 0, MinimumSize = new Size(Touch.InputMinWidth / 2, Touch.Target) };
            _btnGroupEdit = MakeTouchButton(() => Lang.T("Manage Groups…"));
            _btnGroupEdit.Margin = Padding.Empty;
            _btnGroupEdit.TabIndex = 1;
            group.AddVariant(() =>
            {
                _cmbGroup.Margin = new Padding(0, 0, Touch.Gap, 0);
                return new[] { new AdaptiveTable.Cell(_cmbGroup, 0, 0), new AdaptiveTable.Cell(_btnGroupEdit, 1, 0) };
            });
            group.AddVariant(() =>
            {
                _cmbGroup.Margin = new Padding(0, 0, 0, Touch.Gap / 2);
                return new[] { new AdaptiveTable.Cell(_cmbGroup, 0, 0, 2), new AdaptiveTable.Cell(_btnGroupEdit, 0, 1, 2) };
            });
            AddRow(look, () => Lang.T("Group"), group);
            RebuildGroupChooser("", refresh: false);
            SetTip(_btnGroupEdit, () => Lang.T("tip: Manage Groups"));
            _cmbGroup.SelectedIndexChanged += (s, e) => { RefreshAppearanceFromGroup(); Refresh2(); };
            _btnGroupEdit.Click += (s, e) => OpenGroupEditor();

            // Font: every installed font, so the flyout scrolls and can be searched.
            _cmbFont = new TouchChoiceButton { RowHeight = 44, Searchable = true };
            _cmbFont.SetItems(Fluent.InstalledFontNames().Select(n => new TouchChoice { Text = n }), 0);
            _cmbFont.SelectedIndexChanged += (s, e) =>
            {
                // Only a real user pick counts; loading values happens with SelectSilently.
                if (!_initialising) _fontUserChanged = true;
                UpdateFontAvailabilityWarning(_cmbFont, _cmbFont.SelectedItem?.Text ?? "");
                Refresh2();
            };
            _err.SetIconPadding(_cmbFont, -54);
            _fontWarn.SetIconPadding(_cmbFont, -54);       // left of the button's arrow
            AddRow(look, () => Lang.T("Font"), _cmbFont);

            // Font size: a stepper plus "Auto" (size is decided when the key is drawn).
            _stpFontSize = new TouchStepper { Minimum = 0, Maximum = 72, Margin = new Padding(0, 0, Touch.Gap, 0), AccessibleName = Lang.StripMnemonic(Lang.T("Font size")) };
            _stpFontSize.AccessibleDescription = Lang.T("0 = auto / inherit");
            _stpFontSize.ValueBox.AccessibleDescription = _stpFontSize.AccessibleDescription;
            SetTip(_stpFontSize.ValueBox, () => Lang.T("tip: Font size"));
            _stpFontSize.ValueChanged += (s, e) => Refresh2();
            _chkAutoSize = NewCheck(() => Lang.T("Auto"));
            _chkAutoSize.CheckedChanged += (s, e) => { _stpFontSize.Enabled = !_chkAutoSize.Checked; Refresh2(); };
            var sizeRow = InlineRow(_stpFontSize, _chkAutoSize);       // the check box goes under the stepper when they do not fit side by side
            AddRow(look, () => Lang.T("Font size"), sizeRow, fill: false);

            // Colours on one row: three labelled chips (font, key, border); the flyout has palette, hex and the Windows dialog.
            _chipFont   = new ColorChip(Lang.T("chip: Font"),   Color.Gray);
            _chipKey    = new ColorChip(Lang.T("chip: Key"),    Color.Gray);
            _chipBorder = new ColorChip(Lang.T("chip: Border"), Color.Gray);
            _transTexts.Add((_chipFont, () => Lang.T("chip: Font")));
            _transTexts.Add((_chipKey, () => Lang.T("chip: Key")));
            _transTexts.Add((_chipBorder, () => Lang.T("chip: Border")));
            _chipFont.TabIndex = 0; _chipKey.TabIndex = 1; _chipBorder.TabIndex = 2;
            var chips = ReflowRows.Rows(new Control[] { _chipFont, _chipKey, _chipBorder }, 3, 2, 1);      // three on a line, else two, else one
            foreach (var chip in new[] { _chipFont, _chipKey, _chipBorder })
            {
                chip.ValueChanged += (s, e) => Refresh2();
                SetTip(chip, () => Lang.T("tip: Color swatch"));
            }
            AddRow(look, () => Lang.T("Colors"), chips, fill: false);

            // Border thickness: -1 = inherit from the standard group, 0 = no border.
            _stpBorderThickness = new TouchStepper { Minimum = -1, Maximum = 10, AccessibleName = Lang.StripMnemonic(Lang.T("Border thickness")) };
            _stpBorderThickness.AccessibleDescription = Lang.T("-1 = inherit standard");
            _stpBorderThickness.ValueBox.AccessibleDescription = _stpBorderThickness.AccessibleDescription;
            SetTip(_stpBorderThickness.ValueBox, () => Lang.T("tip: Border thickness"));
            _stpBorderThickness.ValueChanged += (s, e) => Refresh2();
            AddRow(look, () => Lang.T("Border thickness"), _stpBorderThickness, fill: false);

            // Every way of changing the look of a key that is in a group asks first: take the key out of the group, yes or no.
            _chipFont.BeforeOpen = _chipKey.BeforeOpen = _chipBorder.BeforeOpen = ConfirmLeaveGroup;
            _cmbFont.BeforeChange = _stpFontSize.BeforeChange = _stpBorderThickness.BeforeChange = ConfirmLeaveGroup;
            _chkAutoSize.BeforeToggle = ConfirmLeaveGroup;
        }

        private void OpenGroupEditor()
        {
            // Index 0 is "(no group)" — pass null so GroupEditorForm shows the first real group.
            string current = _cmbGroup.SelectedIndex == 0 ? null : _cmbGroup.SelectedItem?.Text;
            using var dlg = new GroupEditorForm(_groups, initialGroupName: current);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _groups.Clear();
            _groups.AddRange(dlg.ResultGroups);
            ResultGroupsChanged = true;
            RebuildGroupChooser(current ?? "", refresh: true);
            _cmbGroup.Focus();
        }

        /// <summary>Rebuilds the group chooser after the group list changed; restores <paramref name="previous"/> by name, else "(no group)".</summary>
        private void RebuildGroupChooser(string previous, bool refresh)
        {
            var items = new List<TouchChoice> { new TouchChoice { Text = Lang.T("(no group)") } };   // index 0 always
            items.AddRange(_groups.Select(g => new TouchChoice { Text = g.Name }));
            int idx = 0;
            if (!string.IsNullOrEmpty(previous))
                for (int i = 1; i < items.Count; i++)
                    if (items[i].Text == previous) { idx = i; break; }
            _cmbGroup.SetItems(items, idx);
            if (refresh) { RefreshAppearanceFromGroup(); Refresh2(); }
        }

        // ══════════════════════════════════════════════════════════════
        //  Appearance: per-key → group → global
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolves every appearance value through per-key → currently selected group → global, updates the controls
        /// and caches the values in the <c>_loaded*</c> fields so <see cref="Apply"/> can tell what the user changed.
        /// </summary>
        private void RefreshAppearanceFromGroup()
        {
            if (_original == null || _chipFont == null) return;   // called before the UI is ready
            bool wasInit = _initialising;
            _initialising = true;
            try { RefreshAppearanceFromGroupCore(); }
            finally { _initialising = wasInit; }
        }

        private void RefreshAppearanceFromGroupCore()
        {
            // A fresh baseline: switching groups (or the initial load) resets what counts as "the user changed the font".
            _fontUserChanged = false;

            // The standard group is the resolution root; the owner's theme only backs layouts that pre-date it.
            var std    = _groups.FirstOrDefault(g => g.Name == SettingsManager.StandardGroupName);
            var ownerG = _ownerGlobal;

            Color gFc  = (std != null && !std.FontColor.IsEmpty)   ? std.FontColor   : ownerG?.FontColor   ?? ColorTranslator.FromHtml("#E0E0FF");
            Color gKc  = (std != null && !std.KeyColor.IsEmpty)    ? std.KeyColor    : ownerG?.KeyColor    ?? ColorTranslator.FromHtml("#2D2D4A");
            Color gBc  = (std != null && !std.BorderColor.IsEmpty) ? std.BorderColor : ownerG?.BorderColor ?? _globalBorderColor;
            int   gBt  = (std != null && std.BorderThickness >= 0) ? std.BorderThickness : ownerG?.BorderThickness ?? 1;
            string gFn = (std != null && !string.IsNullOrEmpty(std.FontName)) ? std.FontName : ownerG?.FontName ?? "Arial";

            // Index 0 is always "(no group)", so a real group is only selected when index > 0.
            KeyGroup grp = null;
            if (_cmbGroup != null && _cmbGroup.SelectedIndex > 0)
            {
                string gName = _cmbGroup.SelectedItem?.Text;
                grp = _groups.FirstOrDefault(g => g.Name == gName);
            }

            static Color Rc(Color pk, Color grpC, Color global) => !pk.IsEmpty ? pk : !grpC.IsEmpty ? grpC : global;

            // Group-resolved values (no per-key layer): what the selected group provides on its own.
            _groupFontColor   = Rc(Color.Empty, grp?.FontColor   ?? Color.Empty, gFc);
            _groupKeyColor    = Rc(Color.Empty, grp?.KeyColor    ?? Color.Empty, gKc);
            _groupBorderColor = Rc(Color.Empty, grp?.BorderColor ?? Color.Empty, gBc);
            string grpFnG = grp?.FontName ?? "";
            _groupFontName = !string.IsNullOrEmpty(grpFnG) ? grpFnG : gFn;
            int grpFsG = grp?.FontSize ?? 0;
            _groupFontSize = grpFsG > 0 ? grpFsG : 0;
            int grpBtG = grp?.BorderThickness ?? -1;
            _groupBorderThickness = grpBtG >= 0 ? grpBtG : gBt;

            bool isEmptyKey = string.IsNullOrEmpty(_original.Label) && string.IsNullOrEmpty(_original.Send);

            if (grp != null)
            {
                // A group is selected: show what the group provides, before the user decides to customise.
                _loadedFontColor       = _groupFontColor;
                _loadedKeyColor        = _groupKeyColor;
                _loadedBorderColor     = _groupBorderColor;
                _loadedFontName        = _groupFontName;
                _loadedFontSize        = _groupFontSize;
                _loadedBorderThickness = _groupBorderThickness;
            }
            else
            {
                // (no group): the key's own overrides, falling back to global.
                Color pfc = isEmptyKey ? Color.Empty : _original.FontColor;
                Color pkc = isEmptyKey ? Color.Empty : _original.KeyColor;
                Color pbc = isEmptyKey ? Color.Empty : _original.BorderColor;
                _loadedFontColor   = Rc(pfc, Color.Empty, gFc);
                _loadedKeyColor    = Rc(pkc, Color.Empty, gKc);
                _loadedBorderColor = Rc(pbc, Color.Empty, gBc);

                string pFn = isEmptyKey ? "" : (_original.FontName ?? "");
                _loadedFontName = !string.IsNullOrEmpty(pFn) ? pFn : gFn;
                int pFs = isEmptyKey ? 0 : _original.FontSize;
                _loadedFontSize = pFs > 0 ? pFs : 0;
                int pBt = isEmptyKey ? -1 : _original.BorderThickness;
                _loadedBorderThickness = pBt != -1 ? pBt : gBt;
            }

            _chipFont.Value   = _loadedFontColor;
            _chipKey.Value    = _loadedKeyColor;
            _chipBorder.Value = _loadedBorderColor;

            // Keep the real font name even if it isn't installed here (see _fontUserChanged).
            SelectOrInsertFont(_cmbFont, _loadedFontName);
            UpdateFontAvailabilityWarning(_cmbFont, _loadedFontName);

            int clampedSize = Math.Clamp(_loadedFontSize, 0, (int)_stpFontSize.Maximum);
            if (clampedSize > 0) { _stpFontSize.Value = clampedSize; _chkAutoSize.Checked = false; _stpFontSize.Enabled = true; }
            else                 { _stpFontSize.Value = 0;           _chkAutoSize.Checked = true;  _stpFontSize.Enabled = false; }

            _stpBorderThickness.Value = Math.Clamp(_loadedBorderThickness, -1, (int)_stpBorderThickness.Maximum);
        }

        // ══════════════════════════════════════════════════════════════
        //  Layers: action type, value, picker
        // ══════════════════════════════════════════════════════════════

        private SendMode ModeOf(int layer)
        {
            var modes = ModesOf(layer);
            int i = _types[layer].SelectedIndex;
            return i >= 0 && i < modes.Length ? modes[i] : SendMode.Text;
        }

        /// <summary>Selects <paramref name="mode"/> on a layer's chooser without raising its change event.</summary>
        private void SelectModeSilently(int layer, SendMode mode) => _types[layer].SelectSilently(Array.IndexOf(ModesOf(layer), mode));

        /// <summary>A key that shows a word prediction has no Shift or AltGr action (the Normal layer's type decides).</summary>
        private bool LayersDisabled => ModeOf(0) == SendMode.WordPrediction;

        private static string WpHint =>
            Lang.T("A word prediction key has no label. An action on Shift or AltGr is not possible.");

        /// <summary>
        /// The label stored for a word prediction key. It is only a placeholder (the key shows the predicted word), so it is not
        /// editable: a key that already was a prediction key keeps its label ("woord 1"), a new one gets "word N" for its slot.
        /// </summary>
        private string WpLabel()
        {
            bool wasWp = (_original.Send ?? "").StartsWith("wp:", StringComparison.Ordinal);
            return wasWp && !string.IsNullOrEmpty(_original.Label)
                ? _original.Label
                : string.Format(Lang.T("word {0}"), _wpSlot + 1);
        }

        private void SetHint(string text)
        {
            _lblHint.Text = text ?? "";
            _lblHint.Visible = !string.IsNullOrEmpty(text);
        }

        /// <summary>The user picked another action type for a layer.</summary>
        private void OnTypeChanged(int layer)
        {
            if (!_initialising) _layerTouched[layer] = true;
            var previous = _lastMode[layer];
            ApplyMode(layer, applyPicker: !_initialising);
            // A shortcut, a layout file name or a modifier token is never meant as text: do not carry it over to Text.
            if (!_initialising && ModeOf(layer) == SendMode.Text
                && (previous == SendMode.KeySequence || previous == SendMode.Layout || previous == SendMode.Modifier))
                _values[layer].Text = "";
        }

        // The action type each layer showed after its last ApplyMode, to know which type the user is leaving.
        private readonly SendMode[] _lastMode = new SendMode[Layers];

        /// <summary>
        /// Shows what the layer's type needs: the value box (or modifier chooser / prediction slot), the picker
        /// button, and the validation. <paramref name="applyPicker"/> is true when the user just chose the type
        /// (the value is reset for it) and false while loading.
        /// </summary>
        private void ApplyMode(int layer, bool applyPicker)
        {
            var mode = ModeOf(layer);
            bool isMod = layer == 0 && mode == SendMode.Modifier;
            bool isWp  = layer == 0 && mode == SendMode.WordPrediction;

            if (layer == 0) { _modChooser.Visible = isMod; _values[0].Visible = !isMod; }
            _values[layer].Enabled = !isWp && !(layer > 0 && LayersDisabled);

            if (isWp)
            {
                // Auto-assign the next free slot; if all 0–9 are taken it stays at 9 and the value says why.
                if (applyPicker)
                {
                    int next = 0;
                    while (_usedWpSlots.Contains(next) && next < 9) next++;
                    _wpSlot = Math.Min(9, next);
                }
                ShowWpValue();
            }
            else if (layer == 0 && _valueShowsWp)
            {
                // Leaving word prediction: the value box held the explanation, not something to type.
                // (Layer 0 only: the flag belongs to the Normal layer, and Shift / AltGr are applied after it.)
                _valueShowsWp = false;
                _values[0].Text = "";
            }

            bool showPicker = !isMod && (mode == SendMode.KeySequence || mode == SendMode.Layout);
            _pickers[layer].Visible = showPicker;
            UpdatePickerTexts();
            // Without a picker the value takes over its column instead of leaving a gap.
            Control cell = layer == 0 ? (Control)_valueHost0 : _values[layer];
            _grid.SetColumnSpan(cell, showPicker ? 1 : 2);

            if (applyPicker)
            {
                if (isMod) ApplyModChoice();
                else if (mode == SendMode.KeySequence) { _values[layer].Text = ""; SetHint(Lang.T("Press the record button to record, or type directly")); }
                else if (mode == SendMode.Layout) _values[layer].Text = "";
            }
            _values[layer].AccessibleName = ValueName(layer);
            ValidateLayoutField(layer);
            _lastMode[layer] = mode;
            if (layer == 0) UpdateLayerAvailability();
        }

        // What Shift and AltGr held before the Normal layer became a word prediction, so switching back brings it back.
        private readonly (string Label, SendMode Mode, string Value)?[] _stash = new (string, SendMode, string)?[Layers];

        /// <summary>
        /// A word prediction key has no Shift or AltGr action (the click handler returns before it looks at them), so while
        /// the Normal layer is a word prediction those two rows are emptied and disabled, with a line saying why. Choosing
        /// another type for the Normal layer brings back what they held.
        /// </summary>
        private void UpdateLayerAvailability()
        {
            bool wp = LayersDisabled;
            bool was = _initialising;
            _initialising = true;                    // not an edit by the user: must not mark the layers as touched
            try
            {
                // The Normal label of a prediction key is a placeholder that the predicted word replaces: not editable.
                if (wp && _labelStash == null) { _labelStash = _labels[0].Text; _labels[0].Text = ""; }
                else if (!wp && _labelStash != null) { _labels[0].Text = _labelStash; _labelStash = null; }
                _labels[0].Enabled = !wp;

                for (int i = 1; i < Layers; i++)
                {
                    if (wp && _stash[i] == null)
                    {
                        _stash[i] = (_labels[i].Text, ModeOf(i), _values[i].Text);
                        _labels[i].Text = "";
                        SelectModeSilently(i, SendMode.Text);
                        _values[i].Text = "";
                        ApplyMode(i, applyPicker: false);
                    }
                    else if (!wp && _stash[i] is { } s)
                    {
                        _stash[i] = null;
                        _labels[i].Text = s.Label;
                        SelectModeSilently(i, s.Mode);
                        _values[i].Text = s.Value;
                        ApplyMode(i, applyPicker: false);
                    }
                    _labels[i].Enabled = _types[i].Enabled = _values[i].Enabled = _pickers[i].Enabled = !wp;
                }
            }
            finally { _initialising = was; }

            string hint = WpHint;
            if (wp) SetHint(hint);
            else if (_lblHint.Text == hint) SetHint("");
            Refresh2();                              // the preview shows the placeholder of a prediction key
        }

        private string _labelStash;                  // the Normal label while the key is a word prediction

        private void UpdatePickerTexts()
        {
            for (int i = 0; i < Layers; i++)
            {
                bool layout = ModeOf(i) == SendMode.Layout;
                bool recording = _recording && _recordLayer == i;
                // The toolbar's own icon style: a ring with a red dot (record) or a red square (stop), the folder of "load" for Browse.
                string svg = layout ? "load.svg" : recording ? "stopRecording.svg" : "record.svg";
                _pickers[i].IconImage = SvgIconLoader.Load(svg, Fluent.TextPrimary, 28);
                _pickers[i].IconGlyph = _pickers[i].IconImage != null ? "" : layout ? GlyphFolder : recording ? GlyphStop : GlyphRecord;   // glyph only if the icon file is missing
                _pickers[i].Text = "";
                _pickers[i].AccessibleName = layout ? Lang.StripMnemonic(Lang.T("Browse…"))
                                           : recording ? Lang.T("Stop recording") : Lang.StripMnemonic(Lang.T("Record…"));
                _pickers[i].Invalidate();
            }
        }

        private void ShowWpValue()
        {
            _valueShowsWp = true;
            bool allFull = Enumerable.Range(0, 10).All(i => _usedWpSlots.Contains(i));
            bool was = _initialising;
            _initialising = true;
            _values[0].Text = allFull ? Lang.T("WP all slots full")
                                      : string.Format(Lang.T("Slot {0} (automatic)"), _wpSlot);
            _initialising = was;
        }

        /// <summary>Modifier keys are recognised by their label: the chosen modifier becomes the label (and "{Label}" as the value shown).</summary>
        private void ApplyModChoice()
        {
            if (_initialising || ModeOf(0) != SendMode.Modifier) return;
            int idx = _modChooser.SelectedIndex;
            if (idx < 0 || idx >= _modifiers.Length) return;
            var (modLabel, _) = _modifiers[idx];
            _txtSend.Text  = "{" + modLabel + "}";
            _txtLabel.Text = modLabel;
        }

        /// <summary>Flags the value box when the layer is a layout jump whose file cannot be found; clears the flag otherwise.</summary>
        private void ValidateLayoutField(int layer)
        {
            var box = _values[layer];
            if (ModeOf(layer) != SendMode.Layout) { _err.SetError(box, ""); return; }
            string path = box.Text.Trim();
            bool bad = !string.IsNullOrEmpty(path) && !File.Exists(ResolveLayoutPath(path));
            _err.SetError(box, bad ? Lang.T("err: layout file not found") : "");
        }

        /// <summary>Resolves a layout path the way <c>KeyboardForm.ResolveLayoutPath</c> does at run time.</summary>
        private string ResolveLayoutPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Path.IsPathRooted(path)) return path;
            if (_layoutDir != null)
            {
                string candidate = Path.Combine(_layoutDir, path);
                if (File.Exists(candidate)) return candidate;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        private void OnPickerClick(int layer)
        {
            if (ModeOf(layer) == SendMode.KeySequence) { if (_recording) StopRecording(cancelled: true); else StartRecording(layer); }
            else if (ModeOf(layer) == SendMode.Layout) BrowseLayout(layer);
        }

        private void BrowseLayout(int layer)
        {
            string initDir = _layoutDir ?? AppDomain.CurrentDomain.BaseDirectory;
            using var dlg = new OpenFileDialog
            {
                Title = Lang.T("Layout file"), Filter = "Keyboard layouts (*.kbl)|*.kbl|All files (*.*)|*.*", InitialDirectory = initDir,
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            string selected = dlg.FileName;
            // Prefer a relative path when the file is inside the layout directory: the layout stays portable.
            if (_layoutDir != null && selected.StartsWith(_layoutDir, StringComparison.OrdinalIgnoreCase))
                selected = selected.Substring(_layoutDir.Length).TrimStart('\\', '/');
            _values[layer].Text = selected;
        }

        /// <summary>Best mode for a stored send string of the Normal layer.</summary>
        private SendMode DetectSendMode(string send, string label)
        {
            if (string.IsNullOrEmpty(send) && _modifiers.Any(m => m.Label == label)) return SendMode.Modifier;
            if (!string.IsNullOrEmpty(send) && send.StartsWith("wp:", StringComparison.Ordinal)) return SendMode.WordPrediction;
            if (!string.IsNullOrEmpty(send) && send.StartsWith("layout:", StringComparison.Ordinal)) return SendMode.Layout;
            if (!string.IsNullOrEmpty(send) && !SendKeysHelper.IsPlainText(send)) return SendMode.KeySequence;
            return SendMode.Text;
        }

        /// <summary>Best mode for a stored Shift / AltGr send: only text, a key sequence or a layout jump make sense there.</summary>
        private static SendMode DetectLayerMode(string send)
        {
            if (!string.IsNullOrEmpty(send) && send.StartsWith("layout:", StringComparison.Ordinal)) return SendMode.Layout;
            if (!string.IsNullOrEmpty(send) && !SendKeysHelper.IsPlainText(send)) return SendMode.KeySequence;
            return SendMode.Text;
        }

        // ══════════════════════════════════════════════════════════════
        //  Recording
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Begins recording: a low-level keyboard hook captures the next keystroke (including Win-key combinations)
        /// into the layer's value instead of letting it reach the operating system.
        /// </summary>
        private void StartRecording(int layer)
        {
            if (_recording) return;
            _recording   = true;
            _recordLayer = layer;
            _winHeld     = false;
            UpdatePickerTexts();                // the stop symbol; clicking the button again cancels
            SetHint(Lang.T("Perform the key combination you want on the keyboard."));
            // The label that the shortcut being replaced would have got: a label like that (also one saved in an earlier session)
            // was not written by the user, so the new recording may replace it.
            _autoLabelBefore[layer] = ModeOf(layer) == SendMode.KeySequence ? LabelOfSend(FromHuman(_values[layer].Text)) : null;
            _values[layer].Text = "";

            if (HookDisabledForTest) return;    // tests drive CompleteRecording directly: a real hook would catch the keys of the whole machine

            _hookProc   = LowLevelHookCallback;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            if (_hookHandle == IntPtr.Zero)
            {
                StopRecording(cancelled: true);
                SetHint("Hook failed — try running as administrator");
            }
        }

        private void StopRecording(bool cancelled)
        {
            _recording = false;
            _winHeld   = false;
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            UpdatePickerTexts();
            SetHint(cancelled ? Lang.T("Cancelled") : Lang.T("Recorded — edit if needed"));
        }

        private IntPtr LowLevelHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var kbd = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUp   = wParam == (IntPtr)WM_KEYUP   || wParam == (IntPtr)WM_SYSKEYUP;

            switch (ClassifyHookKey(kbd.vkCode, isDown, isUp))
            {
                case HookKeyAction.WinUp:
                    _winHeld = false;
                    return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                case HookKeyAction.WinDown:
                    _winHeld = true;
                    return (IntPtr)1;
                case HookKeyAction.Pass:
                    return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            bool ctrl  = (Control.ModifierKeys & Keys.Control) != 0;
            bool alt   = (Control.ModifierKeys & Keys.Alt)     != 0;
            bool shift = (Control.ModifierKeys & Keys.Shift)   != 0;
            bool win   = _winHeld;                      // read now: the key-up of the Win key may arrive before the UI thread runs the line below
            int layer  = _recordLayer;
            uint vk    = kbd.vkCode;
            BeginInvoke((Action)(() => CompleteRecording(layer, vk, ctrl, alt, shift, win)));

            return (IntPtr)1;   // suppress: the key must not type into the app behind the editor
        }

        /// <summary>What the recorder's hook does with one keyboard event.</summary>
        internal enum HookKeyAction
        {
            /// <summary>Let the key through to the system.</summary>
            Pass,
            /// <summary>The Win key was released: forget it, let the key-up through.</summary>
            WinUp,
            /// <summary>The Win key was pressed: remember it and suppress it so the Start menu does not react.</summary>
            WinDown,
            /// <summary>A complete shortcut: record it and suppress the key.</summary>
            Record,
        }

        /// <summary>Decides what the hook does with a key event, without touching any state (see <see cref="HookKeyAction"/>).</summary>
        internal static HookKeyAction ClassifyHookKey(uint vk, bool isDown, bool isUp)
        {
            bool win = vk == VK_LWIN || vk == VK_RWIN;
            // Key-up events always pass: suppressing them would leave the OS thinking a key is still held.
            if (isUp) return win ? HookKeyAction.WinUp : HookKeyAction.Pass;
            if (!isDown) return HookKeyAction.Pass;

            // Escape is a key like any other (Ctrl+Esc, Alt+Esc ... are shortcuts): it is recorded. Recording is cancelled by
            // clicking the stop button (or by leaving the window).
            if (win) return HookKeyAction.WinDown;

            // A bare Ctrl / Alt / Shift is not a complete shortcut: wait for the real key.
            if (vk == 0x10 || vk == 0xA0 || vk == 0xA1 ||
                vk == 0x11 || vk == 0xA2 || vk == 0xA3 ||
                vk == 0x12 || vk == 0xA4 || vk == 0xA5)
                return HookKeyAction.Pass;

            return HookKeyAction.Record;
        }

        /// <summary>Puts a recorded combination in <paramref name="layer"/>: the type that fits it, its value, and a label if the user wrote none; then stops recording.</summary>
        internal void CompleteRecording(int layer, uint vk, bool ctrl, bool alt, bool shift, bool win)
        {
            string send = BuildSendFromHook(vk, ctrl, alt, shift, win);

            // Switch to the type that fits the recorded combination (silently: the value is filled in below).
            var newMode = layer == 0 ? DetectSendMode(send, _labels[0].Text) : DetectLayerMode(send);
            if (newMode != ModeOf(layer))
            {
                bool was = _initialising;
                _initialising = true;
                SelectModeSilently(layer, newMode);
                ApplyMode(layer, applyPicker: false);
                _initialising = was;
            }
            _layerTouched[layer] = true;
            _values[layer].Text = ToHuman(send);            // "{Ctrl}c", not "^c"
            ApplyRecordedLabel(layer, BuildHumanLabel(vk, ctrl, alt, shift, win));
            StopRecording(cancelled: false);
        }

        /// <summary>
        /// Puts the label of a recorded shortcut in the layer's label box, unless the user wrote a label of their own:
        /// an empty label, or the one a previous recording put there, is replaced.
        /// </summary>
        private void ApplyRecordedLabel(int layer, string label)
        {
            string cur = _labels[layer].Text;
            bool ownLabel = !string.IsNullOrWhiteSpace(cur) && cur != _recordedLabel[layer]
                            && !string.Equals(cur, _autoLabelBefore[layer], StringComparison.OrdinalIgnoreCase);
            if (ownLabel) return;
            _recordedLabel[layer] = label;
            _labels[layer].Text = label;
        }

        /// <summary>
        /// The label a recording would give a stored shortcut ("^c" gives "Ctrl+C", "win:+s" gives "Win+Shift+S"), or null when
        /// <paramref name="send"/> is not a shortcut of that shape. Compared without regard to case: stock layouts write "Ctrl+c".
        /// </summary>
        internal static string LabelOfSend(string send)
        {
            if (string.IsNullOrEmpty(send)) return null;
            var parts = new List<string>();
            if (send.StartsWith("win:", StringComparison.Ordinal)) { parts.Add("Win"); send = send.Substring(4); }
            int i = 0;
            for (; i < send.Length; i++)
            {
                if (send[i] == '^') parts.Add("Ctrl");
                else if (send[i] == '%') parts.Add("Alt");
                else if (send[i] == '+') parts.Add("Shift");
                else break;
            }
            string key = send.Substring(i);
            if (key.Length > 2 && key[0] == '{' && key[key.Length - 1] == '}') key = key.Substring(1, key.Length - 2);
            else if (key.Length == 1) key = key.ToUpperInvariant();
            else return null;                         // typed text, a group in parentheses: not a single shortcut
            if (parts.Count == 0) return null;        // a bare key is not a shortcut: its label is the user's
            parts.Add(key);
            return string.Join("+", parts);
        }

        /// <summary>The internal send string from raw hook data: ^ Ctrl, % Alt, + Shift, or "win:" for the Win key.</summary>
        internal static string BuildSendFromHook(uint vk, bool ctrl, bool alt, bool shift, bool win)
        {
            bool asPrefix = ShiftIsPrefix(vk, ctrl, alt, shift, win);
            string keyPart = VkCodeToSendKeys(vk, shift);
            // Shift alone on a letter is just the capital letter ("A"): SendKeys types that without a modifier.
            if (shift && !asPrefix) keyPart = keyPart.ToUpperInvariant();

            string prefix = "";
            if (ctrl)     prefix += "^";
            if (alt)      prefix += "%";
            if (asPrefix) prefix += "+";
            // The Win key is sent by SendKeysHelper.SendWinKey, which reads the same ^ % + prefix of its payload.
            return (win ? "win:" : "") + prefix + keyPart;
        }

        /// <summary>A short readable label ("Ctrl+Shift+A") used when the label is still empty after recording.</summary>
        internal static string BuildHumanLabel(uint vk, bool ctrl, bool alt, bool shift, bool win)
        {
            var parts = new List<string>();
            if (win)   parts.Add("Win");
            if (ctrl)  parts.Add("Ctrl");
            if (alt)   parts.Add("Alt");
            if (ShiftIsPrefix(vk, ctrl, alt, shift, win)) parts.Add("Shift");
            string key = VkCodeToSendKeys(vk, shift).TrimStart('{').TrimEnd('}');
            if (vk >= 0x41 && vk <= 0x5A) key = key.ToUpper();
            parts.Add(key);
            return string.Join("+", parts);
        }

        /// <summary>
        /// True when Shift is a modifier of the combination (the "+" prefix). Only a letter pressed with Shift alone is not:
        /// there Shift just makes the capital letter. With Ctrl, Alt or Win held, or on any other key, it is a modifier.
        /// </summary>
        private static bool ShiftIsPrefix(uint vk, bool ctrl, bool alt, bool shift, bool win) =>
            shift && (ctrl || alt || win || !IsLetterVk(vk));

        private static bool IsLetterVk(uint vk) => vk >= 0x41 && vk <= 0x5A;

        /// <summary>A virtual key code as a SendKeys string: letters lowercase, digits, {NAME} tokens; unknown keys as {hex}.</summary>
        private static string VkCodeToSendKeys(uint vk, bool shift)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)('a' + vk - 0x41)).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)('0' + vk - 0x30)).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "{NUMPAD" + (vk - 0x60) + "}";
            if (vk >= 0x70 && vk <= 0x7B) return "{F" + (vk - 0x70 + 1) + "}";
            return vk switch
            {
                0x0D => "{ENTER}", 0x08 => "{BACKSPACE}", 0x09 => "{TAB}", 0x1B => "{ESC}", 0x2E => "{DELETE}",
                0x2D => "{INSERT}", 0x24 => "{HOME}", 0x23 => "{END}", 0x21 => "{PGUP}", 0x22 => "{PGDN}",
                0x25 => "{LEFT}", 0x26 => "{UP}", 0x27 => "{RIGHT}", 0x28 => "{DOWN}",
                0x20 => " ",                                    // Space is a literal space, not a token
                0x14 => "{CAPSLOCK}", 0x90 => "{NUMLOCK}", 0x91 => "{SCROLLLOCK}", 0x2C => "{PRTSC}", 0x13 => "{BREAK}",
                _    => "{" + vk.ToString("X2") + "}",
            };
        }

        // ── Human-readable ↔ internal Send conversion ─────────────────
        // Internal (stored, used by SendKeysHelper): ^ Ctrl, % Alt, + Shift, "win:" prefix.
        // Human-readable (shown in the editor):      {Ctrl}c   {Alt}{F4}   {Win}d

        /// <summary>Converts an internal send string to the readable form shown in the editor. Grouping parentheses are dropped (lossy by design).</summary>
        internal static string ToHuman(string send)
        {
            if (string.IsNullOrEmpty(send)) return send;
            if (send.StartsWith("win:")) return "{Win}" + ToHuman(send.Substring(4));
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < send.Length)
            {
                char ch = send[i];
                if (ch == '{')
                {
                    // A {TOKEN} is copied as a whole: what is inside is a key name or an escaped character
                    // ("{(}", "{+}", "{^}"), not a modifier or a group. "{}}" is the escaped closing brace.
                    int end = i + 1 < send.Length && send[i + 1] == '}' ? i + 2 : send.IndexOf('}', i + 1);
                    if (end < 0) end = send.Length - 1;
                    sb.Append(send, i, end - i + 1);
                    i = end + 1;
                }
                else if (ch == '^') { sb.Append("{Ctrl}");  i++; }
                else if (ch == '%') { sb.Append("{Alt}");   i++; }
                else if (ch == '+') { sb.Append("{Shift}"); i++; }
                else if (ch == '(')
                {
                    i++;
                    while (i < send.Length && send[i] != ')') { sb.Append(send[i]); i++; }
                    if (i < send.Length) i++;
                }
                else { sb.Append(ch); i++; }
            }
            return sb.ToString();
        }

        /// <summary>Converts the readable form back to the internal send string.</summary>
        internal static string FromHuman(string human)
        {
            if (string.IsNullOrEmpty(human)) return human;
            if (human.StartsWith("{Win}")) return "win:" + FromHuman(human.Substring(5));
            return human.Replace("{Ctrl}", "^").Replace("{Alt}", "%").Replace("{Shift}", "+");
        }

        // ══════════════════════════════════════════════════════════════
        //  Populate, preview, apply
        // ══════════════════════════════════════════════════════════════

        /// <summary>Fills all controls from <paramref name="p"/>; called once from <see cref="BuildUI"/>.</summary>
        private void PopulateFields(KeyProps p)
        {
            _initialising = true;
            try
            {
                _labels[0].Text = p.Label ?? "";
                _labels[1].Text = p.ShiftLabel ?? "";
                _labels[2].Text = p.AltGrLabel ?? "";

                _stpColSpan.Value = Math.Max(1, Math.Min(_maxCols, _initColSpan));
                _stpRowSpan.Value = Math.Max(1, Math.Min(_maxRows, _initRowSpan));

                // The group first: RefreshAppearanceFromGroup resolves the per-key → group → global chain.
                int gi = string.IsNullOrEmpty(p.GroupName) ? 0 : _cmbGroup.Items.FindIndex(i => i.Text == p.GroupName);
                _cmbGroup.SelectSilently(gi >= 0 ? gi : 0);
                RefreshAppearanceFromGroup();

                // Layer 0 (Normal): the full set of types.
                var mode0 = DetectSendMode(p.Send ?? "", p.Label ?? "");
                SelectModeSilently(0, mode0);
                if (p.Send != null && p.Send.StartsWith("wp:") && int.TryParse(p.Send.Substring(3), out int slot))
                    _wpSlot = Math.Clamp(slot, 0, 9);
                switch (mode0)
                {
                    case SendMode.Modifier:
                        _values[0].Text = "{" + (p.Label ?? "") + "}";
                        int mi = Array.FindIndex(_modifiers, m => m.Label == (p.Label ?? ""));
                        if (mi >= 0) _modChooser.SelectSilently(mi);
                        break;
                    case SendMode.KeySequence:
                        _values[0].Text = ToHuman(p.Send ?? "");
                        SetHint(Lang.T("Press the record button to record again, or edit directly"));
                        break;
                    case SendMode.Layout:
                        _values[0].Text = (p.Send ?? "").Substring(7);          // strip "layout:"
                        break;
                    case SendMode.WordPrediction:
                        break;                                                   // ShowWpValue fills it
                    default:
                        _values[0].Text = p.Send ?? "";
                        break;
                }

                // Layers 1 and 2 (Shift, AltGr): text, key sequence or layout jump.
                for (int i = 1; i < Layers; i++)
                {
                    string raw = _origSend[i];
                    var mode = DetectLayerMode(raw);
                    SelectModeSilently(i, mode);
                    _values[i].Text = mode == SendMode.Layout ? raw.Substring(7)
                                    : mode == SendMode.KeySequence ? ToHuman(raw)
                                    : raw;
                }
                for (int i = 0; i < Layers; i++) ApplyMode(i, applyPicker: false);
            }
            finally { _initialising = false; }

            Array.Clear(_layerTouched, 0, Layers);
            Refresh2();
        }

        /// <summary>Updates the preview card to the current label, colours, font and border.</summary>
        private void Refresh2()
        {
            if (_preview == null || _chipFont == null || _stpBorderThickness == null) return;
            var ownerGlob = _ownerGlobal;
            string fn = _cmbFont.SelectedItem?.Text ?? ownerGlob?.FontName ?? "Arial";
            // A fixed size of 13 stands in for "auto" (the real size is decided when the key is drawn).
            int fs = (_chkAutoSize.Checked || _stpFontSize.Value == 0) ? 13 : (int)_stpFontSize.Value;
            int btRaw = (int)_stpBorderThickness.Value;
            int bt = btRaw == -1 ? (ownerGlob?.BorderThickness ?? 1) : btRaw;    // -1 = inherit

            string label = LayersDisabled ? WpLabel() : _labels[0]?.Text ?? "";
            _preview.Set(label, _chipKey.Value, _chipFont.Value, _chipBorder.Value, fn, fs, bt);
            _preview.AccessibleName = string.Format(
                Lang.T("preview: key '{0}', key colour {1}, font colour {2}, {3} {4} pt"),
                label, SettingsManager.Hex(_chipKey.Value), SettingsManager.Hex(_chipFont.Value), fn, fs);
        }

        /// <summary>The send string a layer stores.</summary>
        private string BuildSend(int layer, string label)
        {
            var mode = ModeOf(layer);
            string text = _values[layer].Text;
            if (layer == 0)
            {
                switch (mode)
                {
                    case SendMode.Modifier:       return "";                       // the engine recognises a modifier by its label
                    case SendMode.WordPrediction: return "wp:" + _wpSlot;
                    case SendMode.KeySequence:    return FromHuman(text);          // already in SendKeys syntax: do NOT escape
                    case SendMode.Layout:
                        string path = text.Trim();
                        return string.IsNullOrEmpty(path) ? "" : "layout:" + path;
                    default:
                        string send = SendKeysHelper.EscapeForSend(text);
                        return string.IsNullOrEmpty(send) ? label : send;
                }
            }
            // A word prediction key has no Shift / AltGr action.
            if (LayersDisabled) return "";
            // Shift / AltGr: a layer the user did not touch keeps exactly what was stored (the readable form is lossy).
            if (!_layerTouched[layer]) return _origSend[layer];
            switch (mode)
            {
                case SendMode.Layout:      { string path = text.Trim(); return string.IsNullOrEmpty(path) ? "" : "layout:" + path; }
                case SendMode.KeySequence: return FromHuman(text) ?? "";
                default:                   return text ?? "";
            }
        }

        /// <summary>Builds the new <see cref="KeyProps"/> from the controls and closes the dialog with OK.</summary>
        private void Apply()
        {
            // An invalid field (e.g. an unresolvable layout path) blocks Apply; the section that holds it opens and
            // its ErrorProvider icon is the feedback — no blocking message box on top of it.
            if (ShowFirstSectionWithError()) return;

            // A word prediction key keeps its placeholder label (it is not editable: the predicted word replaces it).
            string label = LayersDisabled ? WpLabel() : _labels[0].Text.Trim();

            bool isNoGroup = _cmbGroup.SelectedIndex == 0;
            string groupName = isNoGroup ? "" : (_cmbGroup.SelectedItem?.Text ?? "");

            Color parsedFc = _chipFont.Value, parsedKc = _chipKey.Value, parsedBc = _chipBorder.Value;            string curFont = _cmbFont.SelectedItem?.Text ?? "";
            int rawFs = (_chkAutoSize.Checked || _stpFontSize.Value == 0) ? 0 : (int)_stpFontSize.Value;
            int rawBt = (int)_stpBorderThickness.Value;

            // A group is selected but a field was changed away from what the group provides: detach the key so the
            // explicit values are kept.
            if (!isNoGroup && AppearanceDiffersFromGroup())
            {
                isNoGroup = true;
                groupName = "";
            }

            Color fc, kc, bc;
            string fontName;
            int fontSize, borderThickness;
            if (isNoGroup)
            {
                // The key owns all its appearance: save every field so it looks the same without a group.
                fc = parsedFc; kc = parsedKc; bc = parsedBc;
                fontName = !string.IsNullOrEmpty(curFont) ? curFont : _loadedFontName;
                fontSize = rawFs != 0 ? rawFs : _loadedFontSize > 0 ? _loadedFontSize : 0;
                borderThickness = rawBt >= 0 ? rawBt : _loadedBorderThickness;
            }
            else
            {
                // Unchanged in a group: clear every per-key override so later group edits still cascade.
                fc = kc = bc = Color.Empty;
                fontName = ""; fontSize = 0; borderThickness = -1;
            }

            string send = BuildSend(0, label);
            // A send without a label: mirror the send as the label so the key has something visible.
            if (string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(send)) label = send;

            ResultColSpan = (int)_stpColSpan.Value;
            ResultRowSpan = (int)_stpRowSpan.Value;

            // A word prediction key has no Shift / AltGr label or action (those rows are empty and disabled).
            bool wpKey = LayersDisabled;
            Result = new KeyProps(label, send,
                                  wpKey ? "" : _labels[1].Text ?? "", BuildSend(1, ""),
                                  wpKey ? "" : _labels[2].Text ?? "", BuildSend(2, ""))
            {
                FontName = fontName, FontSize = fontSize,
                FontColor = fc, KeyColor = kc, BorderColor = bc,
                BorderThickness = borderThickness,
                GroupName = groupName,
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// True when a colour, the font, the font size or the border thickness no longer matches what the selected group provides:
        /// <see cref="Apply"/> then takes the key out of the group (it keeps its own look). The user is asked before it comes to that
        /// (<see cref="ConfirmLeaveGroup"/>); this stays as the safety net for a change that did not pass the question.
        /// </summary>
        private bool AppearanceDiffersFromGroup()
        {
            int rawFs = (_chkAutoSize.Checked || _stpFontSize.Value == 0) ? 0 : (int)_stpFontSize.Value;
            int rawBt = (int)_stpBorderThickness.Value;
            return !ColorsMatchRgb(_chipFont.Value,   _groupFontColor) || !ColorsMatchRgb(_chipKey.Value, _groupKeyColor) ||
                   !ColorsMatchRgb(_chipBorder.Value, _groupBorderColor) || _fontUserChanged ||
                   rawFs != _groupFontSize || rawBt != _groupBorderThickness;
        }

        /// <summary>
        /// Asked before the user changes the look of a key that is in a group (a click on a colour, the font, the size, the border
        /// thickness or "Auto"): Yes takes the key out of the group, "(no group)" is chosen at once and the action goes on; No leaves
        /// everything as it was and the action does not happen. The key keeps the values it shows (those of the group): <see cref="Apply"/>
        /// then saves them as its own. Without a group, or while the dialog is being filled, nothing is asked.
        /// </summary>
        private bool ConfirmLeaveGroup()
        {
            if (_initialising || _cmbGroup == null || _cmbGroup.SelectedIndex <= 0) return true;
            string title = Lang.T("title: leave group");
            string text  = string.Format(Lang.T("ask: leave group"), _cmbGroup.SelectedItem?.Text);
            bool yes = LeaveGroupAnswer != null ? LeaveGroupAnswer(title, text)
                     : !IsHandleCreated || !Visible || TouchMessage.Confirm(this, title, text);     // not on screen: no window to ask in
            if (!yes) return false;
            _cmbGroup.SelectSilently(0);                              // raises nothing: the shown values stay, only the group goes
            return true;
        }

        /// <summary>True when both colours are set and have identical R/G/B components.</summary>
        private static bool ColorsMatchRgb(Color a, Color b) =>
            !a.IsEmpty && !b.IsEmpty && a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
