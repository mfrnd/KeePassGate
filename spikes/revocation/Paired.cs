using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Interfaces;
using KeePassLib.Security;
using KeePassLib.Serialization;

// A paired database as another program leaves it. Two copies are made here with KeePassLib: C, and
// P, which is C after a client was revoked, an authority retired and another made, the profile list
// narrowed and both rules narrowed, every one of those stamped later than anything in C. Another
// program then saves or merges them, and the reader below names every time it finds by where the
// time came from rather than printing it, so that a re-run is line for line the recorded output.
//
//   <work> paired <set> argon2d <iterations> <memory KiB> <lanes>
//   <work> paired <set> aes <rounds>
//       writes C-<set>.kdbx, P-plain-<set>.kdbx and, unless <set> ends in "m", P-moved-<set>.kdbx
//       into <work>. A set ending in "b" puts no rule on any node, and a set ending in "m" moves E2
//       out of G before C is taken, so that C and P both carry a previous parent.
//   <work> read <file> [<label>=<start>/<end> ...]       a block per file
//   <work> summary <file> [<label>=<start>/<end> ...]    a line per file
//
// A window is the span of UTC time in which another program ran, and a time inside a window is
// printed as its label. A time that is C's or P's own is printed as `C` or `P`, or `=` where the
// two are the same, and anything else is printed as it is, which a re-run will not reproduce: that
// is a finding, not noise.
static partial class Probe
{
    static string Repeat32(string two) { return string.Concat(Enumerable.Repeat(two, 32)); }
    static DateTime On(int month, int hour) { return new DateTime(2026, month, 1, hour, 0, 0, DateTimeKind.Utc); }

    static readonly string FpA0 = Repeat32("a0"), FpA1 = Repeat32("a1"), FpA2 = Repeat32("a2");
    static readonly string ClientC0 = Repeat32("c0"), ClientC1 = Repeat32("c1"), ClientC2 = Repeat32("c2");

    const string RecordC1 = "{\"profiles\":[\"reader\"],\"name\":\"agent-one\",\"notAfter\":\"2027-01-01T00:00:00Z\"}";
    const string RecordC2 = "{\"profiles\":[\"reader\",\"writer\"],\"name\":\"agent-two\",\"notAfter\":\"2027-02-01T00:00:00Z\"}";
    const string ProfilesC = "[\"reader\",\"writer\"]";
    const string ProfilesP = "[\"reader\"]";
    const string RuleGroupC = "{\"reader\":[\"read\",\"list\"],\"writer\":[\"create\",\"read\",\"update\",\"list\"]}";
    const string RuleGroupP = "{\"reader\":[\"list\"]}";
    const string RuleEntryC = "{\"reader\":[\"read\",\"list\"]}";
    const string RuleEntryP = "{\"reader\":[]}";

    // One key of the database's custom data: its label in the output, and its value and time in C
    // and in P. A null value is a key that copy does not hold.
    sealed class PairedKey
    {
        public readonly string Label, Name, CValue, PValue;
        public readonly DateTime CTime, PTime;

        public PairedKey(string label, string name, string cValue, DateTime cTime, string pValue, DateTime pTime)
        {
            Label = label; Name = name; CValue = cValue; CTime = cTime; PValue = pValue; PTime = pTime;
        }
    }

    static readonly PairedKey[] PairedKeys =
    {
        new PairedKey("A0",   "KPG.Authority." + FpA0,        "authority-document-A0", On(1, 1), "authority-document-A0", On(1, 1)),
        new PairedKey("A0r",  "KPG.RetiredAuthority." + FpA0, "retired",               On(1, 2), "retired",               On(1, 2)),
        new PairedKey("A1",   "KPG.Authority." + FpA1,        "authority-document-A1", On(1, 3), "authority-document-A1", On(1, 3)),
        new PairedKey("C1",   "KPG.Client." + ClientC1,       RecordC1,                On(1, 4), RecordC1,                On(1, 4)),
        new PairedKey("C2",   "KPG.Client." + ClientC2,       RecordC2,                On(1, 5), "",                      On(2, 1)),
        new PairedKey("C0",   "KPG.Client." + ClientC0,       "",                      On(1, 6), "",                      On(1, 6)),
        new PairedKey("C0v",  "KPG.Revoked." + ClientC0,      "2027-01-01T00:00:00Z " + FpA1, On(1, 7), "2027-01-01T00:00:00Z " + FpA1, On(1, 7)),
        new PairedKey("Prof", "KPG.Profiles",                 ProfilesC,               On(1, 8), ProfilesP,               On(2, 5)),
        new PairedKey("C2v",  "KPG.Revoked." + ClientC2,      null,                    default(DateTime), "2027-02-01T00:00:00Z " + FpA1, On(2, 2)),
        new PairedKey("A1r",  "KPG.RetiredAuthority." + FpA1, null,                    default(DateTime), "retired",          On(2, 3)),
        new PairedKey("A2",   "KPG.Authority." + FpA2,        null,                    default(DateTime), "authority-document-A2", On(2, 4)),
    };

    // The modification time each node is given in C and in P.
    static readonly Dictionary<string, DateTime[]> PairedNodeTimes = new Dictionary<string, DateTime[]>
    {
        { "Root", new[] { On(1, 0), On(1, 0) } },
        { "G", new[] { On(1, 9), On(2, 6) } },
        { "H", new[] { On(1, 0), On(1, 0) } },
        { "E1", new[] { On(1, 10), On(2, 7) } },
        { "E2", new[] { On(1, 0), On(1, 0) } },
    };

    sealed class Window
    {
        public string Label;
        public DateTime Start, End;
    }

    static bool Paired(string[] args)
    {
        switch (args[1])
        {
            case "paired": PairedMake(args[2], PairedKdf(args, 3)); return true;
            case "read": PairedRead(args[2], PairedWindows(args, 3), false); return true;
            case "summary": PairedRead(args[2], PairedWindows(args, 3), true); return true;
            default: return false;
        }
    }

    static KdfParameters PairedKdf(string[] a, int i)
    {
        if (a[i] == "aes")
        {
            var p = new AesKdf().GetDefaultParameters();
            p.SetUInt64(AesKdf.ParamRounds, ulong.Parse(a[i + 1], CultureInfo.InvariantCulture));
            return p;
        }
        if (a[i] == "argon2d")
        {
            var p = new Argon2Kdf().GetDefaultParameters();
            p.SetUInt64(Argon2Kdf.ParamIterations, ulong.Parse(a[i + 1], CultureInfo.InvariantCulture));
            p.SetUInt64(Argon2Kdf.ParamMemory, ulong.Parse(a[i + 2], CultureInfo.InvariantCulture) * 1024UL);
            p.SetUInt32(Argon2Kdf.ParamParallelism, uint.Parse(a[i + 3], CultureInfo.InvariantCulture));
            return p;
        }
        throw new ArgumentException("kdf: argon2d <iterations> <memory KiB> <lanes> | aes <rounds>");
    }

    static List<Window> PairedWindows(string[] a, int from)
    {
        var windows = new List<Window>();
        for (int i = from; i < a.Length; i++)
        {
            var m = Regex.Match(a[i], "^([a-z]+)=([^/]+)/([^/]+)$");
            if (!m.Success) throw new ArgumentException("a window is <label>=<start>/<end>: " + a[i]);
            windows.Add(new Window
            {
                Label = m.Groups[1].Value,
                Start = DateTime.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                End = DateTime.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
            });
        }
        return windows;
    }

    static void StampNode(ITimeLogger node, DateTime t)
    {
        node.CreationTime = t; node.LastModificationTime = t; node.LastAccessTime = t; node.LocationChanged = t;
    }

    static PwEntry AddPairedEntry(PwGroup g, string title, DateTime t)
    {
        var e = new PwEntry(true, true);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "not-a-real-secret"));
        g.AddEntry(e, true);
        StampNode(e, t);
        return e;
    }

    static void MoveEntry(PwEntry e, PwGroup from, PwGroup to, DateTime t)
    {
        from.Entries.Remove(e);
        to.AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid;
        e.LocationChanged = t;
    }

    static void PairedMake(string set, KdfParameters kdf)
    {
        bool bare = set.EndsWith("b", StringComparison.Ordinal);
        bool movedFirst = set.EndsWith("m", StringComparison.Ordinal);

        var pd = new PwDatabase();
        var c = Path.Combine(Work, "C-" + set + ".kdbx");
        pd.New(IOConnectionInfo.FromPath(c), Key());
        pd.KdfParameters = kdf;
        var root = pd.RootGroup;
        root.Name = "Root";
        StampNode(root, On(1, 0));
        var g = new PwGroup(true, true, "G", PwIcon.Folder);
        root.AddGroup(g, true);
        var h = new PwGroup(true, true, "H", PwIcon.Folder);
        root.AddGroup(h, true);
        StampNode(h, On(1, 0));
        var e1 = AddPairedEntry(g, "E1", On(1, 0));
        var e2 = AddPairedEntry(g, "E2", On(1, 0));
        if (!bare) g.CustomData.Set("KPG", RuleGroupC);
        StampNode(g, On(1, 0));
        g.LastModificationTime = On(1, 9);
        if (!bare) e1.CustomData.Set("KPG", RuleEntryC);
        e1.LastModificationTime = On(1, 10);
        pd.RecycleBinEnabled = true;
        foreach (var k in PairedKeys) if (k.CValue != null) Set(pd, k.Name, k.CValue, k.CTime);
        if (movedFirst) MoveEntry(e2, g, h, On(1, 11));
        pd.Save(null);
        Console.WriteLine("wrote C-" + set + ".kdbx");

        foreach (var k in PairedKeys)
            if (k.PValue != null && (k.CValue != k.PValue || k.CTime != k.PTime)) Set(pd, k.Name, k.PValue, k.PTime);
        if (!bare) g.CustomData.Set("KPG", RuleGroupP);
        g.LastModificationTime = On(2, 6);
        if (!bare) e1.CustomData.Set("KPG", RuleEntryP);
        e1.LastModificationTime = On(2, 7);
        pd.SaveAs(IOConnectionInfo.FromPath(Path.Combine(Work, "P-plain-" + set + ".kdbx")), false, null);
        Console.WriteLine("wrote P-plain-" + set + ".kdbx");

        if (!movedFirst)
        {
            MoveEntry(e2, g, h, On(2, 8));
            pd.SaveAs(IOConnectionInfo.FromPath(Path.Combine(Work, "P-moved-" + set + ".kdbx")), false, null);
            Console.WriteLine("wrote P-moved-" + set + ".kdbx");
        }
        pd.Close();
    }

    // `=` the value both copies hold, `C` or `P` one copy's, `?` neither's, `-` absent.
    static string What(string have, string c, string p)
    {
        if (have == null) return "-";
        bool isC = c != null && have == c, isP = p != null && have == p;
        if (isC && isP) return "=";
        if (isC) return "C";
        if (isP) return "P";
        return "?";
    }

    static string When(DateTime? have, DateTime? c, DateTime? p, List<Window> windows)
    {
        if (!have.HasValue) return "none";
        var t = have.Value.Kind == DateTimeKind.Local ? have.Value.ToUniversalTime() : have.Value;
        bool isC = c.HasValue && t == c.Value, isP = p.HasValue && t == p.Value;
        if (isC && isP) return "=";
        if (isC) return "C";
        if (isP) return "P";
        foreach (var w in windows) if (t >= Floor(w.Start) && t <= w.End) return w.Label;
        return Iso(t);
    }

    // KeePassXC writes `_LAST_MODIFIED` as Qt's date text, `ddd MMM d hh:mm:ss yyyy GMT`, in
    // whichever language its build chose. The language is printed as a culture name, taken from the
    // day of the week alone, which differs between English and the system's own language wherever
    // the two differ at all; the month is matched against the windows rather than read, since it is
    // a word in that language too.
    static string QtDate(string value, List<Window> windows)
    {
        var m = Regex.Match(value, @"^(\S+) +\S+ +(\d{1,2}) (\d{2}):(\d{2}):(\d{2}) (\d{4}) GMT$");
        if (!m.Success) return "<unread date " + value + ">";
        var day = m.Groups[1].Value;
        string language = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames.Contains(day) ? "en"
            : CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames.Contains(day) ? CultureInfo.CurrentCulture.Name
            : "?";
        int d = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int hh = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        int mm = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        int ss = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
        int yy = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
        foreach (var w in windows)
            foreach (var month in new[] { w.Start.Month, w.End.Month })
            {
                if (d > DateTime.DaysInMonth(yy, month)) continue;
                var t = new DateTime(yy, month, d, hh, mm, ss, DateTimeKind.Utc);
                if (t >= Floor(w.Start) && t <= w.End) return "<date " + language + ", " + w.Label + ">";
            }
        return "<date " + language + ", " + value + ">";
    }

    static string Shown(string key, string value, List<Window> windows)
    {
        if (key == "_LAST_MODIFIED") return QtDate(value, windows);
        if (key == "KPXC_RANDOM_SLUG") return "<random>";
        return value.Length <= 40 ? value : "<" + value.Length + " characters>";
    }

    static string KdfText(KdfParameters p)
    {
        var u = p.KdfUuid;
        if (u.Equals(new AesKdf().Uuid)) return "AES-KDF rounds=" + p.GetUInt64(AesKdf.ParamRounds, 0);
        string name = u.Equals(new Argon2Kdf().Uuid) ? "Argon2d" : "KDF " + u.ToHexString();
        return name + " iterations=" + p.GetUInt64(Argon2Kdf.ParamIterations, 0) + " memory="
            + (p.GetUInt64(Argon2Kdf.ParamMemory, 0) / 1024) + "KiB lanes=" + p.GetUInt32(Argon2Kdf.ParamParallelism, 0);
    }

    static string FormatVersion(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16) + "." + (v & 0xFFFF);
    }

    static string Value(StringDictionaryEx data, string key) { return data.Exists(key) ? data.Get(key) : null; }

    // A node's custom data. Where the node was given a rule, `KPG` is always printed, as `-` when it
    // has gone; every other key is printed when it is there, a `KPG` the node was never given as `?`.
    static string NodeData(StringDictionaryEx data, string ruleC, string ruleP, List<Window> windows, bool given)
    {
        var parts = new List<string>();
        if (given) parts.Add("KPG=" + What(Value(data, "KPG"), ruleC, ruleP));
        foreach (var key in data.Select(kv => kv.Key).Where(k => !given || k != "KPG").OrderBy(k => k, StringComparer.Ordinal))
            parts.Add(key + "=" + (key == "KPG" ? "?" : Shown(key, data.Get(key), windows)));
        return "{" + string.Join(", ", parts) + "}";
    }

    static string NodeTime(string name, DateTime t, List<Window> windows)
    {
        DateTime[] cp;
        if (PairedNodeTimes.TryGetValue(name, out cp)) return When(t, cp[0], cp[1], windows);
        return When(t, null, null, windows);
    }

    static string GroupName(PwDatabase pd, PwUuid uuid)
    {
        if (uuid.Equals(PwUuid.Zero)) return null;
        var g = pd.RootGroup.FindGroup(uuid, true);
        return g == null ? "elsewhere" : g.Name;
    }

    static void PairedRead(string file, List<Window> windows, bool line)
    {
        var pd = Open(file);
        try
        {
            var cd = pd.CustomData;
            var keys = new List<string>();
            bool asC = true, asP = true;
            foreach (var k in PairedKeys)
            {
                string have = cd.Exists(k.Name) ? cd.Get(k.Name) : null;
                asC &= have == k.CValue;
                asP &= have == k.PValue;
                if (have == null) { keys.Add(k.Label + ":-"); continue; }
                keys.Add(k.Label + ":" + What(have, k.CValue, k.PValue) + "/"
                    + When(Time(pd, k.Name), k.CValue != null ? k.CTime : (DateTime?)null, k.PValue != null ? k.PTime : (DateTime?)null, windows));
            }
            string verdict = asP ? "P's" : asC ? "C's" : "neither's";

            var known = new HashSet<string>(PairedKeys.Select(x => x.Name));
            var others = cd.Select(kv => kv.Key).Where(k => !known.Contains(k)).OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => k + "=" + Shown(k, cd.Get(k), windows) + "/" + When(Time(pd, k), null, null, windows)).ToList();
            string otherText = others.Count == 0 ? "none" : string.Join(" ", others);

            // Copies keep their set's name at the end, so a copy ending in "b" was given no rules.
            bool rules = !Path.GetFileNameWithoutExtension(file).EndsWith("b", StringComparison.Ordinal);
            var groups = new[] { pd.RootGroup }.Concat(pd.RootGroup.GetGroups(true)).ToList();
            var entries = pd.RootGroup.GetEntries(true).ToList();

            if (line)
            {
                var g = groups.FirstOrDefault(x => x.Name == "G");
                var e1 = entries.FirstOrDefault(x => x.Strings.ReadSafe(PwDefs.TitleField) == "E1");
                string gText = g == null ? "G absent"
                    : "G " + NodeTime("G", g.LastModificationTime, windows) + " "
                      + NodeData(g.CustomData, RuleGroupC, RuleGroupP, windows, rules);
                string eText = e1 == null ? "E1 absent"
                    : "E1 " + NodeTime("E1", e1.LastModificationTime, windows) + " history " + e1.History.UCount + " "
                      + NodeData(e1.CustomData, RuleEntryC, RuleEntryP, windows, rules);
                string list = string.Join(" ", entries.Select(x => x.Strings.ReadSafe(PwDefs.TitleField) + "@" + x.ParentGroup.Name
                    + (GroupName(pd, x.PreviousParentGroup) == null ? "" : "(from " + GroupName(pd, x.PreviousParentGroup) + ")")));
                Console.WriteLine("kdbx " + FormatVersion(file) + " | " + verdict + " keys: " + string.Join(" ", keys)
                    + " | others: " + otherText + " | " + gText + " " + eText + " | entries: " + list
                    + " | deleted " + pd.DeletedObjects.UCount);
                return;
            }

            Console.WriteLine("  kdbx " + FormatVersion(file) + ", " + KdfText(pd.KdfParameters));
            Console.WriteLine("  " + verdict + " KPG keys: " + string.Join(" ", keys));
            Console.WriteLine("  other keys: " + otherText);
            foreach (var x in groups)
            {
                var from = GroupName(pd, x.PreviousParentGroup);
                bool hasRule = rules && x.Name == "G";
                Console.WriteLine("  group " + x.Name + (from == null ? "" : " from " + from) + ", modified "
                    + NodeTime(x.Name, x.LastModificationTime, windows) + ": "
                    + NodeData(x.CustomData, RuleGroupC, RuleGroupP, windows, hasRule));
            }
            foreach (var x in entries)
            {
                var title = x.Strings.ReadSafe(PwDefs.TitleField);
                var from = GroupName(pd, x.PreviousParentGroup);
                bool hasRule = rules && title == "E1";
                Console.WriteLine("  entry " + title + " in " + x.ParentGroup.Name + (from == null ? "" : " from " + from)
                    + ", modified " + NodeTime(title, x.LastModificationTime, windows) + ", history " + x.History.UCount + ": "
                    + NodeData(x.CustomData, RuleEntryC, RuleEntryP, windows, hasRule));
            }
            Console.WriteLine("  deleted objects: " + pd.DeletedObjects.UCount);
        }
        finally { pd.Close(); }
    }
}
