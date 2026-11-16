// The database of the desktop-hide-and-lock family, made over KeePassLib 2.62 with invented
// content: the group KeePassGate (searching and auto-type off) holding a dozen write-once store
// entries, two of them in a subgroup and one moved into the recycle bin, beside ordinary groups and
// entries; the last selected group stored in the file is KeePassGate.
//
//   hlbase <path> [top|bin|deep] [new]
//
// top puts KeePassGate under the root, bin under the recycle bin, deep under
// Passwords/Old/2019/Misc. With new, one more store entry, S13, is added: the other side of a
// synchronize that brings a new store entry.
using System;
using KeePassLib;
using KeePassLib.Security;

static partial class Desk
{
    static void HlBase(string path, string place, bool withNew)
    {
        var pd = NewDatabase(path, "Desk store (invented)", "Desk store");
        var pw = NewGroup(pd.RootGroup, "PW", "Passwords", At(5));
        var bin = Bin(pd);
        PwGroup parent = pd.RootGroup;
        if (place == "bin") parent = bin;
        if (place == "deep")
        {
            var old = NewGroup(pw, "Old", "Old", At(5));
            var y = NewGroup(old, "Y2019", "2019", At(5));
            parent = NewGroup(y, "Misc", "Misc", At(5));
        }
        var kg = NewGroup(parent, "KG", "KeePassGate", At(5));
        kg.EnableSearching = false;
        kg.EnableAutoType = false;
        var kga = NewGroup(kg, "KGA", "Archive", At(6));
        NewGroup(pd.RootGroup, "EG", "Empty group", At(5));

        Func<PwGroup, string, string, int, PwEntry> store = (g, alias, kind, minute) =>
        {
            var e = NewEntry(pd, g, alias, "KPG " + kind, At(minute));
            Str(e, "KPG.Kind", false, kind);
            Str(e, "KPG.Body", false, "{\"kind\":\"" + kind + "\",\"n\":\"" + alias + "\",\"zqx\":\"zqxstore-" + alias.ToLowerInvariant() + "\"}");
            Str(e, "KPG.Secret", true, "zqxsecret-" + alias.ToLowerInvariant());
            return e;
        };
        var s1 = store(kg, "S1", "grant 1", 10);
        Std(pd, s1, "zqxstore-user1", "zqxstore-pw1", "", "zqxstore-notes1");
        var s2 = store(kg, "S2", "grant 2", 11);
        s2.Expires = true;
        s2.ExpiryTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        store(kg, "S3", "admission 1", 12);
        store(kg, "S4", "admission 2", 13);
        store(kg, "S5", "revocation 1", 14);
        var s6 = store(kg, "S6", "profile 1", 15);
        s6.Tags.Add("kpgtag");
        store(kg, "S7", "authority 1", 16);
        store(kg, "S8", "retirement 1", 17);
        store(kga, "S9", "grant 9", 18);
        store(kga, "S10", "admission 10", 19);
        var s11 = store(bin, "S11", "grant 11", 20);
        s11.PreviousParentGroup = kg.Uuid;
        store(kg, "S12", "profile 12", 21);
        if (withNew) store(kg, "S13", "grant 13", 600);

        var u1 = NewEntry(pd, pw, "U1", "Mail account", Day(2025, 6, 1));
        Std(pd, u1, "alice-invented", "u1-v1", "https://mail.example.invalid/", "");
        Version(u1, Day(2026, 8, 1), () => Std(pd, u1, "alice-invented", "u1-v2", "https://mail.example.invalid/", ""));
        u1.Tags.Add("kpgtag");
        var u2 = NewEntry(pd, pw, "U2", "Bank", At(30));
        Std(pd, u2, "bob-invented", "u2-pw", "", "");
        var u3 = NewEntry(pd, pw, "U3", "Linked", At(31));
        var r = s1.Uuid.ToHexString();
        Std(pd, u3, "{REF:U@I:" + r + "}", "{REF:P@I:" + r + "}", "https://example.invalid/{REF:U@I:" + r + "}", "{REF:N@I:" + r + "}");
        u3.OverrideUrl = "https://override.example.invalid/{REF:P@I:" + r + "}";

        pd.LastSelectedGroup = kg.Uuid;
        pd.LastTopVisibleGroup = pd.RootGroup.Uuid;
        SaveClose(pd, path);
    }
}
