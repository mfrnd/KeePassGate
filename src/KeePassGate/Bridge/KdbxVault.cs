using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// One open KeePass database, as the API reads and changes it.
    /// </summary>
    /// <remarks>
    /// The only thing in this plugin that knows what a `PwEntry` is and also answers to a request.
    /// Everything above it works against <see cref="IVault"/>, which is what lets the handlers be
    /// tested without a password manager.
    ///
    /// **Reads run on the connection's thread, with KeePass's thread held still.** `DESIGN.md`
    /// §119 measured that KeePass's object model answers from a background thread, and every read
    /// here is one; what it did not answer for is a tree changing underneath, which KeePass's
    /// thread does with no sign of it. So each read waits its turn on that thread and holds it
    /// for the read's whole length, through <see cref="IReadHold"/>. **And in KeePass's culture**,
    /// which `GateListener` gives every connection: an entry's strings and attachments are looked
    /// up by name in the culture of the thread asking, and a lookup in another culture than the
    /// one KeePass's thread filled them in can miss a name that is there.
    ///
    /// **Writes go through the plugin's single writer**, which marshals them to the thread that
    /// owns the database, saves, and does not return until KeePass says there is nothing unsaved.
    /// A caller told that something was written can believe it.
    ///
    /// **A dialog open on what is being written refuses the write**, and only on what is being
    /// written. The entry dialog holds a copy of an entry's strings and the group dialog holds a
    /// group's own settings, so a change underneath either is a change the operator's OK destroys.
    /// Neither holds the list of what is inside a group, so adding an entry to a group somebody is
    /// looking at is safe and is allowed. KeePass's attachment editor holds an attachment and none
    /// of its entry, and the main window writes the edited bytes back onto the entry as the editor
    /// closes, so every entry holding an attachment of that name is refused while it is open.
    ///
    /// **Except where the dialog is adding the group itself**, because KeePass's Add Group puts the
    /// group into the tree before it opens and takes it back out on Cancel, and whatever was added
    /// in between goes with it. And a group Add Entry or the TAN wizard will fill on OK cannot be
    /// removed while either is open, since that group is empty until then and what the operator
    /// typed would land in a group that is gone.
    ///
    /// **A change to an entry keeps the entry as it was**, in the entry's own history, which is
    /// what KeePass's editor does on OK. See <see cref="Backing"/>.
    /// </remarks>
    public sealed class KdbxVault : IVault
    {
        /// <summary>What a group identifier may say instead of a UUID.</summary>
        public const string Root = "root";

        /// <summary>What every entry has, named the way KeePass names them.</summary>
        private static readonly string[] Standard =
        {
            PwDefs.TitleField,
            PwDefs.UserNameField,
            PwDefs.PasswordField,
            PwDefs.UrlField,
            PwDefs.NotesField,
        };

        private readonly PwDatabase _database;
        private readonly DatabaseWriter _writer;
        private readonly NodeTags _tags;
        private readonly IReadHold _hold;
        private readonly IGateLog _log;

        /// <summary>
        /// Over one open database, changed through the plugin's single writer, or only read when
        /// there is none, and read where it is asked, with no other thread to hold still.
        /// </summary>
        /// <param name="tags">This database's tags, keyed as <see cref="NodeTags.KeyFrom"/> says.</param>
        public KdbxVault(PwDatabase database, DatabaseWriter writer, NodeTags tags, IGateLog log)
            : this(database, writer, tags, CurrentThread.Instance, log)
        {
        }

        /// <summary>The same, holding KeePass's thread still for every read.</summary>
        public KdbxVault(PwDatabase database, DatabaseWriter writer, NodeTags tags, IReadHold hold, IGateLog log)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (tags == null)
            {
                throw new ArgumentNullException(nameof(tags));
            }

            if (hold == null)
            {
                throw new ArgumentNullException(nameof(hold));
            }

            _database = database;
            _writer = writer;
            _tags = tags;
            _hold = hold;
            _log = log ?? NullGateLog.Instance;
        }

        public string RootAlias
        {
            get { return Root; }
        }

        /// <summary>Whether KeePass has this database open, by both of its own answers.</summary>
        /// <remarks>
        /// Both, because they part company for a moment either way: a lock clears the root group
        /// before it clears the flag saying the database is open, and an unlock sets the root
        /// before the flag. Open is when both say so.
        /// </remarks>
        public bool IsOpen
        {
            get { return _database.IsOpen && _database.RootGroup != null; }
        }

        public string WriteBlocked
        {
            get
            {
                if (!_database.IsOpen || _database.RootGroup == null)
                {
                    return "the database is not open";
                }

                return _writer == null ? "this database is served without a way to write to it" : null;
            }
        }

        private VaultGroup GroupInside(string uuid)
        {
            return Describe(FindGroup(uuid));
        }

        private string GroupTagInside(string uuid)
        {
            var group = FindGroup(uuid);
            return group == null ? null : _tags.Of(group);
        }

        private IReadOnlyList<VaultGroup> SubgroupsInside(string uuid)
        {
            var root = _database.RootGroup;
            var group = FindGroup(root, uuid);
            var found = new List<VaultGroup>();

            if (group == null)
            {
                return null;
            }

            var bin = Bin(root);

            foreach (var child in group.Groups)
            {
                if (Discarded(child, bin))
                {
                    continue;
                }

                found.Add(Describe(child));
            }

            return found;
        }

        private IReadOnlyList<VaultGroup> GroupsInside()
        {
            var found = new List<VaultGroup>();
            var root = _database.RootGroup;

            if (root == null)
            {
                return null;
            }

            found.Add(Describe(root));
            var bin = Bin(root);

            // Depth first, as KeePass's tree shows the groups and as `GetEntries(true)` walks them
            // for the entries: each group, then everything inside it, then the next. The groups
            // came in `GetGroups(true)`'s order, each group's own groups before any of theirs,
            // which is neither, so a client showing them as they came put a sibling between a
            // group and its children.
            root.TraverseTree(TraversalMethod.PreOrder, group =>
            {
                if (!Discarded(group, bin))
                {
                    found.Add(Describe(group));
                }

                return true;
            }, null);

            return found;
        }

        private VaultEntry EntryInside(string uuid)
        {
            return Describe(FindEntry(uuid));
        }

        private VaultEntryView ViewInside(string uuid, Func<VaultFound, VaultSight> sight)
        {
            var entry = FindEntry(uuid);
            if (entry == null)
            {
                return null;
            }

            return Seen(entry, described => sight == null
                ? null
                : sight(new VaultFound(described, false)));
        }

        /// <summary>
        /// An entry as far as a sight reaches into it: the one place an entry's strings and
        /// attachments are turned into what a route answers with and a tag is taken of.
        /// </summary>
        /// <remarks>
        /// **Nothing is decrypted here.** Which strings are protected is read from their flags and
        /// the database's setting, how long they are from the length KeePass keeps beside their
        /// encrypted bytes, and an attachment's size the same way, so an entry over a ceiling is
        /// measured off the view with every value still encrypted. Each value is read when the
        /// view is first asked for it, and KeePass changes neither a `ProtectedString` nor a
        /// `ProtectedBinary` in place, so what is read then is what the entry held when the view
        /// was taken. The entry is described for the view without its title and its URL,
        /// <see cref="Placed"/>, since a description holding either would have read both whatever
        /// the sight.
        /// </remarks>
        /// <param name="asked">
        /// What the caller may read, asked of the entry as described here. Null is answered as
        /// <see cref="VaultSight.Least"/>.
        /// </param>
        private VaultEntryView Seen(PwEntry entry, Func<VaultEntry, VaultSight> asked)
        {
            var strings = entry.Strings.ToList();
            var guarded = strings.Select(pair => Guards(pair.Key, pair.Value)).ToList();

            var described = Placed(entry);
            var sight = asked(described) ?? VaultSight.Least;

            // In the order KeePass keeps them, which is the order `read` writes them in.
            var fields = new List<VaultViewField>();
            for (var index = 0; index < strings.Count; index++)
            {
                // As stored, and never compiled: KeePass resolves a reference against the whole
                // open database, which knows nothing about the rules this answer was allowed by.
                var held = strings[index].Value;
                fields.Add(new VaultViewField(
                    strings[index].Key, guarded[index], held.Length, sight, held.ReadString));
            }

            List<VaultViewAttachment> attachments = null;
            if (sight.Attachments != VaultAttachmentSight.None)
            {
                attachments = new List<VaultViewAttachment>();
                foreach (var pair in entry.Binaries)
                {
                    // `Length` is kept beside the encrypted bytes, so asking it decrypts nothing.
                    // The attachment keeps what reads its bytes only for a sight that reads
                    // attachments.
                    var binary = pair.Value;
                    attachments.Add(new VaultViewAttachment(
                        pair.Key, (int)binary.Length, sight, binary.ReadData, () => NodeTags.DigestOf(binary)));
                }
            }

            return new VaultEntryView(described, sight, fields, attachments, _tags.Of);
        }

        private IReadOnlyList<VaultEntry> EntriesInside(string groupUuid)
        {
            var group = FindGroup(groupUuid);
            var found = new List<VaultEntry>();

            if (group == null)
            {
                return null;
            }

            foreach (var entry in group.Entries)
            {
                found.Add(Describe(entry));
            }

            return found;
        }

        private IReadOnlyList<VaultEntry> EntriesInside()
        {
            var found = new List<VaultEntry>();
            var root = _database.RootGroup;

            if (root == null)
            {
                return null;
            }

            var bin = Bin(root);

            foreach (var entry in root.GetEntries(true))
            {
                if (Discarded(entry, bin))
                {
                    continue;
                }

                found.Add(Describe(entry));
            }

            return found;
        }

        /// <summary>
        /// What an entry's strings, names and values together, come to in characters, measured
        /// without decrypting anything.
        /// </summary>
        /// <remarks>
        /// `ProtectedString.Length` is kept beside the encrypted value, so a protected password is
        /// measured as cheaply as a title.
        /// </remarks>
        private static int Characters(PwEntry entry)
        {
            return entry.Strings.Sum(pair => pair.Key.Length + pair.Value.Length);
        }

        /// <summary>
        /// What an entry's strings would come to with these values set on it: a name it holds
        /// counted with the new value in place of the old, and a name it does not hold as added.
        /// </summary>
        private static int Characters(PwEntry entry, IReadOnlyDictionary<string, string> setting)
        {
            var characters = Characters(entry);
            if (setting == null)
            {
                return characters;
            }

            foreach (var field in setting)
            {
                var held = entry.Strings.Get(field.Key);
                var value = (field.Value ?? string.Empty).Length;
                characters += held == null ? field.Key.Length + value : value - held.Length;
            }

            return characters;
        }

        public VaultWrite CreateGroup(string parentUuid, string name, string notes, VaultAsking asking)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Logged(VaultWrite.Failed("a group needs a name"));
            }

            return Change(() =>
            {
                var parent = FindGroup(parentUuid);
                if (parent == null)
                {
                    return VaultWrite.Missing();
                }

                var refusal = Refusal(asking, new VaultFound(Describe(parent)));
                if (refusal != null)
                {
                    return refusal;
                }

                var blocked = BlockingAdditions(parent);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                var group = new PwGroup(true, true, name, PwIcon.Folder);
                if (notes != null)
                {
                    group.Notes = notes;
                }

                parent.AddGroup(group, true);

                return new VaultChange(
                    VaultWrite.Created(group.Uuid.ToHexString()),
                    () => parent.Groups.Remove(group));
            });
        }

        public VaultWrite UpdateGroup(string uuid, string name, string notes, VaultAsking asking)
        {
            return Change(() =>
            {
                var group = FindGroup(uuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(Describe(group)), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                var blocked = Blocking(group);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                var tag = Before(group);
                if (VaultPrecondition.Fails(tagging.Precondition, tag))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                // Skipped only for a caller that may read what it named, or when nothing was
                // named: `VaultTagging.Sees` has the reason.
                var named = name != null || notes != null;
                var holds = (name == null || string.Equals(group.Name, name, StringComparison.Ordinal))
                    && (notes == null || string.Equals(group.Notes, notes, StringComparison.Ordinal));

                if (holds && (!named || tagging.Sees))
                {
                    // The group already holds what was asked for, which a body naming nothing asks
                    // as well, and `VaultChange.Unchanged` states why that writes nothing. The tag
                    // the precondition read, when it read one, is the tag the group still has.
                    return VaultChange.Unchanged(VaultWrite.Done(tagging.Answers ? tag() : null));
                }

                var wasName = group.Name;
                var wasNotes = group.Notes;
                var restore = Restoring(group);

                if (name != null)
                {
                    group.Name = name;
                }

                if (notes != null)
                {
                    group.Notes = notes;
                }

                group.Touch(true, false);

                return VaultChange.Made(() => VaultWrite.Done(After(asking, group)), () =>
                {
                    group.Name = wasName;
                    group.Notes = wasNotes;
                    restore();
                });
            });
        }

        public VaultWrite DeleteGroup(string uuid, VaultAsking asking)
        {
            return Change(() =>
            {
                var group = FindGroup(uuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(Describe(group)), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (group.ParentGroup == null)
                {
                    return VaultWrite.Conflict("the root group cannot be removed");
                }

                if (group.Groups.UCount > 0 || group.Entries.UCount > 0)
                {
                    // A recursive delete would destroy entries that were never individually decided
                    // or recorded, on the strength of one grant on a parent. A group whose one child
                    // is the recycle bin is refused for its own reason, since every listing leaves
                    // the bin out and the group reads as empty to the client refused.
                    return HoldsOnlyTheBin(group)
                        ? VaultWrite.Conflict("the group holds the recycle bin, which a removal would take with it")
                        : VaultWrite.Conflict("the group still holds something");
                }

                var blocked = Blocking(group) ?? BlockingRemoval(group) ?? RecycleBinBlocked();
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                if (VaultPrecondition.Fails(tagging.Precondition, Before(group)))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                var parent = group.ParentGroup;
                var position = parent.Groups.IndexOf(group);
                var restoreGroup = Restoring(group);
                var wasPrevious = group.PreviousParentGroup;
                Action unmakeBin;
                var bin = RecycleBin(out unmakeBin);

                if (bin != null && !ReferenceEquals(parent, bin) && !ReferenceEquals(group, bin)
                    && !group.IsContainedIn(bin))
                {
                    // The same rule entries have had all along, and the one `DESIGN.md` states
                    // without qualifying it to entries: a removal through the API is not
                    // destruction while the database keeps a bin. The group is empty by the time
                    // this runs, so what was being destroyed was its name, its notes and the `KPG`
                    // rule on it. That last one is the thing an operator would most want back.
                    parent.Groups.Remove(group);
                    group.PreviousParentGroup = parent.Uuid;
                    bin.AddGroup(group, true, true);

                    // Its use and its access time, as KeePass's own delete touches a node it bins,
                    // and not its modification time or its parent's. A merge takes a group whole
                    // from whichever copy's group was modified last, and a group keeps no history:
                    // a parent stamped here won the next synchronize over an edit made to it on
                    // another copy, rules and all, and a child's removal changed the parent's tag.
                    // Not the access times above it either, which KeePass's `Touch(false)` walks,
                    // so that the undo puts back everything this moved.
                    group.Touch(false, false);

                    return new VaultChange(VaultWrite.Done(), () =>
                    {
                        bin.Groups.Remove(group);
                        Reinsert(parent, group, position);
                        group.PreviousParentGroup = wasPrevious;
                        restoreGroup();

                        if (unmakeBin != null)
                        {
                            unmakeBin();
                        }
                    });
                }

                // Nothing is stamped, as KeePass's own permanent delete stamps nothing, for the
                // reason the bin path above gives.
                parent.Groups.Remove(group);
                var deleted = new PwDeletedObject(group.Uuid, DateTime.UtcNow);
                _database.DeletedObjects.Add(deleted);

                return new VaultChange(VaultWrite.Done(), () =>
                {
                    _database.DeletedObjects.Remove(deleted);
                    Reinsert(parent, group, position);
                    restoreGroup();

                    if (unmakeBin != null)
                    {
                        unmakeBin();
                    }
                });
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
                var group = FindGroup(groupUuid);
                if (group == null)
                {
                    return VaultWrite.Missing();
                }

                // Whether the database protects a name the body carries, asked here and of the
                // database as it is, since the operator can change what it protects while this
                // waited for its turn.
                var guarded = fields != null && fields.Keys.Any(Protects);
                var refusal = Refusal(asking, VaultFound.Holding(Describe(group), guarded));
                if (refusal != null)
                {
                    return refusal;
                }

                var blocked = BlockingAdditions(group);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                var entry = new PwEntry(true, true);

                // No history revision, and KeePass's own editor makes none either when it is
                // adding an entry rather than changing one: there is no earlier state for a
                // revision to hold. `Backing` covers the changes that do have one.
                //
                // The five standard fields exist whether or not the caller named them, because an
                // entry KeePass made has them: an entry with no `Password` string at all reads
                // oddly in the user interface and would make an update of one a create. KeePass's
                // own Add Entry also gives a new entry the database's default user name, a generated
                // password and its group's icon, and none of those is applied here. The fourth thing
                // it gives one, the expiry KeePass's options set for new entries, is applied when the
                // caller asks for it, which `ExpireAsAsked` says more about.
                foreach (var name in Standard)
                {
                    entry.Strings.Set(name, Protect(name, string.Empty));
                }

                if (fields != null)
                {
                    if (Clashes(entry, fields.Keys))
                    {
                        return Indistinguishable();
                    }

                    foreach (var field in fields)
                    {
                        entry.Strings.Set(field.Key, Protect(field.Key, field.Value));
                    }
                }

                // Measured as it would be added, so nothing is put into the group to be taken out.
                var characters = Characters(entry);
                if (characters > VaultEntryView.MostCharacters)
                {
                    return VaultWrite.TooLarge(characters);
                }

                ExpireAsAsked(entry, group, expiry);
                group.AddEntry(entry, true);

                return new VaultChange(
                    VaultWrite.Created(entry.Uuid.ToHexString()),
                    () => group.Entries.Remove(entry));
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
                return Logged(VaultWrite.Failed(
                    "an entry that exists already was asked to take the expiry KeePass's options give new ones"));
            }

            return Change(() =>
            {
                var entry = FindEntry(uuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                // Both halves of every string the body names, the flag it carries and what the
                // database decides about its name, as the entry holds them now. Asked through the
                // entry's own dictionary, so a name KeePass would read as the password is judged
                // as the password, whatever `Clashes` goes on to answer about it.
                var guarded = fields != null && fields.Keys.Any(name => Guards(entry, name));
                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(Describe(entry), guarded), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (fields != null && Clashes(entry, fields.Keys))
                {
                    return Indistinguishable();
                }

                var blocked = Blocking(entry);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                var characters = Characters(entry, fields);
                if (characters > VaultEntryView.MostCharacters)
                {
                    return VaultWrite.TooLarge(characters);
                }

                var tag = Before(entry, tagging);
                if (VaultPrecondition.Fails(tagging.Precondition, tag))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                // Skipped only for a caller that may read what it named, or when nothing was
                // named: `VaultTagging.Sees` has the reason.
                var named = (fields != null && fields.Count > 0) || expiry != null;

                if ((!named || tagging.Sees)
                    && AlreadyHolds(entry, fields)
                    && AlreadyExpires(entry, expiry))
                {
                    // Before `Backing`, which is the history revision this would otherwise take of
                    // an entry nothing is about to change. A body naming nothing lands here too.
                    return VaultChange.Unchanged(VaultWrite.Done(tagging.Answers ? tag() : null));
                }

                var restore = Restoring(entry);
                var restoreHistory = Backing(entry);
                var before = new List<KeyValuePair<string, ProtectedString>>();
                var wasExpires = entry.Expires;
                var wasExpiryTime = entry.ExpiryTime;

                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        // What was there, so a save that never lands can put it back. A name the
                        // entry did not carry is recorded as a null, which the undo reads as
                        // "take it off again" rather than "write an empty one".
                        before.Add(new KeyValuePair<string, ProtectedString>(
                            field.Key, entry.Strings.Get(field.Key)));

                        // The flag this string already carries, not the one the database default
                        // would derive. `UpdateField` says why a few methods down: whether a value
                        // is protected is the operator's decision, and re-deriving it here turned
                        // one legitimate update into a permanent downgrade. For a name KeePass
                        // does not normalize, the flag never came back, and every later client
                        // with `read` and no `protected` was handed the value in clear.
                        var existing = entry.Strings.Get(field.Key);

                        entry.Strings.Set(field.Key, existing == null
                            ? Protect(field.Key, field.Value)
                            : new ProtectedString(existing.IsProtected, field.Value ?? string.Empty));
                    }
                }

                // As KeePass's own entry dialog sets it: the moment only when there is one, so an
                // expiry taken off keeps the moment it had, and putting one back is a new moment.
                if (expiry != null)
                {
                    entry.Expires = expiry.When.HasValue;

                    if (expiry.When.HasValue)
                    {
                        entry.ExpiryTime = expiry.When.Value.UtcDateTime;
                    }
                }

                entry.Touch(true, false);

                return VaultChange.Made(() => VaultWrite.Done(After(asking, entry)), () =>
                {
                    foreach (var was in before)
                    {
                        if (was.Value == null)
                        {
                            entry.Strings.Remove(was.Key);
                        }
                        else
                        {
                            entry.Strings.Set(was.Key, was.Value);
                        }
                    }

                    entry.Expires = wasExpires;
                    entry.ExpiryTime = wasExpiryTime;
                    restoreHistory();
                    restore();
                });
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
                var refusal = Refusal(asking, new VaultFound(Describe(entry), false), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                var blocked = Blocking(entry) ?? RecycleBinBlocked();
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                if (VaultPrecondition.Fails(tagging.Precondition, Before(entry, tagging)))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                // No history revision here either, and for the same reason KeePass's own delete
                // makes none: a removal keeps the whole entry, in the recycle bin, so a revision
                // would be a second copy of something nothing has changed.
                return new VaultChange(VaultWrite.Done(), Discard(entry));
            });
        }

        public VaultWrite CreateField(
            string entryUuid, string name, string value, bool isProtected, VaultAsking asking)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Logged(VaultWrite.Failed("a field needs a name"));
            }

            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                // What the rules are asked about is what is stored: protected because the caller
                // marked it or because the database protects the name, as the database decides
                // that now. Asked on one and stored on the other, a caller could put a standard
                // field back unprotected after stripping it, and every later client with `read`
                // and no `protected` was handed it in clear until KeePass next normalized the file.
                var guarded = isProtected || Protects(name);
                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(Describe(entry), guarded), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (entry.Strings.Exists(name))
                {
                    return VaultWrite.Conflict("that field is already there");
                }

                var blocked = Blocking(entry);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                var characters = Characters(entry, new Dictionary<string, string> { { name, value } });
                if (characters > VaultEntryView.MostCharacters)
                {
                    return VaultWrite.TooLarge(characters);
                }

                if (VaultPrecondition.Fails(tagging.Precondition, Before(entry, tagging)))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                var restore = Restoring(entry);
                var restoreHistory = Backing(entry);
                entry.Strings.Set(name, new ProtectedString(guarded, value ?? string.Empty));
                entry.Touch(true, false);

                return VaultChange.Made(() => VaultWrite.Created(name, After(asking, entry)), () =>
                {
                    entry.Strings.Remove(name);
                    restoreHistory();
                    restore();
                });
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
                var guarded = name != null && Guards(entry, name);
                var refusal = Refusal(asking, new VaultFound(Describe(entry), guarded), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                // After the rules, so a caller they refuse is not told which names the entry holds.
                if (!Holds(entry, name))
                {
                    return VaultWrite.Missing();
                }

                var blocked = Blocking(entry);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                var characters = Characters(entry, new Dictionary<string, string> { { name, value } });
                if (characters > VaultEntryView.MostCharacters)
                {
                    return VaultWrite.TooLarge(characters);
                }

                // The entry's tag, since the entry is what answers for the field.
                var tag = Before(entry, tagging);
                if (VaultPrecondition.Fails(tagging.Precondition, tag))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                // Kept as protected as it already was. Whether a field is protected is a property of
                // the database's own arrangement, and changing it as a side effect of writing a
                // value would be this API deciding something the operator decided.
                var existing = entry.Strings.Get(name);
                var flagged = existing != null && existing.IsProtected;
                var replacement = new ProtectedString(flagged, value ?? string.Empty);

                // Skipped only for a caller that may read the value: `VaultTagging.Sees` has
                // the reason.
                if (tagging.Sees && existing != null && existing.Equals(replacement, false))
                {
                    return VaultChange.Unchanged(VaultWrite.Done(tagging.Answers ? tag() : null));
                }

                var restore = Restoring(entry);
                var restoreHistory = Backing(entry);

                entry.Strings.Set(name, replacement);
                entry.Touch(true, false);

                return VaultChange.Made(() => VaultWrite.Done(After(asking, entry)), () =>
                {
                    entry.Strings.Set(name, existing);
                    restoreHistory();
                    restore();
                });
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
                var guarded = name != null && Guards(entry, name);
                var refusal = Refusal(asking, new VaultFound(Describe(entry), guarded), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (!Holds(entry, name))
                {
                    return VaultWrite.Missing();
                }

                if (IsStandard(name))
                {
                    // Emptied, and never taken off. Every entry KeePass makes has all five and its
                    // editor writes all five back on OK, while an entry without one reads as empty
                    // in a listing and as absent from a read, which a client cannot tell from a
                    // value it was refused.
                    return VaultWrite.Conflict("a standard field can be emptied and not taken off");
                }

                var blocked = Blocking(entry);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                if (VaultPrecondition.Fails(tagging.Precondition, Before(entry, tagging)))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                var was = entry.Strings.Get(name);
                var restore = Restoring(entry);
                var restoreHistory = Backing(entry);

                entry.Strings.Remove(name);
                entry.Touch(true, false);

                // The entry's tag, since the entry is still there, and a client that removes one
                // field and then changes another needs it to say which entry it is changing.
                return VaultChange.Made(() => VaultWrite.Done(After(asking, entry)), () =>
                {
                    entry.Strings.Set(name, was);
                    restoreHistory();
                    restore();
                });
            });
        }

        public VaultWrite CreateAttachment(
            string entryUuid, string name, byte[] content, VaultAsking asking)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Logged(VaultWrite.Failed("an attachment needs a name"));
            }

            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(Describe(entry), false), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                if (entry.Binaries.Get(name) != null)
                {
                    return VaultWrite.Conflict("that attachment is already there");
                }

                var blocked = Blocking(entry);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                if (VaultPrecondition.Fails(tagging.Precondition, Before(entry, tagging)))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                var restore = Restoring(entry);
                var restoreHistory = Backing(entry);
                entry.Binaries.Set(name, new ProtectedBinary(true, content ?? Array.Empty<byte>()));
                entry.Touch(true, false);

                return VaultChange.Made(() => VaultWrite.Created(name, After(asking, entry)), () =>
                {
                    entry.Binaries.Remove(name);
                    restoreHistory();
                    restore();
                });
            });
        }

        public VaultWrite UpdateAttachment(
            string entryUuid, string name, byte[] content, VaultAsking asking)
        {
            return Change(() =>
            {
                var entry = FindEntry(entryUuid);
                if (entry == null)
                {
                    return VaultWrite.Missing();
                }

                VaultTagging tagging;
                var refusal = Refusal(asking, new VaultFound(Describe(entry), false), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                // After the rules, for the reason `UpdateField` gives.
                if (!Keeps(entry, name))
                {
                    return VaultWrite.Missing();
                }

                var blocked = Blocking(entry);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                var tag = Before(entry, tagging);
                if (VaultPrecondition.Fails(tagging.Precondition, tag))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                var was = entry.Binaries.Get(name);
                var replacement = new ProtectedBinary(true, content ?? Array.Empty<byte>());

                // The bytes and not the protection flag, which is this route's own choice for
                // what it stores rather than anything the request asked for. KeePass's comparison
                // asks the lengths before it decrypts either side. Skipped only for a caller
                // that may read the attachment: `VaultTagging.Sees` has the reason.
                if (tagging.Sees && was != null && was.Equals(replacement, false))
                {
                    return VaultChange.Unchanged(VaultWrite.Done(tagging.Answers ? tag() : null));
                }

                var restore = Restoring(entry);
                var restoreHistory = Backing(entry);

                entry.Binaries.Set(name, replacement);
                entry.Touch(true, false);

                return VaultChange.Made(() => VaultWrite.Done(After(asking, entry)), () =>
                {
                    entry.Binaries.Set(name, was);
                    restoreHistory();
                    restore();
                });
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
                var refusal = Refusal(asking, new VaultFound(Describe(entry), false), out tagging);
                if (refusal != null)
                {
                    return refusal;
                }

                // After the rules, for the reason `UpdateField` gives.
                if (!Keeps(entry, name))
                {
                    return VaultWrite.Missing();
                }

                var blocked = Blocking(entry);
                if (blocked != null)
                {
                    return VaultWrite.Busy(blocked);
                }

                if (VaultPrecondition.Fails(tagging.Precondition, Before(entry, tagging)))
                {
                    return VaultWrite.PreconditionFailed(tagging.Precondition);
                }

                var was = entry.Binaries.Get(name);
                var restore = Restoring(entry);
                var restoreHistory = Backing(entry);

                entry.Binaries.Remove(name);
                entry.Touch(true, false);

                // The entry's tag, as a field's removal answers it.
                return VaultChange.Made(() => VaultWrite.Done(After(asking, entry)), () =>
                {
                    entry.Binaries.Set(name, was);
                    restoreHistory();
                    restore();
                });
            });
        }

        public VaultGroup Group(string uuid)
        {
            return Reading(() => GroupInside(uuid));
        }

        public string GroupTag(string uuid)
        {
            return Reading(() => GroupTagInside(uuid));
        }

        public IReadOnlyList<VaultGroup> Subgroups(string uuid)
        {
            return Reading(() => SubgroupsInside(uuid));
        }

        public IReadOnlyList<VaultGroup> Groups()
        {
            return Reading(() => GroupsInside());
        }

        public VaultEntry Entry(string uuid)
        {
            return Reading(() => EntryInside(uuid));
        }

        public VaultEntryView View(string uuid, Func<VaultFound, VaultSight> sight)
        {
            return Reading(() => ViewInside(uuid, sight));
        }

        public IReadOnlyList<VaultEntry> Entries(string groupUuid)
        {
            return Reading(() => EntriesInside(groupUuid));
        }

        public IReadOnlyList<VaultEntry> Entries()
        {
            return Reading(() => EntriesInside());
        }

        public T Held<T>(Func<T> read)
        {
            return _hold.Hold(read);
        }

        /// <summary>A read, with KeePass's thread held still for the whole of it.</summary>
        /// <remarks>
        /// **KeePass's collections are not built to be read from another thread**, and this plugin
        /// reads them on a connection thread while the user interface thread changes the same
        /// lists: the operator editing an entry, a merge, or this plugin's own writer. An enumerator
        /// that saw it threw, a copy built out of a tree that grew underneath it threw, a list a
        /// removal was shrinking handed back an empty slot, and a lookup racing a removal missed a
        /// node that was there without any sign of it. The read used to be taken again when it
        /// threw, which answered the first two, and nothing answers the last two from this side:
        /// a list moves its version after its size, so a read that checks finds nothing wrong.
        ///
        /// **So the tree is held still instead.** The read waits for its turn on KeePass's thread,
        /// which then waits for the read: measured, the empty slots and the misses went to none,
        /// for a sweep of ten thousand entries holding the thread for about seventeen
        /// milliseconds. A turn that does not come, or a read that outlasts the hold, is answered
        /// as busy, which is something a caller can act on. `DESIGN.md` §119 has the rest.
        /// </remarks>
        private T Reading<T>(Func<T> read)
        {
            return _hold.Hold(read);
        }

        /// <summary>
        /// The timestamps and the usage count `Touch` is about to move, and how to put them back.
        /// </summary>
        /// <remarks>
        /// Called before the change, because it captures what is there now. `LocationChanged` is in
        /// here with the other two: a removal to the recycle bin stamps it, and an entry put back
        /// carrying a newer one than the copy in somebody's replica is an entry a later merge
        /// relocates. **And the usage count**, which `Touch` advances on every change: left
        /// advanced, a change taken back out left the node counting a use the file never recorded,
        /// in a database marked as holding nothing unsaved, for the next save to write.
        /// </remarks>
        private static Action Restoring(PwEntry entry)
        {
            var modified = entry.LastModificationTime;
            var accessed = entry.LastAccessTime;
            var located = entry.LocationChanged;
            var used = entry.UsageCount;

            return () =>
            {
                entry.LastModificationTime = modified;
                entry.LastAccessTime = accessed;
                entry.LocationChanged = located;
                entry.UsageCount = used;
            };
        }

        /// <summary>
        /// Keeps the entry as it is now in its own history, and says how to take that revision
        /// back out again.
        /// </summary>
        /// <remarks>
        /// **Parity with the dialog on the one thing an operator recovers with.** KeePass's own
        /// entry editor calls `CreateBackup` before it applies an edit, which is what fills the
        /// History tab and what a restore reads. A change made through this API left no revision
        /// at all, so an agent overwriting a password destroyed the previous one outright.
        /// `DESIGN.md` §119 counts a legitimately admitted caller as part of the threat model, and
        /// the audit records no field name and no value on purpose, so the entry's history is the
        /// only place the old value survives.
        ///
        /// **How much history is kept is the database's own setting.** `CreateBackup` applies
        /// `HistoryMaxItems` and `HistoryMaxSize` on the way out, so an operator who keeps none has
        /// already said so in Database Settings, for KeePass's edits and for these alike. A switch
        /// of this plugin's own would be a second answer to a question the database already
        /// answers.
        ///
        /// **The undo puts the whole list back** rather than removing the revision it added,
        /// because making one can prune older ones to fit those limits, and a change whose save
        /// never lands has to leave the entry holding exactly the revisions it held before. The
        /// copy is shallow: the revisions in it are the same objects, and only the list is new.
        /// </remarks>
        private Action Backing(PwEntry entry)
        {
            var history = entry.History.CloneShallow();
            entry.CreateBackup(_database);

            return () => entry.History = history;
        }

        private static Action Restoring(PwGroup group)
        {
            var modified = group.LastModificationTime;
            var accessed = group.LastAccessTime;
            var located = group.LocationChanged;
            var used = group.UsageCount;

            return () =>
            {
                group.LastModificationTime = modified;
                group.LastAccessTime = accessed;
                group.LocationChanged = located;
                group.UsageCount = used;
            };
        }

        /// <summary>Puts a group back into its parent at the place it was taken from.</summary>
        /// <remarks>
        /// **`AddGroup` appends**, so a removal taken back out put the group after all its
        /// siblings, the recycle bin among them, in a database then marked as holding nothing
        /// unsaved, and the operator's next save wrote the new order. It is still what gives the
        /// group its parent back, which nothing public sets, and the group then moves to where it
        /// was. A place the list no longer has is its end.
        /// </remarks>
        private static void Reinsert(PwGroup parent, PwGroup group, int position)
        {
            parent.AddGroup(group, true);
            parent.Groups.Remove(group);
            parent.Groups.Insert(Place(position, parent.Groups.UCount), group);
        }

        /// <summary>Puts an entry back into its group at the place it was taken from.</summary>
        /// <remarks>For the reason the group's overload gives.</remarks>
        private static void Reinsert(PwGroup parent, PwEntry entry, int position)
        {
            parent.AddEntry(entry, true);
            parent.Entries.Remove(entry);
            parent.Entries.Insert(Place(position, parent.Entries.UCount), entry);
        }

        private static uint Place(int position, uint count)
        {
            return position < 0 || (uint)position > count ? count : (uint)position;
        }

        /// <summary>
        /// A write's answer, with the reason written to the log when the write was refused.
        /// </summary>
        /// <remarks>
        /// **A caller is told the kind of refusal and the operator is told the reason.** The route
        /// answers `503 busy`, `409 conflict`, `412` or `500 internal error`, which is all a client
        /// can act on, and several of the reasons behind them are something only the operator can
        /// clear: a dialog left open on the node, a file on disk that is not the one KeePass read,
        /// a database with no file yet, saving disallowed by policy. None of them was written
        /// anywhere, so an agent retrying a `503 busy` for as long as it was allowed to left the
        /// operator nothing to find. At `Warn`, the level `IGateLog` keeps for something refused.
        ///
        /// Not an absence, which every route answers alike and whose node the audit names, and not
        /// a change that was made and could not be saved, which the writer logs itself with the
        /// exception, and that says more than its message does.
        /// </remarks>
        private VaultWrite Logged(VaultWrite write)
        {
            if (write == null)
            {
                return null;
            }

            if (write.IsBusy)
            {
                _log.Warn("a change was refused as busy: " + write.Reason);
            }
            else if (write.IsPreconditionFailed)
            {
                // Something only the client can clear, and the reason names how: a node that
                // changed by reading it again, a field that will not read by sending a field that
                // does, and a client handed no tags by nothing short of a rule holding `read` on
                // the node. Still worth a line: a client refused this way over and over is one
                // writing over somebody, guessing, or naming a tag it was never handed, and the
                // operator is the one who can tell which.
                _log.Warn("a change was refused, its precondition failing: " + write.Reason);
            }
            else if (write.IsConflict)
            {
                _log.Warn("a change was refused as a conflict: " + write.Reason);
            }
            else if (write.IsFailed)
            {
                _log.Warn("a change could not be attempted: " + write.Reason);
            }

            return write;
        }

        private VaultWrite Change(Func<VaultChange> change)
        {
            var blocked = WriteBlocked;
            return Logged(blocked != null ? VaultWrite.Failed(blocked) : _writer.Change(_database, change));
        }

        /// <summary>
        /// The node's tag as a change finds it, taken the first time something reads it and never
        /// again.
        /// </summary>
        /// <remarks>
        /// Read under the writer's lock, after every refusal that does not depend on it, by the
        /// precondition when its answer depends on the tag, and by the answer where the change
        /// turns out to change nothing, which is the tag the node still has. A tag is a digest over
        /// every string and attachment its caller may read, so a write that reads it in neither
        /// place takes none: taken whenever a precondition or an answer was asked for, it was work
        /// on KeePass's thread that nothing read on every change that cannot leave its node
        /// unchanged, and on every write asking `*` or naming no tag.
        /// </remarks>
        private Func<string> Before(PwGroup group)
        {
            string tag = null;
            return () => tag ?? (tag = _tags.Of(group));
        }

        /// <summary>
        /// An entry's tag as a change finds the entry, of the view its caller has of it.
        /// </summary>
        /// <remarks>
        /// **The caller's own view, through the builder every read answers from**, so the tag a
        /// change is asked against is the tag a read handed the same caller, and a value the
        /// caller may not read is in neither. Null for a sight that does not read, whose
        /// precondition <see cref="VaultAsking.Tagging"/> has already made a precondition
        /// nothing meets.
        /// </remarks>
        private Func<string> Before(PwEntry entry, VaultTagging tagging)
        {
            string tag = null;
            return () => tag ?? (tag = Seen(entry, described => tagging.Sight).Tag);
        }

        /// <summary>
        /// What a change's caller is refused before the change is made, or null when the rules
        /// allow it.
        /// </summary>
        /// <remarks>
        /// **The first thing a change does once it has found its node**, inside its turn on
        /// KeePass's thread, where neither the node nor the rules above it can move before the
        /// answer is acted on. Everything a request decided before that turn was decided about a
        /// database the operator went on editing, so none of it is taken on trust here.
        /// <see cref="VaultAsking"/> has the argument.
        ///
        /// **A change handed no questions is refused**, as the rules refusing it is.
        ///
        /// **Busy when the questions cannot be answered.** The admission records are read again
        /// for them, and records that will not list throw <see cref="VaultUnsettled"/>, which is
        /// a database to ask again and neither a refusal nor a defect.
        /// </remarks>
        /// <param name="tagging">What the change does about the tag of the node as it found it.</param>
        private static VaultWrite Refusal(VaultAsking asking, VaultFound found, out VaultTagging tagging)
        {
            tagging = null;

            try
            {
                if (asking == null || !asking.Allows(found))
                {
                    return VaultWrite.Refused();
                }

                tagging = asking.Tagging(found);
                return null;
            }
            catch (VaultUnsettled error)
            {
                return VaultWrite.Busy(error.Message);
            }
        }

        /// <summary>The same question for a change that deals in no tag, which a create is.</summary>
        private static VaultWrite Refusal(VaultAsking asking, VaultFound found)
        {
            VaultTagging tagging;
            return Refusal(asking, found, out tagging);
        }

        /// <summary>The tag a change left, when its answer is to carry one, and null otherwise.</summary>
        /// <remarks>
        /// **Asked of the node as the change left it**, in the same turn as the tag is taken, so
        /// the tag handed out covers nothing its caller may not read at that moment: a change
        /// that makes a string protected leaves a view its own caller reads less of, and the
        /// tag is of the view after.
        /// </remarks>
        private string After(VaultAsking asking, PwGroup group)
        {
            var tagging = Left(asking, new VaultFound(Describe(group)));
            return tagging != null && tagging.Answers ? _tags.Of(group) : null;
        }

        private string After(VaultAsking asking, PwEntry entry)
        {
            // The sight is asked about the entry the view is being taken of, so the question,
            // the view and the tag are of one entry.
            var answers = false;
            var view = Seen(entry, described =>
            {
                var tagging = Left(asking, new VaultFound(described, false));
                answers = tagging != null && tagging.Answers;
                return tagging == null ? null : tagging.Sight;
            });

            return answers ? view.Tag : null;
        }

        /// <summary>
        /// What a change does about the tag of the node it left, or null when that cannot be
        /// answered.
        /// </summary>
        /// <remarks>
        /// A question that cannot be answered leaves the tag out and the change in: the change
        /// is made by now, and a client told `busy` would make it again.
        /// </remarks>
        private static VaultTagging Left(VaultAsking asking, VaultFound left)
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

        /// <summary>Whether every string a body names already holds the value it asks for.</summary>
        /// <remarks>
        /// **A name the entry does not carry is a change**, since writing it adds a string. One it
        /// does carry is compared the way KeePass compares two of its own, apart from the
        /// protection flag, which an update keeps rather than sets. `Clashes` has been asked by the
        /// time this is, so a name the store finds is the name the caller sent, byte for byte.
        /// </remarks>
        private static bool AlreadyHolds(PwEntry entry, IReadOnlyDictionary<string, string> fields)
        {
            if (fields == null)
            {
                return true;
            }

            foreach (var field in fields)
            {
                var existing = entry.Strings.Get(field.Key);
                if (existing == null
                    || !existing.Equals(new ProtectedString(existing.IsProtected, field.Value ?? string.Empty), false))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether the entry's expiry already says what an update asks of it.</summary>
        /// <remarks>
        /// Null asks nothing. No expiry is held when the entry does not expire, whatever moment it
        /// keeps, since taking one off leaves the moment where it is. A moment is held when the
        /// entry expires at exactly that instant, which is the `expires` a read answered for any
        /// moment a `.kdbx` kept, since the file keeps none to a fraction of a second: writing back
        /// what was read writes nothing. One `auto` set in this session does carry a fraction,
        /// which the wire leaves off, so it is written once more, to the second.
        /// <see cref="VaultExpiry.Auto"/> itself never arrives here, an update having refused it
        /// before the change.
        /// </remarks>
        private static bool AlreadyExpires(PwEntry entry, VaultExpiry expiry)
        {
            if (expiry == null)
            {
                return true;
            }

            if (!expiry.When.HasValue)
            {
                return !entry.Expires;
            }

            return entry.Expires && When(entry.ExpiryTime) == expiry.When.Value;
        }

        private string Blocking(PwGroup group)
        {
            return _writer == null ? null : _writer.Blocking(group);
        }

        private string Blocking(PwEntry entry)
        {
            return _writer == null ? null : _writer.Blocking(entry);
        }

        private string BlockingRemoval(PwGroup group)
        {
            return _writer == null ? null : _writer.BlockingRemoval(group);
        }

        private string BlockingAdditions(PwGroup group)
        {
            return _writer == null ? null : _writer.BlockingAdditions(group);
        }

        /// <summary>
        /// Whether the entry carries a string under exactly this name, byte for byte.
        /// </summary>
        /// <remarks>
        /// **Asked before every dictionary call that takes a name from a caller**, and the reason
        /// is that KeePass's dictionary and this plugin's guards do not compare names the same
        /// way. An entry's strings are a `SortedDictionary` under the default comparer, which
        /// compares by the current culture, and the collation gives U+00AD, U+200D, U+2060 and
        /// U+FEFF no weight at all: on an entry carrying only `Password`, `Strings.Exists` is true
        /// for `Pass\u00ADword`, `Get` returns the password, and `Set` under that spelling
        /// replaces the password while keeping the stored key. Everything above this decides what
        /// a name is worth by comparing it byte for byte, so such a name was judged a custom field
        /// and applied to the credential: a client holding `update` and not `protected` overwrote
        /// a password, and one holding `strip` and `create` took the real one off and put an
        /// unprotected value where KeePass reads it from.
        ///
        /// Resolving the caller's name here is what makes the two agree. A name that is not one of
        /// the entry's own keys is a name the entry does not carry, which is the 404 every absent
        /// name already gets; a name that is one of them is the name the guard just judged.
        /// </remarks>
        private static bool Holds(PwEntry entry, string name)
        {
            if (entry == null || name == null)
            {
                return false;
            }

            foreach (var pair in entry.Strings)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The same question about an attachment, whose dictionary compares names the same way.
        /// </summary>
        private static bool Keeps(PwEntry entry, string name)
        {
            if (entry == null || name == null)
            {
                return false;
            }

            foreach (var pair in entry.Binaries)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether any of these names is one the store would put onto a key spelled differently.
        /// </summary>
        /// <remarks>
        /// The other direction of <see cref="Holds"/>. A name the entry does not carry can still
        /// be one its dictionary cannot tell apart from a name it does, and writing it then
        /// replaces that one's value under that one's key. The field and attachment routes name
        /// one thing each, so the conflict an existing name already answers covers them. The two
        /// entry routes take a whole map, so they ask about all of it before writing any of it,
        /// and about the map against itself as well, because two spellings the store cannot
        /// separate can arrive in one body.
        ///
        /// The comparison is the store's own, which is why this keeps its own sorted dictionary
        /// rather than a set: what matters is not that two names collide but which spelling the
        /// database would end up holding.
        /// </remarks>
        private static bool Clashes(PwEntry entry, IEnumerable<string> names)
        {
            var stored = new SortedDictionary<string, string>();

            if (entry != null)
            {
                foreach (var pair in entry.Strings)
                {
                    stored[pair.Key] = pair.Key;
                }
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

        /// <summary>What a caller is told about a name the database cannot keep apart from another.</summary>
        /// <remarks>
        /// A conflict, which is what a name the entry already carries is answered with, because
        /// that is what this is: the database holds that name already, under a spelling of its
        /// own. The reason names the field rather than quoting the caller's bytes back, since it
        /// goes to the log rather than to the client.
        /// </remarks>
        private static VaultWrite Indistinguishable()
        {
            return VaultWrite.Conflict(
                "this database cannot tell that field name apart from one the entry already has");
        }

        /// <summary>
        /// Why a removal cannot happen right now because it would have to make the first recycle
        /// bin, or null when it can.
        /// </summary>
        /// <remarks>
        /// **Only the branch that makes one.** KeePass's database settings dialog fills its recycle
        /// bin combo from `RecycleBinUuid` when it loads and writes the choice back on OK, so a
        /// database that had no bin when that dialog opened has the dialog holding "none": a bin
        /// made underneath it is undone by its OK, and everything the removal moved into it becomes
        /// an ordinary group under the root that `Discarded` no longer recognizes. `DESIGN.md` §138
        /// closes that relocation by putting the bin out of reach, and this is the one way it was
        /// still reachable. A bin that already existed when the dialog opened is what the dialog
        /// puts back, so nothing is refused for it.
        ///
        /// **Refused rather than made permanent.** The alternative to making a bin is destroying
        /// what was removed outright, which is a worse answer to a dialog being open than telling
        /// the caller to come back.
        /// </remarks>
        private string RecycleBinBlocked()
        {
            var root = _database.RootGroup;
            if (_writer == null || !_database.RecycleBinEnabled || root == null)
            {
                return null;
            }

            return Bin(root) != null ? null : _writer.BlockingRecycleBin(_database);
        }

        /// <summary>
        /// Whether a value written under this name would be protected, which is this database's
        /// own setting and not a list kept here.
        /// </summary>
        /// <remarks>
        /// KeePass keeps a per database setting for each of the standard fields, and honoring it is
        /// what keeps a value written through the gate as protected as one typed in would be.
        /// A custom field is not protected unless somebody says so when they create it.
        ///
        /// **Which name is a standard one is the database's answer, not a byte comparison.** This
        /// asked byte for byte, and the store does not: a file whose password string is keyed with a
        /// spelling the collation cannot tell from `Password` is one KeePass reads the password
        /// out of, shows in the password box and saves back under that key, while this reported a
        /// custom field carrying no flag and handed the value to every client with `read` and no
        /// `protected`. A file like that arrives rather than being made here, since the loader
        /// keeps whichever key it saw first and never repairs it, and <see cref="Holds"/> is what
        /// stops a caller making one. Asking the comparison the store itself uses is what makes
        /// the answer to "is this the password" one answer.
        /// </remarks>
        public bool Protects(string field)
        {
            if (field == null)
            {
                return false;
            }

            var protection = _database.MemoryProtection;

            // codeql[cs/complex-condition] the five standard fields as a table, each beside its own switch
            return (Names(field, PwDefs.TitleField) && protection.ProtectTitle)
                || (Names(field, PwDefs.UserNameField) && protection.ProtectUserName)
                || (Names(field, PwDefs.PasswordField) && protection.ProtectPassword)
                || (Names(field, PwDefs.UrlField) && protection.ProtectUrl)
                || (Names(field, PwDefs.NotesField) && protection.ProtectNotes);
        }

        /// <summary>
        /// Whether a stored key is one of the standard fields as far as the database is concerned.
        /// </summary>
        /// <remarks>
        /// The comparer by name, rather than a `StringComparison`, because the comparer is the
        /// point: it is the one KeePass's own string and binary dictionaries sort and look names
        /// up by, so whatever this answers is what `Strings.Get` would find. That comparer collates
        /// in the culture of the thread asking, so this holds because a read runs in KeePass's
        /// culture, which `GateListener` sees to.
        /// </remarks>
        private static bool Names(string field, string standard)
        {
            return Comparer<string>.Default.Compare(field, standard) == 0;
        }

        /// <summary>
        /// Whether a stored key is one of the five every entry has, as the database compares names.
        /// </summary>
        private static bool IsStandard(string field)
        {
            foreach (var standard in Standard)
            {
                if (Names(field, standard))
                {
                    return true;
                }
            }

            return false;
        }

        private ProtectedString Protect(string name, string value)
        {
            return new ProtectedString(Protects(name), value ?? string.Empty);
        }

        /// <summary>Gives a new entry the expiry its create asked for.</summary>
        /// <remarks>
        /// **`auto` is what KeePass's own Add Entry does, step for step.** It reads how many days
        /// KeePass's options give every new entry at the moment the entry is made, so a change to
        /// the options counts from the next create; it sets nothing when that number is negative,
        /// which is the option switched off, and that many days from now otherwise, zero included;
        /// and it sets nothing in the entry templates group or under it, where KeePass applies
        /// none of its new-entry defaults, because a template's expiry would be copied into every
        /// entry made from it. **It is asked for and never assumed.** A create naming no expiry
        /// makes an entry that does not expire, whatever the options say, so what a program gets
        /// is what it asked for.
        ///
        /// A number of days past the last date a `DateTime` holds throws out of `AddDays` here as
        /// it does out of KeePass's own Add Entry, and the writer answers that as a failure on this
        /// side. Only a configuration edited by hand can carry one, since the options dialog stops
        /// at 2920.
        /// </remarks>
        private void ExpireAsAsked(PwEntry entry, PwGroup group, VaultExpiry expiry)
        {
            if (expiry == null)
            {
                // A new `PwEntry` does not expire, which is what a create naming none asks for.
                return;
            }

            if (expiry.When.HasValue)
            {
                entry.Expires = true;
                entry.ExpiryTime = expiry.When.Value.UtcDateTime;
                return;
            }

            if (!expiry.IsAuto || InTemplates(group))
            {
                return;
            }

            var days = KeePass.Program.Config.Defaults.NewEntryExpiresInDays;
            if (days < 0)
            {
                return;
            }

            entry.Expires = true;
            entry.ExpiryTime = DateTime.UtcNow.AddDays(days);
        }

        /// <summary>
        /// Whether a group is the database's entry templates group or sits inside it.
        /// </summary>
        /// <remarks>
        /// Asked up the group's own chain of parents rather than by finding the templates group,
        /// which would be a search of the whole tree for one identifier. A setting naming no group
        /// in the tree matches nothing, which is how KeePass reads one too.
        /// </remarks>
        private bool InTemplates(PwGroup group)
        {
            var templates = _database.EntryTemplatesGroup;
            if (templates == null || templates.Equals(PwUuid.Zero))
            {
                return false;
            }

            for (var current = group; current != null; current = current.ParentGroup)
            {
                if (current.Uuid.Equals(templates))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes an entry the way KeePass does: to the recycle bin when the database keeps one,
        /// and permanently when it does not.
        /// </summary>
        /// <remarks>
        /// KeePass's own delete also removes an entry already in the bin permanently. No entry in
        /// the bin reaches this, because `FindEntry` finds none, `DESIGN.md` §138, and the bin it
        /// would be found in is the bin `RecycleBin` answers.
        /// </remarks>
        /// <returns>How to put the entry back where it came from.</returns>
        private Action Discard(PwEntry entry)
        {
            var parent = entry.ParentGroup;
            var position = parent == null ? -1 : parent.Entries.IndexOf(entry);
            var restore = Restoring(entry);
            Action unmakeBin;
            var bin = RecycleBin(out unmakeBin);

            if (bin != null && parent != null)
            {
                var wasPrevious = entry.PreviousParentGroup;
                parent.Entries.Remove(entry);

                // The three-argument overload, and the previous parent, which is what KeePass's own
                // delete records. The two-argument one forwards with `false` and never stamps
                // `LocationChanged`, so the entry arrives in the bin still claiming to have been
                // moved when it was created. A replica that still holds it in its old group then
                // has an equal timestamp, `RelocateEntries` compares with a strict `>`, and the tie
                // leaves the local side alone. Merging from the replica then lifts the deleted
                // secret back out of the bin, which is exactly what `deleteEntry` promises cannot
                // happen.
                entry.PreviousParentGroup = parent.Uuid;
                bin.AddEntry(entry, true, true);

                // Touched as `DeleteGroup` touches a group it bins, and for its reason: an entry
                // stamped as modified here won the next synchronize as the newer content over an
                // edit made to it on another copy, which the merge then filed in its history.
                entry.Touch(false, false);

                return () =>
                {
                    bin.Entries.Remove(entry);
                    Reinsert(parent, entry, position);
                    entry.PreviousParentGroup = wasPrevious;
                    restore();

                    if (unmakeBin != null)
                    {
                        unmakeBin();
                    }
                };
            }

            if (parent != null)
            {
                parent.Entries.Remove(entry);
            }

            var deleted = new PwDeletedObject(entry.Uuid, DateTime.UtcNow);
            _database.DeletedObjects.Add(deleted);

            return () =>
            {
                _database.DeletedObjects.Remove(deleted);

                if (parent != null)
                {
                    Reinsert(parent, entry, position);
                }

                restore();

                if (unmakeBin != null)
                {
                    unmakeBin();
                }
            };
        }

        /// <summary>The recycle bin, made if this database wants one and has not got one yet.</summary>
        /// <param name="unmake">
        /// How to take back a bin this call had to make, or null when it found one already there.
        /// A removal whose save never lands has to leave the database as it was, and on the first
        /// removal into a fresh bin that includes the bin and the `RecycleBinUuid` pointing at it.
        /// </param>
        private PwGroup RecycleBin(out Action unmake)
        {
            unmake = null;

            var root = _database.RootGroup;
            if (!_database.RecycleBinEnabled || root == null)
            {
                return null;
            }

            var existing = Bin(root);
            if (existing != null)
            {
                return existing;
            }

            var wasUuid = _database.RecycleBinUuid;

            // `RecycleBinChanged` is left alone, as KeePass's own first delete leaves it. A merge
            // reads it as the moment somebody decided about the bin, so a stamp here made an
            // agent's first removal outrank, at the next synchronize, an operator turning the bin
            // off on another copy.
            var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin) { EnableAutoType = false, EnableSearching = false };
            root.AddGroup(bin, true);
            _database.RecycleBinUuid = bin.Uuid;

            unmake = () =>
            {
                root.Groups.Remove(bin);
                _database.RecycleBinUuid = wasUuid;
            };

            return bin;
        }

        /// <summary>
        /// The recycle bin under this root as it already is, or null when this database has none
        /// yet.
        /// </summary>
        /// <remarks>
        /// Separate from `RecycleBin`, which makes one when the database wants one and has not got
        /// one. Asking whether something is in the bin must never be the thing that creates it.
        ///
        /// **Asked once per read, and handed to <see cref="Discarded(PwGroup, PwGroup)"/>.**
        /// Finding the bin is a search of the whole tree, and KeePass appends a bin it makes as
        /// the root's last child, so the search visits every other group first. Asked once per
        /// item, a listing of the database cost its entries times its groups: measured at 20,000
        /// entries in 1,000 groups, a second and a half and 778 MiB of garbage in KeePass's own
        /// process for every `GET /entries`.
        ///
        /// **The root is the caller's**, read once by it, for the reason
        /// <see cref="FindGroup(PwGroup, string)"/> gives.
        /// </remarks>
        private PwGroup Bin(PwGroup root)
        {
            return Bin(_database, root);
        }

        /// <summary>
        /// The group a database names as its recycle bin, under this root, or null when it names
        /// none there.
        /// </summary>
        /// <remarks>
        /// Shared with `DatabaseNodes`, so that what the Profiles page counts and what the vault
        /// leaves out are one bin.
        /// </remarks>
        internal static PwGroup Bin(PwDatabase database, PwGroup root)
        {
            if (database == null
                || root == null
                || database.RecycleBinUuid == null
                || database.RecycleBinUuid.Equals(PwUuid.Zero))
            {
                return null;
            }

            return root.FindGroup(database.RecycleBinUuid, true);
        }

        /// <summary>
        /// Whether this group is the recycle bin or sits inside it.
        /// </summary>
        /// <remarks>
        /// **The bin is out of reach of this API**, which `DESIGN.md` §138 argues. A removal is
        /// usually a move rather than a destruction, so without this a client that may remove an
        /// entry it may not read could relocate that entry into a group a rule at the root lets it
        /// read. Making the destination unreachable closes that without asking anybody to write a
        /// rule for it, and it is right on its own account: the bin holds what the operator decided
        /// they were finished with.
        /// </remarks>
        internal static bool Discarded(PwGroup group, PwGroup bin)
        {
            if (bin == null || group == null)
            {
                return false;
            }

            for (var current = group; current != null; current = current.ParentGroup)
            {
                if (ReferenceEquals(current, bin))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the recycle bin is the one thing in this group.</summary>
        private bool HoldsOnlyTheBin(PwGroup group)
        {
            var bin = Bin(_database.RootGroup);
            return bin != null
                   && group.Entries.UCount == 0
                   && group.Groups.UCount == 1
                   && ReferenceEquals(group.Groups.GetAt(0), bin);
        }

        /// <summary>Whether this entry sits in the recycle bin.</summary>
        internal static bool Discarded(PwEntry entry, PwGroup bin)
        {
            return entry != null && Discarded(entry.ParentGroup, bin);
        }

        private PwGroup FindGroup(string uuid)
        {
            return FindGroup(_database.RootGroup, uuid);
        }

        /// <summary>A group under this root, by identifier or by the root alias.</summary>
        /// <remarks>
        /// **The root is read once, by the caller, and never again.** A lock clears the database
        /// on the user interface thread, and clearing it sets the root group to null, while every
        /// lookup runs on a connection thread. A lookup that asked whether the root was there and
        /// then read it again met that null the second time: a `NullReferenceException`, which no
        /// filter names, answered `500` and counted as a defect in this plugin for a lock the
        /// operator took on purpose. Held in a local, a lock landing mid lookup leaves it looking
        /// through the tree it started in, which is exactly what a read that began a moment
        /// earlier would have seen.
        /// </remarks>
        private PwGroup FindGroup(PwGroup root, string uuid)
        {
            if (string.IsNullOrEmpty(uuid) || root == null)
            {
                return null;
            }

            if (string.Equals(uuid, Root, StringComparison.Ordinal))
            {
                return root;
            }

            var id = Identifier(uuid);
            var group = id == null ? null : root.FindGroup(id, true);
            return Discarded(group, Bin(root)) ? null : group;
        }

        /// <summary>An entry by identifier, reading the root once for the reason `FindGroup` gives.</summary>
        private PwEntry FindEntry(string uuid)
        {
            var root = _database.RootGroup;
            if (string.IsNullOrEmpty(uuid) || root == null)
            {
                return null;
            }

            var id = Identifier(uuid);
            var entry = id == null ? null : root.FindEntry(id, true);
            return Discarded(entry, Bin(root)) ? null : entry;
        }

        /// <summary>
        /// A UUID from what a caller sent, or null for anything that is not one.
        /// </summary>
        /// <remarks>
        /// Refused rather than repaired, like every other read of something a caller sent. What
        /// comes back from refusing is "there is nothing there", which is the same answer a
        /// perfectly formed identifier for something that does not exist gets.
        /// </remarks>
        private PwUuid Identifier(string uuid)
        {
            if (uuid.Length != 32)
            {
                return null;
            }

            foreach (var c in uuid)
            {
                if (!Ascii.IsHexDigit(c))
                {
                    return null;
                }
            }

            try
            {
                return new PwUuid(MemUtil.HexStringToByteArray(uuid));
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("an identifier could not be read: " + error.Message);
                return null;
            }
        }

        private VaultGroup Describe(PwGroup group)
        {
            if (group == null)
            {
                return null;
            }

            return new VaultGroup(
                group.Uuid.ToHexString(),
                group.Name,
                group.Notes,
                group.ParentGroup == null ? null : group.ParentGroup.Uuid.ToHexString(),
                When(group.CreationTime),
                When(group.LastModificationTime),
                KdbxNodes.Of(group, _hold));
        }

        private VaultEntry Describe(PwEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new VaultEntry(
                entry.Uuid.ToHexString(),
                entry.ParentGroup == null ? null : entry.ParentGroup.Uuid.ToHexString(),
                entry.Strings.ReadSafe(PwDefs.TitleField),
                entry.Strings.ReadSafe(PwDefs.UrlField),
                When(entry.CreationTime),
                When(entry.LastModificationTime),
                KdbxNodes.Of(entry, _hold),
                Guards(entry, PwDefs.TitleField),
                Guards(entry, PwDefs.UrlField),
                entry.Expires ? When(entry.ExpiryTime) : (DateTimeOffset?)null);
        }

        /// <summary>
        /// An entry as a view carries it: where it is, its times, its expiry and the node the
        /// rules are asked about, with no title and no URL.
        /// </summary>
        /// <remarks>
        /// **A view holds a string only as one of its fields**, where the sight decides whether
        /// the value is there. A title or a URL in the description beside them would be read to
        /// build every view and held whatever the sight, a protected title among them, with
        /// nothing to stop a later route rendering it. So both are null here and neither is
        /// read, and what a listing shows of them comes from <see cref="Describe(PwEntry)"/>.
        /// </remarks>
        private VaultEntry Placed(PwEntry entry)
        {
            return new VaultEntry(
                entry.Uuid.ToHexString(),
                entry.ParentGroup == null ? null : entry.ParentGroup.Uuid.ToHexString(),
                null,
                null,
                When(entry.CreationTime),
                When(entry.LastModificationTime),
                KdbxNodes.Of(entry, _hold),
                false,
                false,
                entry.Expires ? When(entry.ExpiryTime) : (DateTimeOffset?)null);
        }

        /// <summary>Whether one of an entry's strings is protected.</summary>
        private bool Guards(PwEntry entry, string field)
        {
            return Guards(field, entry.Strings.Get(field));
        }

        /// <summary>
        /// Whether a string of this name, carrying this flag, is one a client without the
        /// `protected` scope may be handed.
        /// </summary>
        /// <remarks>
        /// **Both halves, which is what the write side has always asked.** A string is protected
        /// either because of what it is called, which `Protects` answers off the database's
        /// `MemoryProtection`, or because the flag on that one string says so. Asking only the
        /// flag read a standard string that some other producer stored unflagged as an ordinary
        /// value: `PUT` refused to write it and `GET` handed it over in clear, reporting it as
        /// never having been a secret. `CLAUDE.md` and `openapi.yaml` §508 both say the database's
        /// own setting is what decides, and now both directions ask it.
        ///
        /// The flag term stays, because a custom field's protection is only ever the flag: the
        /// database says nothing about a name it does not know, and `UpdateField` keeps whatever
        /// the operator marked. It is asked first, being one field read where the database's
        /// answer is five comparisons by the current culture, and a tag asks this of every string.
        /// </remarks>
        private bool Guards(string field, ProtectedString value)
        {
            return (value != null && value.IsProtected) || Protects(field);
        }

        /// <summary>
        /// KeePass keeps its times as UTC and says so with the kind, except where it does not, so
        /// this says so for it rather than letting a local time be read as a universal one.
        /// </summary>
        /// <remarks>Shared with <see cref="NodeTags"/>, so that a tag reads a moment as a route serves it.</remarks>
        internal static DateTimeOffset When(DateTime time)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(time.ToUniversalTime(), DateTimeKind.Utc));
        }
    }
}
