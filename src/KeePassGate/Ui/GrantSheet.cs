using System;
using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Authority;

namespace KeePassGate.Ui
{
    /// <summary>One line of the editor: a profile, what it holds here, and where that came from.</summary>
    public sealed class GrantRow
    {
        internal GrantRow(
            string profile, string mask, bool local, string inheritedMask, bool reaches, bool named)
        {
            Profile = profile;
            Mask = mask;
            Local = local;
            InheritedMask = inheritedMask;
            Reaches = reaches;
            Named = named;
        }

        public string Profile { get; private set; }

        /// <summary>What this profile holds here, as `CRUSDLAP`.</summary>
        public string Mask { get; private set; }

        /// <summary>
        /// True when this node's own document says it. False means the row is showing what a group
        /// above grants, which nothing here owns and nothing here writes.
        /// </summary>
        public bool Local { get; private set; }

        /// <summary>
        /// What the same profile holds from above, or null when nothing above names it. Shown on a
        /// local rule's mask, so that what a rule here replaces, and whether it narrows or widens
        /// it, is on the table.
        /// </summary>
        public string InheritedMask { get; private set; }

        /// <summary>
        /// Whether this row is in force at this node. False only for an inherited one that this
        /// node's own `deny` refuses, or that its own document, being unreadable, refuses.
        /// </summary>
        /// <remarks>
        /// Sealing a node does not remove what a group above grants, it stops it arriving, so
        /// the row stays on the table and says so. Dropping it instead would leave an operator
        /// unable to see what the seal is refusing, which is the thing they turned it on for.
        /// </remarks>
        public bool Reaches { get; private set; }

        /// <summary>
        /// Whether the database still names this row's profile. False means the rule grants
        /// nothing, wherever it is and whatever it says.
        /// </summary>
        /// <remarks>
        /// A client holds only the names its record carries that the database's list still
        /// names, so removing a profile leaves its rules in place and inert. They are shown rather
        /// than dropped, because adding the name again puts every one of them back in force, and
        /// the node an operator is looking at is where they would want to know that.
        /// </remarks>
        public bool Named { get; private set; }

        public override string ToString()
        {
            return Profile + " " + Mask
                   + (Local ? string.Empty : " (inherited)")
                   + (Reaches ? string.Empty : " (refused here)")
                   + (Named ? string.Empty : " (not named)");
        }
    }

    /// <summary>
    /// Every decision the grant editor makes, with no window anywhere in it.
    /// </summary>
    /// <remarks>
    /// The editor is a table on somebody else's dialog, and a table is the easy half. What is
    /// worth testing is what it refuses, what it shows about rules it does not own, and exactly
    /// when it writes. So all of that lives here, where a test can drive it without a message pump,
    /// and `GrantPanel` is the part that draws it.
    ///
    /// **It writes only a change in meaning.** A group's or an entry's custom data keeps no
    /// modification times, so a value written back as it was changes nothing there; what matters
    /// is the same rules in other words. The entry dialog compares the entry it hands back with the
    /// one it was given, and a custom data value that differs by a byte is a changed entry: a new
    /// revision kept in its history, a new modification time, and a database marked as modified,
    /// for having been looked at. So `Changed` compares through the reader as well as literally,
    /// and a document written by hand that differs only in spacing is not rewritten for having
    /// been read.
    ///
    /// **No rules is the absent key, never an empty document.** A node whose grants have all been
    /// removed has to be indistinguishable from one that never had any. The exception is a node
    /// that refuses what it does not name, because that is a rule even with nothing beside it.
    /// </remarks>
    public sealed class GrantSheet
    {
        private readonly AclInheritance _above;

        /// <summary>
        /// What the store holds now, which starts as what it held when this opened and moves on
        /// every time a write is reported. Without that, a second commit after a real edit would
        /// write the same text again: a write that changes nothing, and one more chance for a store
        /// that can fail to refuse it.
        /// </summary>
        private string _stored;
        private string _canonical;

        private readonly List<string> _offered = new List<string>();

        /// <summary>The database's list exactly as given, or null when none was.</summary>
        private readonly HashSet<string> _named;
        private readonly Dictionary<string, List<AclVerb>> _rules =
            new Dictionary<string, List<AclVerb>>(StringComparer.Ordinal);

        /// <summary>
        /// How many rules one node may carry.
        /// </summary>
        /// <remarks>
        /// The document's own ceiling, `AclDocument.MostProfiles`, which the reader applies to
        /// every document whether it writes `default` or not. Taken from there rather than worked
        /// out again here, so that the rules this lets a node hold and the rules the reader will
        /// read back are one number.
        /// </remarks>
        public const int Most = AclDocument.MostProfiles;

        private AclDefault _default;

        /// <param name="stored">The document written on this node, or null when there is none.</param>
        /// <param name="above">
        /// The node this one hangs under, for what it inherits. Null at the root group, which
        /// inherits nothing.
        /// </param>
        /// <param name="offered">
        /// The profiles this database names, for the pick list. A rule may still name one that is
        /// not offered, written before the name was removed or by hand; it grants nothing, because
        /// a client holds only the names the list still names, and its row says so. Null asks
        /// nothing about the list and marks no row.
        /// </param>
        /// <param name="readOnlyReason">
        /// Why an edit here could not be kept, or null when it can. Set, the rules stay readable,
        /// which is the point of opening the tab, and nothing can be typed that would be discarded.
        /// </param>
        public GrantSheet(string stored, IAclNode above, IEnumerable<string> offered, string readOnlyReason)
        {
            _stored = stored;

            var already = stored == null ? null : AclDocument.Read(stored);
            _canonical = already == null ? null : already.Write();
            _above = above == null ? AclInheritance.None : AclResolver.Above(above);
            ReadOnlyReason = readOnlyReason;

            if (offered != null)
            {
                _named = new HashSet<string>(offered, StringComparer.Ordinal);

                foreach (var profile in offered)
                {
                    if (AuthorityIssuer.IsUsableProfileName(profile) && !_offered.Contains(profile))
                    {
                        _offered.Add(profile);
                    }
                }

                _offered.Sort(StringComparer.Ordinal);
            }

            Load();
        }

        /// <summary>
        /// True when this node holds something that will not read as a document.
        /// </summary>
        /// <remarks>
        /// Editing stops rather than overwriting it. An unintelligible rule refuses every client
        /// here and under this node that no nearer rule answers, so it may well be doing real
        /// work, and replacing it with whatever a grid happened to show would be destroying a rule
        /// nobody has read.
        /// </remarks>
        public bool Unreadable { get; private set; }

        /// <summary>Why an edit could not be kept, or null.</summary>
        public string ReadOnlyReason { get; private set; }

        /// <summary>Whether anything here can be changed at all.</summary>
        public bool CanEdit
        {
            get { return !Unreadable && string.IsNullOrEmpty(ReadOnlyReason); }
        }

        /// <summary>What this node does with a profile it says nothing about.</summary>
        public AclDefault Default
        {
            get { return _default; }
            set { _default = value; }
        }

        /// <summary>The profiles worth offering in the pick list, sorted.</summary>
        public IEnumerable<string> Offered
        {
            get { return _offered; }
        }

        /// <summary>
        /// The profiles a new rule here could be for: the ones this database names, less every one
        /// the table already shows.
        /// </summary>
        /// <remarks>
        /// **Inherited counts as shown.** A profile a group above grants is already a row here, and
        /// offering it again would be offering to add a second row for the same profile. What the
        /// operator wants for one of those is to change the row they can already see, which is what
        /// choosing a rule on it does: the row stops being inherited and becomes this node's own.
        ///
        /// So a profile leaves this list the moment it appears in the table, whichever way it got
        /// there, and comes back if its local rule is removed and nothing above names it.
        /// </remarks>
        public IEnumerable<string> Available
        {
            get
            {
                // The names, not the table. `Rows` is built to be drawn: it formats a mask for
                // every profile a group above grants, and this wants none of that to answer
                // which names are still free.
                var taken = new List<string>(_rules.Keys);
                foreach (var profile in _above.Profiles)
                {
                    taken.Add(profile);
                }

                var free = new List<string>();
                foreach (var profile in _offered)
                {
                    if (!taken.Contains(profile))
                    {
                        free.Add(profile);
                    }
                }

                return free;
            }
        }

        /// <summary>
        /// The one warning worth reading, or null. Ordered by what it costs the reader to miss:
        /// an edit that cannot be kept comes before an unreadable rule here, which comes before an
        /// unreadable chain above.
        /// </summary>
        public string Warning
        {
            get
            {
                if (!string.IsNullOrEmpty(ReadOnlyReason))
                {
                    return ReadOnlyReason;
                }

                if (Unreadable)
                {
                    return "The rules here cannot be read, so every client is refused here and below "
                        + "unless a nearer rule admits it. Discard them to edit this tab.";
                }

                if (!_above.Readable)
                {
                    return "A rule above this cannot be read, so nothing beyond it arrives here: a "
                        + "profile that neither this tab nor a group in between names is refused. "
                        + "Fix the groups above first.";
                }

                return null;
            }
        }

        /// <summary>
        /// Every row to show: what this node grants, and what it inherits for profiles it says
        /// nothing about.
        /// </summary>
        /// <remarks>
        /// Both in one table, because they are one answer. Sorted by profile so that the same
        /// database reads the same way twice, and inherited rows sit among the local ones rather
        /// than under them, because a reader comparing two profiles wants them next to each other.
        /// </remarks>
        public IList<GrantRow> Rows
        {
            get
            {
                var names = new List<string>(_rules.Keys);

                foreach (var profile in _above.Profiles)
                {
                    if (!_rules.ContainsKey(profile))
                    {
                        names.Add(profile);
                    }
                }

                names.Sort(StringComparer.Ordinal);

                var rows = new List<GrantRow>();
                foreach (var profile in names)
                {
                    var inherited = _above.For(profile);
                    var inheritedMask = inherited == null ? null : VerbMask.Of(inherited);

                    List<AclVerb> here;
                    if (_rules.TryGetValue(profile, out here))
                    {
                        rows.Add(new GrantRow(
                            profile, VerbMask.Of(here), true, inheritedMask, true, Named(profile)));
                        continue;
                    }

                    // An inherited row this node's own `deny` refuses, or its own unreadable
                    // document does. The resolver stops at this node and answers no for it
                    // either way, so a table showing it in force would be showing a permission
                    // nobody has, beside a warning saying it has stopped reaching.
                    rows.Add(new GrantRow(
                        profile, inheritedMask, false, inheritedMask,
                        !Unreadable && _default != AclDefault.Deny, Named(profile)));
                }

                return rows;
            }
        }

        /// <summary>
        /// Whether the database names a profile, which is whether a rule for it grants anything.
        /// </summary>
        /// <remarks>
        /// Asked of the names this sheet was given to offer, which are the database's list. A sheet
        /// given none at all, rather than an empty list, is one nobody asked about the list, and
        /// marks nothing: an editor whose list could not be read is given an empty one, and there
        /// every rule is marked, which is what the gate does with it too.
        /// </remarks>
        private bool Named(string profile)
        {
            return _named == null || _named.Contains(profile);
        }

        /// <summary>
        /// Writes a rule here, or says why it will not.
        /// </summary>
        /// <returns>Null when the rule is now stored, or one sentence saying what is wrong.</returns>
        public string Set(string profile, string mask)
        {
            if (!CanEdit)
            {
                return "Nothing here can be changed.";
            }

            // Keyed exactly, as `Remove` is, and for the same reason: a name with spaces round it
            // is one a document written by hand can hold and no certificate can, and trimming it
            // here would write a second rule beside it, for the name that does match, which
            // grants. So such a name is refused below as not being one.
            var name = profile ?? string.Empty;
            if (name.Trim().Length == 0)
            {
                return "Name the profile this rule is for.";
            }

            if (string.Equals(name, AclDocument.DefaultKey, StringComparison.OrdinalIgnoreCase))
            {
                // The one reserved word this format has, and it was reserved by the authority long
                // before the editor existed, so a client can never hold it as a profile either.
                return "\"" + AclDocument.DefaultKey + "\" is what this node does with profiles it "
                    + "does not name, so it cannot be one.";
            }

            if (!AuthorityIssuer.IsUsableProfileName(name))
            {
                return "A profile name is letters, digits, dash and underscore, up to 64 of them.";
            }

            var held = VerbMask.Read(mask);
            if (held == null)
            {
                return "A rule is " + VerbMask.Letters + ", with " + VerbMask.Absent
                    + " for anything it does not grant.";
            }

            // Refused here rather than discovered on the next open. The document this writes
            // carries `default` as well as the rules, so one more than the reader's ceiling is
            // one rule fewer than it looks. A document that will not read is not a lost rule,
            // it is every rule on this node refused, and every node under it with it.
            if (!_rules.ContainsKey(name) && _rules.Count >= Most)
            {
                return "A node holds " + Most + " rules, and this would be one more. Put the "
                    + "rest on the groups they are about.";
            }

            _rules[name] = new List<AclVerb>(held);
            return null;
        }

        /// <summary>
        /// Takes a rule off this node.
        /// </summary>
        /// <remarks>
        /// Which is not the same as granting nothing. Removing puts back whatever is inherited;
        /// an empty rule keeps this node saying so and grants nothing from here down.
        /// </remarks>
        public bool Remove(string profile)
        {
            // Keyed exactly, and deliberately not trimmed. A document written by hand can carry
            // a name with spaces round it; trimming here would find that rule, but trimming it
            // anywhere it is written back would turn a rule no certificate can match into one
            // that grants. Whoever asks has to hold the name the document holds.
            return CanEdit && profile != null && _rules.Remove(profile);
        }

        /// <summary>Whether this node's own document says anything about a profile.</summary>
        public bool Owns(string profile)
        {
            return profile != null && _rules.ContainsKey(profile);
        }

        /// <summary>
        /// Throws away a document that will not read, which is the only way past one.
        /// </summary>
        /// <remarks>
        /// Deliberately a decision of its own rather than something the first edit does quietly.
        /// What it discards is a rule that is refusing everything under this node, and the operator
        /// has to be the one who says so.
        /// </remarks>
        public void DiscardUnreadable()
        {
            // Not `CanEdit`, which its siblings ask and which is false whenever `Unreadable` is
            // true. Asking it here would turn off the one button that clears that. The
            // question is the other half of `CanEdit`: a tab with no store behind it must not
            // appear to destroy anything. Nothing can reach that state today, because a reason
            // to be read only is also a reason nothing was read to be unreadable, but that is
            // three files agreeing and a method that clears rules should not need them to.
            if (!string.IsNullOrEmpty(ReadOnlyReason) || !Unreadable)
            {
                return;
            }

            Unreadable = false;
            _default = AclDefault.Inherit;
            _rules.Clear();
        }

        /// <summary>
        /// What to store on this node, or null for no key at all.
        /// </summary>
        /// <remarks>
        /// Null when there is nothing to say, and a node that says nothing must be indistinguishable
        /// from one that never did: an empty document left behind would read as somebody having
        /// deliberately written "nothing" and would keep a modification time for a rule that no
        /// longer exists. A `deny` default is something to say even with no rules beside it.
        /// </remarks>
        public string Document
        {
            get
            {
                if (Unreadable)
                {
                    // Untouched, because nothing here understood it.
                    return _stored;
                }

                if (_rules.Count == 0 && _default == AclDefault.Inherit)
                {
                    return null;
                }

                var grants = new Dictionary<string, IEnumerable<AclVerb>>(StringComparer.Ordinal);
                foreach (var rule in _rules)
                {
                    grants[rule.Key] = rule.Value;
                }

                return AclDocument.Of(_default, grants).Write();
            }
        }

        /// <summary>
        /// Whether storing <see cref="Document"/> would change anything.
        /// </summary>
        /// <remarks>
        /// This is what stops opening a dialog from marking a database as modified. Compared
        /// through the reader as well as literally, so a document written by hand that differs only
        /// in spacing or in the order of its rules is not rewritten just for having been read.
        /// </remarks>
        public bool Changed
        {
            get
            {
                var wanted = Document;

                if (_stored == null || wanted == null)
                {
                    return !(_stored == null && wanted == null);
                }

                if (string.Equals(_stored, wanted, StringComparison.Ordinal))
                {
                    return false;
                }

                // Against the stored document written out again, so that the same rules spelled
                // differently do not read as a change. Worked out once when `_stored` was set,
                // because it cannot answer differently until that does, and because this is
                // asked on every edit, twice, with `Document` built each time either way.
                return _canonical == null || !string.Equals(_canonical, wanted, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Told after a write lands, so that what is stored and what is on screen agree again.
        /// </summary>
        /// <remarks>
        /// Reported by the caller rather than assumed by the setter, because whether a write
        /// happened is the store's answer and not this one's.
        /// </remarks>
        public void Stored()
        {
            if (!Unreadable)
            {
                // What was written is what this wrote, so it is already the canonical form and
                // needs no round trip to find that out.
                _stored = Document;
                _canonical = _stored;
            }
        }

        /// <summary>
        /// Takes what the store holds now, when something other than this sheet has changed it.
        /// </summary>
        /// <returns>True when it had changed, and the rules on the sheet are now what it holds.</returns>
        /// <remarks>
        /// **The dialog's copy has a second editor**, on the same dialog: KeePass's own Plugin Data
        /// tab, whose Delete takes the `KPG` key out of it. A sheet that went on holding what it
        /// read when the dialog opened showed rules the dialog's OK was about to drop, judged the
        /// same rule chosen again as unchanged and wrote nothing, and on any other edit wrote the
        /// whole old document back over the delete without a word. So the copy wins over whatever
        /// is on the sheet, which is nothing unwritten, since every edit is written as it is made.
        /// </remarks>
        public bool Reload(string stored)
        {
            if (string.Equals(stored, _stored, StringComparison.Ordinal))
            {
                return false;
            }

            _stored = stored;

            var already = stored == null ? null : AclDocument.Read(stored);
            _canonical = already == null ? null : already.Write();

            _rules.Clear();
            Unreadable = false;
            Load();
            return true;
        }

        /// <summary>Back to what is stored, throwing away everything since.</summary>
        /// <remarks>
        /// For a write the store refused. The rule has been reported as not in force, so it
        /// must not be sitting on the sheet afterwards: it would still be there to be carried
        /// by the next write of anything else, landing without a second word about it.
        /// </remarks>
        public void Revert()
        {
            _rules.Clear();
            Unreadable = false;
            Load();
        }

        private void Load()
        {
            _default = AclDefault.Inherit;

            if (string.IsNullOrEmpty(_stored))
            {
                return;
            }

            var document = AclDocument.Read(_stored);
            if (document == null)
            {
                Unreadable = true;
                return;
            }

            _default = document.Default;
            foreach (var profile in document.Profiles)
            {
                _rules[profile] = new List<AclVerb>(document.Grant(profile));
            }
        }
    }
}
