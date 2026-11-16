using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KeePassLib;
using KeePassLib.Interfaces;
using KeePassLib.Security;
using KeePassLib.Serialization;

// Critique probe for option "rules-linked", lens: security and fit. It reuses the design's own
// model (Model.cs is the design's probe, verbatim but for "partial" and the name of its Main), so
// what is measured is the design as its author implemented it, and adds five cases:
//   plant-confirm  a foreign file plants a rule for an entry and supplies the history item that
//                  "confirms" it; then the tab's Confirm press is simulated
//   midsave        a group's rule change applied from inside a save's progress callback, as a
//                  dialog's OK handled while MainForm.SaveDatabase pumps messages would be
//   paste          Ctrl+A, Ctrl+C in a sealed group, Ctrl+V in an open group (the clipboard path)
//   template       a ruled templates group: its link entry chosen from the template menu
//   otherdb        a ruled entry and a ruled group copied into another database
//   delgroup       a ruled group holding only its link removed the way the API's permanent path
//                  removes a group (one deletion record, for the group), then synchronized
// Invented content only, throwaway files under the work directory. No window.
static partial class P
{
    sealed class MidSave : IStatusLogger
    {
        public int Calls; public int At; public Action Act; public bool Done;
        public void StartLogging(string s, bool b) { }
        public void EndLogging() { }
        public bool SetProgress(uint u) { Calls++; if (!Done && Calls >= At) { Done = true; Act(); } return true; }
        public bool SetText(string s, LogStatusType t) { return true; }
        public bool ContinueWork() { return true; }
    }

    static string AgentAt(PwDatabase db, PwEntry e)
    {
        string own; var v = Read(db); var r = Resolve(v, e, out own);
        return r + (own.Length > 0 ? " [" + own + "]" : "");
    }

    static bool HistoryCarries(PwEntry e, string anchor)
    {
        foreach (var h in e.History) if (Get(h, AnchorF) == anchor) return true;
        return false;
    }

    static PwEntry LinkIn(PwGroup g)
    {
        foreach (var e in g.Entries) if (Get(e, KindF) == "link") return e;
        return null;
    }

    // ---- plant then Confirm ----

    static void RunPlantConfirm(string[] xc)
    {
        Out.WriteLine();
        Out.WriteLine("== plant-confirm: a foreign file brings a rule for entry Q (under the seal S, no rule) and entry Y (in U, agent read,list), each with an OLDER copy of the entry carrying the planted anchor ==");
        var dir = Path.Combine(Work, "plant-confirm"); Directory.CreateDirectory(dir);
        var victim = Path.Combine(dir, "victim.kdbx");
        MakeBase(victim, RXN);
        var f = NewDb("foreign-new.kdbx");
        f.RootGroup.Uuid = Fixed("foreign root"); f.RootGroup.Name = "foreign";
        var aQ = Hex(Fixed("anchor planted-Q2")); var aY = Hex(Fixed("anchor planted-Y2"));
        var q = AddEntry(f.RootGroup, Fixed("entry Q"), "Q", At(-100)); Set(q, AnchorF, aQ);
        var y = AddEntry(f.RootGroup, Fixed("entry Y"), "Y", At(-100)); Set(y, AnchorF, aY);
        AddVersion(f, aQ, "entry/" + Hex(Fixed("entry Q")), RXW, new string[0], "plant-Q2", At(50), true);
        AddVersion(f, aY, "entry/" + Hex(Fixed("entry Y")), RXW, new string[0], "plant-Y2", At(50), true);
        var fpath = Path.Combine(dir, "foreign.kdbx"); SaveTo(f, fpath);
        var b = Open(victim);
        Out.WriteLine("  victim before: Q " + AgentAt(b, E(b, "Q")) + "; Y " + AgentAt(b, E(b, "Y")));
        b.Close();
        foreach (var m in new[] { PwMergeMethod.Synchronize, PwMergeMethod.KeepExisting, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting })
        {
            var t = Open(victim); var src = Open(fpath);
            t.MergeIn(src, m); src.Close();
            var res = Path.Combine(dir, "res-" + m + ".kdbx"); SaveTo(t, res);
            Report("KeePassLib " + m, res, aQ, aY);
        }
        for (var i = 0; i + 1 < xc.Length; i += 2)
        {
            var cell = Path.Combine(dir, "xc" + xc[i]); Directory.CreateDirectory(cell);
            var target = Path.Combine(cell, "target.kdbx"); var source = Path.Combine(cell, "source.kdbx");
            File.Copy(victim, target, true); File.Copy(fpath, source, true);
            var ex = Cli(xc[i + 1], cell, "merge", "-s", target, source);
            Report("keepassxc-cli " + xc[i] + " merge -s (exit " + ex + ")", target, aQ, aY);
        }
    }

    static void Report(string how, string res, string aQ, string aY)
    {
        var r = Open(res);
        var q = E(r, "Q"); var y = E(r, "Y");
        Out.WriteLine("  " + how + ":");
        Out.WriteLine("    after the merge: Q " + AgentAt(r, q) + ", Q carries the planted anchor " + (Get(q, AnchorF) == aQ) + ", Q's history carries it " + HistoryCarries(q, aQ) + ", Q's password is the victim's " + (q.Strings.ReadSafe(PwDefs.PasswordField) == "q-secret-0"));
        Out.WriteLine("                     Y " + AgentAt(r, y) + ", Y carries the planted anchor " + (Get(y, AnchorF) == aY) + ", Y's history carries it " + HistoryCarries(y, aY) + ", Y's password is the victim's " + (y.Strings.ReadSafe(PwDefs.PasswordField) == "y-secret-0"));
        // The tab's Confirm: the anchor of the rule that names the node written back onto the entry,
        // as an edit (the dialog's OK makes one revision and moves the time).
        if (Get(q, AnchorF) != aQ) Edit(q, At(300), e => Set(e, AnchorF, aQ));
        if (Get(y, AnchorF) != aY) Edit(y, At(300), e => Set(e, AnchorF, aY));
        var c = res + ".confirmed.kdbx"; SaveTo(r, c);
        var rc = Open(c);
        Out.WriteLine("    after Confirm:   Q " + AgentAt(rc, E(rc, "Q")) + " (password the victim's " + (E(rc, "Q").Strings.ReadSafe(PwDefs.PasswordField) == "q-secret-0") + ")"
            + "; Y " + AgentAt(rc, E(rc, "Y")) + " (password the victim's " + (E(rc, "Y").Strings.ReadSafe(PwDefs.PasswordField) == "y-secret-0") + ")");
        rc.Close();
    }

    // ---- a group's rule change landing inside a save ----

    static void MakeBaseLast(string path)
    {
        var db = NewDb("base-last-new.kdbx");
        var u = AddGroup(db.RootGroup, "U", Fixed("group U"), T0);
        var x = AddEntry(u, Fixed("entry X"), "X", T0); Password(x, "x-secret-0");
        var y = AddEntry(u, Fixed("entry Y"), "Y", T0); Password(y, "y-secret-0");
        var z = AddEntry(u, Fixed("entry Z"), "Z", T0); Password(z, "z-secret-0");
        var s = AddGroup(db.RootGroup, "S", Fixed("group S"), T0);
        var c = AddGroup(s, "C", Fixed("group C"), T0);
        var q = AddEntry(c, Fixed("entry Q"), "Q", T0); Password(q, "q-secret-0");
        var w = AddGroup(db.RootGroup, "W", Fixed("group W"), T0);
        var vv = AddEntry(w, Fixed("entry V"), "V", T0); Password(vv, "v-secret-0");
        FirstRule(db, u, RG0, "base-U", T0);
        FirstRule(db, s, SEAL, "base-S", T0);
        FirstRule(db, x, RXN, "base-X", T0, false);
        SaveTo(db, path);
    }

    static void RunMidSave()
    {
        Out.WriteLine();
        Out.WriteLine("== midsave: U's rule changed from agent read,list to agent nothing, applied from inside the save's progress callback while the serializer is writing W's first entry; the plugin marks the database modified ==");
        foreach (var first in new[] { true, false })
        {
            var dir = Path.Combine(Work, "midsave-" + (first ? "first" : "last")); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "db.kdbx");
            if (first) MakeBase(path, RXN); else MakeBaseLast(path);
            var db = Open(path);
            var order = new List<string>(); foreach (var g in db.RootGroup.Groups) order.Add(g.Name);
            var list = new List<PwEntry>(); Walk(db.RootGroup, e => list.Add(e));
            var w = G(db, "W"); var idx = -1;
            for (var i = 0; i < list.Count; i++) if (ReferenceEquals(list[i].ParentGroup, w)) { idx = i; break; }
            var anchorU = Get(LinkIn(G(db, "U")), AnchorF);
            Out.WriteLine();
            Out.WriteLine("  root's groups in order: " + string.Join(", ", order.ToArray()) + "; trigger after entry " + (idx + 1).ToString(Inv) + " of " + list.Count.ToString(Inv));
            Out.WriteLine("  before:            Y " + AgentAt(db, E(db, "Y")));
            var lg = new MidSave { At = idx + 1, Act = () => { ChangeRule(db, anchorU, RXN, "mid-U", At(30)); db.Modified = true; } };
            string err = null;
            try { db.Save(lg); } catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
            Out.WriteLine("  save: " + (err ?? "completed") + "; the change ran " + lg.Done + "; Modified after the save " + db.Modified);
            Out.WriteLine("  in memory:         Y " + AgentAt(db, E(db, "Y")));
            db.Close();
            var r = Open(path);
            Out.WriteLine("  the file reopened: Y " + AgentAt(r, E(r, "Y")) + "  || " + State(r));
            r.Close();
        }
    }

    // ---- copies the design did not run ----

    static void RunPaste()
    {
        Out.WriteLine();
        Out.WriteLine("== paste: every entry of the sealed group S selected (its link and entry P), copied and pasted into U (opened to agent read,list) through KdbxFile.WriteEntries and ReadEntries, the path of KeePass's Copy and Paste Entries ==");
        var dir = Path.Combine(Work, "paste"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, RXN);
        var db = Open(path);
        var p = AddEntry(G(db, "S"), Fixed("entry P"), "P", T0); Password(p, "p-secret-0");
        Out.WriteLine("  before: " + State(db));
        var sel = new List<PwEntry>(); foreach (var e in G(db, "S").Entries) sel.Add(e);
        using (var ms = new MemoryStream())
        {
            KdbxFile.WriteEntries(ms, db, sel.ToArray());
            using (var rd = new MemoryStream(ms.ToArray()))
                foreach (var e in KdbxFile.ReadEntries(rd, db, true)) G(db, "U").AddEntry(e, true, true);
        }
        var res = Path.Combine(dir, "after.kdbx"); SaveTo(db, res);
        var r = Open(res);
        Out.WriteLine("  after:  " + State(r));
        r.Close();
    }

    static void RunTemplate()
    {
        Out.WriteLine();
        Out.WriteLine("== template: group T is the templates group and is sealed, so its link entry sits among the templates; the operator picks it from Add Entry's template menu (PwEntry.Duplicate, History.Clear, as EntryTemplates.OnMenuExecute) and fills it in, in group U ==");
        var dir = Path.Combine(Work, "template"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, RXN);
        var db = Open(path);
        var t = AddGroup(db.RootGroup, "Templates", Fixed("group T"), T0);
        var tpl = AddEntry(t, Fixed("entry tpl"), "Web login", T0);
        FirstRule(db, t, SEAL, "tpl-T", T0);
        db.EntryTemplatesGroup = t.Uuid;
        var menu = new List<string>(); foreach (var e in t.Entries) menu.Add(e.Strings.ReadSafe(PwDefs.TitleField));
        Out.WriteLine("  the template menu lists: " + string.Join(", ", menu.ToArray()));
        Out.WriteLine("  before: " + State(db));
        var pe = LinkIn(t).Duplicate(); pe.History.Clear();
        Set(pe, PwDefs.TitleField, "bank login"); Password(pe, "bank-secret");
        G(db, "U").AddEntry(pe, true, true);
        var res = Path.Combine(dir, "after.kdbx"); SaveTo(db, res);
        var r = Open(res);
        PwEntry made = null; foreach (var e in G(r, "U").Entries) if (e.Strings.ReadSafe(PwDefs.TitleField) == "bank login") made = e;
        Out.WriteLine("  after:  " + State(r));
        Out.WriteLine("  the new entry 'bank login' carries KPG.Kind=" + Get(made, KindF) + ", so the design's guard leaves it out of every route; its identifier is its KPG.Anchor " + (Hex(made.Uuid) == Get(made, AnchorF)));
        r.Close();
    }

    static void RunOtherDb()
    {
        Out.WriteLine();
        Out.WriteLine("== otherdb: entry X (ruled) pasted into another database, and group U (ruled, with its link) copied there as the group clipboard does (new identifiers throughout) ==");
        var dir = Path.Combine(Work, "otherdb"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, RXN);
        var src = Open(path);
        var db2 = NewDb("second-new.kdbx");
        var h = AddGroup(db2.RootGroup, "H", Fixed("db2 group H"), T0);
        FirstRule(db2, h, RG0, "db2-H", T0);
        var keep = AddEntry(h, Fixed("db2 entry K"), "K", T0); Password(keep, "k-secret");
        using (var ms = new MemoryStream())
        {
            KdbxFile.WriteEntries(ms, src, new[] { E(src, "X") });
            using (var rd = new MemoryStream(ms.ToArray()))
                foreach (var e in KdbxFile.ReadEntries(rd, db2, true)) h.AddEntry(e, true, true);
        }
        var u2 = G(src, "U").Duplicate(); u2.Name = "U copied"; db2.RootGroup.AddGroup(u2, true);
        src.Close();
        var res = Path.Combine(dir, "second.kdbx"); SaveTo(db2, res);
        var r = Open(res);
        Out.WriteLine("  the second database: " + State(r));
        r.Close();
    }

    static void RunDelGroup()
    {
        Out.WriteLine();
        Out.WriteLine("== delgroup: group R (ruled, holding only its link) removed in copy A the way KdbxVault's permanent path removes an empty group (one deletion record, the group's); B unchanged; then synchronized ==");
        var dir = Path.Combine(Work, "delgroup"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase(basePath, RXN);
        var bdb = Open(basePath);
        var rg = AddGroup(bdb.RootGroup, "R", Fixed("group R"), T0);
        FirstRule(bdb, rg, RG0, "base-R", T0);
        SaveTo(bdb, basePath);
        foreach (var both in new[] { false, true })
        {
            var a = Path.Combine(dir, both ? "A-both.kdbx" : "A-group.kdbx"); var b = Path.Combine(dir, "B.kdbx");
            var da = Open(basePath);
            var g = G(da, "R"); var link = LinkIn(g);
            da.RootGroup.Groups.Remove(g);
            da.DeletedObjects.Add(new PwDeletedObject(g.Uuid, At(20)));
            if (both) da.DeletedObjects.Add(new PwDeletedObject(link.Uuid, At(20)));
            SaveTo(da, a);
            File.Copy(basePath, b, true);
            foreach (var into in new[] { "A", "B" })
            {
                var t = Open(into == "A" ? a : b); var s = Open(into == "A" ? b : a);
                t.MergeIn(s, PwMergeMethod.Synchronize); s.Close();
                var res = Path.Combine(dir, "res-" + (both ? "both" : "group") + "-into" + into + ".kdbx"); SaveTo(t, res);
                var rr = Open(res);
                Out.WriteLine("  deletion records for " + (both ? "the group and its link" : "the group only") + ", Synchronize into " + into + ": group R " + (G(rr, "R") != null ? "is back, holding " + G(rr, "R").Entries.UCount.ToString(Inv) + " entry" : "is gone"));
                rr.Close();
            }
        }
    }

    static void RunDedup()
    {
        Out.WriteLine();
        Out.WriteLine("== dedup: group U (opened to agent read,list, with its link and X, Y, Z) duplicated beside itself as Duplicate Group does (PwGroup.Duplicate, added to the same parent), then KeePass's Delete Duplicate Entries ==");
        var dir = Path.Combine(Work, "dedup"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, RXN);
        var db = Open(path);
        var u2 = G(db, "U").Duplicate(); u2.Name = "U - Copy"; db.RootGroup.AddGroup(u2, true);
        Out.WriteLine("  after Duplicate Group: " + State(db));
        var n = db.DeleteDuplicateEntries(null);
        var link = LinkIn(G(db, "U"));
        Out.WriteLine("  Delete Duplicate Entries deleted " + n.ToString(Inv) + "; U still holds " + G(db, "U").Entries.UCount.ToString(Inv) + " entries, its own link " + (link != null ? "kept" : "gone") + "; U - Copy holds " + u2.Entries.UCount.ToString(Inv));
        var res = Path.Combine(dir, "after.kdbx"); SaveTo(db, res);
        var r = Open(res);
        Out.WriteLine("  after:  " + State(r));
        r.Close();
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        if (args.Length > 2 && args[2] == "dedup")
        {
            using (Out = new StreamWriter(args[1], false, new UTF8Encoding(false))) RunDedup();
            return 0;
        }
        using (Out = new StreamWriter(args[1], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("critique probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString + "; the design's model from rules-linked\\probe\\Program.cs; profile read: " + Profile);
            var xc = new string[args.Length - 2]; Array.Copy(args, 2, xc, 0, xc.Length);
            RunPlantConfirm(xc);
            RunMidSave();
            RunPaste();
            RunTemplate();
            RunOtherDb();
            RunDelGroup();
        }
        return 0;
    }
}
