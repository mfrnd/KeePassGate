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
            bool titleProtected, bool urlProtected, DateTimeOffset? expires)
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
            Expires = expires;
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

        /// <summary>Whether the title is protected.</summary>
        /// <remarks>
        /// The string's own flag or the database's `MemoryProtection`, whichever says so, which is
        /// what the write side has always asked. A standard string stored unflagged in a database
        /// that protects the name is still a secret, and reading only the flag handed it to a
        /// listing that `read` without the `protected` scope refused. Two flags rather than a list
        /// of names, so that nothing above the vault has to know what a field is called in KeePass.
        /// </remarks>
        public bool TitleProtected { get; private set; }

        /// <summary>Whether the entry protects its URL.</summary>
        public bool UrlProtected { get; private set; }

        public DateTimeOffset Created { get; private set; }

        public DateTimeOffset Modified { get; private set; }

        /// <summary>When the entry expires, or null when it does not.</summary>
        /// <remarks>
        /// KeePass keeps two things, whether an entry expires and when, and keeps the moment while
        /// the entry does not expire as well. One value here, null for an entry that does not
        /// expire, so that nothing above the vault can read a moment that is not in force. `read`
        /// answers it, and a listing asked to leave expired entries out judges it against the
        /// gate's clock.
        /// </remarks>
        public DateTimeOffset? Expires { get; private set; }

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

        /// <summary>
        /// Whether it is kept protected in memory, as a password is, either because the string
        /// carries the flag or because the database protects a field of that name.
        /// </summary>
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
            : this(name, size, null)
        {
        }

        public VaultAttachment(string name, int size, byte[] content)
        {
            Name = name;
            Size = size;
            Content = content;
        }

        public string Name { get; private set; }

        public int Size { get; private set; }

        /// <summary>
        /// What it holds, or null when that was not read: a listing reads none, and a read leaves
        /// out one larger than it was allowed to hand over.
        /// </summary>
        public byte[] Content { get; private set; }
    }

    /// <summary>What happened to a change, and whether it is worth trying again.</summary>
    /// <remarks>
    /// Pairing's three answers with three more that a change to a named node can have and an
    /// admission record cannot: a create's identifier, a conflict, and a node that is not
    /// there. `Busy` is told apart from
    /// a failure because a dialog open on the node will not be open forever, and a client that
    /// knows the difference can wait instead of giving up.
    ///
    /// **The refusals are told apart from this plugin failing**, which is what `IsFailed`,
    /// `IsUnsaved` and `IsUnverified` are for. Everything else here is an answer about the
    /// request; those three are an answer about the gate, and a caller acts on them differently.
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

        /// <summary>
        /// The change was made and saved, and the file does not hold what KeePass wrote to it.
        /// </summary>
        /// <remarks>
        /// Its own state rather than <see cref="IsUnsaved"/>, because the two say opposite things
        /// about what is left behind. That one is a change that never reached the disk and has
        /// been taken back out, so a retry cannot duplicate anything. This one is a change that
        /// was written to a file which then disagreed with what was written, and nothing knows
        /// which of the two the file holds: it is not taken back out, and a retry may well make a
        /// second copy. A caller cannot be told to treat them alike.
        /// </remarks>
        public bool IsUnverified { get; private set; }

        /// <summary>
        /// The change could not be attempted, and not because of anything the client sent.
        /// </summary>
        /// <remarks>
        /// Its own state for the reason <see cref="IsUnsaved"/> has one, and it is the other half
        /// of the same mistake: `Failed` and `Missing` were the same triple, so a database that
        /// closed under a request, a change KeePass answered with an error, and a vault that
        /// decided nothing all came back as `404 no such route`. That answer says the request had
        /// nowhere to go. A node the rules refuse and a node that is not there do say that, and
        /// say it alike on purpose; a failure here is neither of them, and reporting it as one
        /// sent a caller looking at its own identifiers for a fault that was on this side.
        ///
        /// **A call breaking what `IVault` asks of its caller is answered this way as well**, which
        /// is an empty name for a new group, field or attachment, or <see cref="VaultExpiry.Auto"/>
        /// for an entry that exists already. The routing table's lengths keep the first from every
        /// client and the handler refuses the second, so only a defect above the vault can send
        /// either, and a failure on this side is exactly what that is.
        /// </remarks>
        public bool IsFailed { get; private set; }

        /// <summary>What was created, when something was.</summary>
        public string Uuid { get; private set; }

        /// <summary>
        /// Why, for the operator: `KdbxVault` writes it to the log when a change is refused. Never
        /// sent to a caller, who is told the kind of refusal and nothing more.
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

        /// <summary>Nothing was attempted, because this plugin could not attempt it.</summary>
        public static VaultWrite Failed(string reason)
        {
            return new VaultWrite(false, false, false, null, reason) { IsFailed = true };
        }

        /// <summary>Made, and not on the disk. The database is holding an unsaved change.</summary>
        public static VaultWrite Unsaved(string reason)
        {
            return new VaultWrite(false, false, false, null, reason) { IsUnsaved = true };
        }

        /// <summary>Saved, and the file does not hold what was written to it.</summary>
        public static VaultWrite Unverified(string reason)
        {
            return new VaultWrite(false, false, false, null, reason) { IsUnverified = true };
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

        /// <summary>
        /// Every string the entry holds, by KeePass's own names, values included, enumerated in
        /// the order KeePass keeps them, which is the order `read` writes them in.
        /// </summary>
        public IReadOnlyDictionary<string, string> Fields { get; private set; }

        /// <summary>
        /// Which of them are kept protected, by the string's own flag or by the database's setting
        /// for a field of that name.
        /// </summary>
        public IReadOnlyList<string> Protected { get; private set; }
    }

    /// <summary>What a create or an update asks of an entry's expiry.</summary>
    /// <remarks>
    /// **Null wherever one is taken means the request named none**, which a create reads as an
    /// entry that does not expire and an update as the expiry the entry already has: an absent
    /// member means the same on those two routes as it does for every other member of their bodies.
    ///
    /// The three values are the three things a request can ask for. `openapi.yaml` §506 says how
    /// each is spelled on the wire and why `auto` is a create's alone.
    /// </remarks>
    public sealed class VaultExpiry
    {
        private VaultExpiry(DateTimeOffset? when, bool auto)
        {
            When = when;
            IsAuto = auto;
        }

        /// <summary>
        /// The entry does not expire, and one that did stops: KeePass's `Expires` is cleared and
        /// its `ExpiryTime` left as it was, which is what KeePass's own entry dialog does.
        /// </summary>
        public static readonly VaultExpiry Never = new VaultExpiry(null, false);

        /// <summary>
        /// Whatever expiry KeePass's options give every new entry, applied where KeePass's own Add
        /// Entry applies it, and none where it applies none.
        /// </summary>
        /// <remarks>
        /// A create's alone. An entry that exists already has an expiry, or has none, because
        /// somebody decided so, and an update asking for this would be asking KeePass's options to
        /// decide it over again.
        /// </remarks>
        public static readonly VaultExpiry Auto = new VaultExpiry(null, true);

        /// <summary>When the entry expires, or null for <see cref="Never"/> and <see cref="Auto"/>.</summary>
        public DateTimeOffset? When { get; private set; }

        /// <summary>Whether this is <see cref="Auto"/>.</summary>
        public bool IsAuto { get; private set; }

        /// <summary>An entry that expires at this moment.</summary>
        public static VaultExpiry At(DateTimeOffset when)
        {
            return new VaultExpiry(when, false);
        }
    }
}
