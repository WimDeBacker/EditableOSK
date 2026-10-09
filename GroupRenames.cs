// GroupRenames.cs — what the Group Editor renamed and deleted, for the keys that name those groups.
//
// A key points at its group by name (KeyProps.GroupName). The Group Editor works on a copy of the groups, so when it renames or deletes
// one, the keys must follow: renamed, they get the new name; deleted, they fall back to no group. Tracked by group object (not by name) in
// GroupEditorForm; this class only carries the result and maps one name.

using System;
using System.Collections.Generic;

namespace OnScreenKeyboard
{
    internal sealed class GroupRenames
    {
        /// <summary>Old name to new name, for the groups that were renamed.</summary>
        public IReadOnlyDictionary<string, string> Renames { get; }

        /// <summary>The names of the groups that were deleted.</summary>
        public IReadOnlyCollection<string> Deleted { get; }

        public GroupRenames(Dictionary<string, string> renames, HashSet<string> deleted)
        {
            Renames = renames ?? new Dictionary<string, string>();
            Deleted = deleted ?? new HashSet<string>();
        }

        /// <summary>True when something was renamed or deleted.</summary>
        public bool Any => Renames.Count > 0 || Deleted.Count > 0;

        /// <summary>
        /// The group name a key with <paramref name="name"/> should have now: the new name when the group was renamed, "" (no group) when it was
        /// deleted, else unchanged. All names are mapped in one pass from the old names, so two groups that swap names work.
        /// </summary>
        public string Map(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (Renames.TryGetValue(name, out string renamed)) return renamed;
            foreach (string d in Deleted) if (d == name) return "";
            return name;
        }
    }
}
