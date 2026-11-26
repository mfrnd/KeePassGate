using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What a group or entry dialog is about, as the grant editor needs it.
    /// </summary>
    /// <remarks>
    /// A seam for the same reason every other seam here exists: reading a KeePass dialog means
    /// reflection over private fields and a reading of the plugin's store, and the editor is worth
    /// testing without either.
    /// </remarks>
    public interface INodeDialogSource
    {
        /// <summary>What the editor is given for this dialog, all of it from a single reading.</summary>
        NodeTab TabFor(Form dialog);
    }

    /// <summary>
    /// Where the changes an OK on a node's dialog holds are written, `STORE.md` §629: into the
    /// plugin's store, as a write of the operator's own.
    /// </summary>
    public interface INodeRules
    {
        /// <summary>
        /// Writes these changes at the dialog's node, answering null where every change was kept,
        /// or what the operator is told once the dialog has closed: that no change was kept and
        /// why, or which kinds of change were left out.
        /// </summary>
        string Write(RuleChanges changes);
    }

    /// <summary>What the tab reads of the store: the node's rules, what arrives, the names, and whether a change can be written.</summary>
    public sealed class GrantReading
    {
        /// <summary>A node's rules, what arrives at the node, and the names, as a reading holds them.</summary>
        /// <param name="names">The profiles the database names, or null for a reading that asks nothing about the list.</param>
        /// <param name="readOnlyReason">Why a change could not be written as this reading stands, or null where a change can be written.</param>
        public GrantReading(NodeRule rule, AclInheritance above, IEnumerable<string> names, string readOnlyReason)
        {
            Rule = rule ?? NodeRule.None;
            Above = above ?? AclInheritance.None;
            Names = names == null ? null : names.ToList().AsReadOnly();
            ReadOnlyReason = readOnlyReason;
        }

        public NodeRule Rule { get; private set; }

        public AclInheritance Above { get; private set; }

        public IReadOnlyList<string> Names { get; private set; }

        public string ReadOnlyReason { get; private set; }
    }

    /// <summary>What the editor is given for a dialog.</summary>
    public sealed class NodeTab
    {
        /// <summary>What the editor is given for a dialog about no group or entry.</summary>
        public static readonly NodeTab None = new NodeTab(null, null, null, null);

        /// <summary>What the editor is given for a dialog: the sheet, the line at the top, and where the tab reads and writes.</summary>
        /// <param name="sheet">The sheet for this dialog, or null where the dialog is about no node.</param>
        /// <param name="scope">What the tab's first line holds for this dialog.</param>
        /// <param name="again">
        /// The node's rules, what arrives and the names, read again when a record lands, or null
        /// where nothing is read again.
        /// </param>
        /// <param name="rules">Where OK writes what the sheet holds, or null where nothing is written.</param>
        public NodeTab(GrantSheet sheet, string scope, Func<GrantReading> again, INodeRules rules)
        {
            Sheet = sheet;
            Scope = scope;
            Again = again;
            Rules = rules;
        }

        public GrantSheet Sheet { get; }

        public string Scope { get; }

        public Func<GrantReading> Again { get; }

        public INodeRules Rules { get; }
    }

    /// <summary>
    /// Builds the KPG tab for whichever group or entry the dialog is about, and writes what the
    /// tab holds when the dialog closes with OK.
    /// </summary>
    /// <remarks>
    /// A dialog with nothing behind it still gets a panel, telling so. Refusing to attach would
    /// leave an operator looking for a tab that is missing for reasons nobody can see, which is
    /// exactly the shape of a bug report nobody can act on.
    ///
    /// **The sheet outlives the panel**, as the settings dialog's buffer does: KeePass's own
    /// handler of the dialog's close takes the dialog off the window manager, which detaches the
    /// page and disposes the panel, before the handler subscribed here runs. What is written is
    /// what the sheet holds, which needs no panel.
    /// </remarks>
    public sealed class GrantPanelFactory : IGatePanelFactory
    {
        private readonly INodeDialogSource _nodes;
        private readonly IGateLog _log;
        private readonly IAdmissionNotice _admissions;
        private readonly IOperatorPrompt _prompt;
        private readonly Control _window;
        private readonly GateIncidents _incidents;

        /// <summary>Builds tabs from what this source reads of a dialog, and writes them at OK.</summary>
        /// <param name="admissions">
        /// Raised when a record lands, which is when a pairing may have named a profile the
        /// database did not, or null for tabs told nothing.
        /// </param>
        /// <param name="prompt">How the operator is told of rules an OK did not keep.</param>
        /// <param name="window">
        /// A window on KeePass's thread that outlives every node dialog, where the message of rules
        /// not kept is posted to, so that KeePass has handled the dialog's result first; null for a
        /// KeePass that names no main window, where the log alone tells of rules not kept.
        /// </param>
        /// <param name="incidents">
        /// Where a defect in the handler of a dialog's close is counted: the plugin's own, which
        /// the Tools window reads, or a count of its own where none is given.
        /// </param>
        public GrantPanelFactory(
            INodeDialogSource nodes,
            IGateLog log,
            IAdmissionNotice admissions,
            IOperatorPrompt prompt,
            Control window,
            GateIncidents incidents)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (prompt == null)
            {
                throw new ArgumentNullException(nameof(prompt));
            }

            _nodes = nodes;
            _log = log ?? NullGateLog.Instance;
            _admissions = admissions;
            _prompt = prompt;
            _window = window;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        /// <summary>
        /// Why KeePass is part way through work of its own, or null where it is not: the question
        /// the writer asks before a write. Null where nobody can be asked, which tells at once.
        /// </summary>
        public Func<string> Busy { get; set; }

        public Control CreatePanel(Form dialog)
        {
            var tab = _nodes.TabFor(dialog) ?? NodeTab.None;

            // Not a dialog about a node: the tab tells so as the reason it takes no change rather
            // than being absent.
            var sheet = tab.Sheet ?? GrantSheet.Of(null, null, Array.Empty<string>(), GrantTab.NoNode);
            var panel = new GrantPanel(sheet, tab.Scope, _log, tab.Again, _admissions);

            if (tab.Rules != null && sheet.CanEdit)
            {
                Commit(dialog, sheet, tab.Rules);
            }

            return panel;
        }

        /// <summary>
        /// Arranges for the dialog's result to decide what happens to what the sheet holds: OK
        /// writes it, and any other close throws it away.
        /// </summary>
        /// <remarks>
        /// **On `FormClosed` and not at the press of OK**, `STORE.md` §629. KeePass's own handler of
        /// the close, subscribed as the dialog was made, has applied the dialog's fields by then.
        /// An OK pressed while a save of the plugin's own pumps under the dialog is handled inside
        /// that save, and the close, and with the close this write, comes once the save has
        /// returned: a write at the press would be made inside the save, and the save would clear
        /// the mark the write set. Cancel, Escape and the close box write nothing, and KeePass asks
        /// nothing about a change of rules alone, since a rule is no part of its node.
        ///
        /// **A change not kept is told** once the dialog has closed: a write refused, a change left
        /// out of what was written, a press made before the tab stopped taking presses, and a
        /// write stopped by a defect. A narrowing the operator takes to be in force has to be seen
        /// to be missing, so the log alone is not enough. Nothing is told where nothing was
        /// pressed. Posted, never raised inside the handler, so that KeePass has handled the
        /// dialog's result first and nothing holds the dialog.
        /// </remarks>
        private void Commit(Form dialog, GrantSheet sheet, INodeRules rules)
        {
            if (dialog == null)
            {
                return;
            }

            FormClosedEventHandler closed = null;
            closed = (sender, e) =>
            {
                dialog.FormClosed -= closed;

                try
                {
                    var news = Kept(dialog, sheet, rules);
                    if (news != null)
                    {
                        Tell(news);
                    }
                }
                // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
                catch (Exception error)
                {
                    // A catch-all, because `FormClosed` is raised on KeePass's own thread as its
                    // group or entry dialog closes, with nothing of KeePass's around it. WinForms
                    // catches what escapes there and puts its own error dialog up, offering to quit
                    // KeePass. Counted as an incident, like every handler of KeePass's events that
                    // catches everything, so the Tools window shows it and the log has it whole.
                    _incidents.Record("the OK of the " + GateName.OnATab + " tab of a group or an entry did not run to its end", error);

                    // And told as a refused OK is told, since the write may not have been made.
                    // Safe inside the catch: every call `Tell` makes is filtered or guarded.
                    Tell(GrantTab.NotKeptDefect);
                }
            };

            try
            {
                dialog.FormClosed += closed;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // A dialog already gone. Nothing is written, which is the safe half of the two.
                _log.Warn("the " + GateName.OnATab + " tab could not watch for the dialog's result: " + error.Message);
            }
        }

        /// <summary>
        /// What the operator is told of a dialog's close: null for any close but OK and for an OK
        /// with nothing pressed, and otherwise what the write answers, or that no change was kept
        /// where the tab stopped taking presses while the dialog was open.
        /// </summary>
        private static string Kept(Form dialog, GrantSheet sheet, INodeRules rules)
        {
            if (dialog.DialogResult != DialogResult.OK)
            {
                return null;
            }

            if (!sheet.CanEdit)
            {
                return sheet.Pending ? GrantTab.NotKept + GrantTab.NoLongerWritable : null;
            }

            var changes = sheet.Changes;
            return changes.IsEmpty ? null : rules.Write(changes);
        }

        /// <summary>
        /// Tells the operator, once the dialog has closed and KeePass is not part way through work
        /// of its own, that rules changed on the tab were not kept, `FreeMessage`.
        /// </summary>
        private void Tell(string news)
        {
            new FreeMessage(_window, _prompt, Busy, _incidents, _log).Tell(GrantTab.NotKeptTitle, news, Untold);
        }

        private const string Untold = "the operator could not be told of rules the " + GateName.OnATab + " tab did not keep";
    }
}
