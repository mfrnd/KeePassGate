using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The grant editor's sheet as a test builds it: a node's rules as `Rules` writes them, what
    /// arrives from a group above granting each profile its mask, and the database's names.
    /// </summary>
    internal static class Sheets
    {
        /// <summary>The names every sheet here is given unless a test names its own.</summary>
        public static readonly string[] Names = { "reader", "ops", "deploy" };

        /// <summary>What arrives under a group granting each profile its mask, or nothing for no pair.</summary>
        public static AclInheritance Arriving(params (string Profile, string Mask)[] rules)
        {
            return rules == null || rules.Length == 0
                ? AclInheritance.None
                : AclResolver.Above(Above(rules));
        }

        /// <summary>A group granting each profile its mask, for what arrives under it.</summary>
        public static TestNode Above(params (string Profile, string Mask)[] rules)
        {
            return TestNode.Root("a group above").Holding(Rules.Node(false, rules));
        }

        /// <summary>A sheet over these rules, with this arriving and the usual names.</summary>
        public static GrantSheet Of(NodeRule rule, AclInheritance above)
        {
            return GrantSheet.Of(rule, above, Names, null);
        }

        /// <summary>The rows of a sheet as text, the way a test reads them most easily.</summary>
        public static IList<string> Lines(GrantSheet sheet)
        {
            return sheet.Rows.Select(row => row.Profile + " " + row.Mask + " " + Kind(row)).ToList();
        }

        private static string Kind(GrantRow row)
        {
            return row.Kind + (row.States.Count == 0 ? string.Empty : ":" + string.Join(",", row.States))
                   + (row.Changed ? " changed" : string.Empty);
        }

        /// <summary>The single row of a profile, asserted to be the only row of the profile.</summary>
        public static GrantRow Single(GrantSheet sheet, string profile)
        {
            var rows = sheet.Rows.Where(row => string.Equals(row.Profile, profile, StringComparison.Ordinal)).ToList();
            if (rows.Count != 1)
            {
                throw new InvalidOperationException(profile + " shows " + rows.Count + " rows: " + string.Join("; ", Lines(sheet)));
            }

            return rows[0];
        }
    }
}
