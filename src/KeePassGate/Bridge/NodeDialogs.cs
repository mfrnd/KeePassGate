using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Ui;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Reads a real KeePass group or entry dialog for the grant editor, and writes what an OK on
    /// its KPG tab holds.
    /// </summary>
    /// <remarks>
    /// The whole of what the editor needs from KeePass, in a single adapter: which node a dialog
    /// is about, what the store holds for the node, what arrives at the node, which profiles are
    /// worth offering, why the tab may not take a press, and where an OK writes.
    ///
    /// **A single reading for the tab**, `STORE.md` §629: the store is read once as the tab
    /// opens, and again only when a record lands, and each reading is let go once the tab has
    /// taken what it shows.
    /// </remarks>
    public sealed class NodeDialogs : INodeDialogSource
    {
        private readonly IGateLog _log;
        private readonly DatabaseWriter _writer;

        /// <summary>Reads node dialogs into tabs, with OK written through this writer.</summary>
        /// <param name="writer">
        /// The plugin's writer, which an OK writes through as a write of the operator's own; null
        /// for a tab that takes no press.
        /// </param>
        public NodeDialogs(IGateLog log, DatabaseWriter writer)
        {
            _log = log ?? NullGateLog.Instance;
            _writer = writer;
        }

        public NodeTab TabFor(Form dialog)
        {
            var node = NodeDialog.Of(dialog, _log);
            if (node == null)
            {
                return NodeTab.None;
            }

            if (!node.HasNode || node.Database == null)
            {
                return new NodeTab(GrantSheet.Of(null, null, null, node.ReadOnlyReason), node.Scope, null, null);
            }

            IReadOnlyCollection<string> below;
            var reading = Read(node, out below);
            var sheet = GrantSheet.Of(reading.Rule, reading.Above, reading.Names, reading.ReadOnlyReason);
            sheet.TakeBelow(below);
            return new NodeTab(
                sheet,
                node.Scope,
                () =>
                {
                    // The versions below as the reading made again holds them, taken before the
                    // tab takes the rest of it, so that every press after it keeps them.
                    IReadOnlyCollection<string> since;
                    var read = Read(node, out since);
                    sheet.TakeBelow(since);
                    return read;
                },
                reading.ReadOnlyReason == null ? new DialogRules(node, _writer, _log) : null);
        }

        /// <summary>
        /// What the store holds for the dialog's node, what arrives at the node and the names the
        /// database holds, from a reading let go at once, with the reason the tab takes no press
        /// and the standing versions at the nodes below the node, `StoredRules.Below`.
        /// </summary>
        /// <remarks>
        /// **Read only wherever an OK could only be refused**, and that is known as the dialog
        /// opens, `STORE.md` §657: no press is taken that the write would refuse. A store that
        /// cannot be read at all is read as holding nothing, with the tab read only, rather than
        /// a tab with rules nobody could see the reason for. A reading made again when a record
        /// lands carries its reason as well, and the tab stops taking presses where the reading
        /// has a reason.
        /// </remarks>
        private GrantReading Read(NodeDialog node, out IReadOnlyCollection<string> below)
        {
            var database = node.Database;
            try
            {
                using (var reading = new KdbxStore(database, GateVersion.Current, _log).Read())
                {
                    var rules = StoredRules.Of(reading);
                    var parent = node.Parent;
                    var above = AclResolver.Above(parent == null ? null : KdbxNodes.Of(parent, rules));
                    below = node.Group == null ? RowChange.Kept(null) : rules.Below(node.Node, new DatabaseNodes(database).Tree(reading, rules, node.Group));
                    return new GrantReading(
                        rules.At(node.Node), above, ProfileNames.Of(reading).Names, node.ReadOnlyReason ?? Unwritable(node, reading));
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfReflection(error))
            {
                _log.Error("the rules of a node could not be read for its " + GateName.OnATab + " tab", error);
                below = RowChange.Kept(null);
                return new GrantReading(null, null, Array.Empty<string>(), node.ReadOnlyReason ?? GrantTab.StoreUnusable);
            }
        }

        /// <summary>Why a press on the tab could only be refused at OK, or null where it can be written.</summary>
        private string Unwritable(NodeDialog node, StoreReading reading)
        {
            var stored = node.Group != null
                ? StoreNodes.Holds(node.Group)
                : StoreNodes.Holds(node.Entry.ParentGroup) || StoreNodes.Of(reading).Holds(node.Entry);
            if (stored)
            {
                return GrantTab.StoreNode;
            }

            if (reading.State == StoreState.None)
            {
                return GrantTab.NoAuthorityYet;
            }

            if (reading.State != StoreState.Whole)
            {
                return GrantTab.StoreUnusable;
            }

            return _writer == null || _writer.Unwritable != null ? GrantTab.NotWatched : null;
        }

        /// <summary>
        /// Where an OK on a dialog's tab writes: the plugin's store, through the writer, as a write
        /// of the operator's own, composed over the reading the write is planned against.
        /// </summary>
        private sealed class DialogRules : INodeRules
        {
            /// <summary>How many characters of a node's identifier name the node in the log.</summary>
            private const int NamedBy = 8;

            private readonly NodeDialog _node;
            private readonly DatabaseWriter _writer;
            private readonly IGateLog _log;

            public DialogRules(NodeDialog node, DatabaseWriter writer, IGateLog log)
            {
                _node = node;
                _writer = writer;
                _log = log;
            }

            public string Write(RuleChanges changes)
            {
                var database = _node.Database;
                var node = _node.Node;
                var parent = _node.Parent;
                var group = _node.Group;
                IReadOnlyList<RuleLeft> left = null;

                // A single reading, made afresh in the writer's turn: the act is composed over the
                // reading the write is planned against, so every version renewed is a version that
                // reading holds, and a rule taken off records no more than arrives at the node in
                // that reading as well as at the press, `STORE.md` §629. The parent is the group the
                // Add Entry dialog adds its entry to as well. The tree is the tree of the same turn,
                // so a node moved since the tab read it is below where it stands now. The parent
                // and the tree are made from the rules the act works out of that reading, once.
                var written = _writer.ByOperator(
                    database,
                    () => new KdbxStore(database, GateVersion.Current, _log, _writer.Holding)
                        .Write(reading => RuleActs.Of(
                            reading,
                            node,
                            parent == null ? null : (Func<StoredRules, IAclNode>)(rules => KdbxNodes.Of(parent, rules)),
                            group == null ? null : (Func<StoredRules, IRuledTree>)(rules => new DatabaseNodes(database).Tree(reading, rules, group)),
                            changes,
                            out left)));

                // The node by the first characters of its identifier, as the store's log lines name
                // an entry, and nothing of what the node holds.
                var named = node.Substring(0, NamedBy);
                if (!written.Taken)
                {
                    _log.Warn("the rules changed on the " + GateName.OnATab + " tab of " + named
                              + " were not written: " + written.Reason);
                    return GrantTab.NotKept + written.Reason;
                }

                // Every change handed over and not written is named in the log, and every press of
                // the operator's own among them is told once the dialog has closed, a line for each
                // reason: a narrowing left out has to be seen to be missing. Each is named by the
                // node it is at, so a renewal the write above left at a node below is named by that
                // node, and the log alone tells of it, `STORE.md` §643; so is a node below that the
                // change above did not reach because the tab had not held its versions, in a line
                // of its own kind, so that the log tells why a widening stopped short of it,
                // `STORE.md` §617.
                var reasons = new List<string>();
                foreach (var change in left ?? Array.Empty<RuleLeft>())
                {
                    _log.Warn(Line(change));
                    var told = Told(change.Reason);
                    if (change.Pressed && !reasons.Contains(told))
                    {
                        reasons.Add(told);
                    }
                }

                if (written.Wrote)
                {
                    _log.Info("the rules changed on the " + GateName.OnATab + " tab of " + named
                              + " were written, and KeePass now has changes to save");
                }

                if (reasons.Count == 0)
                {
                    return null;
                }

                // Where some changes were kept, a last line tells where the operator sees which:
                // no sentence names a profile.
                var lines = new[] { written.Wrote ? GrantTab.NotAllKept : GrantTab.NoneKept }.Concat(reasons);
                return string.Join(Environment.NewLine, written.Wrote ? lines.Concat(new[] { GrantTab.ShownAsKept }) : lines);
            }

            /// <summary>The log's line for a change or a version not written, named by its node's first characters.</summary>
            private static string Line(RuleLeft change)
            {
                var what = (change.Profile == null ? "the box" : "the rule of " + change.Profile) + " at " + change.Node.Substring(0, NamedBy);
                if (change.Reason == RuleLeftReason.NotHeld)
                {
                    return what + " was not carried along by the change above it: the tab did not hold every version standing there at the press";
                }

                return what + (change.Pressed ? " changed on the " + GateName.OnATab + " tab was not written: " : " was left as it was: ") + Why(change.Reason);
            }

            /// <summary>Why a change was not written, for the log.</summary>
            private static string Why(RuleLeftReason reason)
            {
                switch (reason)
                {
                    case RuleLeftReason.TooLong:
                        return "the new version does not fit the room the store gives a rule";
                    case RuleLeftReason.OutOfForm:
                        return "the change is out of form";
                    case RuleLeftReason.NotAllowed:
                        return "the store as the write read the store does not let the change be written";
                    default:
                        return "the store holds no key or cannot be read as a whole";
                }
            }

            /// <summary>The line of the message after the close that tells a reason a change was not written.</summary>
            private static string Told(RuleLeftReason reason)
            {
                switch (reason)
                {
                    case RuleLeftReason.TooLong:
                        return GrantTab.NoRoom;
                    case RuleLeftReason.OutOfForm:
                        return GrantTab.ChangeOutOfForm;
                    case RuleLeftReason.NotAllowed:
                        return GrantTab.StoreChanged;
                    default:
                        return GrantTab.StoreUnreadable;
                }
            }
        }
    }
}
