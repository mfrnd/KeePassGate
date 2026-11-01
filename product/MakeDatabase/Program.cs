using System;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace MakeDatabase
{
    /// <summary>
    /// `MakeDatabase.exe &lt;path.kdbx&gt;`: a new database whose master password is `dummy`, which
    /// is what the scripts open it with on the command line. Run beside a copy of `KeePass.exe`.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("usage: MakeDatabase <path.kdbx>");
                return 2;
            }

            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword("dummy"));

            var database = new PwDatabase();
            database.New(IOConnectionInfo.FromPath(args[0]), key);
            database.Name = "Mono probe";

            var group = new PwGroup(true, true, "Probe", PwIcon.Folder);
            database.RootGroup.AddGroup(group, true);
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "Probe entry"));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "probe"));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "not a secret"));
            group.AddEntry(entry, true);

            database.Save(null);
            database.Close();
            Console.WriteLine("made " + args[0]);
            return 0;
        }
    }
}
