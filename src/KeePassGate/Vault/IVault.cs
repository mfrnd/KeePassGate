using System.Collections.Generic;

namespace KeePassGate.Vault
{
    /// <summary>
    /// One open database, as the API sees it: groups, entries, fields and attachments, and the
    /// changes that can be made to them.
    /// </summary>
    /// <remarks>
    /// **The seam that keeps the handlers testable.** Everything above the KeePass boundary in this
    /// plugin is written against an interface small enough to fake, and this is the one the API
    /// needs. `KdbxVault` is the only implementation that knows what a `PwEntry` is; the handlers
    /// never find out.
    ///
    /// **Reads happen on the connection's thread**, like every other read here, because KeePass's
    /// object model answers from one and `DESIGN.md` §119 measured that rather than assuming
    /// it. **Writes marshal and save**, and do not return until the database says it has no unsaved
    /// changes, so a client told that something was written can believe it.
    ///
    /// **`root` is a group identifier.** Every method taking a group takes it, so a client holding
    /// `create` and nothing else can put an entry somewhere without first being granted `list` to
    /// find out where. No verb implies another, and an identifier can as easily have arrived from
    /// an operator as from a listing.
    ///
    /// Nothing here filters by permission. The rules are asked by the router, about the node each
    /// of these carries, and a vault that quietly returned less than it was asked for would make
    /// the audit a record of the wrong question.
    /// </remarks>
    public interface IVault
    {
        /// <summary>The alias every group identifier accepts, resolving to the root group.</summary>
        string RootAlias { get; }

        /// <summary>
        /// Whether a value written under this name would be one the database protects.
        /// </summary>
        /// <remarks>
        /// Asked rather than assumed, because the answer belongs to the database: KeePass decides
        /// per field and per database which strings it keeps encrypted in memory, and a caller that
        /// guessed would be inventing a definition of secret on somebody else's behalf. It is asked
        /// before a write, so that setting a password needs the same scope as reading one.
        /// </remarks>
        bool Protects(string field);

        /// <summary>A group by identifier or by the root alias, or null when there is none.</summary>
        VaultGroup Group(string uuid);

        /// <summary>Every group under this one, one level down.</summary>
        IReadOnlyList<VaultGroup> Subgroups(string uuid);

        /// <summary>Every group in the database, root included and the recycle bin aside, in tree
        /// order.</summary>
        IReadOnlyList<VaultGroup> Groups();

        /// <summary>An entry by identifier, or null when there is none.</summary>
        VaultEntry Entry(string uuid);

        /// <summary>An entry and everything it holds, or null when there is none.</summary>
        VaultEntryDetail Read(string uuid);

        /// <summary>The entries in a group, one level down.</summary>
        IReadOnlyList<VaultEntry> Entries(string groupUuid);

        /// <summary>Every entry in the database, the recycle bin aside, in tree order.</summary>
        IReadOnlyList<VaultEntry> Entries();

        /// <summary>The names of an entry's strings, never their values.</summary>
        IReadOnlyList<VaultField> Fields(string entryUuid);

        /// <summary>One string's value, or null when the entry or the name is not there.</summary>
        string Field(string entryUuid, string name);

        /// <summary>The names and sizes of an entry's attachments, never their contents.</summary>
        IReadOnlyList<VaultAttachment> Attachments(string entryUuid);

        /// <summary>
        /// One attachment, with its contents when it is no larger than <paramref name="most"/>
        /// bytes, or null when the entry or the name is not there.
        /// </summary>
        /// <remarks>
        /// **The size is asked before anything is decrypted, in the same read.** KeePass keeps an
        /// attachment's length beside its encrypted bytes, so one over the ceiling comes back with
        /// its size and no contents, and a caller refusing it never pays for the plaintext. Two
        /// reads, a listing and then the contents, let an attachment replaced in between be
        /// decrypted whole, at whatever size it had become, before anything could refuse it.
        /// </remarks>
        VaultAttachment Attachment(string entryUuid, string name, int most);

        /// <summary>
        /// Why a change cannot be made right now, or null when one can.
        /// </summary>
        /// <remarks>
        /// Whether this database can be written to at all: open, with a root group, and with a
        /// writer behind it. Asked once, before the change is attempted.
        ///
        /// Not the dialog question. A dialog open on a node holds a copy of what it is showing and
        /// writes that copy back when it is accepted, so a change made underneath one is a change
        /// the operator's OK destroys; `DatabaseWriter.Blocking` is what asks that, per node, on
        /// the thread that can see the dialogs.
        /// </remarks>
        string WriteBlocked { get; }

        /// <param name="name">
        /// Not empty. The routing table's lengths keep an empty one from every client, and a vault
        /// answers one as a failure on this side rather than make a group nobody can name.
        /// </param>
        VaultWrite CreateGroup(string parentUuid, string name, string notes);

        VaultWrite UpdateGroup(string uuid, string name, string notes);

        /// <summary>Removes a group, which is refused while it still holds anything.</summary>
        /// <remarks>
        /// A recursive delete would let one grant on a parent destroy entries that were never
        /// individually decided or recorded, which is the opposite of what a per-node rule is for.
        /// </remarks>
        VaultWrite DeleteGroup(string uuid);

        VaultWrite CreateEntry(string groupUuid, IReadOnlyDictionary<string, string> fields);

        VaultWrite UpdateEntry(string uuid, IReadOnlyDictionary<string, string> fields);

        VaultWrite DeleteEntry(string uuid);

        /// <summary>Adds a string that is not there, and refuses one that is.</summary>
        /// <param name="name">Not empty, for the reason <see cref="CreateGroup"/> gives.</param>
        VaultWrite CreateField(string entryUuid, string name, string value, bool isProtected);

        /// <summary>Changes a string that is there, and refuses one that is not.</summary>
        VaultWrite UpdateField(string entryUuid, string name, string value);

        VaultWrite DeleteField(string entryUuid, string name);

        /// <param name="name">Not empty, for the reason <see cref="CreateGroup"/> gives.</param>
        VaultWrite CreateAttachment(string entryUuid, string name, byte[] content);

        VaultWrite UpdateAttachment(string entryUuid, string name, byte[] content);

        VaultWrite DeleteAttachment(string entryUuid, string name);
    }
}
