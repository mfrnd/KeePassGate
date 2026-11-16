using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KeePassGate.Acl;

// The rollback trial of two access rule models. Console only, invented trees only, no database.
//
// Today's model is the product's own AclResolver and AclDocument at a42e38aa, compiled in. The
// grant-only model and its middle form are the resolution functions of the grant-only design's
// probe (spikes\storage\probes\grant-only\design\probe\Program.cs), carried below unchanged, and
// its fold of grant and withdrawal records (that file's `Read`, lines 652 to 659).
//
// What is measured. A history of edits a person makes is generated; after each edit the person's
// intent is an access table (profile by node by verb and switch), and each model expresses that
// intent in its own records. After the history, losses that can happen to a store are applied, and
// each model's table after the loss is compared with the person's last intent.
//
//   intent   a policy of decisions: per node and profile an "exact" decision (today's rule: from
//            here down the profile has exactly these cells, until a nearer decision) or an "adds"
//            decision (from here down, these cells besides what arrives), and per node a seal
//            (profiles the node has no decision for get nothing from above). Today's kinds of edit
//            make exact decisions and seals; the grant-only form's own act, a grant with its own
//            switches below an inherited rule, makes an adds decision.
//   today    one document per node: a seal is `default: deny`, a decision is the widest mask whose
//            cells lie within what the decision wants at that node.
//   strict   grant-only rows from the table: a node gets at most what every node under it may have
//            (the design's push-down), and a row is written on every node with a decision and on
//            every node a push-down reaches. Records are the design's: letters added are a new
//            grant that lists the withdrawals of its row it saw, letters removed are a withdrawal,
//            and a grant left with nothing is deleted in the same write.
//   middle   every decision is a grant row on its own node; a narrowing, and a seal, is a withheld
//            mark on that node for that profile, holding the letters that arrive and the decision
//            does not hold; the node refers back to its marks. A mark changed is a lift of the
//            mark that holds too much and a new mark for what is missing.
//
// Cells: 14 per node and profile, create, read, update and list each on fields, with attachments
// and with protected values, and strip and delete (the switches are not asked for a removal). The
// design's probe counts 18 answers, strip and delete three times each; the controls use its 18.
static class P
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static TextWriter Out;

    // =====================================================================================
    // Carried from the design's probe, unchanged: lines 51 to 54, 56 to 89, 95 to 141, 143 to 237.
    // The one addition is the field `Index` on MNode, the node's place in its tree's list.
    // =====================================================================================

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
        public int Index;

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

    // =====================================================================================
    // Cells, written here
    // =====================================================================================

    const int CellCount = 14, CellS = 12, CellD = 13;
    static readonly int[] QualifiedBits = { C, R, U, L };
    static readonly AclVerb[] QualifiedVerbs = { AclVerb.Create, AclVerb.Read, AclVerb.Update, AclVerb.List };
    static readonly AclVerb[] CellVerb = new AclVerb[CellCount];
    static readonly AclReach[] CellReach = new AclReach[CellCount];
    static readonly int[] CellBit = new int[CellCount];
    static readonly string[] CellName = new string[CellCount];
    static readonly ushort[] CellTable = new ushort[256];
    static readonly int[] WidestMemo = new int[1 << CellCount];

    static P()
    {
        for (var v = 0; v < 4; v++)
            for (var r = 0; r < 3; r++)
            {
                var i = v * 3 + r;
                CellVerb[i] = QualifiedVerbs[v];
                CellReach[i] = Reaches[r];
                CellBit[i] = QualifiedBits[v];
                CellName[i] = MaskText(QualifiedBits[v]).Replace(".", "") + (r == 0 ? "" : r == 1 ? "+A" : "+P");
            }
        CellVerb[CellS] = AclVerb.Strip; CellReach[CellS] = AclReach.Fields; CellBit[CellS] = S; CellName[CellS] = "S";
        CellVerb[CellD] = AclVerb.Delete; CellReach[CellD] = AclReach.Fields; CellBit[CellD] = D; CellName[CellD] = "D";
        for (var m = 0; m < 256; m++) CellTable[m] = ComputeCells(m);
        for (var i = 0; i < WidestMemo.Length; i++) WidestMemo[i] = -1;
    }

    // The cells a single mask grants, a switch counting for the verbs of the same mask.
    static ushort ComputeCells(int m)
    {
        var c = 0;
        for (var v = 0; v < 4; v++)
        {
            if ((m & QualifiedBits[v]) == 0) continue;
            c |= 1 << (v * 3);
            if ((m & A) != 0) c |= 1 << (v * 3 + 1);
            if ((m & Pr) != 0) c |= 1 << (v * 3 + 2);
        }
        if ((m & S) != 0) c |= 1 << CellS;
        if ((m & D) != 0) c |= 1 << CellD;
        return (ushort)c;
    }

    static ushort CellsOf(int m) { return CellTable[m & 255]; }

    // The letters a set of cells touches: a verb with any cell, a switch with any cell.
    static int LettersOf(int c)
    {
        var m = 0;
        for (var v = 0; v < 4; v++)
        {
            if (((c >> (v * 3)) & 7) != 0) m |= QualifiedBits[v];
            if (((c >> (v * 3 + 1)) & 1) != 0) m |= A;
            if (((c >> (v * 3 + 2)) & 1) != 0) m |= Pr;
        }
        if ((c & (1 << CellS)) != 0) m |= S;
        if ((c & (1 << CellD)) != 0) m |= D;
        return m;
    }

    // The cells a mark holding these letters refuses, as the design's Middle refuses them.
    static int Blocked(int w)
    {
        var c = 0;
        for (var v = 0; v < 4; v++)
        {
            if ((w & QualifiedBits[v]) != 0) { c |= 7 << (v * 3); continue; }
            if ((w & A) != 0) c |= 1 << (v * 3 + 1);
            if ((w & Pr) != 0) c |= 1 << (v * 3 + 2);
        }
        if ((w & S) != 0) c |= 1 << CellS;
        if ((w & D) != 0) c |= 1 << CellD;
        return c;
    }

    static int Pop(int x) { var n = 0; while (x != 0) { x &= x - 1; n++; } return n; }

    // The mask granting the most cells within c: then the fewest letters, then the lowest value.
    static int Widest(int c)
    {
        if (WidestMemo[c] >= 0) return WidestMemo[c];
        int best = 0, bestCells = -1, bestLetters = 9;
        for (var m = 0; m < 256; m++)
        {
            var cm = CellTable[m];
            if ((cm & ~c) != 0) continue;
            var n = Pop(cm); var l = Pop(m);
            if (n > bestCells || (n == bestCells && l < bestLetters)) { best = m; bestCells = n; bestLetters = l; }
        }
        return WidestMemo[c] = best;
    }

    static string CellsText(int c)
    {
        var parts = new List<string>();
        for (var i = 0; i < CellCount; i++) if ((c & (1 << i)) != 0) parts.Add(CellName[i]);
        return string.Join(",", parts.ToArray());
    }

    // =====================================================================================
    // A world, a policy, an edit
    // =====================================================================================

    sealed class World
    {
        public List<MNode> Nodes;
        public string[] Profiles;
        public string[][] One;
        public int N { get { return Nodes.Count; } }
        public int Pn { get { return Profiles.Length; } }

        public World(List<MNode> nodes, string[] profiles)
        {
            Nodes = nodes;
            Profiles = profiles;
            for (var i = 0; i < nodes.Count; i++) nodes[i].Index = i;
            One = profiles.Select(p => new[] { p }).ToArray();
        }
    }

    const byte None = 0, Exact = 1, Adds = 2;

    sealed class Policy
    {
        public byte[] Mode; public int[] Mask; public bool[] Seal;

        public Policy(int nodes, int profiles)
        {
            Mode = new byte[nodes * profiles]; Mask = new int[nodes * profiles]; Seal = new bool[nodes];
        }

        public Policy Clone()
        {
            var q = new Policy(0, 0);
            q.Mode = (byte[])Mode.Clone(); q.Mask = (int[])Mask.Clone(); q.Seal = (bool[])Seal.Clone();
            return q;
        }

        public bool SameAs(Policy o)
        {
            for (var i = 0; i < Mode.Length; i++) if (Mode[i] != o.Mode[i] || (Mode[i] != None && Mask[i] != o.Mask[i])) return false;
            for (var i = 0; i < Seal.Length; i++) if (Seal[i] != o.Seal[i]) return false;
            return true;
        }
    }

    enum Kind { Grant, Widen, Narrow, Empty, Seal, Remove, AddBelow, AddsBelow }
    const int KindCount = 8;
    static readonly string[] KindName = { "grant", "widen", "narrow", "empty rule", "seal", "remove", "add below", "adds below" };

    sealed class Act
    {
        public Kind Kind; public int Node; public int Prof = -1; public byte Mode; public int Mask; public bool IsSeal; public bool SealValue;

        public Policy Apply(World w, Policy p)
        {
            var q = p.Clone();
            if (IsSeal) q.Seal[Node] = SealValue;
            else { var i = Node * w.Pn + Prof; q.Mode[i] = Mode; q.Mask[i] = Mode == None ? 0 : Mask; }
            return q;
        }

        public string Text(World w)
        {
            var at = w.Nodes[Node].Label;
            if (IsSeal) return (SealValue ? "seal " : "unseal ") + at;
            var p = w.Profiles[Prof];
            if (Mode == None) return KindName[(int)Kind] + " " + p + " at " + at;
            return KindName[(int)Kind] + " " + p + " at " + at + " " + (Mode == Adds ? "+" : "=") + MaskText(Mask);
        }
    }

    sealed class Alphabet
    {
        public int[] GrantMasks, WidenBits, NarrowBits, AddWiderBits, AddNarrowerBits, AddsMasks;
    }

    static readonly int[] AllBits = { C, R, U, S, D, L, A, Pr };
    static readonly Alphabet SampleAlphabet = new Alphabet
    {
        GrantMasks = Typical, WidenBits = AllBits, NarrowBits = AllBits, AddWiderBits = AllBits, AddNarrowerBits = AllBits, AddsMasks = Typical,
    };
    static readonly Alphabet SmallAlphabet = new Alphabet
    {
        GrantMasks = new[] { R | L, C | R | U | L | Pr },
        WidenBits = new[] { U, Pr },
        NarrowBits = new[] { R, U, L, Pr },
        AddWiderBits = new[] { U, Pr },
        AddNarrowerBits = new[] { R, U, L, Pr },
        AddsMasks = new[] { U | Pr },
    };

    // The intent: what each profile is meant to have at each node, as 14 cells.
    static ushort[] Intent(World w, Policy pol)
    {
        var t = new ushort[w.N * w.Pn];
        foreach (var x in w.Nodes)
            for (var p = 0; p < w.Pn; p++)
            {
                var acc = 0;
                for (var n = x; n != null; n = n.Up)
                {
                    var i = n.Index * w.Pn + p;
                    if (pol.Mode[i] == Exact) { acc |= CellsOf(pol.Mask[i]); break; }
                    if (pol.Mode[i] == Adds) { acc |= CellsOf(pol.Mask[i]); continue; }
                    if (pol.Seal[n.Index]) break;
                }
                t[x.Index * w.Pn + p] = (ushort)acc;
            }
        return t;
    }

    static bool Same(ushort[] a, ushort[] b) { for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

    // Every edit of a kind the alphabet allows, before asking whether it changes the intent.
    static List<Act> Candidates(World w, Policy pol, ushort[] t, Kind k, Alphabet al, int onlyNode)
    {
        var list = new List<Act>();
        foreach (var n in w.Nodes)
        {
            if (onlyNode >= 0 && n.Index != onlyNode) continue;
            if (k == Kind.Seal) { if (!pol.Seal[n.Index]) list.Add(new Act { Kind = k, Node = n.Index, IsSeal = true, SealValue = true }); continue; }
            if (k == Kind.Remove && pol.Seal[n.Index]) list.Add(new Act { Kind = k, Node = n.Index, IsSeal = true, SealValue = false });
            for (var p = 0; p < w.Pn; p++)
            {
                var i = n.Index * w.Pn + p;
                var mode = pol.Mode[i]; var mask = pol.Mask[i]; var here = t[i];
                switch (k)
                {
                    case Kind.Grant:
                        if (mode == None && here == 0) foreach (var m in al.GrantMasks) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = Exact, Mask = m });
                        break;
                    case Kind.Widen:
                        if (mode != None) foreach (var b in al.WidenBits)
                            if ((mask & b) == 0 && CellsOf(mask | b) != CellsOf(mask)) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = mode, Mask = mask | b });
                        break;
                    case Kind.Narrow:
                        if (mode != None) foreach (var b in al.NarrowBits)
                            if ((mask & b) != 0 && CellsOf(mask & ~b) != CellsOf(mask)) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = mode, Mask = mask & ~b });
                        break;
                    case Kind.Empty:
                        if (here != 0 && !(mode == Exact && CellsOf(mask) == 0)) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = Exact, Mask = 0 });
                        break;
                    case Kind.Remove:
                        if (mode != None) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = None });
                        break;
                    case Kind.AddBelow:
                        if (mode == None && here != 0)
                        {
                            var h = Widest(here);
                            foreach (var b in al.AddWiderBits)
                                if ((h & b) == 0 && CellsOf(h | b) != CellsOf(h)) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = Exact, Mask = h | b });
                            foreach (var b in al.AddNarrowerBits)
                                if ((h & b) != 0 && CellsOf(h & ~b) != CellsOf(h)) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = Exact, Mask = h & ~b });
                        }
                        break;
                    case Kind.AddsBelow:
                        if (mode == None && here != 0) foreach (var m in al.AddsMasks) list.Add(new Act { Kind = k, Node = n.Index, Prof = p, Mode = Adds, Mask = m });
                        break;
                }
            }
        }
        return list;
    }

    // Weights of the kinds in the sample, in fourths: grant 3, widen 1, narrow 1.5, empty 1, seal
    // 0.75, remove 1, add below 1.5, adds below 1.
    static readonly int[] KindWeight = { 12, 4, 6, 4, 3, 4, 6, 4 };

    static Act Pick(World w, Policy pol, ushort[] t, Random rng, Alphabet al, int onlyNode, Kind? only, Policy notThis)
    {
        var open = new bool[KindCount];
        for (var k = 0; k < KindCount; k++) open[k] = only == null || (int)only.Value == k;
        while (true)
        {
            var total = 0;
            for (var k = 0; k < KindCount; k++) if (open[k]) total += KindWeight[k];
            if (total == 0) return null;
            var roll = rng.Next(total);
            var kind = 0;
            for (var k = 0; k < KindCount; k++) { if (!open[k]) continue; if (roll < KindWeight[k]) { kind = k; break; } roll -= KindWeight[k]; }
            var cands = Candidates(w, pol, t, (Kind)kind, al, onlyNode);
            while (cands.Count > 0)
            {
                var i = rng.Next(cands.Count);
                var act = cands[i];
                cands.RemoveAt(i);
                var q = act.Apply(w, pol);
                if (notThis != null && q.SameAs(notThis)) continue;
                if (!Same(t, Intent(w, q))) return act;
            }
            open[kind] = false;
        }
    }

    // =====================================================================================
    // How each model expresses an intent
    // =====================================================================================

    static string[] TodayDocs(World w, Policy pol, ushort[] t)
    {
        var docs = new string[w.N];
        for (var n = 0; n < w.N; n++)
        {
            var grants = new Dictionary<string, IEnumerable<AclVerb>>(StringComparer.Ordinal);
            for (var p = 0; p < w.Pn; p++)
            {
                var i = n * w.Pn + p;
                if (pol.Mode[i] == None) continue;
                var m = Widest(pol.Mode[i] == Exact ? CellsOf(pol.Mask[i]) : t[i]);
                var verbs = new List<AclVerb>();
                foreach (var v in AclVerbs.Grantable) if ((m & Bit(v)) != 0) verbs.Add(v);
                grants[w.Profiles[p]] = verbs;
            }
            if (grants.Count == 0 && !pol.Seal[n]) continue;
            docs[n] = AclDocument.Of(pol.Seal[n] ? AclDefault.Deny : AclDefault.Inherit, grants).Write();
        }
        return docs;
    }

    static int[] StrictRows(World w, Policy pol, ushort[] t)
    {
        var rows = new int[w.N * w.Pn];
        var safe = new int[w.N];
        var have = new int[w.N];
        for (var p = 0; p < w.Pn; p++)
        {
            // What every node under a node may have, a node's children always after it in the list.
            for (var i = w.N - 1; i >= 0; i--)
            {
                var s = (int)t[i * w.Pn + p];
                foreach (var k in w.Nodes[i].Kids) s &= safe[k.Index];
                safe[i] = s;
            }
            for (var i = 0; i < w.N; i++)
            {
                var x = w.Nodes[i];
                var above = x.Up == null ? 0 : have[x.Up.Index];
                var idx = i * w.Pn + p;
                var decided = pol.Mode[idx] != None;
                var own = decided ? CellsOf(pol.Mask[idx]) : 0;
                int best = 0, b1 = -1, b2 = -1, bl = 9;
                for (var m = 0; m < 256; m++)
                {
                    var cm = CellTable[m];
                    if ((cm & ~safe[i]) != 0) continue;
                    var s1 = Pop(above | cm); var s2 = Pop(cm & own); var l = Pop(m);
                    if (s1 > b1 || (s1 == b1 && (s2 > b2 || (s2 == b2 && l < bl)))) { best = m; b1 = s1; b2 = s2; bl = l; }
                }
                have[i] = above | CellTable[best];
                rows[idx] = (decided && best != 0) || (CellTable[best] & ~above) != 0 ? best : 0;
            }
        }
        return rows;
    }

    static void MiddleTargets(World w, Policy pol, int[] rows, int[] marks)
    {
        var g = new int[w.N]; var h = new int[w.N]; var a = new int[w.N];
        for (var p = 0; p < w.Pn; p++)
            for (var i = 0; i < w.N; i++)
            {
                var up = w.Nodes[i].Up;
                var gp = up == null ? 0 : g[up.Index]; var hp = up == null ? 0 : h[up.Index]; var ap = up == null ? 0 : a[up.Index];
                var idx = i * w.Pn + p;
                var mode = pol.Mode[idx];
                var row = mode == Exact ? Widest(CellsOf(pol.Mask[idx])) : mode == Adds ? pol.Mask[idx] : 0;
                var mark = mode == Exact ? LettersOf(ap) & ~row : mode == None && pol.Seal[i] ? LettersOf(ap) : 0;
                rows[idx] = row; marks[idx] = mark;
                g[i] = gp | CellsOf(row); h[i] = hp | mark; a[i] = g[i] & ~Blocked(h[i]);
            }
    }

    // =====================================================================================
    // The stores
    // =====================================================================================

    sealed class TodayStore
    {
        public List<KeyValuePair<int, string>>[] Hist;

        public TodayStore(int n)
        {
            Hist = new List<KeyValuePair<int, string>>[n];
            for (var i = 0; i < n; i++) Hist[i] = new List<KeyValuePair<int, string>> { new KeyValuePair<int, string>(0, null) };
        }

        public int Write(string[] docs, int step)
        {
            var changed = 0;
            for (var i = 0; i < docs.Length; i++)
                if (Hist[i][Hist[i].Count - 1].Value != docs[i]) { Hist[i].Add(new KeyValuePair<int, string>(step, docs[i])); changed++; }
            return changed;
        }

        public string[] At(int step)
        {
            var d = new string[Hist.Length];
            for (var i = 0; i < Hist.Length; i++)
                foreach (var kv in Hist[i]) if (kv.Key <= step) d[i] = kv.Value;
            return d;
        }
    }

    sealed class Grant { public int Id, Node, Prof, Mask, Step, Deleted = -1; public HashSet<int> Saw; }
    sealed class Withdrawal { public int Id, Node, Prof, Mask, Step; }
    sealed class Mark { public int Id, Node, Prof, Mask, Step; }
    sealed class Lift { public int Id, Mark, Node, Step; }

    sealed class GoStore
    {
        public bool IsMiddle;
        // A row taken away whole is withdrawn with all eight letters, as scenario 04 of the
        // design's probe writes it, rather than with the letters removed, as its section 5.2 and its
        // scenarios 02 and 15 write it.
        public bool Whole;
        public List<Grant> Grants = new List<Grant>();
        public List<Withdrawal> Ws = new List<Withdrawal>();
        public List<Mark> Marks = new List<Mark>();
        public List<Lift> Lifts = new List<Lift>();
        public List<KeyValuePair<int, HashSet<int>>>[] Refs;
        public int NextId;

        public GoStore(bool middle, bool whole, int nodes)
        {
            IsMiddle = middle;
            Whole = whole;
            Refs = new List<KeyValuePair<int, HashSet<int>>>[nodes];
            for (var i = 0; i < nodes; i++) Refs[i] = new List<KeyValuePair<int, HashSet<int>>> { new KeyValuePair<int, HashSet<int>>(0, new HashSet<int>()) };
        }

        public HashSet<int> RefAt(int node, int step)
        {
            HashSet<int> r = null;
            foreach (var kv in Refs[node]) if (kv.Key <= step) r = kv.Value;
            return r;
        }

        public HashSet<int> RefNow(int node) { return Refs[node][Refs[node].Count - 1].Value; }
    }

    // The design's fold: a grant's live letters are its mask less every withdrawal of its row it
    // did not see; a row is the union of the live letters of its grants.
    static int[] Fold(World w, IEnumerable<Grant> gs, List<Withdrawal> ws)
    {
        var rows = new int[w.N * w.Pn];
        foreach (var g in gs)
        {
            var m = g.Mask;
            foreach (var wd in ws) if (wd.Node == g.Node && wd.Prof == g.Prof && !g.Saw.Contains(wd.Id)) m &= ~wd.Mask;
            if (m == 0) continue;
            rows[g.Node * w.Pn + g.Prof] |= m;
        }
        return rows;
    }

    static int Live(Grant g, List<Withdrawal> ws)
    {
        var m = g.Mask;
        foreach (var wd in ws) if (wd.Node == g.Node && wd.Prof == g.Prof && !g.Saw.Contains(wd.Id)) m &= ~wd.Mask;
        return m;
    }

    sealed class Written { public int Grants, Withdrawals, Deleted, Marks, Lifts; }

    // A change on the tab: letters removed are a withdrawal, and a grant it leaves with nothing is
    // deleted in the same write; letters added are a new grant listing the withdrawals it saw.
    static void WriteRows(GoStore st, World w, int[] target, int step, Written wr)
    {
        var cur = Fold(w, st.Grants.Where(g => g.Deleted < 0), st.Ws);
        for (var i = 0; i < target.Length; i++)
        {
            var removed = cur[i] & ~target[i];
            var added = target[i] & ~cur[i];
            var node = i / w.Pn; var prof = i % w.Pn;
            if (removed != 0)
            {
                st.Ws.Add(new Withdrawal { Id = st.NextId++, Node = node, Prof = prof, Mask = st.Whole && target[i] == 0 ? 255 : removed, Step = step });
                wr.Withdrawals++;
                foreach (var g in st.Grants)
                    if (g.Deleted < 0 && g.Node == node && g.Prof == prof && Live(g, st.Ws) == 0) { g.Deleted = step; wr.Deleted++; }
            }
            if (added != 0)
            {
                var saw = new HashSet<int>(st.Ws.Where(x => x.Node == node && x.Prof == prof).Select(x => x.Id));
                st.Grants.Add(new Grant { Id = st.NextId++, Node = node, Prof = prof, Mask = added, Step = step, Saw = saw });
                wr.Grants++;
            }
        }
    }

    static bool Lifted(GoStore st, Mark m) { foreach (var l in st.Lifts) if (l.Mark == m.Id) return true; return false; }

    static void WriteMarks(GoStore st, World w, int[] target, int step, Written wr)
    {
        for (var i = 0; i < target.Length; i++)
        {
            var node = i / w.Pn; var prof = i % w.Pn;
            var inForce = st.Marks.Where(m => m.Node == node && m.Prof == prof && !Lifted(st, m)).ToList();
            var now = 0; foreach (var m in inForce) now |= m.Mask;
            if (now == target[i]) continue;
            var keep = 0;
            foreach (var m in inForce)
            {
                if ((m.Mask & ~target[i]) != 0) { st.Lifts.Add(new Lift { Id = st.NextId++, Mark = m.Id, Node = node, Step = step }); wr.Lifts++; }
                else keep |= m.Mask;
            }
            if ((target[i] & ~keep) != 0) { st.Marks.Add(new Mark { Id = st.NextId++, Node = node, Prof = prof, Mask = target[i] & ~keep, Step = step }); wr.Marks++; }
        }
        for (var n = 0; n < w.N; n++)
        {
            var refs = new HashSet<int>(st.Marks.Where(m => m.Node == n && !Lifted(st, m)).Select(m => m.Id));
            if (!refs.SetEquals(st.RefNow(n))) st.Refs[n].Add(new KeyValuePair<int, HashSet<int>>(step, refs));
        }
    }

    // =====================================================================================
    // Answers
    // =====================================================================================

    static ushort[] TodayTable(World w, string[] docs, Dictionary<string, ushort[]> memo)
    {
        string key = null;
        ushort[] t;
        if (memo != null)
        {
            key = string.Join("\u0001", docs.Select(d => d ?? "\u0002").ToArray());
            if (memo.TryGetValue(key, out t)) return t;
        }
        for (var i = 0; i < w.N; i++) w.Nodes[i].Doc = docs[i];
        var read = new AclDocuments();
        t = new ushort[w.N * w.Pn];
        foreach (var x in w.Nodes)
            for (var p = 0; p < w.Pn; p++)
            {
                var bits = 0;
                for (var c = 0; c < CellCount; c++)
                    if (AclResolver.Permits(x, w.One[p], CellVerb[c], CellReach[c], read)) bits |= 1 << c;
                t[x.Index * w.Pn + p] = (ushort)bits;
            }
        if (memo != null) memo[key] = t;
        return t;
    }

    static ushort[] GoTable(World w, int[] rows, int[] held, bool[] refuses, bool middle)
    {
        foreach (var n in w.Nodes)
        {
            n.Row.Clear(); n.Withheld.Clear(); n.Refuses = refuses != null && refuses[n.Index];
            for (var p = 0; p < w.Pn; p++)
            {
                var i = n.Index * w.Pn + p;
                if (rows[i] != 0) n.Row[w.Profiles[p]] = rows[i];
                if (held != null && held[i] != 0) n.Withheld[w.Profiles[p]] = held[i];
            }
        }
        var t = new ushort[w.N * w.Pn];
        foreach (var x in w.Nodes)
            for (var p = 0; p < w.Pn; p++)
            {
                var bits = 0;
                for (var c = 0; c < CellCount; c++)
                {
                    var ok = middle ? Middle(x, w.Profiles[p], CellBit[c], CellReach[c]) : GrantOnly(x, w.Profiles[p], CellBit[c], CellReach[c]);
                    if (ok) bits |= 1 << c;
                }
                t[x.Index * w.Pn + p] = (ushort)bits;
            }
        return t;
    }

    sealed class Records
    {
        public List<Grant> Grants; public List<Withdrawal> Ws; public List<Mark> Marks; public List<Lift> Lifts; public Func<int, IEnumerable<int>> RefOf;
    }

    static ushort[] Eval(World w, bool middle, Records rc)
    {
        var rows = Fold(w, rc.Grants, rc.Ws);
        if (!middle) return GoTable(w, rows, null, null, false);
        var lifted = new HashSet<int>(rc.Lifts.Select(l => l.Mark));
        var present = new HashSet<int>(rc.Marks.Select(m => m.Id));
        var held = new int[w.N * w.Pn];
        foreach (var m in rc.Marks) if (!lifted.Contains(m.Id)) held[m.Node * w.Pn + m.Prof] |= m.Mask;
        var refuses = new bool[w.N];
        for (var n = 0; n < w.N; n++)
        {
            var refs = rc.RefOf(n);
            if (refs != null) foreach (var id in refs) if (!present.Contains(id)) refuses[n] = true;
        }
        return GoTable(w, rows, held, refuses, true);
    }

    static Records Now(GoStore st)
    {
        return new Records
        {
            Grants = st.Grants.Where(g => g.Deleted < 0).ToList(), Ws = st.Ws.ToList(), Marks = st.Marks.ToList(), Lifts = st.Lifts.ToList(),
            RefOf = n => st.RefNow(n),
        };
    }

    static Records At(GoStore st, int s)
    {
        return new Records
        {
            Grants = st.Grants.Where(g => g.Step <= s && (g.Deleted < 0 || g.Deleted > s)).ToList(),
            Ws = st.Ws.Where(x => x.Step <= s).ToList(),
            Marks = st.Marks.Where(m => m.Step <= s).ToList(),
            Lifts = st.Lifts.Where(l => l.Step <= s).ToList(),
            RefOf = n => st.RefAt(n, s),
        };
    }

    // The store as it was at step s, as a store of its own that later writes can continue.
    static GoStore Copy(GoStore st, int s, int nextId)
    {
        var c = new GoStore(st.IsMiddle, st.Whole, st.Refs.Length) { NextId = nextId };
        foreach (var g in st.Grants.Where(g => g.Step <= s))
            c.Grants.Add(new Grant { Id = g.Id, Node = g.Node, Prof = g.Prof, Mask = g.Mask, Step = g.Step, Deleted = g.Deleted >= 0 && g.Deleted <= s ? g.Deleted : -1, Saw = new HashSet<int>(g.Saw) });
        c.Ws.AddRange(st.Ws.Where(x => x.Step <= s));
        c.Marks.AddRange(st.Marks.Where(m => m.Step <= s));
        c.Lifts.AddRange(st.Lifts.Where(l => l.Step <= s));
        for (var n = 0; n < st.Refs.Length; n++) { c.Refs[n].Clear(); c.Refs[n].AddRange(st.Refs[n].Where(kv => kv.Key <= s)); }
        return c;
    }

    // The step at which a node's records, or its reference, last changed; 0 when they never did.
    static int LastChange(GoStore st, int n)
    {
        var s = 0;
        foreach (var g in st.Grants) if (g.Node == n) { s = Math.Max(s, g.Step); s = Math.Max(s, g.Deleted); }
        foreach (var x in st.Ws) if (x.Node == n) s = Math.Max(s, x.Step);
        foreach (var m in st.Marks) if (m.Node == n) s = Math.Max(s, m.Step);
        foreach (var l in st.Lifts) if (l.Node == n) s = Math.Max(s, l.Step);
        foreach (var kv in st.Refs[n]) s = Math.Max(s, kv.Key);
        return s;
    }

    // What both versions allow, as `rules-central` section 3 reads two newest versions: a profile
    // both name keeps the verbs both give it, a profile one names keeps nothing, and the default is
    // deny when either denies. An absent document is one that names nothing.
    static string Meet(string a, string b)
    {
        var da = a == null ? AclDocument.Empty : AclDocument.Read(a);
        var db = b == null ? AclDocument.Empty : AclDocument.Read(b);
        var names = new SortedSet<string>(da.Profiles.Concat(db.Profiles), StringComparer.Ordinal);
        var grants = new Dictionary<string, IEnumerable<AclVerb>>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var ga = da.Grant(name); var gb = db.Grant(name);
            grants[name] = ga != null && gb != null ? ga.Where(v => gb.Holds(v)).ToList() : new List<AclVerb>();
        }
        var deny = da.Default == AclDefault.Deny || db.Default == AclDefault.Deny;
        return AclDocument.Of(deny ? AclDefault.Deny : AclDefault.Inherit, grants).Write();
    }

    // =====================================================================================
    // Counting
    // =====================================================================================

    const int Models = 5;
    static readonly string[] ModelName = { "today's model", "grant-only", "middle form", "grant-only, row whole", "middle form, row whole" };
    static bool IsMiddle(int m) { return m == 2 || m == 4; }

    sealed class Count
    {
        public long Trials, Wider, WiderCells, MaxCells, NarrowerOnly, Equal, Unchanged, WiderThanBefore;
        public string Example;

        public void Add(ushort[] intent, ushort[] before, ushort[] after, Func<string> describe)
        {
            if (Example == null && describe != null)
                for (var i = 0; i < intent.Length; i++)
                    if ((after[i] & ~intent[i]) != 0) { Example = describe(); break; }
            Add(intent, before, after);
        }

        public void Add(ushort[] intent, ushort[] before, ushort[] after)
        {
            Trials++;
            long w = 0, n = 0; var same = true; var wb = false;
            for (var i = 0; i < intent.Length; i++)
            {
                var a = after[i];
                w += Pop(a & ~intent[i]);
                n += Pop(intent[i] & ~a);
                if (a != before[i]) same = false;
                if ((a & ~before[i]) != 0) wb = true;
            }
            if (w > 0) { Wider++; WiderCells += w; if (w > MaxCells) MaxCells = w; }
            else if (n > 0) NarrowerOnly++;
            else Equal++;
            if (same) Unchanged++;
            if (wb) WiderThanBefore++;
        }

        public string Line()
        {
            var mean = Wider == 0 ? "-" : ((double)WiderCells / Wider).ToString("0.0", Inv);
            return F(Trials, 7) + F(Wider, 8) + F(WiderCells, 9) + mean.PadLeft(7) + F(MaxCells, 6) + F(NarrowerOnly, 10) + F(Equal, 8) + F(Unchanged, 10) + F(WiderThanBefore, 10);
        }
    }

    static string F(long v, int width) { return v.ToString(Inv).PadLeft(width); }
    static readonly string CountHead = "trials".PadLeft(7) + "wider".PadLeft(8) + "cells".PadLeft(9) + "mean".PadLeft(7) + "max".PadLeft(6)
        + "narrower".PadLeft(10) + "equal".PadLeft(8) + "no".PadLeft(10) + "wider".PadLeft(10);
    static readonly string CountHead2 = "".PadLeft(7) + "".PadLeft(8) + "".PadLeft(9) + "".PadLeft(7) + "".PadLeft(6)
        + "only".PadLeft(10) + "".PadLeft(8) + "change".PadLeft(10) + "than now".PadLeft(10);

    sealed class Fidelity
    {
        public long States, Exact, Narrower, Wider, NarrowerCells, StoreMismatch, Inexpressible, InexactYetExpressible;
        public long[] Acts = new long[KindCount], NotIntent = new long[KindCount], TurnedInexact = new long[KindCount];
        public string[] Example = new string[KindCount];
    }

    sealed class Block
    {
        public string Title;
        public Dictionary<string, Count> Counts = new Dictionary<string, Count>(StringComparer.Ordinal);
        public Fidelity[] Fid = { new Fidelity(), new Fidelity(), new Fidelity(), new Fidelity(), new Fidelity() };
        public long[] KindActs = new long[KindCount];
        public long[,] KindRecords = new long[KindCount, 8];
        public long Histories, Steps;
        public int MaxK;
        public bool Examples;

        public Count Get(string key)
        {
            Count c;
            if (!Counts.TryGetValue(key, out c)) Counts[key] = c = new Count();
            return c;
        }
    }

    // =====================================================================================
    // One history
    // =====================================================================================

    sealed class Run
    {
        public World W;
        public List<Policy> Pols = new List<Policy>();
        public List<ushort[]> Ts = new List<ushort[]>();
        public List<Act> Acts = new List<Act>();
        public TodayStore Today;
        public GoStore Go, Mid, GoWhole, MidWhole;

        public GoStore Store(int m) { return m == 1 ? Go : m == 2 ? Mid : m == 3 ? GoWhole : MidWhole; }
        public Dictionary<string, ushort[]> Memo = new Dictionary<string, ushort[]>(StringComparer.Ordinal);
        public List<ushort[][]> Tables = new List<ushort[][]>();

        public Run(World w)
        {
            W = w;
            Pols.Add(new Policy(w.N, w.Pn));
            Ts.Add(new ushort[w.N * w.Pn]);
            Acts.Add(null);
            Today = new TodayStore(w.N);
            Go = new GoStore(false, false, w.N);
            Mid = new GoStore(true, false, w.N);
            GoWhole = new GoStore(false, true, w.N);
            MidWhole = new GoStore(true, true, w.N);
            var empty = new ushort[Models][];
            for (var m = 0; m < Models; m++) empty[m] = new ushort[w.N * w.Pn];
            Tables.Add(empty);
        }

        public int H { get { return Pols.Count - 1; } }
    }

    static void Step(Run r, Act act, Block b)
    {
        var w = r.W;
        var s = r.Pols.Count;
        var pol = act.Apply(w, r.Pols[s - 1]);
        var t = Intent(w, pol);
        r.Pols.Add(pol); r.Ts.Add(t); r.Acts.Add(act);

        var docs = TodayDocs(w, pol, t);
        var docsChanged = r.Today.Write(docs, s);
        var strict = StrictRows(w, pol, t);
        var wg = new Written();
        WriteRows(r.Go, w, strict, s, wg);
        var middleRows = new int[w.N * w.Pn]; var middleMarks = new int[w.N * w.Pn];
        MiddleTargets(w, pol, middleRows, middleMarks);
        var wm = new Written();
        WriteRows(r.Mid, w, middleRows, s, wm);
        WriteMarks(r.Mid, w, middleMarks, s, wm);
        WriteRows(r.GoWhole, w, strict, s, new Written());
        WriteRows(r.MidWhole, w, middleRows, s, new Written());
        WriteMarks(r.MidWhole, w, middleMarks, s, new Written());

        var k = (int)act.Kind;
        b.KindActs[k]++;
        b.KindRecords[k, 0] += docsChanged;
        b.KindRecords[k, 1] += wg.Grants; b.KindRecords[k, 2] += wg.Withdrawals; b.KindRecords[k, 3] += wg.Deleted;
        b.KindRecords[k, 4] += wm.Grants; b.KindRecords[k, 5] += wm.Withdrawals; b.KindRecords[k, 6] += wm.Marks; b.KindRecords[k, 7] += wm.Lifts;

        // Control: with no loss, each model's table from its own store, against the intent.
        var tables = new ushort[Models][];
        var compiled = new ushort[Models][];
        tables[0] = compiled[0] = TodayTable(w, docs, r.Memo);
        var strictTable = GoTable(w, strict, null, null, false);
        var middleTable = GoTable(w, middleRows, middleMarks, null, true);
        for (var m = 1; m < Models; m++)
        {
            tables[m] = Eval(w, IsMiddle(m), Now(r.Store(m)));
            compiled[m] = IsMiddle(m) ? middleTable : strictTable;
        }
        var prev = r.Tables[s - 1];

        // Which states a model cannot give exactly, decided from the intent alone. Today: some node
        // is meant to hold a set of cells that no single mask grants (one document answers a profile
        // at a node with one mask). Grant-only: some node is meant to hold less than its parent (a
        // row reaches everything under its node). The middle form, for the expression written here:
        // a mark above a node withholds a letter that node's own row grants (a mark overrides every
        // grant under it); whether marks and push-down together could do better was not tried.
        var cannot = new bool[Models];
        var held = new int[w.N];
        for (var p = 0; p < w.Pn; p++)
            for (var i = 0; i < w.N; i++)
            {
                var idx = i * w.Pn + p;
                var up = w.Nodes[i].Up;
                if (CellsOf(Widest(t[idx])) != t[idx]) cannot[0] = true;
                if (up != null && (t[up.Index * w.Pn + p] & ~t[idx]) != 0) cannot[1] = true;
                var above = up == null ? 0 : held[up.Index];
                if ((CellsOf(middleRows[idx]) & Blocked(above)) != 0) cannot[2] = true;
                held[i] = above | middleMarks[idx];
            }
        cannot[3] = cannot[1];
        cannot[4] = cannot[2];

        for (var m = 0; m < Models; m++)
        {
            var f = b.Fid[m];
            f.States++;
            if (cannot[m]) f.Inexpressible++;
            if (!Same(tables[m], compiled[m])) f.StoreMismatch++;
            if (m == 0 && !r.Today.At(s).SequenceEqual(docs, StringComparer.Ordinal)) f.StoreMismatch++;
            long wide = 0, narrow = 0;
            for (var i = 0; i < t.Length; i++) { wide += Pop(tables[m][i] & ~t[i]); narrow += Pop(t[i] & ~tables[m][i]); }
            if (wide > 0) f.Wider++;
            else if (narrow > 0) { f.Narrower++; f.NarrowerCells += narrow; }
            else f.Exact++;
            f.Acts[k]++;
            var inexact = wide > 0 || narrow > 0;
            if (inexact) f.NotIntent[k]++;
            if (inexact && !cannot[m]) f.InexactYetExpressible++;
            if (inexact && Same(prev[m], r.Ts[s - 1]))
            {
                f.TurnedInexact[k]++;
                if (f.Example[k] == null)
                    f.Example[k] = string.Join("; ", r.Acts.Skip(1).Select(a => a.Text(w)).ToArray()) + " => " + Diff(w, t, tables[m]);
            }
        }
        r.Tables.Add(tables);
        b.Steps++;
    }

    static string Diff(World w, ushort[] intent, ushort[] got)
    {
        var parts = new List<string>();
        for (var i = 0; i < intent.Length; i++)
        {
            var miss = intent[i] & ~got[i]; var extra = got[i] & ~intent[i];
            if (miss == 0 && extra == 0) continue;
            var s = w.Nodes[i / w.Pn].Label + (w.Pn > 1 ? "/" + w.Profiles[i % w.Pn] : "");
            if (miss != 0) s += " lacks " + CellsText(miss);
            if (extra != 0) s += " has " + CellsText(extra);
            parts.Add(s);
        }
        return string.Join("; ", parts.ToArray());
    }

    // The losses, after the history.
    static void Losses(Run r, Block b, List<Act> alternatives)
    {
        var w = r.W; var H = r.H;
        var intent = r.Ts[H];
        var before = r.Tables[H];
        var story = b.Examples ? string.Join("; ", r.Acts.Skip(1).Select(a => a.Text(w)).ToArray()) : null;
        Func<string, ushort[], ushort[], Func<string>> D = (what, want, got) =>
            story == null ? null : (Func<string>)(() => story + " | " + what + " => " + Diff(w, want, got));

        // (a) the whole store as it was k edits ago.
        for (var k = 1; k <= H; k++)
        {
            var s = H - k;
            var after = new ushort[Models][];
            after[0] = TodayTable(w, r.Today.At(s), r.Memo);
            for (var m = 1; m < Models; m++) after[m] = Eval(w, IsMiddle(m), At(r.Store(m), s));
            for (var m = 0; m < Models; m++)
            {
                b.Get("a|" + m).Add(intent, before[m], after[m], D("the whole store at step " + s.ToString(Inv), intent, after[m]));
                b.Get("a" + k.ToString(Inv) + "|" + m).Add(intent, before[m], after[m]);
            }
            if (k > b.MaxK) b.MaxK = k;
        }

        // (b) and (c), today: one node's document back to the one before its last change, the
        // rest as it is. Under a store of versions that is the newest version lost, so (c) is the
        // same documents.
        var now = r.Today.At(H);
        for (var n = 0; n < w.N; n++)
        {
            var hist = r.Today.Hist[n];
            if (hist.Count < 2) continue;
            var docs = (string[])now.Clone();
            docs[n] = hist[hist.Count - 2].Value;
            var after = TodayTable(w, docs, r.Memo);
            b.Get("b|0").Add(intent, before[0], after, D(w.Nodes[n].Label + " back", intent, after));
            b.Get("c|0").Add(intent, before[0], after, D(w.Nodes[n].Label + "'s newest version lost", intent, after));
            // The product keeps the document on the node, so a stale copy of the node winning by
            // time is this same loss.
            b.Get("b-node|0").Add(intent, before[0], after, D("a stale copy of " + w.Nodes[n].Label, intent, after));
            var kind = ((int)r.Acts[hist[hist.Count - 1].Key].Kind).ToString(Inv);
            b.Get("b-kind" + kind + "|0").Add(intent, before[0], after);
            b.Get("c-kind" + kind + "|0").Add(intent, before[0], after);
        }

        // (b) and (c), grant-only and the middle form.
        for (var m = 1; m < Models; m++)
        {
            var st = r.Store(m);
            for (var n = 0; n < w.N; n++)
            {
                var s = LastChange(st, n);
                if (s == 0) continue;
                var node = n;
                // (b): what the node's last change wrote is gone, what it deleted is back, and the
                // node's reference is the one it had before.
                var back = new Records
                {
                    Grants = st.Grants.Where(g => g.Node == node ? g.Step != s && (g.Deleted < 0 || g.Deleted == s) : g.Deleted < 0).ToList(),
                    Ws = st.Ws.Where(x => !(x.Node == node && x.Step == s)).ToList(),
                    Marks = st.Marks.Where(x => !(x.Node == node && x.Step == s)).ToList(),
                    Lifts = st.Lifts.Where(x => !(x.Node == node && x.Step == s)).ToList(),
                    RefOf = i => i == node ? st.RefAt(i, s - 1) : st.RefNow(i),
                };
                var backTable = Eval(w, IsMiddle(m), back);
                b.Get("b|" + m).Add(intent, before[m], backTable, D(w.Nodes[n].Label + " back", intent, backTable));
                var kind = ((int)r.Acts[s].Kind).ToString(Inv);
                b.Get("b-kind" + kind + "|" + m).Add(intent, before[m], backTable);
                // (c): the newest records lost, older ones and deletions as they are, the node's
                // reference as it is.
                var lost = new Records
                {
                    Grants = st.Grants.Where(g => g.Deleted < 0 && !(g.Node == node && g.Step == s)).ToList(),
                    Ws = st.Ws.Where(x => !(x.Node == node && x.Step == s)).ToList(),
                    Marks = st.Marks.Where(x => !(x.Node == node && x.Step == s)).ToList(),
                    Lifts = st.Lifts.Where(x => !(x.Node == node && x.Step == s)).ToList(),
                    RefOf = i => st.RefNow(i),
                };
                var lostTable = Eval(w, IsMiddle(m), lost);
                b.Get("c|" + m).Add(intent, before[m], lostTable, D(w.Nodes[n].Label + "'s newest records lost", intent, lostTable));
                b.Get("c-kind" + kind + "|" + m).Add(intent, before[m], lostTable);
                // A stale copy of the node itself winning by time: every record as it is, since
                // records live in the plugin's group; the node's reference to its marks, which
                // travels with the node, as it was before the node's last change.
                var stale = Now(st);
                stale.RefOf = i => i == node ? st.RefAt(i, s - 1) : st.RefNow(i);
                var staleTable = Eval(w, IsMiddle(m), stale);
                b.Get("b-node|" + m).Add(intent, before[m], staleTable, D("a stale copy of " + w.Nodes[n].Label, intent, staleTable));
            }
        }

        // (d) one record of each kind lost.
        for (var n = 0; n < w.N; n++)
        {
            if (now[n] == null) continue;
            var docs = (string[])now.Clone();
            docs[n] = null;
            var lostDoc = TodayTable(w, docs, r.Memo);
            b.Get("d-document|0").Add(intent, before[0], lostDoc, D(w.Nodes[n].Label + "'s document lost", intent, lostDoc));
        }
        for (var m = 1; m < Models; m++)
        {
            var st = r.Store(m);
            var cur = Now(st);
            foreach (var g in cur.Grants)
            {
                var rc = Now(st); rc.Grants.Remove(rc.Grants.First(x => x.Id == g.Id));
                var t1 = Eval(w, IsMiddle(m), rc);
                b.Get("d-grant|" + m).Add(intent, before[m], t1, D("grant " + MaskText(g.Mask) + " of " + w.Profiles[g.Prof] + " at " + w.Nodes[g.Node].Label + " lost", intent, t1));
            }
            foreach (var x in cur.Ws)
            {
                var rc = Now(st); rc.Ws.RemoveAll(y => y.Id == x.Id);
                var t2 = Eval(w, IsMiddle(m), rc);
                b.Get("d-withdrawal|" + m).Add(intent, before[m], t2, D("withdrawal " + MaskText(x.Mask) + " of " + w.Profiles[x.Prof] + " at " + w.Nodes[x.Node].Label + " lost", intent, t2));
            }
            if (!IsMiddle(m)) continue;
            foreach (var mk in cur.Marks.Where(x => !Lifted(st, x)))
            {
                var id = mk.Id; var node = mk.Node;
                var both = Now(st); both.Marks.RemoveAll(y => y.Id == id);
                both.RefOf = i => i == node ? st.RefNow(i).Where(z => z != id) : st.RefNow(i);
                var t3 = Eval(w, true, both);
                b.Get("d-mark-both|" + m).Add(intent, before[m], t3, D("mark " + MaskText(mk.Mask) + " of " + w.Profiles[mk.Prof] + " at " + w.Nodes[mk.Node].Label + " and its reference lost", intent, t3));
                var rec = Now(st); rec.Marks.RemoveAll(y => y.Id == id);
                b.Get("d-mark-record|" + m).Add(intent, before[m], Eval(w, true, rec));
            }
            foreach (var l in cur.Lifts)
            {
                var rc = Now(st); rc.Lifts.RemoveAll(y => y.Id == l.Id);
                b.Get("d-lift|" + m).Add(intent, before[m], Eval(w, true, rc));
            }
        }

        // (e) two edits of the same node made apart, from the state before the last edit, then
        // both kept.
        var basePol = r.Pols[H - 1];
        var baseDocs = r.Today.At(H - 1);
        var alt = 0;
        foreach (var y in alternatives)
        {
            alt++;
            var polY = y.Apply(w, basePol);
            var tY = Intent(w, polY);
            var both = new ushort[intent.Length]; var either = new ushort[intent.Length];
            for (var i = 0; i < intent.Length; i++) { both[i] = (ushort)(intent[i] & tY[i]); either[i] = (ushort)(intent[i] | tY[i]); }

            // Whether any of the three policies holds a decision of the grant-only form's own kind,
            // which today's model expresses by a rule it rewrites when what arrives changes.
            var pure = !basePol.Mode.Concat(r.Pols[H].Mode).Concat(polY.Mode).Any(x => x == Adds);

            var docsY = TodayDocs(w, polY, tY);
            var merged = new string[w.N];
            for (var n = 0; n < w.N; n++)
            {
                var d0 = baseDocs[n]; var dx = now[n]; var dy = docsY[n];
                merged[n] = dx == d0 ? dy : dy == d0 ? dx : dx == dy ? dx : Meet(dx, dy);
            }
            var afterToday = TodayTable(w, merged, r.Memo);
            b.Get("e-both|0").Add(both, before[0], afterToday, D("apart: " + y.Text(w), both, afterToday));
            b.Get("e-either|0").Add(either, before[0], afterToday, D("apart: " + y.Text(w), either, afterToday));
            if (pure) { b.Get("e-both-pure|0").Add(both, before[0], afterToday); b.Get("e-either-pure|0").Add(either, before[0], afterToday); }

            for (var m = 1; m < Models; m++)
            {
                var st = r.Store(m);
                var other = Copy(st, H - 1, 1000000 * alt);
                var wr = new Written();
                if (!IsMiddle(m)) WriteRows(other, w, StrictRows(w, polY, tY), H, wr);
                else
                {
                    var rows = new int[w.N * w.Pn]; var marks = new int[w.N * w.Pn];
                    MiddleTargets(w, polY, rows, marks);
                    WriteRows(other, w, rows, H, wr);
                    WriteMarks(other, w, marks, H, wr);
                }
                // The union: every record either copy holds; a grant deleted in one copy and held
                // in the other is held, as a merge that applies no deletion leaves it.
                var held = new HashSet<int>(st.Grants.Concat(other.Grants).Where(g => g.Deleted < 0).Select(g => g.Id));
                var rc = new Records
                {
                    Grants = st.Grants.Concat(other.Grants).Where(g => held.Contains(g.Id)).GroupBy(g => g.Id).Select(g => g.First()).ToList(),
                    Ws = st.Ws.Concat(other.Ws).GroupBy(x => x.Id).Select(x => x.First()).ToList(),
                    Marks = st.Marks.Concat(other.Marks).GroupBy(x => x.Id).Select(x => x.First()).ToList(),
                    Lifts = st.Lifts.Concat(other.Lifts).GroupBy(x => x.Id).Select(x => x.First()).ToList(),
                    RefOf = i => st.RefNow(i).Concat(other.RefNow(i)),
                };
                var after = Eval(w, IsMiddle(m), rc);
                b.Get("e-both|" + m).Add(both, before[m], after, D("apart: " + y.Text(w), both, after));
                b.Get("e-either|" + m).Add(either, before[m], after, D("apart: " + y.Text(w), either, after));
                if (pure) { b.Get("e-both-pure|" + m).Add(both, before[m], after); b.Get("e-either-pure|" + m).Add(either, before[m], after); }
            }
        }
        b.Histories++;
    }

    // The alternatives to the last edit: in the sample, one of each kind at the same node, drawn;
    // in the small world, every one.
    static List<Act> Alternatives(Run r, Random rng, Alphabet al)
    {
        var w = r.W; var H = r.H;
        var x = r.Acts[H];
        var list = new List<Act>();
        for (var k = 0; k < KindCount; k++)
        {
            if (rng != null)
            {
                var a = Pick(w, r.Pols[H - 1], r.Ts[H - 1], rng, al, x.Node, (Kind)k, r.Pols[H]);
                if (a != null) list.Add(a);
                continue;
            }
            foreach (var a in Candidates(w, r.Pols[H - 1], r.Ts[H - 1], (Kind)k, al, x.Node))
            {
                var q = a.Apply(w, r.Pols[H - 1]);
                if (q.SameAs(r.Pols[H]) || Same(r.Ts[H - 1], Intent(w, q))) continue;
                list.Add(a);
            }
        }
        return list;
    }

    // =====================================================================================
    // The two worlds
    // =====================================================================================

    static void RunSample(Block b, int histories, int size, int edits, int seed)
    {
        var rng = new Random(seed);
        for (var h = 0; h < histories; h++)
        {
            var w = new World(Tree(rng, size), Profiles);
            var r = new Run(w);
            for (var s = 0; s < edits; s++)
            {
                var act = Pick(w, r.Pols[r.H], r.Ts[r.H], rng, SampleAlphabet, -1, null, null);
                if (act == null) break;
                Step(r, act, b);
            }
            Losses(r, b, Alternatives(r, rng, SampleAlphabet));
        }
    }

    // root, G under it, X and S in G, S1 in S: the design's base without H and Q.
    static World SmallWorld()
    {
        var root = new MNode { Label = "root", Group = true };
        var g = new MNode { Label = "G", Up = root, Group = true };
        var x = new MNode { Label = "X", Up = g };
        var s = new MNode { Label = "S", Up = g, Group = true };
        var s1 = new MNode { Label = "S1", Up = s };
        root.Kids.Add(g); g.Kids.Add(x); g.Kids.Add(s); s.Kids.Add(s1);
        return new World(new List<MNode> { root, g, x, s, s1 }, new[] { "agent" });
    }

    static void RunSmall(Block b, int length)
    {
        var w = SmallWorld();
        var path = new List<Act>();
        Walk(w, new Policy(w.N, w.Pn), path, length, b);
    }

    static void Walk(World w, Policy pol, List<Act> path, int length, Block b)
    {
        if (path.Count == length)
        {
            var r = new Run(w);
            foreach (var a in path) Step(r, a, b);
            Losses(r, b, Alternatives(r, null, SmallAlphabet));
            return;
        }
        var t = Intent(w, pol);
        for (var k = 0; k < KindCount; k++)
            foreach (var a in Candidates(w, pol, t, (Kind)k, SmallAlphabet, -1))
            {
                var q = a.Apply(w, pol);
                if (Same(t, Intent(w, q))) continue;
                path.Add(a);
                Walk(w, q, path, length, b);
                path.RemoveAt(path.Count - 1);
            }
    }

    // =====================================================================================
    // Control 1: the design's own trial 4, and trial 2 beside it, answered by this harness
    // =====================================================================================

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

    // The design's 18 answers per node and profile, in its order.
    static bool[] Answers18(World w, bool today)
    {
        var a = new bool[w.N * w.Pn * Verbs.Length * Reaches.Length];
        var i = 0;
        var read = new AclDocuments();
        foreach (var n in w.Nodes)
            for (var p = 0; p < w.Pn; p++)
                foreach (var v in Verbs)
                    foreach (var r in Reaches)
                        a[i++] = today ? AclResolver.Permits(n, w.One[p], v, r, read) : GrantOnly(n, w.Profiles[p], Bit(v), r);
        return a;
    }

    // The same trees through this harness's own 14 cells: today's from TodayTable, grant-only from
    // the rows the documents give, through GoTable.
    static bool[] Cells14(World w, bool today)
    {
        ushort[] t;
        if (today) t = TodayTable(w, w.Nodes.Select(n => n.Doc).ToArray(), null);
        else
        {
            var rows = new int[w.N * w.Pn];
            foreach (var n in w.Nodes) for (var p = 0; p < w.Pn; p++) { int m; if (n.Row.TryGetValue(w.Profiles[p], out m)) rows[n.Index * w.Pn + p] = m; }
            t = GoTable(w, rows, null, null, false);
        }
        var a = new bool[t.Length * CellCount];
        for (var i = 0; i < t.Length; i++) for (var c = 0; c < CellCount; c++) a[i * CellCount + c] = (t[i] & (1 << c)) != 0;
        return a;
    }

    static void RunDesignControl(int trees, int size, int seed)
    {
        var lossToday = new Tally(); var lossGo = new Tally(); var backToday = new Tally(); var backGo = new Tally();
        var lossToday14 = new Tally(); var lossGo14 = new Tally(); var backToday14 = new Tally(); var backGo14 = new Tally();
        var backGoUnexplained = 0;
        var rng = new Random(seed + 1);
        for (var t = 0; t < trees; t++)
        {
            var all = Tree(rng, size);
            Rule(rng, all, false);
            RowsFromDocs(all);
            var w = new World(all, Profiles);
            var today = Answers18(w, true); var go = Answers18(w, false);
            var today14 = Cells14(w, true); var go14 = Cells14(w, false);

            foreach (var n in all.Where(n => n.Doc != null).ToList())
            {
                // 2. one rule lost
                var doc = n.Doc; var row = new Dictionary<string, int>(n.Row);
                n.Doc = null; n.Row.Clear();
                lossToday.Add(today, Answers18(w, true)); lossGo.Add(go, Answers18(w, false));
                lossToday14.Add(today14, Cells14(w, true)); lossGo14.Add(go14, Cells14(w, false));

                // 4. one rule rolled back to another version (a random other document)
                var masks = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var p in Profiles) if (rng.NextDouble() < 0.5) masks[p] = rng.NextDouble() < 0.12 ? 0 : RandomMask(rng);
                var deny = rng.NextDouble() < 0.15;
                if (masks.Count > 0 || deny)
                {
                    n.Doc = DocOf(deny, masks);
                    foreach (var kv in masks) if (kv.Value != 0) n.Row[kv.Key] = kv.Value;
                    backToday.Add(today, Answers18(w, true));
                    var after = Answers18(w, false);
                    backGo.Add(go, after);
                    backToday14.Add(today14, Cells14(w, true)); backGo14.Add(go14, Cells14(w, false));
                    var widerHere = false;
                    for (var i = 0; i < go.Length; i++) if (!go[i] && after[i]) widerHere = true;
                    var explained = false;
                    foreach (var kv in n.Row) { int was; row.TryGetValue(kv.Key, out was); if ((kv.Value & ~was) != 0) explained = true; }
                    if (widerHere && !explained) backGoUnexplained++;
                }
                n.Doc = doc; n.Row.Clear(); foreach (var kv in row) n.Row[kv.Key] = kv.Value;
            }
        }
        Out.WriteLine("== control 1: the design's own trials, its generator and draws, " + trees.ToString(Inv) + " trees of " + size.ToString(Inv) + " nodes, seed " + seed.ToString(Inv) + " ==");
        Out.WriteLine("   recorded in grant-only\\design\\recorded\\model.txt, sections 2 and 4");
        Out.WriteLine("   the design's 18 answers per node and profile:");
        Out.WriteLine("   4. a random other document, today's model:  " + backToday);
        Out.WriteLine("   4. a random other document, grant-only:     " + backGo + "; wider without the older row holding a letter the newer lacks: " + backGoUnexplained.ToString(Inv));
        Out.WriteLine("   2. one rule lost, today's model:            " + lossToday);
        Out.WriteLine("   2. one rule lost, grant-only:               " + lossGo);
        Out.WriteLine("   this harness's 14 cells (strip and delete once each, so cells differ from answers):");
        Out.WriteLine("   4. a random other document, today's model:  " + backToday14.ToString().Replace("answers", "cells"));
        Out.WriteLine("   4. a random other document, grant-only:     " + backGo14.ToString().Replace("answers", "cells"));
        Out.WriteLine("   2. one rule lost, today's model:            " + lossToday14.ToString().Replace("answers", "cells"));
        Out.WriteLine("   2. one rule lost, grant-only:               " + lossGo14.ToString().Replace("answers", "cells"));
    }

    // =====================================================================================
    // Printing a block
    // =====================================================================================

    static void Print(Block b)
    {
        Out.WriteLine();
        Out.WriteLine("== " + b.Title + " ==");
        Out.WriteLine("   histories " + b.Histories.ToString(Inv) + ", edits " + b.Steps.ToString(Inv));
        Out.WriteLine("   models: today's; grant-only and its middle form, a row taken away whole withdrawn with the letters removed");
        Out.WriteLine("   (design 5.2); the same two, \"row whole\", with such a row withdrawn with all eight letters (design probe, scenario 04)");
        Out.WriteLine("   edits by kind: " + string.Join(", ", Enumerable.Range(0, KindCount).Select(k => KindName[k] + " " + b.KindActs[k].ToString(Inv)).ToArray()));
        Out.WriteLine();
        Out.WriteLine("   records written, by the kind of edit (today: documents changed; grant-only: grants, withdrawals, grants deleted;");
        Out.WriteLine("   middle form: grants, withdrawals, marks, lifts):");
        for (var k = 0; k < KindCount; k++)
            Out.WriteLine("   " + KindName[k].PadRight(11) + F(b.KindActs[k], 7) + " edits | today " + F(b.KindRecords[k, 0], 6)
                + " | grant-only " + F(b.KindRecords[k, 1], 6) + F(b.KindRecords[k, 2], 6) + F(b.KindRecords[k, 3], 6)
                + " | middle " + F(b.KindRecords[k, 4], 6) + F(b.KindRecords[k, 5], 6) + F(b.KindRecords[k, 6], 6) + F(b.KindRecords[k, 7], 6));

        Out.WriteLine();
        Out.WriteLine("   control 2, no loss: each model's table from its own store against the intent, after every edit");
        for (var m = 0; m < Models; m++)
        {
            var f = b.Fid[m];
            Out.WriteLine("   " + ModelName[m].PadRight(22) + " states " + f.States.ToString(Inv) + ": the intent exactly " + f.Exact.ToString(Inv)
                + ", narrower " + f.Narrower.ToString(Inv) + " (" + f.NarrowerCells.ToString(Inv) + " cells), wider " + f.Wider.ToString(Inv)
                + "; store unlike its own expression " + f.StoreMismatch.ToString(Inv));
            Out.WriteLine("   " + "".PadRight(22) + " states it cannot give exactly " + f.Inexpressible.ToString(Inv)
                + "; states not the intent although it could give it " + f.InexactYetExpressible.ToString(Inv));
        }
        Out.WriteLine("   by the kind of the last edit: states not the intent / edits that made an exact state inexact, per model");
        for (var k = 0; k < KindCount; k++)
            Out.WriteLine("   " + KindName[k].PadRight(11) + string.Join("", Enumerable.Range(0, 3).Select(m => " | " + ModelName[m] + " "
                + b.Fid[m].NotIntent[k].ToString(Inv) + " / " + b.Fid[m].TurnedInexact[k].ToString(Inv)).ToArray()));
        for (var m = 0; m < 3; m++)
            for (var k = 0; k < KindCount; k++)
                if (b.Fid[m].Example[k] != null) Out.WriteLine("   first example, " + ModelName[m] + ", " + KindName[k] + ": " + b.Fid[m].Example[k]);

        Section(b, "(a) the whole store as it was k edits ago, every k", "a");
        Out.WriteLine("   by k: trials, then wider than the intent for each model");
        for (var k = 1; k <= b.MaxK; k++)
        {
            var c0 = b.Get("a" + k.ToString(Inv) + "|0");
            Out.WriteLine("   k " + F(k, 2) + F(c0.Trials, 8) + string.Join("", Enumerable.Range(0, 3).Select(m => F(b.Get("a" + k.ToString(Inv) + "|" + m).Wider, 8)).ToArray()));
        }
        Section(b, "(b) one node's rule back to its state before its last change, the rest as it is", "b");
        Section(b, "(b) by the mechanism named, a stale copy of the node itself winning by time (what travels with the node goes back)", "b-node");
        Section(b, "(c) the newest records of a node lost, older records and deletions as they are (today: the newest version, the same documents as (b))", "c");
        foreach (var loss in new[] { "b", "c" })
        {
            Out.WriteLine();
            Out.WriteLine("   (" + loss + ") by the kind of the edit that made the node's last change in that model: trials / wider than the intent");
            for (var k = 0; k < KindCount; k++)
                Out.WriteLine("   " + KindName[k].PadRight(11) + string.Join("", Enumerable.Range(0, 3).Select(m =>
                {
                    var c = b.Get(loss + "-kind" + k.ToString(Inv) + "|" + m.ToString(Inv));
                    return " | " + ModelName[m] + " " + c.Trials.ToString(Inv) + " / " + c.Wider.ToString(Inv);
                }).ToArray()));
        }
        Out.WriteLine();
        Out.WriteLine("   (d) one record of each kind lost");
        Out.WriteLine("   " + "".PadRight(34) + CountHead);
        Out.WriteLine("   " + "".PadRight(34) + CountHead2);
        Row(b, "today's model, a document", "d-document|0");
        Row(b, "grant-only, a grant", "d-grant|1");
        Row(b, "grant-only, a withdrawal", "d-withdrawal|1");
        Row(b, "middle form, a grant", "d-grant|2");
        Row(b, "middle form, a withdrawal", "d-withdrawal|2");
        Row(b, "middle form, a mark and its ref", "d-mark-both|2");
        Row(b, "middle form, a mark, ref kept", "d-mark-record|2");
        Row(b, "middle form, a lift", "d-lift|2");
        Row(b, "grant-only, row whole, a grant", "d-grant|3");
        Row(b, "grant-only, row whole, withdrawal", "d-withdrawal|3");
        Row(b, "middle, row whole, a grant", "d-grant|4");
        Row(b, "middle, row whole, a withdrawal", "d-withdrawal|4");
        Row(b, "middle, row whole, mark and ref", "d-mark-both|4");
        Row(b, "middle, row whole, mark, ref kept", "d-mark-record|4");
        Row(b, "middle, row whole, a lift", "d-lift|4");
        Section(b, "(e) two edits of one node made apart, both kept; against what both allow", "e-both");
        Section(b, "(e) the same merges, against what either allows", "e-either");
        Section(b, "(e) only where no policy of the three holds a grant of the grant-only form's own kind; against what both allow", "e-both-pure");
        Section(b, "(e) the same, against what either allows", "e-either-pure");
        if (!b.Examples) return;
        Out.WriteLine();
        Out.WriteLine("   the first trial wider than the intent, for each loss and model: the edits | the loss => what differs from the intent");
        foreach (var key in new[] { "a", "b", "b-node", "c", "d-document", "d-grant", "d-withdrawal", "d-mark-both", "d-mark-record", "d-lift", "e-both", "e-either" })
            for (var m = 0; m < Models; m++)
            {
                Count c;
                if (b.Counts.TryGetValue(key + "|" + m.ToString(Inv), out c) && c.Example != null)
                    Out.WriteLine("   " + key + ", " + ModelName[m] + ": " + c.Example);
            }
    }

    static void Section(Block b, string title, string key)
    {
        Out.WriteLine();
        Out.WriteLine("   " + title);
        Out.WriteLine("   " + "".PadRight(34) + CountHead);
        Out.WriteLine("   " + "".PadRight(34) + CountHead2);
        for (var m = 0; m < Models; m++) Row(b, ModelName[m], key + "|" + m.ToString(Inv));
    }

    static void Row(Block b, string name, string key)
    {
        Out.WriteLine("   " + name.PadRight(34) + b.Get(key).Line());
    }

    static int Main(string[] args)
    {
        var path = args[0];
        var histories = int.Parse(args[1], Inv);
        var size = int.Parse(args[2], Inv);
        var edits = int.Parse(args[3], Inv);
        var seed = int.Parse(args[4], Inv);
        var small = int.Parse(args[5], Inv);
        using (Out = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            Out.WriteLine("rollback trial of two access rule models; today's answers: KeePassGate.Acl.AclResolver at a42e38aa");
            Out.WriteLine("arguments: " + string.Join(" ", args.Skip(1).ToArray()));
            Out.WriteLine();
            RunDesignControl(400, 30, 20261115);

            var sample = new Block
            {
                Title = "sample: " + histories.ToString(Inv) + " histories, trees of " + size.ToString(Inv) + " nodes (the design's generator), three profiles, "
                    + edits.ToString(Inv) + " edits each, seed " + seed.ToString(Inv),
            };
            RunSample(sample, histories, size, edits, seed);
            Print(sample);

            var smallBlock = new Block
            {
                Title = "exhaustive: every history of " + small.ToString(Inv) + " edits from nothing, root > G > {X, S > S1}, one profile, the small alphabet",
                Examples = true,
            };
            RunSmall(smallBlock, small);
            Print(smallBlock);
        }
        return 0;
    }
}
