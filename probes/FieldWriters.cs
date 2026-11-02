// Every method in KeePass.exe that calls one of the in-place mutators of an entry's dictionaries.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using KeePassLib;
using KeePassLib.Collections;

static class FieldWriters
{
    static readonly Dictionary<short, OpCode> Codes = new Dictionary<short, OpCode>();

    static void Main(string[] args)
    {
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var c = (OpCode)f.GetValue(null);
            Codes[c.Value] = c;
        }

        var wanted = args.Length > 0 ? new HashSet<string>(args) : new HashSet<string>
        {
            "ProtectedStringDictionary.Set", "ProtectedStringDictionary.Remove", "ProtectedStringDictionary.Clear",
            "ProtectedStringDictionary.EnableProtection", "ProtectedStringDictionary.CopyTo",
            "ProtectedBinaryDictionary.Set", "ProtectedBinaryDictionary.Remove", "ProtectedBinaryDictionary.Clear",
            "StringDictionaryEx.Set", "StringDictionaryEx.Remove",
            "PwEntry.AddTag", "PwEntry.RemoveTag",
        };

        var asm = typeof(PwEntry).Assembly;
        var hits = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        Type[] types;
        try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

        foreach (var t in types)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var methods = t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all));
            foreach (var m in methods)
            {
                byte[] il;
                try { var body = m.GetMethodBody(); if (body == null) continue; il = body.GetILAsByteArray(); }
                catch (Exception) { continue; }
                foreach (var callee in Stores(m, il))
                {
                    var name = callee.DeclaringType == null ? callee.Name : callee.DeclaringType.Name + "." + callee.Name;
                    if (!wanted.Contains(name)) continue;
                    SortedSet<string> set;
                    if (!hits.TryGetValue(name, out set)) hits[name] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(t.FullName + "::" + m.Name);
                }
            }
        }

        foreach (var p in hits)
        {
            Console.WriteLine("== " + p.Key + " (" + p.Value.Count + " callers)");
            foreach (var c in p.Value) Console.WriteLine("  " + c);
        }
    }

    static IEnumerable<FieldInfo> Stores(MethodBase m, byte[] il)
    {
        var found = new List<FieldInfo>();
        var targs = m.DeclaringType != null && m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
        var margs = m.IsGenericMethod ? m.GetGenericArguments() : null;
        int i = 0;
        while (i < il.Length)
        {
            OpCode op;
            short v = il[i];
            if (v == 0xFE) { v = (short)(0xFE00 | il[i + 1]); i += 2; } else i += 1;
            if (!Codes.TryGetValue(v, out op)) break;
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, i); break;
                default: size = 4; break;
            }
            if (op.OperandType == OperandType.InlineField && (op.Value == OpCodes.Stfld.Value || op.Value == OpCodes.Ldflda.Value))
            {
                try { found.Add(m.Module.ResolveField(BitConverter.ToInt32(il, i), targs, margs)); } catch (Exception) { }
            }
            i += size;
        }
        return found;
    }
}
