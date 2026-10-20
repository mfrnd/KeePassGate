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
        /// <summary>The sheet for this dialog, or null when it is not one the editor mounts on.</summary>
        GrantSheet SheetFor(Form dialog);

        /// <summary>Where a rule typed on this dialog is written, or null when nowhere is safe.</summary>
        IGrantStore StoreFor(Form dialog);

        /// <summary>What the tab's first line says for this dialog.</summary>
        string ScopeFor(Form dialog);
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

        public GrantPanelFactory(INodeDialogSource nodes, IGateLog log)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            _nodes = nodes;
            _log = log ?? NullGateLog.Instance;
        }

        public Control CreatePanel(Form dialog)
        {
            var sheet = _nodes.SheetFor(dialog);
            if (sheet == null)
            {
                // Not a dialog about a node. The tab says so rather than being absent, and nothing
                // on it can be typed into.
                sheet = new GrantSheet(null, null, new List<string>(), GrantTab.NoNodeScope);
            }

            return new GrantPanel(sheet, _nodes.StoreFor(dialog), _nodes.ScopeFor(dialog), _log);
        }
    }
}
