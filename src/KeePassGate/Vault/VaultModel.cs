using System;
using System.Collections.Generic;
using KeePassGate.Acl;

namespace KeePassGate.Vault
{
    /// <summary>
    /// A group, as the API answers about one.
    /// </summary>
    /// <remarks>
    /// Carries its own <see cref="Node"/> rather than leaving the caller to look one up, so the
    /// thing being described and the thing being authorized cannot come apart. That matters most
    /// where a handler filters a list: it asks about the node it is holding, not about a node it
    /// fetched again by name.
    /// </remarks>
    public sealed class VaultGroup
    {
        public VaultGroup(
            string uuid, string name, string notes, string parent,
            DateTimeOffset created, DateTimeOffset modified, IAclNode node)
        {
            Uuid = uuid;
            Name = name;
            Notes = notes;
            Parent = parent;
            Created = created;
            Modified = modified;
            Node = node;
        }

        /// <summary>Uppercase hex, as KeePass's `PwUuid.ToHexString` writes a UUID.</summary>
        public string Uuid { get; private set; }

        public string Name { get; private set; }

        public string Notes { get; private set; }

        /// <summary>The group above, or null at the root.</summary>
        public string Parent { get; private set; }

        public DateTimeOffset Created { get; private set; }

        public DateTimeOffset Modified { get; private set; }

        /// <summary>What the rules are asked about when this is what a request is about.</summary>
        public IAclNode Node { get; private set; }
    }

    /// <summary>An entry, without its fields, which are asked for separately.</summary>
    public sealed class VaultEntry
    {
        public VaultEntry(
            string uuid, string group, string title, string url,
            DateTimeOffset created, DateTimeOffset modified, IAclNode node,
            bool titleProtected, bool urlProtected)
        {
            Uuid = uuid;
            Group = group;
            Title = title;
            Url = url;
            Created = created;
            Modified = modified;
            Node = node;
            TitleProtected = titleProtected;
            UrlProtected = urlProtected;
        }

        public string Uuid { get; private set; }

        /// <summary>The group holding it.</summary>
        public string Group { get; private set; }

        /// <summary>
        /// What a listing says about it, beside its identifier.
        /// </summary>
        /// <remarks>
        /// A title and a URL are what make a list usable at all, and granting `list` is how an
        /// operator says they may be seen. Everything else an entry holds needs `read`.
        ///
        /// Either of them can be protected, and then `list` is not enough on its own:
        /// <see cref="TitleProtected"/> and <see cref="UrlProtected"/> say which, and the
        /// handler withholds those without the `protected` switch, as the read route does.
        /// </remarks>
        public string Title { get; private set; }

        public string Url { get; private set; }

        /// <summary>Whether the entry protects its title.</summary>
        /// <remarks>
        /// Read per string rather than off the database's `MemoryProtection`, because that is the
        /// default a database was made with and this is what the entry actually carries. Two flags
        /// rather than a list of names, so that nothing above the vault has to know what a field
        /// is called in KeePass.
        /// </remarks>
        public bool TitleProtected { get; private set; }

        /// <summary>Whether the entry protects its URL.</summary>
        public bool UrlProtected { get; private set; }

        public DateTimeOffset Created { get; private set; }

        public DateTimeOffset Modified { get; private set; }

        public IAclNode Node { get; private set; }
    }

    /// <summary>One of an entry's strings, named and described but not quoted.</summary>
    /// <remarks>
    /// **No length.** A `list` grant says which fields exist; the length of a password is worth
    /// something to whoever is guessing it, and handing that over is not what listing means.
    /// </remarks>
    public sealed class VaultField
    {
        public VaultField(string name, bool isProtected)
        {
            Name = name;
            IsProtected = isProtected;
        }

        public string Name { get; private set; }

        /// <summary>Whether the database keeps it protected in memory, as it does a password.</summary>
        public bool IsProtected { get; private set; }
    }

    /// <summary>
    /// One of an entry's attachments, named and sized.
    /// </summary>
    /// <remarks>
    /// The size stays, unlike a field's length, because a client has to decide whether to ask for
    /// something before it asks, and an attachment can be megabytes.
    /// </remarks>
    public sealed class VaultAttachment
    {
        public VaultAttachment(string name, int size)
        {
            Name = name;
            Size = size;
        }

        public string Name { get; private set; }

        public int Size { get; private set; }
    }

    /// <summary>What happened to a change, and whether it is worth trying again.</summary>
    /// <remarks>
    /// Pairing's three answers with three more that a change to a named node can have and an
    /// admission record cannot: a create's identifier, a conflict, and a node that is not
    /// there. `Busy` is told apart from
    /// a failure because a dialog open on the node will not be open forever, and a client that
    /// knows the difference can wait instead of giving up.
    /// </remarks>
    public sealed class VaultWrite
    {
        private VaultWrite(bool ok, bool busy, bool conflict, string uuid, string reason)
        {
            Ok = ok;
            IsBusy = busy;
            IsConflict = conflict;
            Uuid = uuid;
            Reason = reason ?? string.Empty;
        }

        public bool Ok { get; private set; }

        /// <summary>Try again shortly: a dialog is open on it, or another write holds the lock.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>The change asks for something the database already says otherwise about.</summary>
        public bool IsConflict { get; private set; }

        /// <summary>
        /// The change was made and did not reach the disk.
        /// </summary>
        /// <remarks>
        /// Its own state because it is none of the others. It is not a refusal, not a conflict and
        /// not an absence, and answering it as any of those tells a caller something untrue about
        /// its own request: `Failed` and `Missing` are the same triple, so a save that never
        /// reached the disk came back as "no such route" and a client retrying that made a second
        /// entry beside the first.
        /// </remarks>
        public bool IsUnsaved { get; private set; }

        /// <summary>What was created, when something was.</summary>
        public string Uuid { get; private set; }

        /// <summary>
        /// Why, in the vault's own words. Nothing reads it yet, and it is never sent to a caller.
        /// </summary>
        public string Reason { get; private set; }

        public static VaultWrite Done()
        {
            return new VaultWrite(true, false, false, null, null);
        }

        public static VaultWrite Created(string uuid)
        {
            return new VaultWrite(true, false, false, uuid, null);
        }

        public static VaultWrite Busy(string reason)
        {
            return new VaultWrite(false, true, false, null, reason);
        }

        public static VaultWrite Conflict(string reason)
        {
            return new VaultWrite(false, false, true, null, reason);
        }

        public static VaultWrite Failed(string reason)
        {
            return new VaultWrite(false, false, false, null, reason);
        }

        /// <summary>Made, and not on the disk. The database is holding an unsaved change.</summary>
        public static VaultWrite Unsaved(string reason)
        {
            return new VaultWrite(false, false, false, null, reason) { IsUnsaved = true };
        }

        /// <summary>Nothing of that name is there, which every route answers the same way.</summary>
        public static VaultWrite Missing()
        {
            return new VaultWrite(false, false, false, null, "there is nothing there");
        }
    }

    /// <summary>An entry and everything it holds, which is what `read` answers with.</summary>
    public sealed class VaultEntryDetail
    {
        public VaultEntryDetail(VaultEntry entry, IReadOnlyDictionary<string, string> fields, IReadOnlyList<string> protectedFields)
        {
            Entry = entry;
            Fields = fields;
            Protected = protectedFields;
        }

        public VaultEntry Entry { get; private set; }

        /// <summary>Every string the entry holds, by KeePass's own names, values included.</summary>
        public IReadOnlyDictionary<string, string> Fields { get; private set; }

        /// <summary>Which of them the database keeps protected.</summary>
        public IReadOnlyList<string> Protected { get; private set; }
    }
}
