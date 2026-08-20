using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassGate.Authority;
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
        /// The profiles this database has admitted somebody into, for the pick list.
        /// </summary>
        /// <remarks>
        /// From the admission records, because that is where a profile becomes real: a client holds
        /// what the operator approved at pairing, and nothing else defines the set. Empty rather
        /// than wrong when they cannot be read, since an editor offering nothing still shows the
        /// rules and still lets the ones already there be changed, where an invented list would
        /// offer names that grant nobody anything.
        /// </remarks>
        private IEnumerable<string> ProfilesOf(PwDatabase database)
        {
            var offered = new List<string>();

            if (database == null || database.RootGroup == null)
            {
                return offered;
            }

            try
            {
                // `Records` rather than the listing, and the listing's `Complete` is not asked
                // about: a purge that misses a client leaves a profile named by nobody, which
                // is a rule doing nothing rather than a rule letting somebody in.
                var records = new AdmissionStore(new CustomDataStore(database), _log)
                    .All().Records;

                foreach (var record in records)
                {
                    foreach (var profile in record.Profiles)
                    {
                        if (!offered.Contains(profile))
                        {
                            offered.Add(profile);
                        }
                    }
                }
            }
            catch (Exception error) when (Expected.OfReflection(error) || Expected.OfCustomData(error))
            {
                _log.Error("the profiles this database has admitted could not be read", error);
            }

            return offered;
        }
    }
}
