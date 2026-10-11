// LayoutPickerDialog.cs — "Open a keyboard": the ready-made layouts that came with the program, each with its title and a short description in the
// language of the screens, and a button for any other file. The layouts live in the program's folder (Program Files), where nobody looks;
// this list is how a user finds them.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OnScreenKeyboard
{
    /// <summary>One layout in the list: its file, its title and description for the language of the screens.</summary>
    internal sealed class LayoutInfo
    {
        public string Path, Title, Description;

        /// <summary>
        /// The layouts (*.kbl) in <paramref name="folder"/>, sorted by title. A title and a description are taken in the language of the screens,
        /// else in the language of the keyboard itself, else English, else (title) the file name. Files that cannot be read are left out.
        /// </summary>
        public static List<LayoutInfo> FromFolder(string folder, string screenLanguage)
        {
            var result = new List<LayoutInfo>();
            try
            {
                foreach (string file in Directory.GetFiles(folder, "*.kbl"))
                {
                    var info = SettingsManager.ReadInfo(file);
                    if (info == null) continue;
                    var (titles, descriptions, language) = info.Value;
                    string Pick(Dictionary<string, string> d) => GroupNames.Lookup(d, screenLanguage) ?? GroupNames.Lookup(d, language) ?? GroupNames.Lookup(d, "en") ?? "";
                    string title = Pick(titles);
                    result.Add(new LayoutInfo
                    {
                        Path = file,
                        Title = title.Length > 0 ? title : System.IO.Path.GetFileNameWithoutExtension(file),
                        Description = Pick(descriptions),
                    });
                }
            }
            catch { }
            return result.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    internal sealed class LayoutPickerDialog : FluentDialogBase
    {
        private readonly IReadOnlyList<LayoutInfo> _items;
        private readonly TouchList _list = new TouchList { Dock = DockStyle.Fill, TabIndex = 0 };
        private readonly Label _description;

        /// <summary>The file of the layout the user chose; null when the user asked for another file (<see cref="BrowseRequested"/>) or cancelled.</summary>
        public string ChosenPath { get; private set; }

        /// <summary>True when the user chose "Other file…": the caller shows the ordinary file dialog.</summary>
        public bool BrowseRequested { get; private set; }

        public LayoutPickerDialog(IReadOnlyList<LayoutInfo> items)
        {
            _items = items;
            Text = Lang.T("layouts: title");
            var cancel = MakeTouchButton(() => Lang.T("Cancel"));
            var browse = MakeTouchButton(() => Lang.T("layouts: other"));
            var open   = MakeTouchButton(() => Lang.T("layouts: open"), FluentButton.Variant.Success);
            BuildFrame(MakeFooter(null, cancel, browse, open), withSections: false);

            var t = AddSection(() => Lang.T("layouts: title"));
            AddWideRow(t, Wrap(new Label
            {
                Text = Lang.T("layouts: hint"), AutoSize = true, MaximumSize = new Size(Touch.LabelMaxWidth * 3, 0), UseMnemonic = false,
                Font = Fluent.FontLabel, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
            }), fill: false);

            // The list sits in a 1 px frame, like the list of the Group Editor: its colour is the control boundary and shows the focus.
            var frame = new Panel
            {
                Padding = new Padding(1), Dock = DockStyle.Fill, Margin = new Padding(0, Touch.Gap, 0, Touch.Gap),
                MinimumSize = new Size(0, 5 * Touch.Target + 2), Height = 5 * Touch.Target + 2, Tag = "notheme",
                BackColor = _dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder,
            };
            frame.Controls.Add(_list);
            _list.GotFocus  += (s, e) => frame.BackColor = _dark ? Fluent.DialogDarkText : Fluent.Accent;
            _list.LostFocus += (s, e) => frame.BackColor = _dark ? Fluent.DialogDarkBorder : Fluent.ControlBorder;
            _list.AccessibleName = Lang.T("layouts: title");
            _list.EmptyText = Lang.T("layouts: none");
            foreach (var i in items) _list.Items.Add(i.Title);
            AddWideRow(t, frame);

            // Always three lines of room, so a long description never makes the window grow when another row is chosen.
            _description = Wrap(new Label
            {
                AutoSize = true, UseMnemonic = false, MaximumSize = new Size(Touch.LabelMaxWidth * 3, 0),
                MinimumSize = new Size(0, Fluent.FontHint.Height * 3 + 4),
                Font = Fluent.FontHint, ForeColor = Fluent.TextPrimary, BackColor = Color.Transparent,
            });
            AddWideRow(t, _description, fill: false);

            _list.SelectedIndexChanged += (s, e) => ShowDescription();
            _list.DoubleClick += (s, e) => Choose();
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Choose(); e.Handled = true; e.SuppressKeyPress = true; } };
            open.Click   += (s, e) => Choose();
            browse.Click += (s, e) => { BrowseRequested = true; DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            open.Enabled = items.Count > 0;
            _list.SelectedIndexChanged += (s, e) => open.Enabled = _list.SelectedIndex >= 0;
            if (items.Count > 0) _list.SelectedIndex = 0;
            AcceptButton = open;
            CancelButton = cancel;
            ActiveControl = _list;
        }

        private void ShowDescription()
        {
            int i = _list.SelectedIndex;
            _description.Text = i >= 0 && i < _items.Count ? _items[i].Description : "";
            _description.AccessibleName = _description.Text;
        }

        private void Choose()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            ChosenPath = _items[i].Path;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
