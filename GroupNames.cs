// GroupNames.cs — the names of style groups.
//
// A group has an id (KeyGroup.Name: language-neutral, never changed) and a name per language (KeyGroup.Names). This class decides which
// name is shown, makes ids for new groups, and knows the ids and names of the groups of the ready-made layouts so that an older file
// (one name per group, Dutch) is read as a file with ids and names in both languages.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OnScreenKeyboard
{
    internal static class GroupNames
    {
        /// <summary>The language tag of the keyboard that is open (<c>nl-BE</c> …); set by the keyboard window, read when a name is chosen.</summary>
        internal static Func<string> KeyboardLanguage = () => "";

        /// <summary>True for the reserved root group of the style chain.</summary>
        internal static bool IsStandard(KeyGroup g) => g != null && g.Name == SettingsManager.StandardGroupName;

        /// <summary>
        /// The name to show for <paramref name="g"/>: in the language of the screens, else in the language of the keyboard, else English,
        /// else the first name there is; the standard group without any name is "Standard" / "Standaard"; else the id.
        /// </summary>
        internal static string Display(KeyGroup g) => Display(g, Lang.CurrentCode, KeyboardLanguage());

        internal static string Display(KeyGroup g, string interfaceLanguage, string keyboardLanguage)
        {
            if (g == null) return "";
            string n = Lookup(g.Names, interfaceLanguage) ?? Lookup(g.Names, keyboardLanguage) ?? Lookup(g.Names, "en");
            if (n != null) return n;
            if (IsStandard(g)) return Lang.T("group name: standard");
            n = g.Names.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            return n ?? g.Name;
        }

        /// <summary>The name for a language tag: an exact match (<c>nl-BE</c>), else the language part (<c>nl</c>), else a regional name of that language.</summary>
        internal static string Lookup(Dictionary<string, string> names, string tag)
        {
            if (names == null || names.Count == 0 || string.IsNullOrWhiteSpace(tag)) return null;
            tag = tag.Trim();
            if (names.TryGetValue(tag, out string v) && !string.IsNullOrWhiteSpace(v)) return v;
            string lang = tag.Split('-', '_')[0];
            if (names.TryGetValue(lang, out v) && !string.IsNullOrWhiteSpace(v)) return v;
            foreach (var kv in names)
                if (!string.IsNullOrWhiteSpace(kv.Value) && kv.Key.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        // ── Ids ──────────────────────────────────────────────────────

        /// <summary>A language-neutral id made from a name: lower-case letters and digits, words joined by "-" ("Function keys" → "function-keys").</summary>
        internal static string Slug(string name)
        {
            var sb = new StringBuilder();
            string d = (name ?? "").Normalize(NormalizationForm.FormD);
            bool dash = false;
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;     // accents
                if (c < 128 && char.IsLetterOrDigit(c)) { if (dash && sb.Length > 0) sb.Append('-'); sb.Append(char.ToLowerInvariant(c)); dash = false; }
                else dash = true;
            }
            return sb.Length > 0 ? sb.ToString() : "group";
        }

        /// <summary>An id made from <paramref name="name"/> that is not in <paramref name="used"/> (compared without regard to case): "vowels", "vowels-2", …</summary>
        internal static string UniqueId(string name, IEnumerable<string> used)
        {
            var taken = new HashSet<string>(used ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            string id = Slug(name);
            if (id == SettingsManager.StandardGroupName) id = "group";
            string candidate = id;
            for (int n = 2; taken.Contains(candidate) || candidate == SettingsManager.StandardGroupName; n++) candidate = id + "-" + n;
            return candidate;
        }

        // ── Older files ──────────────────────────────────────────────

        // The groups of the ready-made layouts, by the (Dutch) name an older file gives them: id, Dutch name, English name.
        private static readonly (string Old, string Id, string Nl, string En)[] Known =
        {
            ("Woord",           "prediction",   "Woordvoorspelling", "Word prediction"),
            ("Voorspelling",    "prediction",   "Woordvoorspelling", "Word prediction"),
            ("Prediction",      "prediction",   "Woordvoorspelling", "Word prediction"),
            ("Functietoetsen",  "function-keys","Functietoetsen",    "Function keys"),
            ("Cijfers",         "numbers",      "Cijfers",           "Numbers"),
            ("Klinkers",        "vowels",       "Klinkers",          "Vowels"),
            ("Medeklinkers",    "consonants",   "Medeklinkers",      "Consonants"),
            ("Leestekens",      "punctuation",  "Leestekens",        "Punctuation"),
            ("Besturing",       "controls",     "Besturing",         "Controls"),
            ("Rekenkunde",      "arithmetic",   "Rekenkunde",        "Arithmetic"),
            ("Vergelijkingen",  "comparisons",  "Vergelijkingen",    "Comparisons"),
            ("Haakjes",         "brackets",     "Haakjes",           "Brackets"),
            ("Constanten",      "constants",    "Constanten",        "Constants"),
            ("Analyse",         "calculus",     "Analyse",           "Calculus"),
            ("Verzamelingen",   "sets",         "Verzamelingen",     "Sets"),
            ("Logica",          "logic",        "Logica",            "Logic"),
            ("Goniometrie",     "trigonometry", "Goniometrie",       "Trigonometry"),
            ("Grieks",          "greek",        "Grieks",            "Greek letters"),
            ("Bewerken",        "editing",      "Bewerken",          "Editing"),
            ("Modifier",        "modifiers",    "Modifier",          "Modifier keys"),
            ("Navigatie",       "navigation",   "Navigatie",         "Navigation"),
        };

        /// <summary>The id an older file's group name stands for: the id of the ready-made group of that name, else the name itself.</summary>
        internal static string IdForOldName(string oldName)
        {
            foreach (var k in Known)
                if (string.Equals(k.Old, oldName, StringComparison.OrdinalIgnoreCase)) return k.Id;
            return oldName;
        }

        /// <summary>
        /// The names of a group of the ready-made layouts (<c>vowels</c> …) in every language the program has a text for: the keys
        /// <c>group default: &lt;id&gt;</c> of the language files (<see cref="Lang.TryGetIn"/>), so a new keyboard from the wizard gets its
        /// group names in all languages and a new language is one file. Empty for any other id. Without any language file the table of older
        /// files (Dutch and English) is used.
        /// </summary>
        internal static Dictionary<string, string> DefaultNames(string id)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (code, _) in Lang.GetAvailable())
                if (Lang.TryGetIn(code, "group default: " + id, out string text) && !string.IsNullOrWhiteSpace(text)) names[code] = text;
            if (names.Count == 0)
                foreach (var k in Known)
                    if (k.Id == id) { names["nl"] = k.Nl; names["en"] = k.En; break; }
            return names;
        }

        /// <summary>
        /// Turns the one name of a group in an older file into an id and names: a group of the ready-made layouts gets its id and its Dutch and
        /// English name; any other group gets an id made from its name and that name for <paramref name="keyboardLanguage"/>. The standard
        /// group keeps its id and gets no names (it is shown as "Standard" / "Standaard"). <paramref name="usedIds"/> grows.
        /// </summary>
        internal static (string Id, Dictionary<string, string> Names) FromLegacyName(string oldName, string keyboardLanguage, HashSet<string> usedIds)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (oldName == SettingsManager.StandardGroupName) { usedIds.Add(oldName); return (oldName, names); }
            foreach (var k in Known)
                if (string.Equals(k.Old, oldName, StringComparison.OrdinalIgnoreCase) && !usedIds.Contains(k.Id))
                {
                    names["nl"] = k.Nl; names["en"] = k.En;
                    usedIds.Add(k.Id);
                    return (k.Id, names);
                }
            string id = UniqueId(oldName, usedIds);
            usedIds.Add(id);
            string lang = string.IsNullOrWhiteSpace(keyboardLanguage) ? "en" : keyboardLanguage;
            names[lang] = oldName;
            return (id, names);
        }
    }
}
