using System;
using KeePassLib;
using KeePassLib.Collections;
static class NullAdd
{
    static void Main()
    {
        Try("Add", () => new PwObjectList<PwEntry>().Add((PwEntry)null));
        Try("Insert", () => new PwObjectList<PwEntry>().Insert(0, null));
        Try("AddEntry", () => new PwGroup(true, true).AddEntry(null, true));
        Try("AddGroup", () => new PwGroup(true, true).AddGroup(null, true));
        Try("Add(list)", () => new PwObjectList<PwEntry>().Add((PwObjectList<PwEntry>)null));
        var f = typeof(PwObjectList<PwEntry>).GetField("m_vObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Console.WriteLine("m_vObjects: " + (f == null ? "none" : f.FieldType.FullName));
    }
    static void Try(string what, Action a)
    {
        try { a(); Console.WriteLine(what + ": accepted"); }
        catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name); }
    }
}
