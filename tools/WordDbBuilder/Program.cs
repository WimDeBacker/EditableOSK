// WordDbBuilder — makes a word database source (worddb_XX.xml) from text corpora, in the format of worddb_NL.xml:
//   <?xml ...?> / <WordDatabase generated=".." wordCount="N"> / <Word value=".." frequency=".."><Next value=".." frequency=".." />…</Word>… / </WordDatabase>
// The keyboard reads at most 10 <Next> words per word. How the words are chosen is described in README.md; the short version:
//   * a word is a run of letters; a hyphen ends it (the keyboard ends a word at a hyphen too), a digit makes a break;
//   * an apostrophe inside a word is kept, but an elision (l', qu', dell' …) is split off as a word of its own that ends in an apostrophe,
//     because the keyboard treats such words that way (WordPredictor.TryApostropheInWord);
//   * words are lower-cased unless they are mostly written with a capital in the middle of a sentence (names);
//   * frequencies and word pairs of the sources are mixed with weights (spoken, everyday sentences count most).
// Reading the corpora is the slow part (tens of millions of words), so what is counted is kept in a cache (--cache folder): changing weights,
// filters, the seed or the blocked words then takes seconds. --sample-every K reads one line in K, for a quick try.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordDbBuilder;

internal sealed record Source(string Name, string Path, string Format, double Weight);

internal sealed class Rules
{
    public string Language = "";
    public HashSet<string> Elisions = new();        // the part before the apostrophe: "l", "qu" …
    public HashSet<string> SingleLetters = new();   // one-letter words that exist
    public HashSet<string> Blocked = new();         // words that are never suggested (not part of what is counted)
    public static Rules For(string lang)
    {
        var r = new Rules { Language = lang };
        switch (lang)
        {
            case "fr":
                r.Elisions = new HashSet<string> { "c", "d", "j", "l", "m", "n", "s", "t", "qu", "jusqu", "lorsqu", "puisqu", "quoiqu", "presqu" };
                r.SingleLetters = new HashSet<string> { "a", "à", "y", "ô" };
                break;
            case "it":
                r.Elisions = new HashSet<string> { "l", "un", "dell", "all", "dall", "nell", "sull", "coll", "quell", "quest", "c", "d", "m", "t", "s", "v", "n", "anch", "com", "dov", "cos", "sant", "bell", "buon", "nessun", "qualcun", "gl", "tutt", "senz", "quand", "mezz", "po", "ch", "cent", "dev" };
                r.SingleLetters = new HashSet<string> { "a", "e", "i", "o", "è", "é", "à", "ò", "ù", "ì" };
                break;
            case "es":
                r.SingleLetters = new HashSet<string> { "a", "y", "o", "e", "u" };
                break;
            default:
                r.SingleLetters = new HashSet<string> { "a", "i" };
                break;
        }
        return r;
    }
    /// <summary>What decides how words are counted (not the blocked words): part of the cache key.</summary>
    public string Identity() => Language + "|" + string.Join(",", Elisions.OrderBy(x => x, StringComparer.Ordinal)) + "|" + string.Join(",", SingleLetters.OrderBy(x => x, StringComparer.Ordinal));
}

internal sealed class Tok
{
    public string Text;      // as written (apostrophe normalised to ')
    public bool SentenceStart;
    public bool Break;       // a break in front of it: no word pair with the token before
}

internal sealed class Counts
{
    public int Lower, CapMid, CapStart;
    public int Total => Lower + CapMid + CapStart;
}

/// <summary>What was counted in one source: words (by lower-case form) and, for the candidate words, pairs.</summary>
internal sealed class SourceCounts
{
    public long Tokens;
    public Dictionary<string, Counts> Words = new(StringComparer.Ordinal);
    public Dictionary<long, int> Pairs = new();      // (candidate id << 32 | candidate id) → times
}

internal static class Program
{
    // Bump when the way words are made changes: the cache is then not used.
    const int TokenizerVersion = 3;
    const int CandidateCount = 60000;

    static int Main(string[] args)
    {
        string lang = "fr", outPath = "worddb.xml", musthave = null, blocklist = null, reportPath = null, seedFile = null, cacheDir = null;
        int top = 20000, nextCount = 10, seedRank = 2500, sampleEvery = 1;
        var sources = new List<Source>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lang": lang = args[++i]; break;
                case "--out": outPath = args[++i]; break;
                case "--top": top = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--next": nextCount = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--musthave": musthave = args[++i]; break;
                case "--seed": seedFile = args[++i]; break;                 // the words of this file are raised to rank --seed-rank when they rank lower
                case "--seed-rank": seedRank = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--blocklist": blocklist = args[++i]; break;
                case "--report": reportPath = args[++i]; break;
                case "--cache": cacheDir = args[++i]; break;
                case "--sample-every": sampleEvery = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                case "--source": sources.Add(ParseSource(args[++i])); break;
                case "--selftest": return SelfTest() ? 0 : 3;
                default: Console.Error.WriteLine("unknown argument " + args[i]); return 2;
            }
        }
        if (!SelfTest()) { Console.Error.WriteLine("the self-test failed: not building"); return 3; }
        if (sources.Count == 0) { Console.Error.WriteLine("no --source"); return 2; }
        var rules = Rules.For(lang);
        if (blocklist != null && File.Exists(blocklist))
            foreach (var line in File.ReadAllLines(blocklist, Encoding.UTF8))
                if (line.Trim().Length > 0 && !line.StartsWith("#")) rules.Blocked.Add(line.Trim().ToLowerInvariant());
        var report = new StringBuilder();
        void Log(string s) { Console.WriteLine(s); report.AppendLine(s); }

        // The words that must be there: the words of the seed file (also the candidates for word pairs, so a change of the list invalidates the pairs).
        var seeds = new HashSet<string>(StringComparer.Ordinal);
        if (seedFile != null && File.Exists(seedFile))
            foreach (var line in File.ReadAllLines(seedFile, Encoding.UTF8))
                if (line.Trim().Length > 0 && !line.StartsWith("#"))
                    foreach (var t in Tokenize(line.Trim(), rules)) seeds.Add(t.Text.ToLowerInvariant());
        seeds.ExceptWith(rules.Blocked);

        // ── Counting (from the cache when it is there) ──
        string keyBase = $"v{TokenizerVersion}|{rules.Identity()}|sample{sampleEvery}|cand{CandidateCount}|seeds:{Hash(string.Join(",", seeds.OrderBy(x => x, StringComparer.Ordinal)))}";
        var counts = new List<SourceCounts>();
        var candidates = new List<string>();
        for (int s = 0; s < sources.Count; s++) counts.Add(null);
        var wordCaches = new bool[sources.Count];
        for (int s = 0; s < sources.Count; s++)
        {
            string file = cacheDir == null ? null : System.IO.Path.Combine(cacheDir, $"words_{Hash(keyBase + "|" + SourceId(sources[s]))}.bin");
            if (file != null && File.Exists(file)) { counts[s] = ReadWords(file); wordCaches[s] = true; Log($"{sources[s].Name}: counted words read from the cache"); }
            else
            {
                var sc = new SourceCounts();
                foreach (var sentence in Sentences(sources[s], sampleEvery))
                    foreach (var t in Tokenize(sentence, rules))
                    {
                        if (!Acceptable(t.Text, rules)) continue;
                        sc.Tokens++;
                        string key = t.Text.ToLowerInvariant();
                        if (!sc.Words.TryGetValue(key, out var c)) sc.Words[key] = c = new Counts();
                        if (!char.IsUpper(t.Text[0])) c.Lower++; else if (t.SentenceStart) c.CapStart++; else c.CapMid++;
                    }
                counts[s] = sc;
                if (file != null) { Directory.CreateDirectory(cacheDir); WriteWords(file, sc); }
            }
            Log($"{sources[s].Name}: {counts[s].Tokens:N0} words, {counts[s].Words.Count:N0} different");
        }

        // The candidates for word pairs: the most frequent words of all sources together (by plain count, so independent of the weights), and the seeds.
        var rawTotal = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var sc in counts) foreach (var (k, c) in sc.Words) rawTotal[k] = rawTotal.GetValueOrDefault(k) + c.Total;
        candidates = rawTotal.Where(kv => !rules.Blocked.Contains(kv.Key)).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(CandidateCount).Select(kv => kv.Key).ToList();
        var candSet = new HashSet<string>(candidates, StringComparer.Ordinal);
        foreach (var k in seeds) if (candSet.Add(k)) candidates.Add(k);
        var candId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < candidates.Count; i++) candId[candidates[i]] = i;

        for (int s = 0; s < sources.Count; s++)
        {
            string file = cacheDir == null ? null : System.IO.Path.Combine(cacheDir, $"pairs_{Hash(keyBase + "|" + SourceId(sources[s]) + "|" + string.Join(",", sources.Select(x => x.Name)))}.bin");
            if (file != null && File.Exists(file)) { counts[s].Pairs = ReadPairs(file); Log($"{sources[s].Name}: {counts[s].Pairs.Count:N0} word pairs read from the cache"); continue; }
            var dict = new Dictionary<long, int>();
            foreach (var sentence in Sentences(sources[s], sampleEvery))
            {
                int prev = -1;
                foreach (var t in Tokenize(sentence, rules))
                {
                    if (t.Break || t.SentenceStart) prev = -1;
                    if (!Acceptable(t.Text, rules)) { prev = -1; continue; }
                    int id = candId.TryGetValue(t.Text.ToLowerInvariant(), out var v) ? v : -1;
                    if (id >= 0 && prev >= 0) { long k = ((long)prev << 32) | (uint)id; dict[k] = dict.GetValueOrDefault(k) + 1; }
                    prev = id;
                }
            }
            counts[s].Pairs = dict;
            if (file != null) WritePairs(file, dict);
            Log($"{sources[s].Name}: {dict.Count:N0} different word pairs among the candidates");
        }

        // ── The vocabulary: blended frequency per million words ──
        var all = new Dictionary<string, double>(StringComparer.Ordinal);
        var detail = new Dictionary<string, Counts>(StringComparer.Ordinal);
        for (int s = 0; s < sources.Count; s++)
            foreach (var (key, c) in counts[s].Words)
            {
                double pm = c.Total * 1e6 / Math.Max(1, counts[s].Tokens);
                all[key] = all.GetValueOrDefault(key) + sources[s].Weight * pm;
                if (!detail.TryGetValue(key, out var d)) detail[key] = d = new Counts();
                d.Lower += c.Lower; d.CapMid += c.CapMid; d.CapStart += c.CapStart;
            }
        bool IsName(string key) { var d = detail[key]; return d.CapMid >= 3 && d.CapMid > 2 * d.Lower && !key.EndsWith("'"); }

        // A name counts only by the later sources: the first (everyday) source is full of example names (Tom, Mary) that nobody types.
        foreach (var key in all.Keys.ToList())
            if (IsName(key))
            {
                double score = 0;
                for (int s = 1; s < sources.Count; s++)
                    if (counts[s].Words.TryGetValue(key, out var c)) score += sources[s].Weight * c.Total * 1e6 / Math.Max(1, counts[s].Tokens);
                all[key] = score;
            }

        // Typing without accents ("etait" for "était") is common in everyday sentences: such a form is left out when the accented one is far more frequent.
        var accented = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, c) in detail)
        {
            string plain = Deaccent(key);
            if (plain != key && c.Total > accented.GetValueOrDefault(plain)) accented[plain] = c.Total;
        }

        // Enough evidence: seen twice in the first (everyday) source, or often in another; never a blocked word.
        bool Enough(string key)
        {
            if (rules.Blocked.Contains(key)) return false;
            if (seeds.Contains(key)) return true;
            if (accented.TryGetValue(key, out var acc) && acc >= 5 * detail[key].Total) return false;
            if (counts[0].Words.TryGetValue(key, out var c0) && c0.Total >= 2) return true;
            for (int s = 1; s < sources.Count; s++) if (counts[s].Words.TryGetValue(key, out var c) && c.Total >= 30) return true;
            return false;
        }
        List<KeyValuePair<string, double>> Vocabulary() => all.Where(kv => Enough(kv.Key)).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(top).ToList();
        var vocab = Vocabulary();
        if (seeds.Count > 0 && vocab.Count > seedRank)
        {
            // The words of the seed file that rank lower (or are missing) get the frequency of rank --seed-rank.
            double floor = vocab[seedRank].Value;
            var boosted = new List<string>();
            foreach (var key in seeds)
            {
                if (all.TryGetValue(key, out var sc) && sc >= floor) continue;
                all[key] = floor; boosted.Add(key);
                if (!detail.ContainsKey(key)) detail[key] = new Counts { Lower = 1 };
            }
            Log($"seeds: {seeds.Count} words from the seed list, {boosted.Count} raised to rank {seedRank}: " + string.Join(" ", boosted.Take(60)));
            vocab = Vocabulary();
        }
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < vocab.Count; i++) ids[vocab[i].Key] = i;
        Log($"vocabulary: {vocab.Count:N0} words (of {all.Count:N0} different)");
        string Display(string key) => IsName(key) ? char.ToUpperInvariant(key[0]) + key.Substring(1) : key;

        // ── Word pairs: blended per million words, for the words of the vocabulary ──
        var nextScores = new Dictionary<int, Dictionary<int, double>>();       // candidate id → candidate id → score
        var nextRaw = new Dictionary<long, int>();
        for (int s = 0; s < sources.Count; s++)
            foreach (var (k, n) in counts[s].Pairs)
            {
                nextRaw[k] = nextRaw.GetValueOrDefault(k) + n;
                int a = (int)(k >> 32), b = (int)(k & 0xFFFFFFFF);
                if (!nextScores.TryGetValue(a, out var m)) nextScores[a] = m = new Dictionary<int, double>();
                m[b] = m.GetValueOrDefault(b) + sources[s].Weight * n * 1e6 / Math.Max(1, counts[s].Tokens);
            }

        // ── Write the file ──
        var entries = new List<(string Word, int Freq, List<(string Word, int Freq)> Next)>();
        for (int i = 0; i < vocab.Count; i++)
        {
            var next = new List<(string, int)>();
            if (candId.TryGetValue(vocab[i].Key, out int ca) && nextScores.TryGetValue(ca, out var m))
                foreach (var (cb, sc) in m.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                {
                    if (next.Count >= nextCount) break;
                    string bk = candidates[cb];
                    if (!ids.ContainsKey(bk)) continue;                                  // not in the vocabulary
                    if (nextRaw[((long)ca << 32) | (uint)cb] < 2) continue;              // seen only once: chance
                    next.Add((Display(bk), Math.Max(1, (int)Math.Round(sc))));
                }
            entries.Add((Display(vocab[i].Key), Math.Max(1, (int)Math.Round(vocab[i].Value)), next));
        }
        using (var w = new StreamWriter(outPath, false, new UTF8Encoding(false)))
        {
            w.NewLine = "\n";
            w.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            w.WriteLine($"<WordDatabase generated=\"{DateTime.UtcNow:O}\" wordCount=\"{entries.Count}\">");
            foreach (var e in entries)
            {
                w.WriteLine($"  <Word value=\"{Esc(e.Word)}\" frequency=\"{e.Freq}\">");
                foreach (var n in e.Next) w.WriteLine($"    <Next value=\"{Esc(n.Word)}\" frequency=\"{n.Freq}\" />");
                w.WriteLine("  </Word>");
            }
            w.WriteLine("</WordDatabase>");
        }
        Log($"written: {outPath} ({new FileInfo(outPath).Length / 1e6:N1} MB, {entries.Count:N0} words)");

        // ── The quality report ──
        Log($"elided words in the list: {entries.Count(e => e.Word.EndsWith("'"))}; words with an apostrophe inside: {entries.Count(e => e.Word.Contains('\'') && !e.Word.EndsWith("'"))}; names (capital): {entries.Count(e => char.IsUpper(e.Word[0]))}");
        Log("first 80 words: " + string.Join(" ", entries.Take(80).Select(e => e.Word)));
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++) rank[entries[i].Word.ToLowerInvariant()] = i + 1;
        if (musthave != null && File.Exists(musthave))
        {
            int total = 0, missing = 0; var bad = new List<string>();
            foreach (var line in File.ReadAllLines(musthave, Encoding.UTF8))
            {
                if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                total++;
                var worst = 0; string worstWord = null;
                foreach (var t in Tokenize(line.Trim(), rules))
                {
                    string key = t.Text.ToLowerInvariant();
                    int r = rank.TryGetValue(key, out var rr) ? rr : int.MaxValue;
                    if (r > worst) { worst = r; worstWord = key; }
                }
                if (worst > 3000) { missing++; bad.Add($"{line.Trim()}  ← {worstWord}: " + (worst == int.MaxValue ? "not in the list" : "rank " + worst)); }
            }
            Log($"must-have phrases: {total - missing} of {total} have all their words in the first 3000");
            foreach (var b in bad) Log("   not good enough: " + b);
        }
        foreach (var probe in new[] { "je", "tu", "il", "nous", "vous", "de", "le", "la", "les", "est", "c'", "l'", "qu'", "ne", "pas", "merci", "bonjour", "s'", "j'", "d'" })
        {
            int i = entries.FindIndex(e => string.Equals(e.Word, probe, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) Log($"after '{entries[i].Word}': " + string.Join(", ", entries[i].Next.Take(8).Select(n => n.Word)));
        }
        Log("total");
        if (reportPath != null) File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
        return 0;
    }

    // ── The self-test: what was wrong before is tested before every run ──
    static bool SelfTest()
    {
        bool ok = true;
        void Check(bool cond, string what) { if (!cond) { Console.Error.WriteLine("self-test failed: " + what); ok = false; } }
        Check(Deaccent("était") == "etait" && Deaccent("français") == "francais" && Deaccent("œuvre") == "oeuvre" && Deaccent("ñandú") == "nandu", "Deaccent");
        var fr = Rules.For("fr");
        string Words(string s, Rules r) => string.Join(" ", Tokenize(s, r).Select(t => t.Text));
        Check(Words("Qu'aujourd'hui l'homme peut-être très 12 grand.", fr) == "Qu' aujourd'hui l' homme peut être très grand", "French elision and hyphen: " + Words("Qu'aujourd'hui l'homme peut-être très 12 grand.", fr));
        Check(Words("l' État, c’est jusqu'à demain", fr) == "l' État c' est jusqu' à demain", "tokenised elision, typographic apostrophe: " + Words("l' État, c’est jusqu'à demain", fr));
        var it = Rules.For("it");
        Check(Words("dell'amico e un'altra", it) == "dell' amico e un' altra", "Italian elision: " + Words("dell'amico e un'altra", it));
        Check(Tokenize("Il gatto. Il cane", it).Count(t => t.SentenceStart) == 2, "sentence starts");
        Check(!Acceptable("UE", fr) && Acceptable("à", fr) && !Acceptable("x", fr) && Acceptable("l'", fr), "which tokens count");
        return ok;
    }

    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    static string SourceId(Source s)
    {
        var f = new FileInfo(s.Path);
        return $"{s.Name}|{s.Path}|{s.Format}|{(f.Exists ? f.Length : 0)}|{(f.Exists ? f.LastWriteTimeUtc.Ticks : 0)}";
    }

    // name:path:format:weight — the path may start with a drive letter (C:\…), so the format and the weight are taken from the end.
    static Source ParseSource(string text)
    {
        int last = text.LastIndexOf(':'), prev = text.LastIndexOf(':', last - 1), first = text.IndexOf(':');
        return new Source(text[..first], text[(first + 1)..prev], text[(prev + 1)..last], double.Parse(text[(last + 1)..], CultureInfo.InvariantCulture));
    }

    // ── The cache ──
    static void WriteWords(string file, SourceCounts sc)
    {
        using var w = new BinaryWriter(File.Create(file));
        w.Write(sc.Tokens); w.Write(sc.Words.Count);
        foreach (var (k, c) in sc.Words) { w.Write(k); w.Write(c.Lower); w.Write(c.CapMid); w.Write(c.CapStart); }
    }

    static SourceCounts ReadWords(string file)
    {
        using var r = new BinaryReader(File.OpenRead(file));
        var sc = new SourceCounts { Tokens = r.ReadInt64() };
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) sc.Words[r.ReadString()] = new Counts { Lower = r.ReadInt32(), CapMid = r.ReadInt32(), CapStart = r.ReadInt32() };
        return sc;
    }

    static void WritePairs(string file, Dictionary<long, int> pairs)
    {
        using var w = new BinaryWriter(File.Create(file));
        w.Write(pairs.Count);
        foreach (var (k, n) in pairs) { w.Write(k); w.Write(n); }
    }

    static Dictionary<long, int> ReadPairs(string file)
    {
        using var r = new BinaryReader(File.OpenRead(file));
        int n = r.ReadInt32();
        var d = new Dictionary<long, int>(n);
        for (int i = 0; i < n; i++) d[r.ReadInt64()] = r.ReadInt32();
        return d;
    }

    // The letters without their accents, from a table (string normalisation is not available in the invariant mode the tool runs in).
    static string Deaccent(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == 'œ') { sb.Append("oe"); continue; }
            if (c == 'æ') { sb.Append("ae"); continue; }
            int i = "àáâãäåçèéêëìíîïñòóôõöùúûüýÿ".IndexOf(c);
            sb.Append(i >= 0 ? "aaaaaaceeeeiiiinooooouuuuyy"[i] : c);
        }
        return sb.ToString();
    }

    /// <summary>The sentences (lines) of a source: Tatoeba (id, language, sentence; tab separated) or plain lines. With sampleEvery K one line in K.</summary>
    static IEnumerable<string> Sentences(Source src, int sampleEvery)
    {
        long n = 0;
        // Tatoeba holds floods of made-up sentences from one pattern ("Vado a costruire edifici in Messico", "Vai a costruire ponti in Svezia"; in Italian one
        // word of such a pattern was in 4 % of all sentences). Exact repeats are skipped, and of the sentences that begin with the same three words
        // only the first TatoebaPrefixCap are used. A natural opening ("non lo so") is used often enough anyway.
        var seen = src.Format == "tatoeba" ? new HashSet<string>(StringComparer.Ordinal) : null;
        var prefixes = src.Format == "tatoeba" ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
        foreach (var line in File.ReadLines(src.Path, Encoding.UTF8))
        {
            if (sampleEvery > 1 && n++ % sampleEvery != 0) continue;
            if (src.Format == "tatoeba")
            {
                int t1 = line.IndexOf('\t'); if (t1 < 0) continue;
                int t2 = line.IndexOf('\t', t1 + 1); if (t2 < 0) continue;
                string sentence = line.Substring(t2 + 1);
                if (!seen.Add(sentence)) continue;
                string prefix = Prefix(sentence);
                int used = prefixes.GetValueOrDefault(prefix);
                if (used >= TatoebaPrefixCap) continue;
                prefixes[prefix] = used + 1;
                yield return sentence;
            }
            else yield return line;
        }
    }

    const int TatoebaPrefixCap = 60;

    /// <summary>The first three words of a sentence, lower case (letters and apostrophes only).</summary>
    static string Prefix(string sentence)
    {
        var sb = new StringBuilder();
        int words = 0; bool inWord = false;
        foreach (char c in sentence)
        {
            if (char.IsLetter(c) || c == '\'' || c == '’') { sb.Append(char.ToLowerInvariant(c)); inWord = true; }
            else if (inWord) { if (++words == 3) break; sb.Append(' '); inWord = false; }
        }
        return sb.ToString();
    }

    static bool Acceptable(string text, Rules rules)
    {
        if (text.Length == 0) return false;
        string key = text.ToLowerInvariant();
        if (key.Length == 1) return rules.SingleLetters.Contains(key);
        if (key.EndsWith("'")) return true;                                    // an elision (checked when the tokens are made)
        if (text.Length >= 2 && text.All(c => char.IsUpper(c) || c == '\'')) return false;     // an acronym
        return true;
    }

    /// <summary>The words of a sentence, in order. Elisions are split off; hyphens end a word; digits make a break.</summary>
    static List<Tok> Tokenize(string sentence, Rules rules)
    {
        bool start = true, brk = false;
        var sb = new StringBuilder();
        var result = new List<Tok>();
        void Flush()
        {
            if (sb.Length == 0) return;
            string raw = sb.ToString(); sb.Clear();
            foreach (var piece in Split(raw, rules))
            {
                result.Add(new Tok { Text = piece, SentenceStart = start, Break = brk });
                start = false; brk = false;
            }
        }
        for (int i = 0; i < sentence.Length; i++)
        {
            char c = sentence[i];
            if (c == '’' || c == '`' || c == '´') c = '\'';
            if (char.IsLetter(c)) { sb.Append(c); continue; }
            if (c == '\'' && sb.Length > 0 && !(i + 1 < sentence.Length && sentence[i + 1] == '\'')) { sb.Append('\''); continue; }
            Flush();
            if (char.IsDigit(c)) brk = true;
            else if (c == '.' || c == '!' || c == '?' || c == '…' || c == ';') { start = true; }
            // other characters (space, comma, hyphen, quotes) just end the word
        }
        Flush();
        return result;
    }

    /// <summary>One run of letters and apostrophes → the words it holds: "qu'aujourd'hui" → "qu'" + "aujourd'hui"; "l'" → "l'".</summary>
    static IEnumerable<string> Split(string raw, Rules rules)
    {
        string rest = raw.TrimStart('\'');
        if (rest.Length == 0) yield break;
        while (true)
        {
            int ap = rest.IndexOf('\'');
            if (ap < 0) { if (rest.Length > 0) yield return rest; yield break; }
            string left = rest[..ap];
            if (rules.Elisions.Contains(left.ToLowerInvariant()))
            {
                yield return left + "'";
                rest = rest[(ap + 1)..];
                if (rest.Length == 0) yield break;
                continue;
            }
            // not an elision: the whole run is one word (aujourd'hui, prud'homme); a trailing apostrophe is dropped
            yield return rest.TrimEnd('\'');
            yield break;
        }
    }
}
