using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Runs work on the calling thread with the backup and restore privileges enabled, as Git Bash
    /// run elevated enables them in every process it starts.
    /// </summary>
    /// <remarks>
    /// **On this thread alone, through a copy of the process's token**, because the unit tier runs
    /// fixtures side by side and a privilege enabled on the process would reach every test running
    /// beside this test. The copy is an impersonation token with both enabled, which the thread
    /// wears for the work and takes off again whatever the work did.
    ///
    /// **Only an account that holds them can enable them**, which is an elevated account: Windows takes
    /// both out of the token an administrator is given unelevated. So <see cref="Run"/> answers
    /// false having run nothing, and the test asking ignores itself with <see cref="NotHeld"/>.
    ///
    /// Its calls into Windows are its own, each made once here, rather than the shared `User32`'s,
    /// which are every call the tests make into the window manager.
    /// </remarks>
    internal static class BackupPrivileges
    {
        /// <summary>Why a test that needs the privileges did not run.</summary>
        public const string NotHeld =
            "the account holds neither the backup nor the restore privilege, which only an elevated account does";

        private static readonly string[] Names = { "SeBackupPrivilege", "SeRestorePrivilege" };

        /// <summary>
        /// Runs <paramref name="work"/> with the privileges as the process holds them, enabled or
        /// not, and answers true: what a test asking both ways is given beside <see cref="Run"/>.
        /// </summary>
        public static bool AsHeld(Action work)
        {
            work();
            return true;
        }

        /// <summary>
        /// Runs <paramref name="work"/> with both privileges enabled and answers true, or answers
        /// false, having run nothing, when the account does not hold them.
        /// </summary>
        public static bool Run(Action work)
        {
            using (var process = WindowsIdentity.GetCurrent(TokenAccessLevels.Duplicate | TokenAccessLevels.Query))
            {
                SafeAccessTokenHandle copy;
                // codeql[cs/call-to-unmanaged-code] the one call to it in the tests
                if (!Imported.DuplicateTokenEx(
                        process.AccessToken,
                        (uint)(TokenAccessLevels.AdjustPrivileges | TokenAccessLevels.Query | TokenAccessLevels.Impersonate),
                        IntPtr.Zero, SecurityImpersonation, TokenImpersonation, out copy))
                {
                    throw new Win32Exception();
                }

                using (copy)
                {
                    foreach (var name in Names)
                    {
                        if (!Enable(copy, name))
                        {
                            return false;
                        }
                    }

                    WindowsIdentity.RunImpersonated(copy, work);
                    return true;
                }
            }
        }

        private static bool Enable(SafeAccessTokenHandle token, string name)
        {
            var privilege = new TokenPrivilege { Count = 1, Attributes = PrivilegeEnabled };
            // codeql[cs/call-to-unmanaged-code] the one call to it in the tests
            if (!Imported.LookupPrivilegeValue(null, name, out privilege.Identifier))
            {
                throw new Win32Exception();
            }

            // codeql[cs/call-to-unmanaged-code] the one call to it in the tests
            if (!Imported.AdjustTokenPrivileges(token, false, ref privilege, 0, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception();
            }

            // Succeeding is not enabling: a privilege the token does not hold is passed over, and
            // only the last error tells.
            return Marshal.GetLastWin32Error() != NotAllAssigned;
        }

        private const int SecurityImpersonation = 2;
        private const int TokenImpersonation = 2;
        private const uint PrivilegeEnabled = 2;
        private const int NotAllAssigned = 1300;

        /// <summary>A `TOKEN_PRIVILEGES` holding one privilege, its identifier and what to do with it.</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivilege
        {
            public uint Count;
            public long Identifier;
            public uint Attributes;
        }

        // cspell:ignore advapi
        private static class Imported
        {
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed copies a token rather than its handle
            internal static extern bool DuplicateTokenEx(
                SafeAccessTokenHandle existing, uint access, IntPtr attributes, int level, int type,
                out SafeAccessTokenHandle copy);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed names a privilege's identifier
            internal static extern bool LookupPrivilegeValue(string system, string name, out long identifier);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed enables a privilege
            internal static extern bool AdjustTokenPrivileges(
                SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
                ref TokenPrivilege state, int length, IntPtr previous, IntPtr returned);
        }
    }
}
