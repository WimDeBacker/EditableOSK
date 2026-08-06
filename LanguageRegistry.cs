// LanguageRegistry.cs — discovers .wfq word-frequency databases in a folder
//                        and groups them by language code.
//
// Design:
//   • DatabaseInfo  — lightweight metadata record about one .wfq file.
//   • LanguageRegistry — scans a directory, peeks at each file's root-element
//     attributes (fast even for 500 MB databases), and provides lookup by
//     language code.
//
// "Peeking" uses XmlReader which reads only the opening tag of the root
// element — the word data (potentially millions of lines) is never loaded.
//
// Overlay files (e.g. "worddb_NL.learned.wfq" — see WordDatabase's learning
// engine) are NOT databases in their own right; they only ever apply on top
// of a base file that's already loaded. The scan excludes them so they never
// show up as if they were independently selectable.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace OnScreenKeyboard
{
    // ═══════════════════════════════════════════════════════════════════════
    // DatabaseInfo
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lightweight metadata about one .wfq word-frequency database file.
    /// Reading this record does NOT load the word data.
    /// </summary>
    public sealed class DatabaseInfo
    {
        /// <summary>Full file-system path to the .wfq file.</summary>
        public string FilePath    { get; }

        /// <summary>
        /// Language code read from the file (e.g. "nl", "en"), or an empty
        /// string for files that predate the language attribute.
        /// </summary>
        public string Language    { get; }

        /// <summary>
        /// Human-readable name derived from the file name without extension
        /// (e.g. "worddb_NL").
        /// </summary>
        public string DisplayName { get; }

        internal DatabaseInfo(string filePath, string language)
        {
            FilePath    = filePath;
            Language    = language ?? string.Empty;
            DisplayName = Path.GetFileNameWithoutExtension(filePath);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LanguageRegistry
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans a directory for <c>.wfq</c> word-frequency database files and
    /// provides lookup by language code.
    ///
    /// <para>
    /// Only the root-element <c>language</c> attribute is read during the
    /// scan — the word data is never loaded. This keeps the scan fast even
    /// when the folder contains multi-hundred-megabyte files.
    /// </para>
    ///
    /// <para>
    /// Multiple databases per language are supported: a "general Dutch" base
    /// and a "children's Dutch" base can both coexist with <c>language="nl"</c>;
    /// the user chooses between them in Edit Keyboard → Word Prediction. Each
    /// one's learned words live in its own overlay file — see
    /// <c>WordDatabase</c>'s learning engine — which is not itself a database
    /// and is excluded from this scan.
    /// </para>
    /// </summary>
    public sealed class LanguageRegistry
    {
        private readonly IReadOnlyList<DatabaseInfo> _all;

        /// <summary>
        /// Scans <paramref name="folder"/> and builds the registry.
        /// Missing or inaccessible files are silently skipped.
        /// </summary>
        public LanguageRegistry(string folder)
        {
            _all = BuildList(folder);
        }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// All discovered databases, sorted alphabetically by display name.
        /// </summary>
        public IReadOnlyList<DatabaseInfo> All => _all;

        /// <summary>
        /// Distinct language codes present across all databases, sorted
        /// alphabetically.  Empty-string entries (unknown language) come last.
        /// </summary>
        public IReadOnlyList<string> Languages =>
            _all.Select(d => d.Language)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => string.IsNullOrEmpty(l) ? 1 : 0)   // unknowns last
                .ThenBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();

        /// <summary>
        /// Returns all databases whose <c>language</c> attribute matches
        /// <paramref name="code"/> (case-insensitive).  Returns an empty list
        /// if no match is found.
        /// </summary>
        public IReadOnlyList<DatabaseInfo> GetForLanguage(string code) =>
            _all.Where(d => string.Equals(d.Language, code, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();

        // ── Private helpers ──────────────────────────────────────────────

        // Overlay files use this suffix (see WordDatabase.DeriveOverlayPath).
        private const string OverlaySuffix = ".learned.wfq";

        private static IReadOnlyList<DatabaseInfo> BuildList(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return Array.Empty<DatabaseInfo>();

            var list = new List<DatabaseInfo>();
            foreach (string path in Directory.GetFiles(folder, "*.wfq")
                                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (path.EndsWith(OverlaySuffix, StringComparison.OrdinalIgnoreCase))
                    continue; // an overlay, not a standalone database

                try
                {
                    string lang = PeekLanguage(path);
                    list.Add(new DatabaseInfo(path, lang));
                }
                catch
                {
                    // Skip files that cannot be read or parsed (corrupt, locked,
                    // wrong format).  The rest of the registry is still valid.
                }
            }

            list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName,
                                               StringComparison.OrdinalIgnoreCase));

            return list.AsReadOnly();
        }

        /// <summary>
        /// Opens a .wfq file and reads only the root element's <c>language</c>
        /// attribute. Does NOT read the word data — XmlReader stops after the
        /// opening tag.
        /// </summary>
        private static string PeekLanguage(string path)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver   = null,
            };
            using var reader = XmlReader.Create(path, settings);
            reader.MoveToContent();   // advance to the root element opening tag
            return reader.GetAttribute("language") ?? string.Empty;
        }
    }
}
