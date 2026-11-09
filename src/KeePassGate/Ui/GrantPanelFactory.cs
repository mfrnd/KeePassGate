using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What a group or entry dialog is about, as the grant editor needs it.
    /// </summary>
    /// <remarks>
    /// A seam for the same reason every other one here exists: reading a KeePass dialog means
    /// reflection over private fields, and the editor is worth testing without either.
    /// </remarks>
    public interface INodeDialogSource
    {
        /// <summary>
        /// What the editor is given for this dialog, all of it from one reading of the dialog.
        /// </summary>
        /// <remarks>
        /// **One reading rather than three.** The sheet, the store and the line at the top were
        /// asked for apart, and each read the dialog again by reflection, so a KeePass that renamed
        /// one of the fields read there logged the same warning three times for every dialog, and
        /// nothing required the three to agree.
        /// </remarks>
        NodeTab TabFor(Form dialog);
    }

    /// <summary>What the editor is given for one dialog.</summary>
    public sealed class NodeTab
    {
        /// <summary>What the editor is given for a dialog about no group or entry.</summary>
        public static readonly NodeTab None = new NodeTab(null, null, GrantTab.NoNodeScope);

        public NodeTab(GrantSheet sheet, IGrantStore store, string scope)
            : this(sheet, store, scope, null)
        {
        }

        public NodeTab(GrantSheet sheet, IGrantStore store, string scope, Func<IEnumerable<string>> profiles)
        {
            Sheet = sheet;
            Store = store;
            Scope = scope;
            Profiles = profiles;
        }

        /// <summary>The sheet for this dialog, or null when it is not one the editor mounts on.</summary>
        public GrantSheet Sheet { get; }

        /// <summary>Where a rule chosen on this dialog is written, or null when nowhere is safe.</summary>
        public IGrantStore Store { get; }

        /// <summary>What the tab's first line holds for this dialog.</summary>
        public string Scope { get; }

        /// <summary>
        /// Reads the database's list of profiles as it stands, or null where it is not read again.
        /// </summary>
        public Func<IEnumerable<string>> Profiles { get; }
    }

    /// <summary>
    /// Builds the KPG tab for whichever group or entry the dialog is about.
    /// </summary>
    /// <remarks>
    /// A dialog with nothing behind it still gets a panel, saying so. Refusing to attach would
    /// leave an operator looking for a tab that is missing for reasons they cannot see, which is
    /// exactly the shape of a bug report nobody can act on.
    /// </remarks>
    public sealed class GrantPanelFactory : IGatePanelFactory
    {
        private readonly INodeDialogSource _nodes;
        private readonly IGateLog _log;
        private readonly IAdmissionNotice _admissions;

        public GrantPanelFactory(INodeDialogSource nodes, IGateLog log)
            : this(nodes, log, null)
        {
        }

        /// <summary>The same, with every tab told when a record lands.</summary>
        /// <param name="admissions">
        /// Raised when a record lands, which is when a pairing may have named a profile the
        /// database did not, or null for tabs told nothing.
        /// </param>
        public GrantPanelFactory(INodeDialogSource nodes, IGateLog log, IAdmissionNotice admissions)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            _nodes = nodes;
            _log = log ?? NullGateLog.Instance;
            _admissions = admissions;
        }

        public Control CreatePanel(Form dialog)
        {
            var tab = _nodes.TabFor(dialog) ?? NodeTab.None;
            var sheet = tab.Sheet;
            if (sheet == null)
            {
                // Not a dialog about a node. The tab states so in its top line rather than being
                // absent, and nothing on it can be changed. The sheet's reason to be read only is
                // a sentence of its own: given the top line's, the warning band repeated it.
                sheet = new GrantSheet(null, null, new List<string>(), GrantTab.NothingToChange);
            }

            return new GrantPanel(sheet, tab.Store, tab.Scope, _log, tab.Profiles, _admissions);
        }
    }
}
