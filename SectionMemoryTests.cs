// SectionMemoryTests.cs — a window with tabs opens on the tab that was open last time (this run of the program only).

using System.Windows.Forms;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_RememberSection()
        {
            Section("Windows with tabs — the tab that was open is open again next time");
            bool was = FluentDialogBase.RememberSections;
            try
            {
                FluentDialogBase.ForgetSections();
                FluentDialogBase.RememberSections = true;
                Lang.Load("en");

                using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), owner: null))
                {
                    f.ShowInTaskbar = false; f.Show();
                    Assert(f.SectionBarAccess.SelectedIndex == 0, "the first time the window opens on its first tab");
                    f.SectionBarAccess.Select(1, focus: false);                   // the person opens Accessibility
                    f.Close();
                }
                using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), owner: null))
                {
                    f.ShowInTaskbar = false; f.Show();
                    Assert(f.SectionBarAccess.SelectedIndex == 1, "the next window opens on Accessibility");
                    f.SectionBarAccess.Select(0, focus: false);
                    f.Close();
                }
                using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), owner: null))
                {
                    f.ShowInTaskbar = false; f.Show();
                    Assert(f.SectionBarAccess.SelectedIndex == 0, "going back to the first tab is remembered too");
                    f.Close();
                }

                // A tab chosen by code while the window is not shown, and a program that does not remember, change nothing.
                FluentDialogBase.ForgetSections();
                using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), owner: null))
                    f.SectionBarAccess.Select(2, focus: false);
                using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), owner: null))
                {
                    f.ShowInTaskbar = false; f.Show();
                    Assert(f.SectionBarAccess.SelectedIndex == 0, "a tab chosen while the window was not shown is not remembered");
                    FluentDialogBase.RememberSections = false;
                    f.SectionBarAccess.Select(1, focus: false);
                    f.Close();
                }
                using (var f = new KeyboardEditorForm(new VisualTheme(), new WindowState(), new LayoutMeta(), owner: null))
                {
                    FluentDialogBase.RememberSections = true;
                    f.ShowInTaskbar = false; f.Show();
                    Assert(f.SectionBarAccess.SelectedIndex == 0, "with remembering switched off nothing is remembered");
                    f.Close();
                }
            }
            finally { FluentDialogBase.RememberSections = was; FluentDialogBase.ForgetSections(); }
        }
    }
}
