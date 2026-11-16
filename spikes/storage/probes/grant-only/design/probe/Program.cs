using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Acl;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

// Probe for the storage option "grant-only": rules that can only add.
//
// Two parts, both console only, invented content only.
//
//   model   generated trees. Today's answers come from the plugin's own AclResolver and AclDocument,
//           compiled into this probe from the review4 worktree at a42e38aa. The grant-only answers
//           come from the resolver written here. What is counted: how often a loss, a rollback, a
//           copy or a displacement makes an answer go from refused to permitted, under each model.
//
//   store   the store this model needs, over KeePassLib 2.62 databases: grants and withdrawals as
//           entries written once in a group of the plugin's own, merged in both directions under
//           KeePassLib's five methods and by keepassxc-cli. And the tree as the policy: moves.
//
// The grant-only model as probed (form "a", a switch counts for the verbs of the row it is in):
//   row(N, p)   the letters profile p is granted on node N: the union of the live letters of every
//               grant record naming (N, p)
//   answer      p may do verb v, as far in as reach r, at node X when some node N on the path from X
//               to the root has v in row(N, p) and, where r needs a switch and v is not a removal,
//               has the switch in the same row
// The store as probed:
//   grant       KPG.Kind=grant  KPG.Store=<store id>  KPG.Id  KPG.Node  KPG.Profile  KPG.Mask
//               KPG.Saw=<ids of the withdrawals of that row it was made after, or none>
//   withdrawal  KPG.Kind=withdrawn  KPG.Id  KPG.Node  KPG.Profile  KPG.Mask=<letters taken away>
//               and, as a second witness, the database custom data key KPG.Withdrawn.<id>
//   a grant's live letters = its mask less the letters of every withdrawal of its row that it did
//   not see. A grant counts with this database's store id and outside the recycle bin. A withdrawal
//   counts wherever it is found.
static class P
{
    static string Work;
    static TextWriter Out;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string Pw = "probe-only";
    const string Letters = "CRUSDLAP";
    const int C = 1, R = 2, U = 4, S = 8, D = 16, L = 32, A = 64, Pr = 128;
    static readonly AclVerb[] Verbs = { AclVerb.Create, AclVerb.Read, AclVerb.Update, AclVerb.Strip, AclVerb.Delete, AclVerb.List };
    static readonly AclReach[] Reaches = { AclReach.Fields, AclReach.Attachments, AclReach.Protected };

    static int Bit(AclVerb v)
    {
        switch (v)
        {
            case AclVerb.Create: return C;
            case AclVerb.Read: return R;
            case AclVerb.Update: return U;
            case AclVerb.Strip: return S;
            case AclVerb.Delete: return D;
            case AclVerb.List: return L;
            case AclVerb.Attachments: return A;
            case AclVerb.Protected: return Pr;
        }
        return 0;
    }

    static string MaskText(int m)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 8; i++) sb.Append((m & (1 << i)) != 0 ? Letters[i] : '.');
        return sb.ToString();
    }

    static int MaskOf(string text)
    {
        var m = 0;
        if (text == null || text.Length != 8) return -1;
        for (var i = 0; i < 8; i++)
        {
            if (text[i] == Letters[i]) m |= 1 << i;
            else if (text[i] != '.') return -1;
        }
        return m;
    }

    // =====================================================================================
    // The model part
    // =====================================================================================

    sealed class MNode : IAclNode
    {
        public string Label;
        public MNode Up;
        public List<MNode> Kids = new List<MNode>();
        public bool Group;
        public string Doc;
        public Dictionary<string, int> Row = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Withheld = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool Refuses;

        string IAclNode.Document { get { return Doc; } }
        IAclNode IAclNode.Parent { get { return Up; } }
        string IAclNode.Name { get { return Label; } }
        T IAclNode.Held<T>(Func<T> walk) { return walk(); }
    }

    static readonly string[] Profiles = { "agent", "ops", "reader" };
    static readonly int[] Typical =
    {
        R | L, R | L | Pr, R | L | A, C | R | U | L, C | R | U | S | D | L, 255, C, U | Pr, L, D, R | L | A | Pr, C | R | U | L | Pr,
    };

    static List<MNode> Tree(Random rng, int count)
    {
        var all = new List<MNode>();
        var root = new MNode { Label = "n0", Group = true };
        all.Add(root);
        var groups = new List<MNode> { root };
        for (var i = 1; i < count; i++)
        {
            MNode up;
            do { up = groups[rng.Next(groups.Count)]; } while (Depth(up) >= 5);
            var n = new MNode { Label = "n" + i.ToString(Inv), Up = up, Group = rng.NextDouble() < 0.4 };
            up.Kids.Add(n);
            all.Add(n);
            if (n.Group) groups.Add(n);
        }
        return all;
    }

    static int Depth(MNode n) { var d = 0; for (var c = n.Up; c != null; c = c.Up) d++; return d; }

    static int RandomMask(Random rng)
    {
        return rng.NextDouble() < 0.8 ? Typical[rng.Next(Typical.Length)] : rng.Next(1, 256);
    }

    static string DocOf(bool deny, Dictionary<string, int> masks)
    {
        var grants = new Dictionary<string, IEnumerable<AclVerb>>(StringComparer.Ordinal);
        foreach (var kv in masks)
        {
            var verbs = new List<AclVerb>();
            foreach (var v in AclVerbs.Grantable) if ((kv.Value & Bit(v)) != 0) verbs.Add(v);
            grants[kv.Key] = verbs;
        }
        return AclDocument.Of(deny ? AclDefault.Deny : AclDefault.Inherit, grants).Write();
    }

    // The nearest rule about a profile above a node, as a mask, or -1 when none names it.
    static int NearestAbove(MNode n, string profile)
    {
        for (var c = n.Up; c != null; c = c.Up)
        {
            if (c.Doc == null) continue;
            var g = AclDocument.Read(c.Doc).Grant(profile);
            if (g != null) { var m = 0; foreach (var v in g) m |= Bit(v); return m; }
        }
        return -1;
    }

    // Rules as today's model allows them, narrowings included; or held to rules that never narrow.
    static void Rule(Random rng, List<MNode> all, bool narrowingFree)
    {
        foreach (var n in all)
        {
            n.Doc = null;
            if (rng.NextDouble() >= 0.3) continue;
            var deny = !narrowingFree && rng.NextDouble() < 0.15;
            var masks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in Profiles)
            {
                if (rng.NextDouble() >= 0.5) continue;
                var m = !narrowingFree && rng.NextDouble() < 0.12 ? 0 : RandomMask(rng);
                if (narrowingFree) { var above = NearestAbove(n, p); if (above > 0) m |= above; }
                masks[p] = m;
            }
            if (masks.Count == 0 && !deny) continue;
            n.Doc = DocOf(deny, masks);
        }
    }

    // The documents read as grants: every mask a row, the default and an empty array meaning nothing.
    static void RowsFromDocs(List<MNode> all)
    {
        foreach (var n in all)
        {
            n.Row.Clear();
            if (n.Doc == null) continue;
            var d = AclDocument.Read(n.Doc);
            foreach (var p in d.Profiles)
            {
                var m = 0;
                foreach (var v in d.Grant(p)) m |= Bit(v);
                if (m != 0) n.Row[p] = m;
            }
        }
    }

    static bool GrantOnly(MNode x, string profile, int verb, AclReach reach)
    {
        for (var n = x; n != null; n = n.Up)
        {
            int row;
            if (!n.Row.TryGetValue(profile, out row) || (row & verb) == 0) continue;
            if (reach == AclReach.Fields || verb == S || verb == D) return true;
            if (reach == AclReach.Attachments && (row & A) != 0) return true;
            if (reach == AclReach.Protected && (row & Pr) != 0) return true;
        }
        return false;
    }

    // The middle form: grants as above, less what a mark on the path withholds; a node whose mark
    // is referred to and not found refuses.
    static bool Middle(MNode x, string profile, int verb, AclReach reach)
    {
        var held = 0;
        for (var n = x; n != null; n = n.Up)
        {
            if (n.Refuses) return false;
            int w;
            if (n.Withheld.TryGetValue(profile, out w)) held |= w;
            if (n.Withheld.TryGetValue("*", out w)) held |= w;
        }
        if ((held & verb) != 0) return false;
        if (verb != S && verb != D)
        {
            if (reach == AclReach.Attachments && (held & A) != 0) return false;
            if (reach == AclReach.Protected && (held & Pr) != 0) return false;
        }
        return GrantOnly(x, profile, verb, reach);
    }

    delegate bool Ask(MNode x, string profile, AclVerb verb, AclReach reach);

    static bool Today(MNode x, string profile, AclVerb verb, AclReach reach, AclDocuments read)
    {
        return AclResolver.Permits(x, new[] { profile }, verb, reach, read);
    }

    static bool[] Answers(List<MNode> nodes, Ask ask)
    {
        var a = new bool[nodes.Count * Profiles.Length * Verbs.Length * Reaches.Length];
        var i = 0;
        foreach (var n in nodes)
            foreach (var p in Profiles)
                foreach (var v in Verbs)
                    foreach (var r in Reaches)
                        a[i++] = ask(n, p, v, r);
        return a;
    }

    static Ask AskToday()
    {
        var read = new AclDocuments();
        return (x, p, v, r) => Today(x, p, v, r, read);
    }

    static readonly Ask AskGrantOnly = (x, p, v, r) => GrantOnly(x, p, Bit(v), r);
    static readonly Ask AskMiddle = (x, p, v, r) => Middle(x, p, Bit(v), r);

    sealed class Tally
    {
        public int Trials, Wider, Narrower, WiderAnswers;
        public void Add(bool[] before, bool[] after)
        {
            Trials++;
            var w = 0; var n = 0;
            for (var i = 0; i < before.Length; i++)
            {
                if (!before[i] && after[i]) w++;
                if (before[i] && !after[i]) n++;
            }
            if (w > 0) Wider++;
            if (n > 0) Narrower++;
            WiderAnswers += w;
        }
        public override string ToString()
        {
            return Trials.ToString(Inv).PadLeft(6) + " trials, wider in " + Wider.ToString(Inv).PadLeft(5) + " (" + WiderAnswers.ToString(Inv)
                + " answers), narrower in " + Narrower.ToString(Inv).PadLeft(5);
        }
    }

    static List<MNode> Subtree(MNode n)
    {
        var list = new List<MNode> { n };
        for (var i = 0; i < list.Count; i++) list.AddRange(list[i].Kids);
        return list;
    }

    static void RunModel(int trees, int size, int seed)
    {
        Out.WriteLine("== the model: " + trees.ToString(Inv) + " generated trees of " + size.ToString(Inv) + " nodes, three profiles, seed " + seed.ToString(Inv) + " ==");
        Out.WriteLine("   today's answers: KeePassGate.Acl.AclResolver.Permits, compiled from the worktree at a42e38aa");
        Out.WriteLine("   an answer is one (node, profile, verb, reach): 6 verbs, 3 reaches");
        var rng = new Random(seed);

        // 1. No narrowing anywhere: do the two models answer alike?
        long same = 0, differ = 0; var ruled = 0;
        for (var t = 0; t < trees; t++)
        {
            var all = Tree(rng, size);
            Rule(rng, all, true);
            RowsFromDocs(all);
            ruled += all.Count(n => n.Doc != null);
            var a = Answers(all, AskToday());
            var b = Answers(all, AskGrantOnly);
            for (var i = 0; i < a.Length; i++) { if (a[i] == b[i]) same++; else differ++; }
        }
        Out.WriteLine();
        Out.WriteLine("1. rule sets in which no nearer rule holds less than the rule above it, and no seal (" + ruled.ToString(Inv) + " rules):");
        Out.WriteLine("   answers alike under both models: " + same.ToString(Inv) + ", different: " + differ.ToString(Inv));

        // 2. to 5. Arbitrary rule sets of today's model, and the same documents read as grants.
        var lossToday = new Tally(); var lossGo = new Tally(); var lossTodayFree = new Tally();
        var copyToday = new Tally(); var copyGo = new Tally();
        var rootToday = new Tally(); var rootGo = new Tally();
        var backToday = new Tally(); var backGo = new Tally();
        long ignoreWider = 0, ignoreNarrower = 0, ignoreAnswers = 0; var ignoreTrees = 0; var narrowingTrees = 0;
        var backGoUnexplained = 0;
        rng = new Random(seed + 1);
        for (var t = 0; t < trees; t++)
        {
            var all = Tree(rng, size);
            Rule(rng, all, false);
            RowsFromDocs(all);
            var today = Answers(all, AskToday());
            var go = Answers(all, AskGrantOnly);

            // 5. today's documents read as grants: which way do the answers move?
            var w = 0;
            for (var i = 0; i < today.Length; i++)
            {
                ignoreAnswers++;
                if (!today[i] && go[i]) { ignoreWider++; w++; }
                if (today[i] && !go[i]) ignoreNarrower++;
            }
            if (w > 0) ignoreTrees++;
            if (all.Any(n => n.Doc != null && (n.Doc.Contains("\"deny\"") || n.Doc.Contains("[]") || Narrows(n)))) narrowingTrees++;

            foreach (var n in all.Where(n => n.Doc != null).ToList())
            {
                // 2. one rule lost
                var doc = n.Doc; var row = new Dictionary<string, int>(n.Row);
                n.Doc = null; n.Row.Clear();
                lossToday.Add(today, Answers(all, AskToday()));
                lossGo.Add(go, Answers(all, AskGrantOnly));

                // 4. one rule rolled back to another version
                var masks = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var p in Profiles) if (rng.NextDouble() < 0.5) masks[p] = rng.NextDouble() < 0.12 ? 0 : RandomMask(rng);
                var deny = rng.NextDouble() < 0.15;
                if (masks.Count > 0 || deny)
                {
                    n.Doc = DocOf(deny, masks);
                    foreach (var kv in masks) if (kv.Value != 0) n.Row[kv.Key] = kv.Value;
                    backToday.Add(today, Answers(all, AskToday()));
                    var after = Answers(all, AskGrantOnly);
                    backGo.Add(go, after);
                    var widerHere = false;
                    for (var i = 0; i < go.Length; i++) if (!go[i] && after[i]) widerHere = true;
                    var explained = false;
                    foreach (var kv in n.Row) { int was; row.TryGetValue(kv.Key, out was); if ((kv.Value & ~was) != 0) explained = true; }
                    if (widerHere && !explained) backGoUnexplained++;
                }
                n.Doc = doc; n.Row.Clear(); foreach (var kv in row) n.Row[kv.Key] = kv.Value;
            }

            foreach (var n in all.Where(n => n.Up != null).ToList())
            {
                var sub = Subtree(n);
                if (!sub.Any(s => s.Doc != null) && n.Up.Up == null) continue;
                var subToday = Answers(sub, AskToday());
                var subGo = Answers(sub, AskGrantOnly);

                // 3. a copy beside its original that carries no rule: the subtree with its rules gone
                if (sub.Any(s => s.Doc != null))
                {
                    var docs = sub.Select(s => s.Doc).ToList(); var rows = sub.Select(s => new Dictionary<string, int>(s.Row)).ToList();
                    foreach (var s in sub) { s.Doc = null; s.Row.Clear(); }
                    copyToday.Add(subToday, Answers(sub, AskToday()));
                    copyGo.Add(subGo, Answers(sub, AskGrantOnly));
                    for (var i = 0; i < sub.Count; i++) { sub[i].Doc = docs[i]; foreach (var kv in rows[i]) sub[i].Row[kv.Key] = kv.Value; }
                }

                // 6. displaced to the root, its own rules with it
                if (n.Up.Up != null)
                {
                    var up = n.Up; n.Up = all[0];
                    rootToday.Add(subToday, Answers(sub, AskToday()));
                    rootGo.Add(subGo, Answers(sub, AskGrantOnly));
                    n.Up = up;
                }
            }
        }

        // Control for 2: the same loss where no rule narrows.
        rng = new Random(seed + 2);
        for (var t = 0; t < trees; t++)
        {
            var all = Tree(rng, size);
            Rule(rng, all, true);
            var today = Answers(all, AskToday());
            foreach (var n in all.Where(n => n.Doc != null).ToList())
            {
                var doc = n.Doc; n.Doc = null;
                lossTodayFree.Add(today, Answers(all, AskToday()));
                n.Doc = doc;
            }
        }

        Out.WriteLine();
        Out.WriteLine("2. one rule lost (the node's whole document; under grant-only, the node's rows):");
        Out.WriteLine("   today's model, rules as it allows them:      " + lossToday);
        Out.WriteLine("   today's model, rule sets with no narrowing:  " + lossTodayFree);
        Out.WriteLine("   grant-only, the same documents as grants:    " + lossGo);
        Out.WriteLine();
        Out.WriteLine("3. a copy of a node, beside its original, that carries no rule (the node and everything under it):");
        Out.WriteLine("   today's model:  " + copyToday);
        Out.WriteLine("   grant-only:     " + copyGo);
        Out.WriteLine();
        Out.WriteLine("4. one rule rolled back to another version of itself (a random other document):");
        Out.WriteLine("   today's model:  " + backToday);
        Out.WriteLine("   grant-only:     " + backGo + "; wider without the older row holding a letter the newer lacks: " + backGoUnexplained.ToString(Inv));
        Out.WriteLine();
        Out.WriteLine("5. today's documents read as grants (a seal and an empty array ignored), " + trees.ToString(Inv) + " trees, " + narrowingTrees.ToString(Inv) + " of them holding a narrowing:");
        Out.WriteLine("   answers: " + ignoreAnswers.ToString(Inv) + "; refused today and permitted as grants: " + ignoreWider.ToString(Inv)
            + " (in " + ignoreTrees.ToString(Inv) + " trees); permitted today and refused as grants: " + ignoreNarrower.ToString(Inv));
        Out.WriteLine();
        Out.WriteLine("6. a node displaced to the root with its own rules (what a merge does with an entry whose group is missing):");
        Out.WriteLine("   today's model:  " + rootToday);
        Out.WriteLine("   grant-only:     " + rootGo);

        // 7. The middle form.
        var mGrant = new Tally(); var mRecord = new Tally(); var mBoth = new Tally(); var mCopy = new Tally(); var mBare = new Tally(); var mRoot = new Tally(); var mRootUnder = new Tally();
        var marks = 0;
        rng = new Random(seed + 3);
        for (var t = 0; t < trees; t++)
        {
            var all = Tree(rng, size);
            foreach (var n in all)
            {
                if (rng.NextDouble() < 0.3) foreach (var p in Profiles) if (rng.NextDouble() < 0.5) n.Row[p] = RandomMask(rng);
                if (n.Up != null && rng.NextDouble() < 0.15)
                {
                    n.Withheld[rng.NextDouble() < 0.4 ? "*" : Profiles[rng.Next(3)]] = rng.NextDouble() < 0.6 ? 255 : rng.Next(1, 256);
                    marks++;
                }
            }
            var was = Answers(all, AskMiddle);
            foreach (var n in all.Where(n => n.Row.Count > 0).ToList())
            {
                var row = new Dictionary<string, int>(n.Row); n.Row.Clear();
                mGrant.Add(was, Answers(all, AskMiddle));
                foreach (var kv in row) n.Row[kv.Key] = kv.Value;
            }
            foreach (var n in all.Where(n => n.Withheld.Count > 0).ToList())
            {
                var held = new Dictionary<string, int>(n.Withheld);
                n.Withheld.Clear(); n.Refuses = true;              // the record lost, the node still refers to it
                mRecord.Add(was, Answers(all, AskMiddle));
                n.Refuses = false;                                  // the record and the node's reference both lost
                mBoth.Add(was, Answers(all, AskMiddle));
                foreach (var kv in held) n.Withheld[kv.Key] = kv.Value;

                var sub = Subtree(n);
                var subWas = Answers(sub, AskMiddle);
                var rows = sub.Select(s => new Dictionary<string, int>(s.Row)).ToList();
                foreach (var s in sub) s.Row.Clear();               // a copy beside the original: the reference travels, no grant does
                mCopy.Add(subWas, Answers(sub, AskMiddle));
                var helds = sub.Select(s => new Dictionary<string, int>(s.Withheld)).ToList();
                foreach (var s in sub) s.Withheld.Clear();          // a copy by a program that drops the reference
                mBare.Add(subWas, Answers(sub, AskMiddle));
                for (var i = 0; i < sub.Count; i++) { foreach (var kv in rows[i]) sub[i].Row[kv.Key] = kv.Value; foreach (var kv in helds[i]) sub[i].Withheld[kv.Key] = kv.Value; }

                if (n.Up.Up != null)
                {
                    var above = false;
                    for (var c = n.Up; c != null; c = c.Up) if (c.Withheld.Count > 0) above = true;
                    var up = n.Up; n.Up = all[0];
                    (above ? mRootUnder : mRoot).Add(subWas, Answers(sub, AskMiddle));
                    n.Up = up;
                }
            }
        }
        Out.WriteLine();
        Out.WriteLine("7. the middle form: grants, and marks that withhold (" + marks.ToString(Inv) + " marks):");
        Out.WriteLine("   one node's grants lost:                                          " + mGrant);
        Out.WriteLine("   a mark's record lost, the node still referring to it (refuses):  " + mRecord);
        Out.WriteLine("   a mark's record and the node's reference both lost:              " + mBoth);
        Out.WriteLine("   a copy beside the original, the reference carried, no grant:     " + mCopy);
        Out.WriteLine("   a copy beside the original that carries neither:                 " + mBare);
        Out.WriteLine("   a marked node displaced to the root, mark and grants with it:    " + mRoot);
        Out.WriteLine("   the same where a group above it carried a mark too:              " + mRootUnder);
    }

    // Whether this node's document holds, for some profile, less than the nearest rule above it.
    static bool Narrows(MNode n)
    {
        var d = AclDocument.Read(n.Doc);
        foreach (var p in d.Profiles)
        {
            var above = NearestAbove(n, p);
            if (above <= 0) continue;
            var m = 0; foreach (var v in d.Grant(p)) m |= Bit(v);
            if ((above & ~m) != 0) return true;
        }
        return false;
    }

    // =====================================================================================
    // The store part
    // =====================================================================================

    const string Sid = "6f1d2c3b4a5968778695a4b3c2d1e0f9";
    const string OtherSid = "00112233445566778899aabbccddeeff";
    const string KindField = "KPG.Kind", StoreField = "KPG.Store", IdField = "KPG.Id", NodeField = "KPG.Node",
        ProfileField = "KPG.Profile", MaskField = "KPG.Mask", SawField = "KPG.Saw";
    const string KeyPrefix = "KPG.Withdrawn.";
    const string NotesLine = "KeePassGate data. Managed on the KPG tab in KeePass.";

    static byte[] Sha(string text) { using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text)); }
    static byte[] First(byte[] from, int count) { var b = new byte[count]; Array.Copy(from, b, count); return b; }
    static DateTime At(int s) { return T0.AddSeconds(s); }
    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }
    static PwUuid Fixed(string name) { return new PwUuid(First(Sha("probe " + name), 16)); }
    static string Hex(PwUuid u) { return u.ToHexString(); }
    static string IdOf(string name) { return MemUtil.ByteArrayToHexString(First(Sha("id " + name), 16)).ToLowerInvariant(); }
    static readonly PwUuid StoreUuid = Fixed("store group");

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup AddGroup(PwGroup parent, string name, PwUuid uuid, DateTime t)
    {
        var g = new PwGroup(false, false) { Uuid = uuid, Name = name };
        Stamp(g, t);
        parent.AddGroup(g, true);
        g.LocationChanged = t;
        return g;
    }

    static PwEntry AddEntry(PwGroup parent, PwUuid uuid, string title, DateTime t)
    {
        var e = new PwEntry(false, false) { Uuid = uuid };
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, ""));
        Stamp(e, t);
        parent.AddEntry(e, true);
        e.LocationChanged = t;
        return e;
    }

    static void Set(PwEntry e, string name, string value) { e.Strings.Set(name, new ProtectedString(false, value)); }
    static string Get(PwEntry e, string name) { return e.Strings.Exists(name) ? e.Strings.ReadSafe(name) : null; }

    static void Edit(PwEntry e, DateTime t, Action<PwEntry> change) { e.CreateBackup(null); change(e); e.LastModificationTime = t; }
    static void Password(PwEntry e, string value) { e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, value)); }

    static void Move(PwEntry e, PwGroup to, DateTime t)
    {
        var from = e.ParentGroup;
        from.Entries.Remove(e);
        to.AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid;
        e.LocationChanged = t;
    }

    static void Move(PwGroup g, PwGroup to, DateTime t)
    {
        var from = g.ParentGroup;
        from.Groups.Remove(g);
        to.AddGroup(g, true, true);
        g.PreviousParentGroup = from.Uuid;
        g.LocationChanged = t;
    }

    static void DeleteForGood(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    static PwGroup Store(PwDatabase db) { return db.RootGroup.FindGroup(StoreUuid, true); }

    sealed class Withdrawal { public string Id, Node, Profile; public int Mask; }

    static List<Withdrawal> Withdrawals(PwDatabase db, bool keys, out int entries, out int keyCount)
    {
        var found = new Dictionary<string, Withdrawal>(StringComparer.Ordinal);
        entries = 0; keyCount = 0;
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (Get(e, KindField) != "withdrawn") continue;
            var m = MaskOf(Get(e, MaskField));
            var id = Get(e, IdField); var node = Get(e, NodeField); var profile = Get(e, ProfileField);
            if (m < 0 || id == null || node == null || profile == null) continue;
            entries++;
            found[id] = new Withdrawal { Id = id, Node = node, Profile = profile, Mask = m };
        }
        foreach (var kv in db.CustomData)
        {
            if (!kv.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)) continue;
            keyCount++;
            if (!keys) continue;
            var parts = kv.Value.Split(' ');
            if (parts.Length != 3) continue;
            var m = MaskOf(parts[2]);
            if (m < 0) continue;
            var id = kv.Key.Substring(KeyPrefix.Length);
            if (!found.ContainsKey(id)) found[id] = new Withdrawal { Id = id, Node = parts[0], Profile = parts[1], Mask = m };
        }
        return found.Values.ToList();
    }

    sealed class Fold
    {
        public Dictionary<string, Dictionary<string, int>> Rows = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        public int Live, Cut, Foreign, InBin, Bad, WithdrawalEntries, WithdrawalKeys, Outside;
    }

    static bool InBin(PwDatabase db, PwEntry e)
    {
        if (db.RecycleBinUuid == null || db.RecycleBinUuid.Equals(PwUuid.Zero)) return false;
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        return bin != null && e.IsContainedIn(bin);
    }

    static Fold Read(PwDatabase db, bool keys)
    {
        var f = new Fold();
        var ws = Withdrawals(db, keys, out f.WithdrawalEntries, out f.WithdrawalKeys);
        var store = Store(db);
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (Get(e, KindField) != "grant") continue;
            if (Get(e, StoreField) != Sid) { f.Foreign++; continue; }
            if (InBin(db, e)) { f.InBin++; continue; }
            var m = MaskOf(Get(e, MaskField));
            var node = Get(e, NodeField); var profile = Get(e, ProfileField); var saw = Get(e, SawField);
            if (m < 0 || node == null || node.Length != 32 || string.IsNullOrEmpty(profile) || saw == null) { f.Bad++; continue; }
            if (store == null || !e.IsContainedIn(store)) f.Outside++;
            var seen = new HashSet<string>(saw.Split(' '), StringComparer.Ordinal);
            foreach (var w in ws) if (w.Node == node && w.Profile == profile && !seen.Contains(w.Id)) m &= ~w.Mask;
            if (m == 0) { f.Cut++; continue; }
            f.Live++;
            Dictionary<string, int> rows;
            if (!f.Rows.TryGetValue(node, out rows)) f.Rows[node] = rows = new Dictionary<string, int>(StringComparer.Ordinal);
            int was; rows.TryGetValue(profile, out was);
            rows[profile] = was | m;
        }
        return f;
    }

    static int RowAt(Fold f, PwUuid node, string profile)
    {
        Dictionary<string, int> rows; int m;
        return f.Rows.TryGetValue(Hex(node), out rows) && rows.TryGetValue(profile, out m) ? m : 0;
    }

    // What the profile may do at an entry, as one mask: each verb it may use on the fields, and a
    // switch where it reaches at least one of those verbs.
    static string Effective(Fold f, PwEntry e, string profile)
    {
        var rows = new List<int> { RowAt(f, e.Uuid, profile) };
        for (var g = e.ParentGroup; g != null; g = g.ParentGroup) rows.Add(RowAt(f, g.Uuid, profile));
        var m = 0;
        foreach (var row in rows)
        {
            m |= row & (C | R | U | S | D | L);
            if ((row & (C | R | U | L)) != 0) m |= row & (A | Pr);
        }
        return MaskText(m);
    }

    static string NewGrant(PwDatabase db, string name, PwUuid node, string profile, string mask, DateTime t, string sid = Sid, PwGroup into = null)
    {
        int a, b;
        var saw = Withdrawals(db, true, out a, out b).Where(w => w.Node == Hex(node) && w.Profile == profile).Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var id = IdOf(name);
        var e = AddEntry(into ?? Store(db), Fixed("record " + name), "KPG grant " + profile + " " + mask + " " + Hex(node).Substring(0, 8) + " " + id.Substring(0, 6), t);
        Set(e, PwDefs.NotesField, NotesLine);
        Set(e, KindField, "grant");
        Set(e, StoreField, sid);
        Set(e, IdField, id);
        Set(e, NodeField, Hex(node));
        Set(e, ProfileField, profile);
        Set(e, MaskField, mask);
        Set(e, SawField, saw.Count == 0 ? "none" : string.Join(" ", saw.ToArray()));
        return id;
    }

    static string Withdraw(PwDatabase db, string name, PwUuid node, string profile, string mask, DateTime t, bool key = true, PwGroup into = null)
    {
        var id = IdOf(name);
        var e = AddEntry(into ?? Store(db), Fixed("record " + name), "KPG withdrawn " + profile + " " + mask + " " + Hex(node).Substring(0, 8) + " " + id.Substring(0, 6), t);
        Set(e, PwDefs.NotesField, NotesLine);
        Set(e, KindField, "withdrawn");
        Set(e, IdField, id);
        Set(e, NodeField, Hex(node));
        Set(e, ProfileField, profile);
        Set(e, MaskField, mask);
        if (key) db.CustomData.Set(KeyPrefix + id, Hex(node) + " " + profile + " " + mask);

        // A grant this leaves nothing of is deleted in the same write, as a record is at a revocation.
        int a, b;
        var ws = Withdrawals(db, true, out a, out b);
        foreach (var g in db.RootGroup.GetEntries(true).ToList())
        {
            if (Get(g, KindField) != "grant" || Get(g, NodeField) != Hex(node) || Get(g, ProfileField) != profile) continue;
            var m = MaskOf(Get(g, MaskField));
            var seen = new HashSet<string>((Get(g, SawField) ?? "").Split(' '), StringComparer.Ordinal);
            foreach (var w in ws) if (w.Node == Hex(node) && w.Profile == profile && !seen.Contains(w.Id)) m &= ~w.Mask;
            if (m == 0) DeleteForGood(db, g, t);
        }
        return id;
    }

    static PwDatabase NewDb(string name)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(Path.Combine(Work, name)), Key());
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, SlowKdf ? 14UL : 2UL);
        a.SetUInt64(Argon2Kdf.ParamMemory, SlowKdf ? 128UL * 1024UL * 1024UL : 1024UL * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, 1U);
        db.KdfParameters = a;
        db.SettingsChanged = T0;
        db.RootGroup.Uuid = Fixed("root");
        db.RootGroup.Name = "probe";
        Stamp(db.RootGroup, T0);
        return db;
    }

    static PwDatabase Open(string path) { var db = new PwDatabase(); db.Open(IOConnectionInfo.FromPath(path), Key(), null); return db; }
    static void SaveTo(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); db.Close(); }

    static string KdbxVersion(string path)
    {
        var b = new byte[12];
        using (var f = File.OpenRead(path)) f.Read(b, 0, 12);
        return BitConverter.ToUInt16(b, 10).ToString(Inv) + "." + BitConverter.ToUInt16(b, 8).ToString(Inv);
    }

    static readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal);

    // The base every scenario starts from.
    //   G            granted: agent .R...L..            (grant g1)
    //     X          no grant of its own
    //     Y          granted: agent ..U....P            (grant g2)
    //     S / S1     a subgroup and its entry, no grant
    //   H            private: nothing granted here or above
    //     Q          a secret
    //   Recycle Bin
    //   KeePassGate  the store group
    static void MakeBase(string path)
    {
        var db = NewDb("base-new.kdbx");
        var g = AddGroup(db.RootGroup, "G", Fixed("group G"), T0);
        AddEntry(g, Fixed("entry X"), "X", T0);
        AddEntry(g, Fixed("entry Y"), "Y", T0);
        var s = AddGroup(g, "S", Fixed("group S"), T0);
        AddEntry(s, Fixed("entry S1"), "S1", T0);
        var h = AddGroup(db.RootGroup, "H", Fixed("group H"), T0);
        AddEntry(h, Fixed("entry Q"), "Q", T0);
        var bin = AddGroup(db.RootGroup, "Recycle Bin", Fixed("bin"), T0);
        db.RecycleBinEnabled = true;
        db.RecycleBinUuid = bin.Uuid;
        var store = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, T0);
        store.EnableSearching = false;
        store.EnableAutoType = false;
        store.Notes = NotesLine;
        NewGrant(db, "g1", Fixed("group G"), "agent", ".R...L..", T0);
        NewGrant(db, "g2", Fixed("entry Y"), "agent", "..U....P", T0);
        foreach (var name in new[] { "entry X", "entry Y", "entry S1", "entry Q" }) Known.Add(Hex(Fixed(name)));
        SaveTo(db, path);
    }

    static PwEntry E(PwDatabase db, string name) { return db.RootGroup.FindEntry(Fixed("entry " + name), true); }
    static PwGroup G(PwDatabase db, string name) { return db.RootGroup.FindGroup(Fixed("group " + name), true); }

    static string Verdict(PwDatabase db, bool keys)
    {
        var f = Read(db, keys);
        var rows = new List<string>();
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (Get(e, KindField) != null) continue;
            var path = e.ParentGroup.GetFullPath("/", false);
            rows.Add((path.Length == 0 ? "" : path + "/") + e.Strings.ReadSafe(PwDefs.TitleField) + (Known.Contains(Hex(e.Uuid)) ? "" : "*") + "=" + Effective(f, e, "agent"));
        }
        rows.Sort(StringComparer.Ordinal);
        return string.Join("  ", rows.ToArray()) + "  [grants live " + f.Live.ToString(Inv) + ", cut " + f.Cut.ToString(Inv)
            + (f.Foreign > 0 ? ", other store " + f.Foreign.ToString(Inv) : "") + (f.InBin > 0 ? ", in bin " + f.InBin.ToString(Inv) : "")
            + (f.Outside > 0 ? ", outside the store group " + f.Outside.ToString(Inv) : "") + (f.Bad > 0 ? ", unreadable " + f.Bad.ToString(Inv) : "")
            + "; withdrawals " + f.WithdrawalEntries.ToString(Inv) + " entries, " + f.WithdrawalKeys.ToString(Inv) + " keys]";
    }

    static string State(PwDatabase db)
    {
        var with = Verdict(db, true);
        var without = Verdict(db, false);
        return with == without ? with : with + "\n        ENTRIES ALONE (keys not read): " + without;
    }

    sealed class Scenario
    {
        public string Name, What, Expect;
        public Action<PwDatabase> A, B;
        public Func<string> ForeignB;
        public bool Slow;
    }

    static List<Scenario> Scenarios()
    {
        var list = new List<Scenario>();
        Action<PwDatabase> stale = db => AddEntry(G(db, "H"), Fixed("entry B-only"), "Bonly", At(200));
        Known.Add(Hex(Fixed("entry B-only")));
        Known.Add(Hex(Fixed("entry S2")));

        list.Add(new Scenario
        {
            Name = "01", What = "grants added in both copies: A grants agent C on G, B grants agent .R...LA. on S",
            Expect = "the union",
            A = db => NewGrant(db, "g3", Fixed("group G"), "agent", "C.......", At(100)),
            B = db => NewGrant(db, "g4", Fixed("group S"), "agent", ".R...LA.", At(200)),
        });
        list.Add(new Scenario
        {
            Name = "02", What = "A takes Y's grant away; B, not knowing, changes Y's password later",
            Expect = "Y left with what G gives",
            A = db => Withdraw(db, "w1", Fixed("entry Y"), "agent", "..U....P", At(100)),
            B = db => Edit(E(db, "Y"), At(200), e => Password(e, "b")),
        });
        list.Add(new Scenario
        {
            Name = "03", What = "both copies narrow G's row: A takes L away, B takes R away",
            Expect = "neither letter",
            A = db => Withdraw(db, "w1", Fixed("group G"), "agent", ".....L..", At(100)),
            B = db => Withdraw(db, "w2", Fixed("group G"), "agent", ".R......", At(200)),
        });
        list.Add(new Scenario
        {
            Name = "04", What = "A takes G's whole row away; B, not knowing, adds U to it",
            Expect = "nothing from G",
            A = db => Withdraw(db, "w1", Fixed("group G"), "agent", "CRUSDLAP", At(100)),
            B = db => NewGrant(db, "g3", Fixed("group G"), "agent", "..U.....", At(200)),
        });
        list.Add(new Scenario
        {
            Name = "05", What = "A takes Y's grant away and then grants .R.....P there; B is a copy from before, saved later",
            Expect = "Y with the new grant only",
            A = db => { Withdraw(db, "w1", Fixed("entry Y"), "agent", "..U....P", At(100)); NewGrant(db, "g3", Fixed("entry Y"), "agent", ".R.....P", At(110)); },
            B = stale,
        });
        list.Add(new Scenario
        {
            Name = "06", What = "A takes Y's grant away; the withdrawal's entry is then deleted with no deletion record; B is a copy from before",
            Expect = "Y left with what G gives while a key is found",
            A = db =>
            {
                var id = Withdraw(db, "w1", Fixed("entry Y"), "agent", "..U....P", At(100));
                var w = db.RootGroup.FindEntry(Fixed("record w1"), true);
                w.ParentGroup.Entries.Remove(w);
            },
            B = stale,
        });
        list.Add(new Scenario
        {
            Name = "07", What = "a file made elsewhere (B) plants a grant for agent on H in a group carrying the store group's identifier, under another store id",
            Expect = "Q closed",
            A = db => AddEntry(G(db, "H"), Fixed("entry A-only"), "Aonly", At(100)),
            ForeignB = () =>
            {
                var db = NewDb("foreign-new.kdbx");
                var store = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, At(300));
                NewGrant(db, "planted", Fixed("group H"), "agent", ".R...L.P", At(300), OtherSid);
                var p = Path.Combine(Work, "foreign-grant.kdbx");
                SaveTo(db, p);
                return p;
            },
        });
        Known.Add(Hex(Fixed("entry A-only")));
        list.Add(new Scenario
        {
            Name = "08", What = "a file made elsewhere (B) plants a withdrawal of agent's row on G",
            Expect = "G closed to agent (a planted withdrawal takes away)",
            A = db => AddEntry(G(db, "H"), Fixed("entry A-only"), "Aonly", At(100)),
            ForeignB = () =>
            {
                var db = NewDb("foreign-new.kdbx");
                var store = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, At(300));
                Withdraw(db, "planted-w", Fixed("group G"), "agent", "CRUSDLAP", At(300), false);
                var p = Path.Combine(Work, "foreign-withdrawal.kdbx");
                SaveTo(db, p);
                return p;
            },
        });
        list.Add(new Scenario
        {
            Name = "09", What = "the tree as the policy: A moves X out of G into H; B is a copy from before, saved later",
            Expect = "X closed where the move is carried",
            A = db => Move(E(db, "X"), G(db, "H"), At(100)),
            B = stale,
        });
        list.Add(new Scenario
        {
            Name = "10", What = "A moves X out of G into H; B changes X's password later, X still in G",
            Expect = "X closed where the move is carried",
            A = db => Move(E(db, "X"), G(db, "H"), At(100)),
            B = db => Edit(E(db, "X"), At(200), e => Password(e, "b")),
        });
        list.Add(new Scenario
        {
            Name = "11", What = "A moves X out of G into H; B, not knowing, moves X into G/S later",
            Expect = "the later move wins: X open",
            A = db => Move(E(db, "X"), G(db, "H"), At(100)),
            B = db => Move(E(db, "X"), G(db, "S"), At(200)),
        });
        list.Add(new Scenario
        {
            Name = "12", What = "A moves X out of G into H; B had moved X into G/S earlier",
            Expect = "X closed where the move is carried",
            A = db => Move(E(db, "X"), G(db, "H"), At(200)),
            B = db => Move(E(db, "X"), G(db, "S"), At(100)),
        });
        list.Add(new Scenario
        {
            Name = "13", What = "A moves the group S out of G to the root; B adds an entry S2 in S later",
            Expect = "S1 and S2 closed where the move is carried",
            A = db => Move(G(db, "S"), db.RootGroup, At(100)),
            B = db => AddEntry(G(db, "S"), Fixed("entry S2"), "S2", At(200)),
        });
        list.Add(new Scenario
        {
            Name = "14", What = "A deletes X for good, to take it away; B is a copy from before, saved later",
            Expect = "X gone where the deletion is carried",
            A = db => DeleteForGood(db, E(db, "X"), At(100)),
            B = stale,
        });
        list.Add(new Scenario
        {
            Name = "15", What = "A takes G's grant away (the robust way to close G); B is a copy from before in which X's password changed later",
            Expect = "everything under G closed but Y's own grant",
            A = db => Withdraw(db, "w1", Fixed("group G"), "agent", ".R...L..", At(100)),
            B = db => Edit(E(db, "X"), At(200), e => Password(e, "b")),
        });
        list.Add(new Scenario
        {
            Name = "16", Slow = true, What = "as 06, with a key derivation that takes more than a second (the case in which KeePassXC replaces the database's custom data)",
            Expect = "Y left with what G gives while a key is found",
            A = db =>
            {
                Withdraw(db, "w1", Fixed("entry Y"), "agent", "..U....P", At(100));
                var w = db.RootGroup.FindEntry(Fixed("record w1"), true);
                w.ParentGroup.Entries.Remove(w);
            },
            B = stale,
        });
        list.Add(new Scenario
        {
            Name = "17", Slow = true, What = "as 02, with a key derivation that takes more than a second",
            Expect = "Y left with what G gives",
            A = db => Withdraw(db, "w1", Fixed("entry Y"), "agent", "..U....P", At(100)),
            B = db => Edit(E(db, "Y"), At(200), e => Password(e, "b")),
        });
        return list;
    }

    static bool SlowKdf;

    static string Prepare(Scenario sc, string prefix, out string a, out string b)
    {
        var dir = Path.Combine(Work, prefix + sc.Name);
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        SlowKdf = sc.Slow;
        MakeBase(basePath);
        SlowKdf = false;
        a = Path.Combine(dir, "A.kdbx");
        b = Path.Combine(dir, "B.kdbx");
        var da = Open(basePath); sc.A(da); SaveTo(da, a);
        if (sc.ForeignB != null) File.Copy(sc.ForeignB(), b, true);
        else { var db = Open(basePath); sc.B(db); SaveTo(db, b); }
        return dir;
    }

    static void Header(Scenario sc, string a, string b)
    {
        Out.WriteLine();
        Out.WriteLine(sc.Name + ": " + sc.What);
        Out.WriteLine("    expected: " + sc.Expect);
        var da = Open(a); Out.WriteLine("    A alone: " + State(da)); da.Close();
        var db = Open(b); Out.WriteLine("    B alone: " + State(db)); db.Close();
    }

    static void RunKeePass()
    {
        Out.WriteLine("== KeePass " + PwDefs.VersionString + " (KeePassLib), PwDatabase.MergeIn, each result saved and read back ==");
        Out.WriteLine("   each line: every user entry by path with what agent may do there; * marks an entry under an identifier the base did not hold");
        var versions = new SortedSet<string>();
        foreach (var sc in Scenarios())
        {
            if (sc.Slow) continue;
            string a, b;
            var dir = Prepare(sc, "kp", out a, out b);
            Header(sc, a, b);
            foreach (var method in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids })
            {
                foreach (var into in new[] { "A", "B" })
                {
                    var target = Open(into == "A" ? a : b);
                    var source = Open(into == "A" ? b : a);
                    target.MergeIn(source, method, null);
                    source.Close();
                    var result = Path.Combine(dir, method + "-into" + into + ".kdbx");
                    SaveTo(target, result);
                    versions.Add(KdbxVersion(result));
                    var read = Open(result);
                    Out.WriteLine("    " + method.ToString().PadRight(17) + " into " + into + ": " + State(read));
                    read.Close();
                }
            }
        }
        Out.WriteLine();
        Out.WriteLine("KDBX versions of the results: " + string.Join(", ", versions.ToArray()));
    }

    static void RunCopies()
    {
        Out.WriteLine();
        Out.WriteLine("== copies, in memory, KeePassLib " + PwDefs.VersionString + " ==");
        var path = Path.Combine(Work, "copies-base.kdbx");
        MakeBase(path);

        var db = Open(path);
        Out.WriteLine("  base:                                              " + State(db));
        var x2 = E(db, "X").Duplicate(); G(db, "G").AddEntry(x2, true);
        var y2 = E(db, "Y").Duplicate(); G(db, "G").AddEntry(y2, true);
        Out.WriteLine("  PwEntry.Duplicate of X and of Y, beside them:      " + State(db));
        var g2 = G(db, "G").Duplicate(); g2.Name = "G copy"; db.RootGroup.AddGroup(g2, true);
        Out.WriteLine("  PwGroup.Duplicate of G, under the root:            " + State(db));
        var q2 = E(db, "Q").Duplicate(); G(db, "G").AddEntry(q2, true);
        Out.WriteLine("  PwEntry.Duplicate of Q (the secret) put into G:    " + State(db));
        db.Close();

        db = Open(path);
        var copy = db.RootGroup.FindEntry(Fixed("record g1"), true).Duplicate();
        Store(db).AddEntry(copy, true);
        var stray = db.RootGroup.FindEntry(Fixed("record g2"), true).Duplicate();
        G(db, "H").AddEntry(stray, true);
        Out.WriteLine("  a duplicate of grant g1 in the store group and of g2 in H: " + State(db));
        db.Close();

        db = Open(path);
        Withdraw(db, "w1", Fixed("entry Y"), "agent", "..U....P", At(100));
        var back = Open(path);
        var old = back.RootGroup.FindEntry(Fixed("record g2"), true).Duplicate();   // a copy of the withdrawn grant under a new identifier
        back.Close();
        G(db, "H").AddEntry(old, true);
        Out.WriteLine("  Y's grant withdrawn, then a copy of that grant under a new identifier put into H: " + State(db));
        db.Close();

        db = Open(path);
        var g1 = db.RootGroup.FindEntry(Fixed("record g1"), true);
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        Move(g1, bin, At(100));
        Out.WriteLine("  grant g1 moved to the recycle bin by hand:         " + State(db));
        db.Close();

        db = Open(path);
        var src = Open(path);
        db.MergeIn(src, PwMergeMethod.CreateNewUuids, null);
        src.Close();
        Out.WriteLine("  the base imported into itself with Create new IDs: " + State(db));
        db.DeleteDuplicateEntries(null);
        Out.WriteLine("  then DeleteDuplicateEntries:                       " + State(db));
        db.Close();
    }

    static void RunCost(int groups, int perGroup)
    {
        var db = NewDb("cost.kdbx");
        var store = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, T0);
        var nodes = new List<PwUuid>();
        for (var g = 0; g < groups; g++)
        {
            var grp = AddGroup(db.RootGroup, "g" + g.ToString(Inv), new PwUuid(true), T0);
            if (g < 30) nodes.Add(grp.Uuid);
            for (var i = 0; i < perGroup; i++) AddEntry(grp, new PwUuid(true), "e" + i.ToString(Inv), T0);
        }
        for (var i = 0; i < 30; i++)
        {
            NewGrant(db, "cost g" + i.ToString(Inv), nodes[i], "agent", ".R...L..", T0);
            NewGrant(db, "cost h" + i.ToString(Inv), nodes[i], "ops", "CRU..L..", T0);
            if (i < 10) Withdraw(db, "cost w" + i.ToString(Inv), nodes[i], "ops", "C.......", T0);
        }
        var entries = db.RootGroup.GetEntries(true).UCount;
        Read(db, true);
        var sw = Stopwatch.StartNew();
        const int runs = 20;
        Fold f = null;
        for (var i = 0; i < runs; i++) f = Read(db, true);
        sw.Stop();
        Out.WriteLine("  " + entries.ToString(Inv) + " entries, " + groups.ToString(Inv) + " groups, 60 grants, 10 withdrawals: the whole fold "
            + (sw.Elapsed.TotalMilliseconds / runs).ToString("0.00", Inv) + " ms (live " + f.Live.ToString(Inv) + ")");
        db.Close();
    }

    static string Cli(string exe, string dir, params string[] args)
    {
        var info = new ProcessStartInfo { FileName = exe, WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var sb = new StringBuilder();
        foreach (var a in args) sb.Append('"').Append(a).Append("\" ");
        info.Arguments = sb.ToString();
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            var bytes = new UTF8Encoding(false).GetBytes(Pw + "\n" + Pw + "\n");
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            p.StandardInput.BaseStream.Flush();
            p.StandardInput.Close();
            p.WaitForExit();
            o.Wait(); e.Wait();
            return p.ExitCode.ToString(Inv);
        }
    }

    static void RunXc(string version, string exe)
    {
        Out.WriteLine("== KeePassXC " + version + ", keepassxc-cli merge -s <target> <source>, the target read back with KeePassLib " + PwDefs.VersionString + " ==");
        var versions = new SortedSet<string>();

        // A plain save first: add an entry elsewhere, read the store back.
        {
            var dir = Path.Combine(Work, "xc" + version + "-save");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "base.kdbx");
            MakeBase(path);
            var before = Open(path); var was = State(before) + " ;; " + Listing(before); before.Close();
            var exit = Cli(exe, dir, "add", path, "H/xc-added");
            var after = Open(path); var now = Listing(after);
            Out.WriteLine("  a plain save (add, exit " + exit + "), file then KDBX " + KdbxVersion(path) + ": every record's identifier, strings, flags and times unchanged: "
                + (was.EndsWith(now, StringComparison.Ordinal) ? "yes" : "NO"));
            after.Close();
        }

        foreach (var sc in Scenarios())
        {
            string a, b;
            var dir = Prepare(sc, "xc" + version + "-", out a, out b);
            Header(sc, a, b);
            foreach (var into in new[] { "A", "B" })
            {
                var cell = Path.Combine(dir, "into" + into);
                Directory.CreateDirectory(cell);
                var target = Path.Combine(cell, "target.kdbx");
                var source = Path.Combine(cell, "source.kdbx");
                File.Copy(into == "A" ? a : b, target, true);
                File.Copy(into == "A" ? b : a, source, true);
                var sw = Stopwatch.StartNew();
                var exit = Cli(exe, cell, "merge", "-s", target, source);
                sw.Stop();
                if (sc.Slow) Out.WriteLine("    (the merge took " + sw.Elapsed.TotalSeconds.ToString("0.0", Inv) + " s)");
                versions.Add(KdbxVersion(target));
                var read = Open(target);
                Out.WriteLine("    into " + into + " (exit " + exit + "): " + State(read));
                read.Close();
            }
        }
        Out.WriteLine();
        Out.WriteLine("KDBX versions of the targets after the merges: " + string.Join(", ", versions.ToArray()));
    }

    static string Listing(PwDatabase db)
    {
        var rows = new List<string>();
        foreach (var e in Store(db).Entries)
        {
            var sb = new StringBuilder();
            sb.Append(Hex(e.Uuid)).Append(' ').Append(e.LastModificationTime.ToString("o", Inv)).Append(' ').Append(e.History.UCount.ToString(Inv));
            var keys = new List<string>(e.Strings.GetKeys());
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) sb.Append(' ').Append(k).Append(e.Strings.Get(k).IsProtected ? "[p]=" : "=").Append(e.Strings.ReadSafe(k));
            rows.Add(sb.ToString());
        }
        rows.Sort(StringComparer.Ordinal);
        return string.Join(" ;; ", rows.ToArray());
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        var what = args[1];
        using (Out = new StreamWriter(args[2], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString + ", " + what);
            if (what == "model") RunModel(int.Parse(args[3], Inv), int.Parse(args[4], Inv), int.Parse(args[5], Inv));
            else if (what == "keepass") { RunKeePass(); RunCopies(); Out.WriteLine(); Out.WriteLine("== cost, in memory, a console process =="); RunCost(500, 20); }
            else if (what == "xc") RunXc(args[3], args[4]);
            else if (what == "state")
            {
                Scenarios();
                foreach (var n in new[] { "entry X", "entry Y", "entry S1", "entry Q" }) Known.Add(Hex(Fixed(n)));
                for (var i = 3; i < args.Length; i++) { var d = Open(args[i]); Out.WriteLine(Path.GetFileName(Path.GetDirectoryName(args[i])) + "/" + Path.GetFileName(args[i]) + " (KDBX " + KdbxVersion(args[i]) + "): " + State(d)); d.Close(); }
            }
            else return 2;
        }
        return 0;
    }
}
