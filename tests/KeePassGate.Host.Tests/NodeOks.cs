using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A press on the KPG tab of a group's dialog in a real database, and its OK, as the product
    /// writes it, with no dialog shown: for a host fixture that changes rules the way an operator
    /// does, the write above among them.
    /// </summary>
    internal static class NodeOks
    {
        /// <summary>
        /// The tab's sheet over a reading of the database, with what arrives and the versions
        /// below as the tab reads them, `NodeDialogs`; the press; and the OK, composed by
        /// `RuleActs` over the reading the store's own write is planned against, the group's
        /// parent and the tree of that turn made from its rules. True where the OK wrote anything,
        /// and false where the press was refused or left nothing to write.
        /// </summary>
        public static bool Pressed(PwDatabase database, PwGroup group, Func<GrantSheet, bool> press)
        {
            var id = DatabaseRules.Id(group.Uuid);
            var store = new KdbxStore(database, GateVersion.Current, NullGateLog.Instance);
            GrantSheet sheet;
            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), "a press is made on a whole store: " + reading.Reason);
                var rules = StoredRules.Of(reading);
                var parent = group.ParentGroup;
                sheet = GrantSheet.Of(
                    rules.At(id), AclResolver.Above(parent == null ? null : KdbxNodes.Of(parent, rules)), ProfileNames.Of(reading).Names, null);
                sheet.TakeBelow(rules.Below(id, new DatabaseNodes(database).Tree(reading, rules, group)));
            }

            if (!press(sheet))
            {
                return false;
            }

            var changes = sheet.Changes;
            if (changes.IsEmpty)
            {
                return false;
            }

            IReadOnlyList<RuleLeft> left = null;
            var above = group.ParentGroup;
            var written = store.Write(reading => RuleActs.Of(
                reading,
                id,
                above == null ? null : (Func<StoredRules, IAclNode>)(rules => KdbxNodes.Of(above, rules)),
                rules => new DatabaseNodes(database).Tree(reading, rules, group),
                changes,
                out left));
            Assert.That(written.Taken, Is.True, written.Reason);
            Assert.That(
                (left ?? Array.Empty<RuleLeft>()).Select(change => change.Node.Substring(0, 8) + " " + change.Profile + " " + change.Reason),
                Is.Empty,
                "every change written");
            return written.Wrote;
        }
    }
}
