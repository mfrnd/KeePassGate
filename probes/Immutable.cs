using System;
using System.Linq;
using System.Reflection;
using KeePassLib.Security;
static class Immutable
{
    static void Main()
    {
        foreach (var t in new[] { typeof(ProtectedString), typeof(ProtectedBinary) })
        {
            Console.WriteLine("== " + t.Name);
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Console.WriteLine("  field " + f.Name + " : " + f.FieldType.Name + (f.IsInitOnly ? " readonly" : " WRITABLE"));
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                               .Where(m => !m.Name.StartsWith("get_")).OrderBy(m => m.Name))
                Console.WriteLine("  method " + (m.IsPublic ? "public " : "private ") + m.Name);
        }
    }
}
