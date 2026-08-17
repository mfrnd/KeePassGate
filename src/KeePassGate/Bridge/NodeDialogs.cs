using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Reads a real KeePass group or entry dialog for the grant editor.
    /// </summary>
    /// <remarks>
    /// The whole of what the editor needs from KeePass, in one adapter: which node a dialog is
    /// about, what it inherits, where a rule is written, and which profiles are worth offering.
    /// </remarks>
    public sealed class NodeDialogs : INodeDialogSource
    {
        private readonly IGateLog _log;

        public NodeDialogs(IGateLog log)
        {
            _log = log ?? NullGateLog.Instance;
        }

        public GrantSheet SheetFor(Form dialog)
        {
            var node = NodeDialog.Of(dialog, _log);
            if (node == null)
            {
                return null;
            }

            return new GrantSheet(
                node.Document(),
                node.Above,
                ProfilesOf(node.Database),
                node.ReadOnlyReason);
        }

        public IGrantStore StoreFor(Form dialog)
        {
            var node = NodeDialog.Of(dialog, _log);
            return node == null ? null : node.Store();
        }

        public string ScopeFor(Form dialog)
        {
            var node = NodeDialog.Of(dialog, _log);
            return node == null ? GrantTab.NoNodeScope : node.Scope;
        }

        /// <summary>
        /// The profiles a rule here may be written for.
        /// </summary>
        /// <remarks>
        /// The database's own list and nothing else, because that is what a profile is: a name the
        /// operator keeps on the Profiles page and that pairing adds to. It is also what makes the
        /// pick list complete, since a profile can be created before any client holds it.
        ///
        /// The admission records are deliberately not read. A record goes on holding a name the
        /// operator has taken off the list, and offering that name here would let a rule be
        /// written for a profile that grants nothing, which reads exactly like one that works. A
        /// record left behind by a recreated authority would likewise add a name this database
        /// never kept.
        ///
        /// Empty rather than wrong when the list cannot be read. An editor offering nothing still
        /// shows the rules that are there, where an invented list would offer names that grant
        /// nobody anything.
        /// </remarks>
        private IEnumerable<string> ProfilesOf(PwDatabase database)
        {
            if (database == null || database.RootGroup == null)
            {
                return Array.Empty<string>();
            }

            try
            {
                return ProfileNames.Read(
                    database.CustomData.Get(ProfileNames.CustomDataKey)).Names;
            }
            catch (Exception error) when (Expected.OfReflection(error) || Expected.OfCustomData(error))
            {
                _log.Error("the profiles this database names could not be read", error);
                return Array.Empty<string>();
            }
        }
    }
}
