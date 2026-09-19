using System.Collections.Generic;

namespace OnScreenKeyboard
{
    /// <summary>
    /// The undo / redo bookkeeping of the keyboard editor, kept free of any UI so it can be tested
    /// on its own. It only orders and caps snapshots; taking and applying them is the caller's job.
    /// </summary>
    /// <typeparam name="T">The snapshot type (for the keyboard: layout + theme + window + meta).</typeparam>
    internal sealed class UndoHistory<T>
    {
        /// <summary>Most snapshots kept on the undo side; the oldest is dropped beyond this.</summary>
        public const int MaxDepth = 50;

        // Newest entry at the front: AddFirst / RemoveFirst / RemoveLast are all O(1).
        private readonly LinkedList<T> _undo = new LinkedList<T>();
        private readonly LinkedList<T> _redo = new LinkedList<T>();

        public int  UndoCount => _undo.Count;
        public int  RedoCount => _redo.Count;
        public bool CanUndo   => _undo.Count > 0;
        public bool CanRedo   => _redo.Count > 0;

        /// <summary>
        /// Records the state as it was BEFORE a new edit. A new edit invalidates everything that
        /// could be redone.
        /// </summary>
        public void Push(T snapshotBeforeEdit)
        {
            _undo.AddFirst(snapshotBeforeEdit);
            _redo.Clear();
            if (_undo.Count > MaxDepth) _undo.RemoveLast();
        }

        /// <summary>
        /// Steps back. <paramref name="current"/> (the state being left) becomes redoable and
        /// <paramref name="restored"/> is the snapshot to apply. False, with nothing changed, when
        /// there is nothing to undo.
        /// </summary>
        public bool TryUndo(T current, out T restored)
        {
            if (_undo.Count == 0) { restored = default!; return false; }
            _redo.AddFirst(current);
            restored = _undo.First!.Value;
            _undo.RemoveFirst();
            return true;
        }

        /// <summary>Steps forward again; the mirror image of <see cref="TryUndo"/>.</summary>
        public bool TryRedo(T current, out T restored)
        {
            if (_redo.Count == 0) { restored = default!; return false; }
            _undo.AddFirst(current);
            restored = _redo.First!.Value;
            _redo.RemoveFirst();
            return true;
        }

        /// <summary>Forgets all history, e.g. when a different layout file is loaded.</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
