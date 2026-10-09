// ModifierLatch.cs — the state of a Shift / Ctrl / Alt key on the on-screen keyboard.
//
// A modifier is Off, Latched (applies to the next key only, then switches off) or Locked (stays on until it is tapped again,
// like Caps Lock). "Sticky modifiers" (LayoutMeta.StickyModifiers) decides whether the Locked state exists:
//   off  one tap latches, the next tap clears it;
//   on   tap 1 latches, tap 2 locks, tap 3 clears (the Windows Sticky Keys behaviour).
// Caps is a lock only (lockOnly): one tap switches it on, the next tap off, whatever the option says, like the Caps Lock key.
// Pulled out of KeyboardForm (which cannot be built in the test suite) so the rule can be tested, and so the tooltip of the option
// can be checked against it.

namespace OnScreenKeyboard
{
    internal enum ModifierState { Off, Latched, Locked }

    internal static class ModifierLatch
    {
        /// <summary>
        /// The state after the modifier was tapped. <paramref name="lockOnly"/> (Caps): the modifier is never just latched, a tap
        /// switches it on (locked) or off, whatever <paramref name="sticky"/> says.
        /// </summary>
        public static ModifierState Toggle(ModifierState current, bool sticky, bool lockOnly = false)
        {
            if (lockOnly)
                return current == ModifierState.Off ? ModifierState.Locked : ModifierState.Off;
            if (!sticky)                      // no locked state: a tap latches, the next tap clears (a leftover Locked is cleared too)
                return current == ModifierState.Off ? ModifierState.Latched : ModifierState.Off;
            switch (current)
            {
                case ModifierState.Off:     return ModifierState.Latched;
                case ModifierState.Latched: return ModifierState.Locked;
                default:                    return ModifierState.Off;
            }
        }

        /// <summary>The state after an ordinary key was pressed: a latched modifier has done its job, a locked one stays.</summary>
        public static ModifierState AfterKey(ModifierState current) =>
            current == ModifierState.Latched ? ModifierState.Off : current;
    }
}
