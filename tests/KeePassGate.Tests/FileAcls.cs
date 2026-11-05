using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The access control lists an administrator puts on the files the plugin writes for itself,
    /// for the tests of what it does under them.
    /// </summary>
    internal static class FileAcls
    {
        /// <summary>The account these tests run as, which is the one an ACL here grants or refuses.</summary>
        public static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User;

        /// <summary>
        /// What an administrator's append only ACL gives the account: the right to append, and the
        /// synchronize right Windows asks for on every open. Read attributes, which it asks for as
        /// well, comes from the directory here, since the account may list it.
        /// </summary>
        public const FileSystemRights AppendOnly = FileSystemRights.AppendData | FileSystemRights.Synchronize;

        /// <summary>
        /// Gives the current account exactly these rights on a file and nothing inherited.
        /// </summary>
        /// <remarks>
        /// Written as SDDL rather than as a `FileSystemAccessRule`, which adds the synchronize right
        /// to every rule that allows anything: a rule for the append right alone is stored as
        /// append and synchronize, and hides the very thing an ACL written by hand gets wrong.
        /// </remarks>
        public static void Allow(string path, FileSystemRights rights)
        {
            var security = new FileSecurity();
            security.SetSecurityDescriptorSddlForm(
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, "D:P(A;;0x{0:X};;;{1})", (int)rights, Me.Value),
                AccessControlSections.Access);
            File.SetAccessControl(path, security);
        }

        /// <summary>
        /// Leaves the current account able to list a directory and open what is in it, and not to
        /// add, remove or rename anything there but for what <paramref name="also"/> gives back.
        /// </summary>
        public static void Seal(string directory, FileSystemRights also = 0)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                Me,
                FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes | FileSystemRights.Traverse
                    | FileSystemRights.Synchronize | also,
                AccessControlType.Allow));
            Directory.SetAccessControl(directory, security);
        }

        /// <summary>
        /// Gives the current account everything back on the directory and on whatever is in it, so
        /// that it can be deleted. The account owns all of it, which is what lets it do this after
        /// granting itself so little.
        /// </summary>
        public static void Unseal(string directory)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                Me, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(directory, security);

            foreach (var file in Directory.GetFiles(directory))
            {
                Allow(file, FileSystemRights.FullControl);
            }
        }
    }
}
