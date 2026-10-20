using System;
using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using System.Collections.Generic;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The KPG tab: what this database's authority is, and the three things an operator can do to
    /// it.
    /// </summary>
    /// <remarks>
    /// Two things are said on screen rather than assumed. That these buttons act on the database
    /// only when the dialog is accepted: `AuthorityPanelFactory` wraps the database so that what
    /// this page writes is held until OK and thrown away on Cancel.
    /// And what recreating costs, in the number of clients that lose their admission, because that
    /// is the number the operator cannot work out for themselves.
    ///
    /// Nothing in here throws. It runs on the UI thread of a password manager.
    /// </remarks>
    public sealed class AuthorityPanel : UserControl
    {
        /// <summary>
        /// What OK and Cancel mean for everything on this tab, in one line.
        /// </summary>
        /// <remarks>
        /// One line rather than two, and that is a measurement rather than a preference. The tab
        /// is 449 by 314 and nothing on it scrolls, so a sentence that wraps is a whole line of
        /// the tab spent saying the same thing twice. It says the two things that are true and
        /// useful: which button makes any of this real, and that the file is written when the
        /// database is saved. `GateTabFitsTests` is what fails when it grows back.
        /// </remarks>
        public const string ImmediateNotice =
            "Changes apply on OK; the database must then be saved.";

        /// <summary>
        /// How much of somebody else's exception text this page will show. Two lines at the width
        /// the tab allows; the rest is in the log.
        /// </summary>
        private const int MessageLimit = 160;

        private readonly IGateDatabase _database;
        private readonly IOperatorPrompt _prompt;
        private readonly IGateLog _log;
        private readonly IGateStatus _gate;
        private readonly GateIncidents _incidents;
        private readonly AuthorityService _service;
        private readonly Font _titleFont;
        private readonly Font _valueFont;
        private readonly FlowLayoutPanel _layout;

        /// <summary>The labels that wrap, which are the ones a narrow tab has to be told about.</summary>
        private readonly List<Label> _wrapping = new List<Label>();

        private readonly Label _serving;
        private readonly Label _status;
        private readonly TableLayoutPanel _facts;
        private readonly Label _detail;
        private readonly Label _result;
        private readonly Button _create;
        private readonly Button _recreate;
        private readonly Button _delete;

        public AuthorityPanel(IGateDatabase database, IOperatorPrompt prompt, IGateLog log, string version)
            : this(database, prompt, log, version, null)
        {
        }

        public AuthorityPanel(
            IGateDatabase database, IOperatorPrompt prompt, IGateLog log, string version, IGateStatus gate)
            : this(database, prompt, log, version, gate, null)
        {
        }

        public AuthorityPanel(
            IGateDatabase database,
            IOperatorPrompt prompt,
            IGateLog log,
            string version,
            IGateStatus gate,
            GateIncidents incidents)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            _database = database;
            _prompt = prompt ?? new MessageBoxPrompt();
            _log = log ?? NullGateLog.Instance;
            _gate = gate;
            _incidents = incidents;
            _service = database == null || !database.IsOpen
                ? null
                : new AuthorityService(database.PluginCustomData, _log);

            Name = GateTab.PanelControlName;
            Dock = DockStyle.Fill;
            Padding = new Padding(Spacing.Gap);

            // Neither axis scrolls. KeePass gives a settings tab a fixed area and the content has
            // to fit it: a scroll bar here would hide the half of the tab that says what the
            // buttons cost. `GateTabFitsTests` measures this against the real dialog, so content
            // that outgrows the tab fails a test rather than appearing on somebody's screen.
            AutoScroll = false;

            _layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false
            };

            // From this panel's own font, not the system default. The labels beside these inherit
            // `Font`, and on any machine where the two differ the table would show its names in one
            // typeface and its values in another.
            _titleFont = new Font(Font, FontStyle.Bold);
            _valueFont = new Font(Font, FontStyle.Bold);

            // The name and the version share a line rather than taking two, and the line about
            // the socket goes tight under them: all three answer questions about the plugin
            // rather than about this database, so they read as one band and are separated from
            // what follows once rather than twice. The room that buys is what the headline
            // under it spends on having air above as well as below.
            var heading = NewRow(new Padding(0, 0, 0, Spacing.Tight));
            heading.Controls.Add(new TextLabel
            {
                Name = GateTab.TitleLabelName,
                Text = "KeePassGate",
                Font = _titleFont,
                AutoSize = true,
                Margin = new Padding(0, 0, Spacing.Gap, 0)
            });

            heading.Controls.Add(new TextLabel
            {
                Name = GateTab.VersionLabelName,
                Text = "Version " + version,
                AutoSize = true,
                Margin = new Padding(0)
            });

            _layout.Controls.Add(heading);
            _serving = NewTextLabel(GateTab.ServingLabelName, new Padding(0, 0, 0, Spacing.Gap));
            _layout.Controls.Add(_serving);

            // Air above as well as below, so it reads as the opening sentence of the page
            // rather than as a caption on whatever is over it or a label on the table under
            // it. A first line pressed against the top edge reads as one of those two, and
            // which one is not something the page gets to choose.
            _status = NewTextLabel(GateTab.StatusLabelName, new Padding(0, Spacing.Gap, 0, Spacing.Gap));

            // A table rather than sentences, because these are scanned rather than read. Two
            // columns that size themselves: the names are known and short, and the values are what
            // somebody came to look at.
            _facts = new TableLayoutPanel
            {
                Name = GateTab.FactsTableName,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = new Padding(0, 0, 0, Spacing.Tight)
            };

            _facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _detail = NewTextLabel(GateTab.DetailLabelName, new Padding(0, 0, 0, Spacing.Gap));
            _layout.Controls.Add(_status);
            _layout.Controls.Add(_facts);
            _layout.Controls.Add(_detail);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, Spacing.Tight)
            };

            _create = NewButton(GateTab.CreateButtonName, GateTab.CreateButtonText, OnCreate);
            _recreate = NewButton(GateTab.RecreateButtonName, GateTab.RecreateButtonText, OnRecreate);
            _delete = NewButton(GateTab.DeleteButtonName, GateTab.DeleteButtonText, OnDelete);
            buttons.Controls.Add(_create);
            buttons.Controls.Add(_recreate);
            buttons.Controls.Add(_delete);
            _layout.Controls.Add(buttons);

            // Air over it, because it is about the buttons above rather than part of them.
            _layout.Controls.Add(
                NewTextLabel(GateTab.NoticeLabelName, new Padding(0, Spacing.Gap, 0, 0), ImmediateNotice));
            _result = NewTextLabel(GateTab.ResultLabelName, new Padding(0));
            _layout.Controls.Add(_result);

            Controls.Add(_layout);

            Describe();
        }

        /// <summary>
        /// Creates an authority, exactly as the button does. Public because a button press needs a
        /// visible control tree and a test of the real dialog does not have one.
        /// </summary>
        public void CreateAuthority()
        {
            Act(() => _service.Create());
        }

        /// <summary>Recreates it, asking first. Nothing happens if the answer is no.</summary>
        public void RecreateAuthority()
        {
            Act(() =>
            {
                if (!_prompt.Confirm(
                        "Recreate the authority",
                        "Recreating the authority replaces it and revokes every client at once." +
                        Environment.NewLine + Environment.NewLine +
                        Cost(CountRecords()) + Environment.NewLine + Environment.NewLine +
                        "Recreate it?"))
                {
                    return null;
                }

                return _service.Recreate();
            });
        }

        /// <summary>Deletes it, asking first. Nothing happens if the answer is no.</summary>
        public void DeleteAuthority()
        {
            Act(() =>
            {
                if (!_prompt.Confirm(
                        "Delete the authority",
                        "Deleting the authority stops this database being served at all." +
                        Environment.NewLine + Environment.NewLine +
                        Cost(CountRecords()) + Environment.NewLine + Environment.NewLine +
                        "Delete it?"))
                {
                    return null;
                }

                return _service.Delete();
            });
        }

        /// <summary>Reads the database again and redraws. Safe to call at any time.</summary>
        public void Describe()
        {
            try
            {
                var description = DescribeNow();
                _serving.Text = GateStatusText.For(
                    _gate, description.IsServed, _incidents == null ? null : _incidents.Summary);
                _status.Text = description.Headline;
                Draw(description.Facts);
                _detail.Text = string.Join(Environment.NewLine, description.Details);
                _create.Enabled = description.CanCreate;
                _recreate.Enabled = description.CanRecreate;
                _delete.Enabled = description.CanDelete;
            }
            catch (Exception error)
            {
                _log.Error("the authority could not be described", error);
                _status.Text = "The authority could not be read.";
                Draw(null);
                _detail.Text = Clip(error.Message);
                _create.Enabled = false;
                _recreate.Enabled = false;
                _delete.Enabled = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_titleFont != null)
                {
                    _titleFont.Dispose();
                }

                if (_valueFont != null)
                {
                    _valueFont.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Keeps everything inside the width the tab actually has, whatever that turns out to be.
        /// </summary>
        /// <remarks>
        /// Wrapping labels are given a maximum width rather than a fixed one, so they grow
        /// downwards instead of sideways. Sizes are read from the panel rather than written into
        /// it, because a number chosen here is a number that is wrong at another font size, another
        /// display scaling, or on a KeePass whose dialog is not the size this one is.
        /// </remarks>
        protected override void OnLayout(LayoutEventArgs e)
        {
            FitToWidth();
            base.OnLayout(e);
        }

        private void FitToWidth()
        {
            // Setting a property in the constructor lays the panel out before it has been built,
            // so this runs at least once with nothing to fit.
            if (_layout == null)
            {
                return;
            }

            var available = ClientSize.Width - Padding.Horizontal - _layout.Padding.Horizontal;
            if (available <= 0)
            {
                return;
            }

            foreach (var label in _wrapping)
            {
                var width = available - label.Margin.Horizontal;

                // Only when it changes: assigning this asks for another layout pass.
                if (width > 0 && label.MaximumSize.Width != width)
                {
                    label.MaximumSize = new Size(width, 0);
                }
            }
        }

        private static FlowLayoutPanel NewRow(Padding margin)
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = margin
            };
        }

        private Label NewTextLabel(string name, Padding margin, string text = "")
        {
            var label = new TextLabel
            {
                Name = name,
                Text = text,
                AutoSize = true,
                Margin = margin
            };

            _wrapping.Add(label);
            return label;
        }

        private static Button NewButton(string name, string text, EventHandler onClick)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, Spacing.Gap, 0)
            };

            button.Click += onClick;
            return button;
        }

        /// <summary>
        /// Draws a set of facts this database does not hold.
        /// </summary>
        /// <remarks>
        /// For measurement. What has to fit the tab is the longest each value can ever be, and a
        /// database created a moment ago holds none of them. The next reading replaces whatever is
        /// put here, so this cannot leave anything on screen that outlives it.
        /// </remarks>
        public void DrawFacts(IReadOnlyList<Fact> facts)
        {
            Draw(facts);
        }

        /// <summary>
        /// Puts the facts into the table, replacing whatever was there.
        /// </summary>
        /// <remarks>
        /// Rebuilt rather than updated in place, because the number of rows changes with what the
        /// database holds and a table that kept a row from the last reading would be showing a fact
        /// about an authority that has since been deleted.
        ///
        /// The value is bold and the name is not. One of the two columns is what somebody came to
        /// read, and which one it is should not have to be worked out.
        /// </remarks>
        private void Draw(IReadOnlyList<Fact> facts)
        {
            _facts.SuspendLayout();

            try
            {
                var old = new List<Control>();
                foreach (Control child in _facts.Controls)
                {
                    old.Add(child);
                }

                _facts.Controls.Clear();
                _facts.RowStyles.Clear();
                _facts.RowCount = 0;

                foreach (var control in old)
                {
                    control.Dispose();
                }

                if (facts == null)
                {
                    return;
                }

                _facts.RowCount = facts.Count;

                for (var at = 0; at < facts.Count; at++)
                {
                    _facts.Controls.Add(NewFactLabel(facts[at].Name, false), 0, at);
                    _facts.Controls.Add(NewFactLabel(facts[at].Value, true), 1, at);
                }
            }
            finally
            {
                _facts.ResumeLayout();
            }
        }

        private Label NewFactLabel(string text, bool value)
        {
            return new TextLabel
            {
                Text = text,
                AutoSize = true,
                Font = value ? _valueFont : Font,

                // Enough of a gap between the columns to read as two, and none above or below: the
                // rows are one line each, and leading between them costs height the tab does not
                // have to spare.
                //
                // The panel's own inset, which `Spacing` makes the same amount, rather than the
                // constant: this row is built on every reading, after the page was scaled to its
                // dialog, and a scaling reaches only the controls that are already there. The inset
                // was one of those.
                Margin = value ? new Padding(0) : new Padding(0, 0, Padding.Left, 0)
            };
        }

        private AuthorityDescription DescribeNow()
        {
            if (_service == null)
            {
                return AuthorityDescription.NoDatabase();
            }

            // **Asked of the database, not of the store it hands out.** The database can be gone
            // by now: KeePass locks or closes it while this dialog is open, which is a state
            // `PendingDatabase.Apply` names and answers. What that leaves is a store over nothing,
            // never a null one, because every implementation here builds one on demand and the
            // tab's own buffer wraps whatever that answers with. Reading through it finds no keys,
            // which reads exactly like a database that has no authority yet, so the tab offered
            // Create for a database that was not there. `IsOpen` is the question with an answer,
            // and the constructor was already asking it.
            if (!_database.IsOpen)
            {
                return AuthorityDescription.NoDatabase();
            }

            // Asked for again rather than remembered, for the reason `OpenDatabase` gives: a
            // database that is locked and opened again comes back with its custom data replaced.
            var store = _database.PluginCustomData;

            using (var reading = _service.Read())
            {
                // The records only. What this builds is the expiry warnings, and a listing that
                // The listing itself, not its records: whether it could be made at all is what
                // tells a database that admitted nobody from one this could not read, and the two
                // say opposite things to an operator deciding whether to recreate the authority.
                return AuthorityDescription.Of(
                    reading, new AdmissionStore(store, _log).All(), DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// How many clients are admitted, read now rather than remembered.
        /// </summary>
        /// <remarks>
        /// This reads the whole authority to get one integer, which imports two PKCS#12 blobs to
        /// parse certificates nothing here looks at. Caching it from the last `Describe` was
        /// tried and reverted: a panel can be asked to confirm before it has ever described
        /// itself, and the number would then be zero in a sentence telling the operator what
        /// they are about to lose. A wrong count there is worse than a slow one.
        ///
        /// It is also not slow. An import off a warm runtime is about 0.13 ms on .NET Framework
        /// 4.8, measured over two hundred of them, so the two here cost a quarter of a millisecond
        /// and the whole tab's worth costs a few. That number is written down because it is the
        /// reason not to cache this later either, and rediscovering it means measuring again.
        /// </remarks>
        private int CountRecords()
        {
            using (var reading = _service.Read())
            {
                return reading.ClientRecordCount;
            }
        }

        private void OnCreate(object sender, EventArgs e)
        {
            CreateAuthority();
        }

        private void OnRecreate(object sender, EventArgs e)
        {
            RecreateAuthority();
        }

        private void OnDelete(object sender, EventArgs e)
        {
            DeleteAuthority();
        }

        private static string Cost(int records)
        {
            if (records <= 0)
            {
                return "No client has been admitted yet, so nothing else is lost.";
            }

            return records == 1
                ? "One client is admitted, and it would have to pair again."
                : records + " clients are admitted, and every one of them would have to pair again.";
        }

        /// <summary>
        /// Runs one operator action. A null result means they were asked and said no, which is not
        /// a failure and is not reported as one.
        /// </summary>
        private void Act(Func<AuthorityChange> action)
        {
            if (_service == null)
            {
                return;
            }

            AuthorityChange change;
            try
            {
                change = action();
            }
            catch (Exception error)
            {
                _log.Error("an authority action failed", error);
                _result.Text = "That did not work: " + Clip(error.Message);
                Describe();
                return;
            }

            if (change == null)
            {
                return;
            }

            if (change.Succeeded)
            {
                try
                {
                    _database.MarkModified();
                }
                catch (Exception error)
                {
                    _log.Error("the database could not be marked as modified", error);
                }

                // Nothing said, and the line collapses so the page gets the height back.
                //
                // What a sentence would add is the word for something already on screen. The table
                // above fills in, empties, or shows a different fingerprint, and the buttons swap
                // over between create and delete. A failure still says so, because nothing else on
                // the page would.
                _result.Text = string.Empty;
            }
            else
            {
                _result.Text = change.Message;
            }

            Describe();
        }

        /// <summary>
        /// An exception message, cut to something this page can hold.
        /// </summary>
        /// <remarks>
        /// The text comes from whatever threw, so its length is not this plugin's to choose,
        /// and the page it lands on has `AutoScroll` off: a long one wraps, grows the page
        /// past the tab, and pushes the buttons out of reach with no way to scroll to them.
        /// The whole message still reaches the log, which is where a long one belongs.
        /// </remarks>
        private static string Clip(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            var flattened = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flattened.Length <= MessageLimit
                ? flattened
                : flattened.Substring(0, MessageLimit) + "...";
        }
    }
}
