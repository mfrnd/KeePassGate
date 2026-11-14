using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Vault;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A database in a dictionary, so the API's handlers can be driven without a password manager.
    /// </summary>
    /// <remarks>
    /// The whole reason `IVault` is a seam. What the handlers do to a tree, what they say about it
    /// and what they refuse are all decidable here in a few lines each; `KdbxVaultTests` is where
    /// the same operations meet a real KDBX4, and what it asks is whether the real vault holds to
    /// what is decided here.
    ///
    /// The nodes it hands out are `TestNode`s wired into the same shape as the tree, so a rule
    /// written on a group really does reach the entries under it.
    /// </remarks>
    internal sealed class FakeVault : IVault
    {
        /// <summary>The five strings every entry KeePass makes holds.</summary>
        private static readonly string[] Standard = { "Title", "UserName", "Password", "URL", "Notes" };

        private readonly Dictionary<string, Node> _groups = new Dictionary<string, Node>(StringComparer.Ordinal);
        private readonly Dictionary<string, Kept> _entries = new Dictionary<string, Kept>(StringComparer.Ordinal);
        private readonly Node _root;

        private int _next;
        private string _writeBlocked;
        private int _lookupsBefore;
        private Action _then;
        private Action _waiting;
        private int _held;
        private int _changing;

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
        /// **For a change, one is the moment between the router and the write.** The router asks
        /// the vault once, for the node it may refuse the change at, and a write's handler asks
        /// for nothing before it writes, so one lookup answered lets the router pass a change
        /// whose turn then finds something else. The change counts as well: it is the handler's
        /// first and only call, and what it asks the rules is asked inside it.
        ///
        /// **Under a hold it waits for the hold.** What an operator does is done on KeePass's
        /// thread, and a read that holds that thread is over before any of it happens, so
        /// whatever comes due inside <see cref="Held"/> runs once the hold is let go. A hold set
        /// to lapse holds nothing, and there it runs where it came due, under the read.
        /// </remarks>
        public void After(int lookups, Action then)
        {
            _lookupsBefore = lookups;
            _then = then;
            _waiting = null;
        }

        /// <summary>Set to make every change come back busy, as a dialog on the node does.</summary>
        public string Busy { get; set; }

        public string RootAlias
        {
            get { return "root"; }
        }

        /// <summary>
        /// How many reads have been held by <see cref="Held"/>, each counted once however many
        /// holds it took inside itself.
        /// </summary>
        public int HeldReads { get; private set; }

        /// <summary>
        /// How many lookups were answered with nothing held: outside every <see cref="Held"/>
        /// and outside every change.
        /// </summary>
        /// <remarks>
        /// What a request that changes nothing may never do. Two lookups each held alone are two
        /// moments of the database, with room between them for whatever the operator does next.
        /// </remarks>
        public int LooseLookups { get; private set; }

        /// <summary>
        /// How many times a value was measured or read, an attachment read or digested, or a
        /// tag taken, with a hold standing.
        /// </summary>
        /// <remarks>
        /// What a read of an entry may never do: each of those grows with the entry, and the
        /// hold is KeePass's own thread standing still. A view is taken inside the hold and
        /// asked for its values after it.
        /// </remarks>
        public int ValueWorkHeld { get; private set; }

        /// <summary>Set to refuse every hold, as KeePass's thread does while it cannot be held.</summary>
        public bool RefusesHolds { get; set; }

        /// <summary>
        /// Set to let every hold lapse: the read runs to its end and is refused then, as a read
        /// that outlasted the hold on KeePass's thread is.
        /// </summary>
        public bool Lapses { get; set; }

        /// <summary>
        /// Reads where it is, since a database in a dictionary has no thread to hold, and keeps
        /// count of what was read inside a hold and what was not.
        /// </summary>
        public T Held<T>(Func<T> read)
        {
            if (_held > 0)
            {
                // A hold inside a hold is the same hold, as it is on KeePass's thread.
                return read();
            }

            if (RefusesHolds)
            {
                throw new VaultUnsettled("KeePass's thread could not be held");
            }

            HeldReads++;
            _held++;

            T answer;
            try
            {
                answer = read();
            }
            finally
            {
                _held--;

                // The first moment KeePass's thread has for what the operator asked of it
                // meanwhile.
                var waiting = _waiting;
                _waiting = null;
                waiting?.Invoke();
            }

            if (Lapses)
            {
                throw new VaultUnsettled("the read took longer than KeePass's thread could be held for");
            }

            return answer;
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
        private bool Guards(Kept entry, string name)
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
            var node = NodeOf(uuid)
                ?? throw new InvalidOperationException("there is nothing called " + uuid);
            node.Holding(document);
            return this;
        }

        /// <summary>Adds a group, without going through the rules.</summary>
        public string Add(string parentUuid, string name)
        {
            var parent = Find(parentUuid)
                ?? throw new InvalidOperationException("there is no group called " + parentUuid);

            var group = new Node(Identifier(), name, string.Empty, parent.Uuid, parent.Rules.Child(name));
            _groups[group.Uuid] = group;
            return group.Uuid;
        }

        /// <summary>
        /// Adds an entry holding only its title, without going through the rules.
        /// </summary>
        /// <remarks>
        /// Not the shape KeePass or the gate makes: both give every entry all five standard
        /// strings, the password among them protected, which <see cref="CreateEntry"/> does here
        /// too. An entry holding only its title is how an entry another program wrote can arrive,
        /// and it holds no protected value, so the view of a rule without `protected` withholds
        /// nothing of it.
        /// </remarks>
        public string AddEntry(string groupUuid, string title)
        {
            var group = Find(groupUuid)
                ?? throw new InvalidOperationException("there is no group called " + groupUuid);

            var entry = new Kept(Identifier(), group.Uuid, group.Rules.Child(title));
            entry.Fields["Title"] = title;
            _entries[entry.Uuid] = entry;
            return entry.Uuid;
        }

        /// <summary>
        /// Sets a string on an entry without going through the rules or a route's own limits, as
        /// KeePass or a merge can.
        /// </summary>
        public void Plant(string entryUuid, string field, string value)
        {
            var entry = FindEntry(entryUuid)
                ?? throw new InvalidOperationException("there is no entry called " + entryUuid);
            entry.Fields[field] = value;
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

        /// <summary>
        /// Whether a field was stored with its own protection flag set, apart from whether the
        /// database protects its name.
        /// </summary>
        /// <remarks>
        /// What the real vault stores with the value, and what every lookup here folds into one
        /// answer with the name, so a write that dropped the flag for a name the database protects
        /// could not otherwise be told from a write that kept it.
        /// </remarks>
        public bool Flagged(string entryUuid, string field)
        {
            return _entries[entryUuid].Protected.Contains(field);
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

            if (string.Equals(uuid, Vanishes, StringComparison.Ordinal))
            {
                return null;
            }

            var group = Find(uuid);
            return group == null ? null : group.Describe();
        }

        /// <summary>How many tags have been taken, so that a test can say a route took none.</summary>
        public int TagsTaken { get; private set; }

        /// <summary>
        /// Whether the caller of the last change the rules allowed was found to read every value
        /// that change named, which is what the real vault skips an update on.
        /// </summary>
        /// <remarks>
        /// This vault writes every update, so the skip itself is the host tier's to pin, over
        /// the real vault. What is pinned here is the answer, read off the caller's view of the
        /// node as the real vault reads it, from the sight a handler hands over.
        /// </remarks>
        public bool Saw { get; private set; }

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

        public VaultEntryView View(string uuid, Func<VaultFound, VaultSight> sight)
        {
            Looking();

            if (string.Equals(uuid, Vanishes, StringComparison.Ordinal))
            {
                return null;
            }

            var entry = FindEntry(uuid);
            if (entry == null)
            {
                return null;
            }

            var described = entry.Placed();
            return Seen(
                entry,
                described,
                sight == null ? null : sight(new VaultFound(described, false)));
        }

        /// <summary>
        /// An entry's tag as a change finds the entry, of the view its caller has of the entry,
        /// as the real vault takes it: null for a sight that does not read.
        /// </summary>
        private string Found(Kept entry, VaultTagging tagging)
        {
            return Viewed(entry, tagging).Tag;
        }

        /// <summary>
        /// An entry as the caller of a change may read it, as the change finds the entry: the
        /// view its tag is taken of and the view that decides what its caller reads.
        /// </summary>
        private VaultEntryView Viewed(Kept entry, VaultTagging tagging)
        {
            return Seen(entry, entry.Placed(), tagging.Sight);
        }

        /// <summary>
        /// Whether a listing of entries shows the string stored under this name, as the real
        /// vault decides it: the title and the URL, which <see cref="Kept.Describe"/> reads for
        /// a listing, compared as the store compares names and not byte for byte.
        /// </summary>
        private static bool Listed(string name)
        {
            return Comparer<string>.Default.Compare(name, "Title") == 0
                   || Comparer<string>.Default.Compare(name, "URL") == 0;
        }

        /// <summary>
        /// An entry as far as a sight reaches into it, as the real vault builds a view: nothing
        /// read or measured until it is asked for, a protected string out of sight holding no
        /// value and nothing its value could be measured with, no
        /// string holding a value for a sight that does not read, no attachments at all when
        /// nothing about them is in sight, and attachments holding no bytes when they are listed
        /// and not read.
        /// </summary>
        /// <remarks>
        /// Each value read counts in <see cref="StringReads"/>, each value measured in
        /// <see cref="StringMeasures"/>, and each attachment read or digested in
        /// <see cref="AttachmentReads"/>, since the real vault decrypts for a read and a digest,
        /// and KeePass can for a measurement.
        /// With <see cref="IgnoresCeiling"/> every length and size is told as nothing, which is a
        /// view handing over more than it measured.
        /// </remarks>
        private VaultEntryView Seen(Kept entry, VaultEntry described, VaultSight sight)
        {
            var fields = new List<VaultViewField>();
            foreach (var pair in entry.Fields)
            {
                var value = pair.Value ?? string.Empty;
                fields.Add(new VaultViewField(
                    pair.Key,
                    Guards(entry, pair.Key),
                    () =>
                    {
                        StringMeasures++;
                        Working();
                        return IgnoresCeiling ? 0 : value.Length;
                    },
                    sight,
                    () =>
                    {
                        StringReads++;
                        Working();
                        return value;
                    },
                    Listed(pair.Key)));
            }

            List<VaultViewAttachment> attachments = null;
            if (sight != null && sight.Attachments != VaultAttachmentSight.None)
            {
                attachments = new List<VaultViewAttachment>();
                foreach (var pair in entry.Attachments)
                {
                    var content = pair.Value;
                    attachments.Add(new VaultViewAttachment(
                        pair.Key,
                        IgnoresCeiling ? 0 : content.Length,
                        sight,
                        () =>
                        {
                            // A copy, as the real vault hands out: the route serving an
                            // attachment overwrites what it read.
                            AttachmentReads++;
                            Working();
                            return (byte[])content.Clone();
                        },
                        () =>
                        {
                            AttachmentReads++;
                            Working();
                            using (var sha256 = System.Security.Cryptography.SHA256.Create())
                            {
                                return sha256.ComputeHash(content);
                            }
                        }));
                }
            }

            return new VaultEntryView(described, sight, fields, attachments, Tag);
        }

        /// <summary>Counts work on an entry's values that is done with a hold standing.</summary>
        private void Working()
        {
            if (_held > 0)
            {
                ValueWorkHeld++;
            }
        }

        /// <summary>
        /// What a view holds, condensed as the real vault condenses a view and from the view
        /// alone: protection as the database decides it, a withheld value as a fixed mark, each
        /// attachment the view holds by name and size, with its digest only where attachments
        /// are read and only up to the largest the gate serves, and nothing for attachments the
        /// view does not hold, with no mark of how far the sight reaches. The view never asks
        /// this for a sight that does not read.
        /// </summary>
        /// <remarks>
        /// **Less the two times**, because nothing here keeps any. The real vault's tag carries
        /// both to the second and every change it makes touches the modification time, so there a
        /// change that puts back what was there gives a new tag unless it lands within the same
        /// second, where here it always gives back the tag that was there.
        /// </remarks>
        private string Tag(VaultEntryView view)
        {
            TagsTaken++;
            Working();

            var parts = new List<string>
            {
                "entry",
                view.Entry.Uuid,
                view.Entry.Group,
                view.Entry.Expires.HasValue
                    ? view.Entry.Expires.Value.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "never",
            };

            foreach (var field in view.Fields)
            {
                parts.Add(field.Name);
                parts.Add(field.IsProtected ? "protected" : "plain");
                parts.Add(field.IsWithheld ? "withheld" : "value");
                parts.Add(field.IsWithheld ? string.Empty : field.Value);
            }

            // What the view holds of the attachments, and no mark for how far the sight reaches,
            // as the real tag is taken.
            foreach (var attachment in view.Attachments ?? Array.Empty<VaultViewAttachment>())
            {
                parts.Add("attachment");
                parts.Add(attachment.Name);
                parts.Add(attachment.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));

                var digest = attachment.Digest;
                parts.Add(digest == null ? "unread" : Convert.ToBase64String(digest));
            }

            return Tags.Of(parts.ToArray());
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
            return entry == null ? null : entry.Describe();
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
        ///
        /// **A group as well**, whose lookup answers nothing for it the same way. Every change still
        /// finds the node, which is the other race: an operator's move, caught between taking the
        /// node out of one group and putting it into the other, misses a handler's lookup and is
        /// over by the time the write looks again.
        ///
        /// **And an entry's view**, which is the lookup every read of an entry makes after the
        /// router has found its node, so the move can miss that lookup alone.
        /// </remarks>
        public string Vanishes { get; set; }

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

        /// <summary>
        /// Hands over an attachment's contents, an entry's strings or a field's value whatever size
        /// they are when set, by measuring each as nothing, as a vault that broke its promise
        /// would.
        /// </summary>
        /// <remarks>
        /// What `ReadAttachment`'s, `ReadEntry`'s and `ReadField`'s own checks of what they were
        /// handed are for, and without this nothing here could ever hand them more than it
        /// measured.
        /// </remarks>
        public bool IgnoresCeiling { get; set; }

        /// <summary>
        /// How many string values have been read off a view, each of which the real vault
        /// decrypts, so that a test can tell an answer was refused before any value was read
        /// rather than after, and that a value out of the caller's sight was never read.
        /// </summary>
        public int StringReads { get; set; }

        /// <summary>
        /// How many string values a view has been asked the length of, each of which KeePass may
        /// decrypt to count, so that a test can tell a value out of the caller's sight was never
        /// measured, and that an answer asking no size measured nothing.
        /// </summary>
        public int StringMeasures { get; set; }

        /// <summary>
        /// How many times an attachment's contents have been read off a view, to hand over or to
        /// digest for a tag, each of which the real vault decrypts, so that a test can tell an
        /// answer was refused before an attachment was decrypted rather than after.
        /// </summary>
        public int AttachmentReads { get; set; }

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

        public VaultWrite CreateGroup(string parentUuid, string name, string notes, VaultAsking asking)
        {
            return Change(() =>
            {
                var parent = Find(parentUuid);
                if (parent == null)
                {
                    return VaultWrite.Missing();
                }

                var refusal = Refusal(asking, new VaultFound(parent.Describe()));
                if (refusal != null)
                {
                    return refusal;
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

        public VaultWrite UpdateGroup(string uuid, string name, string notes, VaultAsking asking)
        {
            return Change(() =>
            {
                var group = Find(uuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(group.Describe()), out tagging);
                Saw = tagging != null && tagging.Sight.Reads;
                if (refusal != null)
                {
                    return refusal;
                }

                var refused = Refused(tagging, () => group.Tag());
                if (refused != null)
                {
                    return refused;
                }

                group.Name = name ?? group.Name;
                group.Notes = notes ?? group.Notes;
                return VaultWrite.Done(Left(asking, group));
            });
        }

        public VaultWrite DeleteGroup(string uuid, VaultAsking asking)
        {
            return Change(() =>
            {
                var group = Find(uuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(group.Describe()), out tagging);
                if (refusal != null)
                {
                    return refusal;
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
            string groupUuid,
            IReadOnlyDictionary<string, string> fields,
            VaultAsking asking,
            VaultExpiry expiry = null)
        {
            return Change(() =>
            {
                var group = Find(groupUuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                var refusal = Refusal(asking, VaultFound.Holding(group.Describe(), fields != null && fields.Keys.Any(Protects)));
                if (refusal != null)
                {
                    return refusal;
                }

                if (Busy != null)
                {
                    return VaultWrite.Busy(Busy);
                }

                var uuid = AddEntry(group.Uuid, string.Empty);
                var entry = _entries[uuid];

                foreach (var name in Standard)
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
            VaultAsking asking,
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

                var guarded = fields != null && fields.Keys.Any(name => Guards(entry, name));
                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), guarded), out tagging);
                Saw = tagging != null && Viewed(entry, tagging).Reads(fields == null ? null : fields.Keys);
                if (refusal != null)
                {
                    return refusal;
                }

                if (fields != null && Clashes(entry.Fields.Keys, fields.Keys))
                {
                    return VaultWrite.Conflict("that field is already there");
                }

                if (Busy != null)
                {
                    return VaultWrite.Busy(Busy);
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
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

                return VaultWrite.Done(Left(asking, entry));
            });
        }

        public VaultWrite DeleteEntry(string uuid, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(uuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), false), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
                if (refused != null)
                {
                    return refused;
                }

                _entries.Remove(entry.Uuid);
                return VaultWrite.Done();
            });
        }

        public VaultWrite CreateField(
            string entryUuid, string name, string value, bool isProtected, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                // The real vault's rule: what the rules are asked about is what is stored.
                var guarded = isProtected || (name != null && Protects(name));
                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), guarded), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (name == null || entry.Fields.ContainsKey(name))
                {
                    return VaultWrite.Conflict("that field is already there");
                }

                if (Busy != null)
                {
                    return VaultWrite.Busy(Busy);
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
                if (refused != null)
                {
                    return refused;
                }

                entry.Fields[name] = value ?? string.Empty;

                if (guarded)
                {
                    entry.Protected.Add(name);
                }

                return VaultWrite.Created(name, Left(asking, entry));
            });
        }

        public VaultWrite UpdateField(string entryUuid, string name, string value, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), name != null && Guards(entry, name)), out tagging);
                Saw = tagging != null && Viewed(entry, tagging).Reads(new[] { name });
                if (refusal != null)
                {
                    return refusal;
                }

                // After the rules, as the real vault asks it, so a refused caller learns no name.
                if (!Holds(entry.Fields.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                if (Busy != null)
                {
                    return VaultWrite.Busy(Busy);
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
                if (refused != null)
                {
                    return refused;
                }

                entry.Fields[name] = value ?? string.Empty;
                return VaultWrite.Done(Left(asking, entry));
            });
        }

        public VaultWrite DeleteField(string entryUuid, string name, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), name != null && Guards(entry, name)), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (!Holds(entry.Fields.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                // The real vault's rule, and in its order: emptied and never taken off, compared as
                // the store compares names.
                if (Standard.Any(standard => Comparer<string>.Default.Compare(name, standard) == 0))
                {
                    return VaultWrite.Conflict("a standard field can be emptied and not taken off");
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
                if (refused != null)
                {
                    return refused;
                }

                entry.Fields.Remove(name);
                entry.Protected.Remove(name);
                return VaultWrite.Done(Left(asking, entry));
            });
        }

        public VaultWrite CreateAttachment(string entryUuid, string name, byte[] content, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), false), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (name == null || entry.Attachments.ContainsKey(name))
                {
                    return VaultWrite.Conflict("that attachment is already there");
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
                if (refused != null)
                {
                    return refused;
                }

                entry.Attachments[name] = content ?? Array.Empty<byte>();
                return VaultWrite.Created(name, Left(asking, entry));
            });
        }

        public VaultWrite UpdateAttachment(string entryUuid, string name, byte[] content, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), false), out tagging);
                Saw = tagging != null && Viewed(entry, tagging).ReadsAttachment(name);
                if (refusal != null)
                {
                    return refusal;
                }

                if (!Holds(entry.Attachments.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
                if (refused != null)
                {
                    return refused;
                }

                entry.Attachments[name] = content ?? Array.Empty<byte>();
                return VaultWrite.Done(Left(asking, entry));
            });
        }

        public VaultWrite DeleteAttachment(string entryUuid, string name, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(entry.Describe(), false), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (!Holds(entry.Attachments.Keys, name))
                {
                    return VaultWrite.Missing();
                }

                var refused = Refused(tagging, () => Found(entry, tagging));
                if (refused != null)
                {
                    return refused;
                }

                entry.Attachments.Remove(name);
                return VaultWrite.Done(Left(asking, entry));
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
        /// What every change asks about its caller once it has found its node, as the real vault
        /// asks it: inside the change, of the node as it is found there.
        /// </summary>
        /// <remarks>
        /// The real vault's answers, so a handler test meets them: a change handed no questions
        /// is refused, as one the rules refuse is, and questions that cannot be answered are
        /// `busy`.
        /// </remarks>
        private VaultWrite Refusal(VaultAsking asking, VaultFound found, out VaultTagging tagging)
        {
            tagging = null;

            try
            {
                if (asking == null || !asking.Allows(found))
                {
                    return VaultWrite.Refused();
                }

                tagging = asking.Tagging(found);
                Saw = false;
                return null;
            }
            catch (VaultUnsettled error)
            {
                return VaultWrite.Busy(error.Message);
            }
        }

        /// <summary>The same question for a change that deals in no tag, which a create is.</summary>
        private VaultWrite Refusal(VaultAsking asking, VaultFound found)
        {
            VaultTagging tagging;
            return Refusal(asking, found, out tagging);
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
        /// A change that measures the entry asks for the dialog itself before it measures, since
        /// the real vault answers `503` before `413`, so the question here is its second asking.
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

            return VaultPrecondition.Fails(tagging.Precondition, tag)
                ? VaultWrite.PreconditionFailed(tagging.Precondition)
                : null;
        }

        /// <summary>
        /// The tag a change left, when its answer is to carry one: asked of the node as the
        /// change left it, as the real vault asks.
        /// </summary>
        private string Left(VaultAsking asking, Kept entry)
        {
            // Of the view the caller has of the entry the change left, as the real vault takes
            // the tag in an answer.
            var described = entry.Placed();
            var tagging = Tagging(asking, new VaultFound(described, false));

            return tagging != null && tagging.Answers ? Seen(entry, described, tagging.Sight).Tag : null;
        }

        private static string Left(VaultAsking asking, Node group)
        {
            var tagging = Tagging(asking, new VaultFound(group.Describe()));
            return tagging != null && tagging.Answers ? group.Tag() : null;
        }

        private static VaultTagging Tagging(VaultAsking asking, VaultFound left)
        {
            try
            {
                return asking.Tagging(left);
            }
            catch (VaultUnsettled)
            {
                return null;
            }
        }

        private VaultWrite Change(Func<VaultWrite> change)
        {
            // A change has KeePass's thread for the whole of its turn, so nothing it looks up is
            // looked up with nothing held.
            _changing++;
            try
            {
                Looking();

                // Not `Busy`, which each change answers itself once it has found the node, so that
                // a node gone or a name taken is answered first, as the real vault answers them.
                return WriteBlocked != null ? VaultWrite.Failed(WriteBlocked) : change();
            }
            finally
            {
                _changing--;
            }
        }

        /// <summary>Counts one lookup or change, and runs what <see cref="After"/> left once it is due.</summary>
        /// <remarks>
        /// Before whatever it counts, so the event lands between the last answer that was due and
        /// this one, unless a hold keeps it waiting. Cleared before it runs, because what it runs
        /// may well be a change of its own.
        /// </remarks>
        private void Looking()
        {
            if (_held == 0 && _changing == 0)
            {
                LooseLookups++;
            }

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

            if (_held > 0 && !Lapses)
            {
                _waiting = then;
                return;
            }

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

        private Kept FindEntry(string uuid)
        {
            Kept entry;
            return uuid != null && IsOpen && _entries.TryGetValue(uuid, out entry) ? entry : null;
        }

        private string Identifier()
        {
            _next++;
            return _next.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
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

            /// <summary>
            /// What the group holds, condensed as the real vault condenses it, less the two times
            /// for the reason the entry's tag gives.
            /// </summary>
            public string Tag()
            {
                return Tags.Of("group", Uuid, Parent, Name, Notes);
            }
        }

        private sealed class Kept
        {
            public Kept(string uuid, string group, TestNode node)
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
            public VaultEntry Describe()
            {
                string title;
                string url;

                Fields.TryGetValue("Title", out title);
                Fields.TryGetValue("URL", out url);

                return new VaultEntry(
                    Uuid, Group, title ?? string.Empty, url ?? string.Empty,
                    DateTimeOffset.MinValue, DateTimeOffset.MinValue, Node,
                    Protected.Contains("Title"), Protected.Contains("URL"), Expires);
            }

            /// <summary>
            /// The entry as a view carries it, as the real vault describes one for a view: with
            /// no title and no URL, each of which a view holds as a field or not at all.
            /// </summary>
            public VaultEntry Placed()
            {
                return new VaultEntry(
                    Uuid, Group, (string)null, null,
                    DateTimeOffset.MinValue, DateTimeOffset.MinValue, Node,
                    false, false, Expires);
            }
        }

        /// <summary>A digest of the parts, each behind its length, as the real vault's tags are made.</summary>
        /// <remarks>
        /// Content rather than a counter, as the real vault's tags are, and with no key, since
        /// nothing here is a secret. With no times among the parts, a change that puts back what
        /// was there gives the tag back here, which the real vault does only within the second the
        /// change touched.
        /// </remarks>
        private static class Tags
        {
            public static string Of(params string[] parts)
            {
                var joined = new System.Text.StringBuilder();
                foreach (var text in parts.Select(part => part ?? string.Empty))
                {
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
