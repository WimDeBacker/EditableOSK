// StickyModifierTests.cs — what "Sticky modifiers" does (ModifierLatch) and that the option's tooltip says it.
//
// The tooltip once described the behaviour of the option being OFF (one tap applies to the next key), which is how the keyboard works
// either way; what the option adds is the locked state on the second tap. KeyboardForm cannot be built in the suite, so the rule was
// pulled out into ModifierLatch and is tested here, and the wording is checked against it.

using System;
using System.Linq;

namespace OnScreenKeyboard
{
    public static partial class TestRunner
    {
        private static void T_StickyModifiers()
        {
            Section("Sticky modifiers — the latch / lock rule and the wording of its tooltip");

            ModifierState Taps(ModifierState start, bool sticky, int n)
            {
                var s = start;
                for (int i = 0; i < n; i++) s = ModifierLatch.Toggle(s, sticky);
                return s;
            }

            // Option OFF (what a mouse user gets by default): one tap latches, the next tap clears; there is no locked state.
            Assert(Taps(ModifierState.Off, false, 1) == ModifierState.Latched, "off: one tap latches the modifier for the next key");
            Assert(Taps(ModifierState.Off, false, 2) == ModifierState.Off, "off: a second tap clears it again");
            bool everLocked = false;
            var st = ModifierState.Off;
            for (int i = 0; i < 12; i++) { st = ModifierLatch.Toggle(st, false); everLocked |= st == ModifierState.Locked; }
            Assert(!everLocked, "off: however often it is tapped, the modifier is never locked");
            Assert(ModifierLatch.Toggle(ModifierState.Locked, false) == ModifierState.Off, "off: a leftover locked modifier (the option was switched off meanwhile) is cleared by a tap");

            // Option ON (Windows Sticky Keys): tap 1 latches, tap 2 locks, tap 3 clears.
            Assert(Taps(ModifierState.Off, true, 1) == ModifierState.Latched, "on: tap 1 latches");
            Assert(Taps(ModifierState.Off, true, 2) == ModifierState.Locked, "on: tap 2 locks it on");
            Assert(Taps(ModifierState.Off, true, 3) == ModifierState.Off, "on: tap 3 clears it");
            Assert(Taps(ModifierState.Off, true, 4) == ModifierState.Latched, "on: the cycle starts again");

            // After an ordinary key: a latched modifier has done its job, a locked one stays, an off one stays off.
            Assert(ModifierLatch.AfterKey(ModifierState.Latched) == ModifierState.Off, "after a key: a latched modifier switches off");
            Assert(ModifierLatch.AfterKey(ModifierState.Locked) == ModifierState.Locked, "after a key: a locked modifier stays on");
            Assert(ModifierLatch.AfterKey(ModifierState.Off) == ModifierState.Off, "after a key: an off modifier stays off");

            // Typing "Shift, a, b" with the option off gives one capital; with the locked state (on, two taps) it gives two.
            int capitals(bool sticky, int taps)
            {
                var s = Taps(ModifierState.Off, sticky, taps);
                int n = 0;
                for (int key = 0; key < 3; key++) { if (s != ModifierState.Off) n++; s = ModifierLatch.AfterKey(s); }
                return n;
            }
            Assert(capitals(false, 1) == 1, "off: Shift once, then three letters: one capital");
            Assert(capitals(true, 1) == 1, "on: Shift once is the same: one capital");
            Assert(capitals(true, 2) == 3, "on: Shift twice (locked), then three letters: three capitals");
            Assert(capitals(false, 2) == 0, "off: Shift twice just cancels itself");

            // The wording must describe the extra locked state, in both languages (the old text described the off behaviour).
            string was = Lang.CurrentCode;
            try
            {
                Lang.Load("en");
                string en = Lang.T("tip: Sticky modifiers");
                Assert(en.Contains("twice") && en.Contains("lock") && en.Contains("third time"),
                    "tooltip (English): says that a second tap locks the modifier and a third tap releases it");
                Assert(en.Contains("Off:") && en.Contains("next key"), "tooltip (English): says what Off means (one tap applies to the next key only)");
                Lang.Load("nl");
                string nl = Lang.T("tip: Sticky modifiers");
                Assert(nl != en && nl.Contains("twee keer") && nl.Contains("derde keer") && nl.Contains("vast"),
                    "tooltip (Dutch): says that a second tap locks the modifier and a third tap releases it");
                Assert(nl.Contains("Uit:") && nl.Contains("volgende toets"), "tooltip (Dutch): says what Off means");
            }
            finally { Lang.Load(was); }

            // The check box in the Keyboard Editor carries that tooltip (so the words reach the user).
            using var f = KeyboardEditor();
            var tips = (System.Collections.IList)typeof(FluentDialogBase).GetField("_transTooltips", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f);
            bool found = false;
            foreach (var t in tips)
            {
                var ctrl = (System.Windows.Forms.Control)t.GetType().GetField("Item1").GetValue(t);
                var get = (Func<string>)t.GetType().GetField("Item2").GetValue(t);
                if (ctrl == Priv<System.Windows.Forms.Control>(f, "_chkStickyMods") && get() == Lang.T("tip: Sticky modifiers")) found = true;
            }
            Assert(found, "the Sticky modifiers check box has the 'tip: Sticky modifiers' tooltip");
        }
    }
}
