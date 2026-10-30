using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Vault;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A database in a dictionary, so the API's handlers can be driven without a password manager.
    /// </summary>
    /// <remarks>
    /// The whole reason `IVault` is a seam. What the handlers do to a tree, what they say about it
    /// and what they refuse are all decidable here in a few lines each; `KdbxVaultTests` is where
    /// the same operations meet a real KDBX4, and it is short because this is where the thinking
    /// happens.
    ///
    /// The nodes it hands out are `TestNode`s wired into the same shape as the tree, so a rule
    /// written on a group really does reach the entries under it.
    /// </remarks>
    internal sealed class FakeVault : IVault
    {
        private readonly Dictionary<string, Node> _groups = new Dictionary<string, Node>(StringComparer.Ordinal);
        private readonly Dictionary<string, Held> _entries = new Dictionary<string, Held>(StringComparer.Ordinal);
        private readonly Node _root;

        private int _next;
        private string _writeBlocked;
        private int _lookupsBefore;
        private Action _then;

        public FakeVault()
        {
            _root = new Node(Identifier(), "Invented", string.Empty, null, TestNode.Root("root"));
            _groups[_root.Uuid] = _root;
        }

        /// <summary>Set to refuse every change, the way a locked or busy database does.</summary>
        /// <remarks>A database <see cref="Lock"/> has locked refuses every change whatever this says,
        /// with the reason the real vault gives.</remarks>
        public string WriteBlocked
        {
            get { return IsOpen ? _writeBlocked : "the database is not open"; }
            set { _writeBlocked = value; }
        }

        /// <summary>Whether the database is open, which it is until <see cref="Lock"/>.</summary>
        public bool IsOpen { get; private set; } = true;

        /// <summary>
        /// Locks the database, as KeePass's lock or File, Close does a real one: every lookup finds
        /// nothing from then on, and every change is refused the way a closed database refuses it.
        /// </summary>
        public void Lock()
        {
            IsOpen = false;
        }

        /// <summary>
        /// Runs something once this many lookups and changes have been answered, which is a delete,
        /// a lock or a close landing at a chosen moment of a request.
        /// </summary>
        /// <remarks>
        /// **One is the moment between the router and the handler.** The router asks the vault
        /// once, for the node it decides about, and everything after that is the handler's, so one
        /// lookup answered lets the router decide about a node the handler then cannot find. A
        /// change counts as well, because a write's handler may ask for nothing before it writes.
        /// </remarks>
        public void After(int lookups, Action then)
        {
            _lookupsBefore = lookups;
            _then = then;
        }

        /// <summary>Set to make every change come back busy, as a dialog on the node does.</summary>
        public string Busy { get; set; }

        public string RootAlias
        {
            get { return "root"; }
        }

        public string RootUuid
        {
            get { return _root.Uuid; }
        }

        /// <summary>What a real database protects by default, which is enough to test with.</summary>
        /// <remarks>
        /// Asked with the comparer the store looks names up by, as the real one is, so that a key
        /// the database would read as the password is the password here too.
        /// </remarks>
        public bool Protects(string field)
        {
            return Comparer<string>.Default.Compare(field, "Password") == 0;
        }

        /// <summary>
        /// Whether one of an entry's strings is protected, which is the name or the flag.
        /// </summary>
        /// <remarks>
        /// The real vault asks both halves, so this does: a string is protected because of what it
        /// is called, which the database decides, or because somebody marked that one.
        /// </remarks>
        private bool Guards(Held entry, string name)
        {
            return Protects(name) || entry.Protected.Contains(name);
        }

        /// <summary>Whether the entry carries a name byte for byte, which is what a route resolves.</summary>
        /// <remarks>
        /// The real vault's rule, because the whole point of this class is that a handler cannot
        /// tell the two apart. A name the store cannot keep apart from a stored one is not a
        /// second name: reaching it is nothing there, and adding it is a conflict.
        /// </remarks>
        private static bool Holds(IEnumerable<string> keys, string name)
        {
            if (name == null)
            {
                return false;
            }

            foreach (var key in keys)
            {
                if (string.Equals(key, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether any of these names would be put onto a key spelled differently.</summary>
        private static bool Clashes(IEnumerable<string> keys, IEnumerable<string> names)
        {
            var stored = new SortedDictionary<string, string>();

            foreach (var key in keys)
            {
                stored[key] = key;
            }

            foreach (var name in names)
            {
                if (name == null)
                {
                    continue;
                }

                string kept;
                if (!stored.TryGetValue(name, out kept))
                {
                    stored[name] = name;
                    continue;
                }

                if (!string.Equals(kept, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Writes a rule on a group or an entry, which is what a grant editor does.</summary>
        public FakeVault Granting(string uuid, string document)
        {
            var node = NodeOf(uuid);
            Assert(node != null, "there is nothing called " + uuid);
            node.Holding(document);
            return this;
        }

        /// <summary>Adds a group, without going through the rules.</summary>
        public string Add(string parentUuid, string name)
        {
            var parent = Find(parentUuid);
            Assert(parent != null, "there is no group called " + parentUuid);

            var group = new Node(Identifier(), name, string.Empty, parent.Uuid, parent.Rules.Child(name));
            _groups[group.Uuid] = group;
            return group.Uuid;
        }

        /// <summary>Adds an entry, without going through the rules.</summary>
        public string AddEntry(string groupUuid, string title)
        {
            var group = Find(groupUuid);
            Assert(group != null, "there is no group called " + groupUuid);

            var entry = new Held(Identifier(), group.Uuid, group.Rules.Child(title));
            entry.Fields["Title"] = title;
            _entries[entry.Uuid] = entry;
            return entry.Uuid;
        }

        /// <summary>Marks a field this entry already has as protected.</summary>
        /// <remarks>
        /// The route for it would be a create, and a create refuses a name that is taken. What a
        /// test wants here is the state, not the journey.
        /// </remarks>
        public void Protect(string entryUuid, string field)
        {
            // `Add` answers whether it was new, so no `Contains` in front of it.
            _entries[entryUuid].Protected.Add(field);
        }

        /// <summary>Gives an entry an expiry, or takes it away with null, as KeePass's dialog does.</summary>
        public void Expire(string entryUuid, DateTimeOffset? when)
        {
            _entries[entryUuid].Expires = when;
        }

        /// <summary>
        /// How many days KeePass's options give a new entry before it expires, negative for none,
        /// which is what KeePass itself answers until somebody switches the option on.
        /// </summary>
        public int NewEntryExpiresInDays { get; set; } = -1;

        public VaultGroup Group(string uuid)
        {
            Looking();

            var group = Find(uuid);
            return group == null ? null : group.Describe();
        }

        /// <summary>How many tags have been taken, so that a test can say a route took none.</summary>
        public int TagsTaken { get; private set; }

        public string GroupTag(string uuid)
        {
            Looking();

            var group = Find(uuid);
            if (group == null)
            {
                return null;
            }

            TagsTaken++;
            return group.Tag();
        }

        public string EntryTag(string uuid)
        {
            Looking();

            if (string.Equals(uuid, Vanishes, StringComparison.Ordinal))
            {
                return null;
            }

            var entry = FindEntry(uuid);
            return entry == null ? null : Tag(entry);
        }

        /// <summary>
        /// What the entry holds, condensed as the real vault condenses it: protection as the
        /// database decides it, and an attachment's bytes only up to the largest the gate serves,
        /// each one counted as a read, since the real vault decrypts it to hash it.
        /// </summary>
        private string Tag(Held entry)
        {
            TagsTaken++;

            var parts = new List<string>
            {
                "entry",
                entry.Uuid,
                entry.Group,
                entry.Expires.HasValue
                    ? entry.Expires.Value.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "never",
            };

            foreach (var field in entry.Fields)
            {
                parts.Add(field.Key);
                parts.Add(Guards(entry, field.Key) ? "protected" : "plain");
                parts.Add(field.Value);
            }

            foreach (var attachment in entry.Attachments)
            {
                parts.Add(attachment.Key);
                parts.Add(attachment.Value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

                if (attachment.Value.Length <= VaultAttachment.MostServed)
                {
                    AttachmentReads++;
                    parts.Add(Convert.ToBase64String(attachment.Value));
                }
            }

            return Tags.Of(parts.ToArray());
        }

        /// <summary>The entry as one lookup describes it, saying whether it holds a protected value.</summary>
        private VaultEntry Single(Held entry)
        {
            return entry.Describe(entry.Fields.Keys.Any(n => Guards(entry, n)));
        }

        public IReadOnlyList<VaultGroup> Subgroups(string uuid)
        {
            Looking();

            var group = Find(uuid);

            return group == null
                ? null
                : _groups.Values.Where(g => g.Parent == group.Uuid).Select(g => g.Describe()).ToList();
        }

        public IReadOnlyList<VaultGroup> Groups()
        {
            Looking();

            return IsOpen ? _groups.Values.Select(g => g.Describe()).ToList() : null;
        }

        public VaultEntry Entry(string uuid)
        {
            Looking();

            if (string.Equals(uuid, Vanishes, StringComparison.Ordinal))
            {
                return null;
            }

            var entry = FindEntry(uuid);
            return entry == null ? null : Single(entry);
        }

        /// <summary>An entry that is there until it is read, which is a delete mid-request.</summary>
        /// <remarks>
        /// The only way to hold still the race the read routes have to survive: the value is
        /// fetched, and by the time the entry is looked up again to ask the rules about it, the
        /// operator has moved it to the recycle bin.
        ///
        /// Both lookups that carry a node answer nothing for it, because a route reaching for the
        /// node it was going to ask about is what the race is: which of the two a handler happens
        /// to call is not something the operator's delete knows about.
        /// </remarks>
        public string Vanishes { get; set; }

        /// <summary>
        /// How many times the whole of an entry was asked for, values included.
        /// </summary>
        /// <remarks>
        /// The real vault answers this by decrypting every string the entry holds, so a route that
        /// wants one value and calls it takes every other one out of protected memory as well.
        /// Counted rather than assumed, because nothing else here can see the difference.
        /// </remarks>
        public int Detailed { get; private set; }

        public VaultEntryDetail Read(string uuid)
        {
            Looking();
            Detailed++;

            if (string.Equals(uuid, Vanishes, StringComparison.Ordinal))
            {
                return null;
            }

            var entry = FindEntry(uuid);

            return entry == null
                ? null
                : new VaultEntryDetail(
                    Single(entry),
                    new Dictionary<string, string>(entry.Fields, StringComparer.Ordinal),
                    entry.Fields.Keys.Where(n => Guards(entry, n)).ToList());
        }

        public IReadOnlyList<VaultEntry> Entries(string groupUuid)
        {
            Looking();

            var group = Find(groupUuid);

            return group == null
                ? null
                : _entries.Values.Where(e => e.Group == group.Uuid).Select(e => e.Describe()).ToList();
        }

        public IReadOnlyList<VaultEntry> Entries()
        {
            Looking();

            return IsOpen ? _entries.Values.Select(e => e.Describe()).ToList() : null;
        }

        public IReadOnlyList<VaultField> Fields(string entryUuid)
        {
            Looking();

            var entry = FindEntry(entryUuid);

            return entry == null
                ? null
                : entry.Fields.Keys.Select(n => new VaultField(n, Guards(entry, n))).ToList();
        }

        public string Field(string entryUuid, string name)
        {
            Looking();

            var entry = FindEntry(entryUuid);
            string value;

            return entry != null && Holds(entry.Fields.Keys, name) && entry.Fields.TryGetValue(name, out value)
                ? value
                : null;
        }

        public IReadOnlyList<VaultAttachment> Attachments(string entryUuid)
        {
            Looking();

            var entry = FindEntry(entryUuid);

            return entry == null
                ? null
                : entry.Attachments
                    .Select(a => new VaultAttachment(a.Key, a.Value.Length))
                    .ToList();
        }

        /// <summary>
        /// Hands over an attachment's contents whatever size it is when set, as a vault that broke
        /// its promise would.
        /// </summary>
        /// <remarks>
        /// What `ReadAttachment`'s own check of what it was handed is for, and without this
        /// nothing here could ever hand it more than it asked for.
        /// </remarks>
        public bool IgnoresCeiling { get; set; }

        /// <summary>
        /// How many attachments have been handed over with their contents, which is how many were
        /// decrypted, so that a test can say an answer was refused before anything was decrypted
        /// rather than after.
        /// </summary>
        public int AttachmentReads { get; set; }

        public VaultAttachment Attachment(string entryUuid, string name, int most)
        {
            Looking();

            var content = Attachment(entryUuid, name);
            if (content == null)
            {
                return null;
            }

            if (content.Length > most && !IgnoresCeiling)
            {
                return new VaultAttachment(name, content.Length, null);
            }

            AttachmentReads++;
            return new VaultAttachment(name, content.Length, content);
        }

        /// <summary>
        /// What is stored under this name, for a test to look at: nothing is counted and no
        /// ceiling applies.
        /// </summary>
        public byte[] Attachment(string entryUuid, string name)
        {
            var entry = FindEntry(entryUuid);
            byte[] content;

            return entry != null && Holds(entry.Attachments.Keys, name)
                && entry.Attachments.TryGetValue(name, out content)
                    ? content
                    : null;
        }

        public VaultWrite CreateGroup(string parentUuid, string name, string notes)
        {
            return Change(() =>
            {
                var parent = Find(parentUuid);
                if (parent == null)
                {
                    return VaultWrite.Missing();
                }

                if (Busy != null)
                {
                    return VaultWrite.Busy(Busy);
                }

                var uuid = Add(parent.Uuid, name);
                _groups[uuid].Notes = notes ?? string.Empty;
                return VaultWrite.Created(uuid);
            });
        }

        public VaultWrite UpdateGroup(string uuid, string name, string notes, VaultTagging tagging)
        {
            return Change(() =>
            {
                var group = Find(uuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => group.Tag());
                if (refused != null)
                {
                    return refused;
                }

                group.Name = name ?? group.Name;
                group.Notes = notes ?? group.Notes;
                return VaultWrite.Done(tagging.Answers ? group.Tag() : null);
            });
        }

        public VaultWrite DeleteGroup(string uuid, VaultTagging tagging)
        {
            return Change(() =>
            {
                var group = Find(uuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                if (group.Parent == null)
                {
                    return VaultWrite.Conflict("the root group cannot be removed");
                }

                if (_groups.Values.Any(g => g.Parent == group.Uuid) ||
                    _entries.Values.Any(e => e.Group == group.Uuid))
                {
                    return VaultWrite.Conflict("the group still holds something");
                }

                var refused = Refused(tagging, () => group.Tag());
                if (refused != null)
                {
                    return refused;
                }

                _groups.Remove(group.Uuid);
                return VaultWrite.Done();
            });
        }

        public VaultWrite CreateEntry(
            string groupUuid, IReadOnlyDictionary<string, string> fields, VaultExpiry expiry = null)
        {
            return Change(() =>
            {
                var group = Find(groupUuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                if (Busy != null)
                {
                    return VaultWrite.Busy(Busy);
                }

                var uuid = AddEntry(group.Uuid, string.Empty);
                var entry = _entries[uuid];

                foreach (var name in new[] { "Title", "UserName", "Password", "URL", "Notes" })
                {
                    entry.Fields[name] = string.Empty;
                }

                entry.Protected.Add("Password");

                if (fields != null)
                {
                    if (Clashes(entry.Fields.Keys, fields.Keys))
                    {
                        _entries.Remove(uuid);
                        return VaultWrite.Conflict("that field is already there");
                    }

                    foreach (var field in fields)
                    {
                        entry.Fields[field.Key] = field.Value;
                    }
                }

                // The real vault's rule, templates group aside, which nothing here has.
                if (expiry != null && expiry.IsAuto)
                {
                    entry.Expires = NewEntryExpiresInDays < 0
                        ? (DateTimeOffset?)null
                        : DateTimeOffset.UtcNow.AddDays(NewEntryExpiresInDays);
                }
                else if (expiry != null)
                {
                    entry.Expires = expiry.When;
                }

                return VaultWrite.Created(uuid);
            });
        }

        public VaultWrite UpdateEntry(
            string uuid,
            IReadOnlyDictionary<string, string> fields,
            VaultTagging tagging,
            VaultExpiry expiry = null)
        {
            if (expiry != null && expiry.IsAuto)
            {
                return VaultWrite.Failed("only a new entry takes the expiry the options set");
            }

            return Change(() =>
            {
                var entry = FindEntry(uuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                if (fields != null && Clashes(entry.Fields.Keys, fields.Keys))
                {
                    return VaultWrite.Conflict("that field is already there");
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        entry.Fields[field.Key] = field.Value;
                    }
                }

                if (expiry != null)
                {
                    entry.Expires = expiry.When;
                }

                return VaultWrite.Done(Left(tagging, entry));
            });
        }

        public VaultWrite DeleteEntry(string uuid, VaultTagging tagging)
        {
            return Change(() =>
            {
                var entry = FindEntry(uuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                _entries.Remove(entry.Uuid);
                return VaultWrite.Done();
            });
        }

        public VaultWrite CreateField(
            string entryUuid, string name, string value, bool isProtected, VaultTagging tagging)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                if (name == null || entry.Fields.ContainsKey(name))
                {
                    return VaultWrite.Conflict("that field is already there");
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                entry.Fields[name] = value ?? string.Empty;

                if (isProtected)
                {
                    entry.Protected.Add(name);
                }

                return VaultWrite.Created(name, Left(tagging, entry));
            });
        }

        public VaultWrite UpdateField(string entryUuid, string name, string value, VaultTagging tagging)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null || !Holds(entry.Fields.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                entry.Fields[name] = value ?? string.Empty;
                return VaultWrite.Done(Left(tagging, entry));
            });
        }

        public VaultWrite DeleteField(string entryUuid, string name, VaultTagging tagging)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null || !Holds(entry.Fields.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                entry.Fields.Remove(name);
                entry.Protected.Remove(name);
                return VaultWrite.Done(Left(tagging, entry));
            });
        }

        public VaultWrite CreateAttachment(string entryUuid, string name, byte[] content, VaultTagging tagging)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                if (name == null || entry.Attachments.ContainsKey(name))
                {
                    return VaultWrite.Conflict("that attachment is already there");
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                entry.Attachments[name] = content ?? Array.Empty<byte>();
                return VaultWrite.Created(name, Left(tagging, entry));
            });
        }

        public VaultWrite UpdateAttachment(string entryUuid, string name, byte[] content, VaultTagging tagging)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null || !Holds(entry.Attachments.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                entry.Attachments[name] = content ?? Array.Empty<byte>();
                return VaultWrite.Done(Left(tagging, entry));
            });
        }

        public VaultWrite DeleteAttachment(string entryUuid, string name, VaultTagging tagging)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null || !Holds(entry.Attachments.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => Tag(entry));
                if (refused != null)
                {
                    return refused;
                }

                entry.Attachments.Remove(name);
                return VaultWrite.Done(Left(tagging, entry));
            });
        }

        /// <summary>The node a group or an entry hands the rules, for a test that writes one.</summary>
        public TestNode NodeOf(string uuid)
        {
            var group = Find(uuid);
            if (group != null)
            {
                return group.Rules;
            }

            var entry = FindEntry(uuid);
            return entry == null ? null : entry.Node;
        }

        /// <summary>
        /// The refusals every change makes once the node is found and its content-independent
        /// refusals are made, in the real vault's order: a dialog on the node, then the condition.
        /// </summary>
        /// <remarks>
        /// That order is RFC 9110 section 13.2.1's, and the real vault's, so this keeps it: a fake
        /// answering `503` where the vault answers `412`, or the other way round, would let a
        /// handler test pass on an order nothing has. A change that turns out to change nothing is
        /// asked the condition as well, since the real vault asks it before it compares anything.
        /// </remarks>
        private VaultWrite Refused(VaultTagging tagging, Func<string> tag)
        {
            if (tagging == null)
            {
                throw new ArgumentNullException(nameof(tagging));
            }

            if (Busy != null)
            {
                return VaultWrite.Busy(Busy);
            }

            return VaultPrecondition.Fails(tagging.Precondition, tagging.NeedsTag ? tag() : null)
                ? VaultWrite.PreconditionFailed()
                : null;
        }

        private string Left(VaultTagging tagging, Held entry)
        {
            return tagging.Answers ? Tag(entry) : null;
        }

        private VaultWrite Change(Func<VaultWrite> change)
        {
            Looking();

            // Not `Busy`, which each change answers itself once it has found the node, so that a
            // node gone or a name taken is answered first, as the real vault answers them.
            return WriteBlocked != null ? VaultWrite.Failed(WriteBlocked) : change();
        }

        /// <summary>Counts one lookup or change, and runs what <see cref="After"/> left once it is due.</summary>
        /// <remarks>
        /// Before whatever it counts, so the event lands between the last answer that was due and
        /// this one. Cleared before it runs, because what it runs may well be a change of its own.
        /// </remarks>
        private void Looking()
        {
            if (_then == null)
            {
                return;
            }

            if (_lookupsBefore > 0)
            {
                _lookupsBefore--;
                return;
            }

            var then = _then;
            _then = null;
            then();
        }

        private Node Find(string uuid)
        {
            if (uuid == null || !IsOpen)
            {
                return null;
            }

            if (string.Equals(uuid, RootAlias, StringComparison.Ordinal))
            {
                return _root;
            }

            Node group;
            return _groups.TryGetValue(uuid, out group) ? group : null;
        }

        private Held FindEntry(string uuid)
        {
            Held entry;
            return uuid != null && IsOpen && _entries.TryGetValue(uuid, out entry) ? entry : null;
        }

        private string Identifier()
        {
            _next++;
            return _next.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class Node
        {
            public Node(string uuid, string name, string notes, string parent, TestNode rules)
            {
                Uuid = uuid;
                Name = name;
                Notes = notes;
                Parent = parent;
                Rules = rules;
            }

            public string Uuid { get; private set; }

            public string Name { get; set; }

            public string Notes { get; set; }

            public string Parent { get; private set; }

            public TestNode Rules { get; private set; }

            public VaultGroup Describe()
            {
                return new VaultGroup(
                    Uuid, Name, Notes, Parent, DateTimeOffset.MinValue, DateTimeOffset.MinValue, Rules);
            }

            /// <summary>What the group holds, condensed, as the real vault condenses it.</summary>
            public string Tag()
            {
                return Tags.Of("group", Uuid, Parent, Name, Notes);
            }
        }

        private sealed class Held
        {
            public Held(string uuid, string group, TestNode node)
            {
                Uuid = uuid;
                Group = group;
                Node = node;
            }

            public string Uuid { get; private set; }

            public string Group { get; private set; }

            public TestNode Node { get; private set; }

            /// <summary>
            /// KeePass's own shape, which is a sorted dictionary under the default comparer.
            /// </summary>
            /// <remarks>
            /// **Not a choice about tidiness.** The default comparer compares by the current
            /// culture, and that collation gives `U+00AD`, `U+200D`, `U+2060` and `U+FEFF` no
            /// weight and reads a composed character and its decomposed spelling as one. Keyed
            /// byte for byte, as these were, no case here could express a name the database
            /// cannot tell from another, so the guard cases passed against a store the real one
            /// does not resemble and a fix that only touched the handlers would have looked
            /// finished.
            /// </remarks>
            public SortedDictionary<string, string> Fields { get; } =
                new SortedDictionary<string, string>();

            public SortedSet<string> Protected { get; } = new SortedSet<string>();

            public SortedDictionary<string, byte[]> Attachments { get; } =
                new SortedDictionary<string, byte[]>();

            /// <summary>When it expires, or null when it does not, as the real vault describes one.</summary>
            public DateTimeOffset? Expires { get; set; }

            /// <summary>The entry as the real vault describes one.</summary>
            /// <param name="holdsProtected">
            /// Whether any of its strings is protected, which a single lookup says and a listing
            /// leaves null, as in the real vault.
            /// </param>
            public VaultEntry Describe(bool? holdsProtected = null)
            {
                string title;
                string url;

                Fields.TryGetValue("Title", out title);
                Fields.TryGetValue("URL", out url);

                return new VaultEntry(
                    Uuid, Group, title ?? string.Empty, url ?? string.Empty,
                    DateTimeOffset.MinValue, DateTimeOffset.MinValue, Node,
                    Protected.Contains("Title"), Protected.Contains("URL"), Expires,
                    holdsProtected);
            }
        }

        /// <summary>A digest of the parts, each behind its length, as the real vault's tags are made.</summary>
        /// <remarks>
        /// Content rather than a counter, so that a change that puts back what was there gives
        /// the tag back as well, which is what the real one does, and with no key, since nothing
        /// here is a secret.
        /// </remarks>
        private static class Tags
        {
            public static string Of(params string[] parts)
            {
                var joined = new System.Text.StringBuilder();
                foreach (var part in parts)
                {
                    var text = part ?? string.Empty;
                    joined.Append(text.Length).Append(':').Append(text);
                }

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var digest = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(joined.ToString()));
                    return BitConverter.ToString(digest, 0, 16).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
        }
    }
}
