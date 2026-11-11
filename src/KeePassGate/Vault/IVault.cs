using System;
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
    /// changes, so a client told that something was written can believe it. An update naming only
    /// what the node already holds is marshaled and checked like any write and then saves nothing,
    /// since it writes nothing: `openapi.yaml` §504 says why.
    ///
    /// **`root` is a group identifier.** Every method taking a group takes it, so a client holding
    /// `create` and nothing else can put an entry somewhere without first being granted `list` to
    /// find out where. No verb implies another, and an identifier can as easily have arrived from
    /// an operator as from a listing.
    ///
    /// No read here filters by permission. The rules are asked by the router, about the node each
    /// of these carries, and a vault that quietly returned less than it was asked for would make
    /// the audit a record of the wrong question.
    ///
    /// **A change asks the rules itself, through the <see cref="VaultAsking"/> it is handed.**
    /// The router's answer was given before the change waited for its turn, about a database the
    /// operator went on editing, so it can refuse a change and cannot permit one: the change
    /// finds its node inside its turn and asks about the node as it stands there. A vault knows
    /// nothing of rules or callers by that; it asks what it was handed and acts on the answer.
    ///
    /// **A listing of something that is not there is null, never empty.** An empty list is an
    /// answer about a group or an entry, that it holds nothing, and a group that had gone by the
    /// time it was listed, or a database that had locked, used to come back as one: a client was
    /// told `200` and an empty group. Null is what every single lookup here already answers for
    /// a node that is not there, and <see cref="IsOpen"/> is what tells a node that has gone from
    /// a database that has, which `openapi.yaml` §503 and §502 answer differently.
    /// </remarks>
    public interface IVault
    {
        /// <summary>The alias every group identifier accepts, resolving to the root group.</summary>
        string RootAlias { get; }

        /// <summary>
        /// Whether the database is still open, with a tree to read.
        /// </summary>
        /// <remarks>
        /// Asked once a lookup has found nothing, to say why. A lock or a close leaves every lookup
        /// finding nothing, and a node in a database that has locked has not gone: the request is
        /// answered as one meeting the locked database before routing is. Asked after the lookup
        /// rather than before it, so that a lock landing between the two is answered as the lock it
        /// is by then.
        /// </remarks>
        bool IsOpen { get; }

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

        /// <summary>
        /// What <paramref name="read"/> returned, with the database held still for the whole of it.
        /// </summary>
        /// <remarks>
        /// **A request that changes nothing is one of these**, taken by the router around the
        /// decision and the handler both, so the client's record, the rules and everything read
        /// are of one database. Each read inside it is part of the one hold rather than a hold of
        /// its own, so a listing is judged against the tree it was taken from, and a thousand
        /// entries wait for one turn rather than a thousand. Throws `VaultUnsettled` as they do.
        /// **Nothing that waits belongs inside it**, the audit and the writer least of all, since
        /// the operator's window waits with it.
        /// </remarks>
        T Held<T>(Func<T> read);

        /// <summary>A group by identifier or by the root alias, or null when there is none.</summary>
        VaultGroup Group(string uuid);

        /// <summary>
        /// A group's tag, by identifier or by the root alias, or null when there is none.
        /// </summary>
        /// <remarks>
        /// **Asked on its own, and only by a route that will use it.** A tag is a digest of
        /// everything the node holds, so a lookup that only wants to know which node a request is
        /// about, which is what the router asks for every request, never takes one.
        ///
        /// **Of the moment its content is of**, on every route that serves both. A request that
        /// changes nothing holds KeePass's thread from its decision to its answer, so no change
        /// lands between a tag and what it is served with, whichever is taken first. Each used to
        /// be read under a hold of its own, with room for a change between them, and a newer tag
        /// beside older content is what a write would have carried straight over the change the
        /// client never saw.
        /// </remarks>
        string GroupTag(string uuid);

        /// <summary>Every group under this one, one level down, or null when it is not there.</summary>
        IReadOnlyList<VaultGroup> Subgroups(string uuid);

        /// <summary>Every group in the database, root included and the recycle bin aside, depth
        /// first in KeePass's tree order, or null when the database has no tree to read.</summary>
        IReadOnlyList<VaultGroup> Groups();

        /// <summary>An entry by identifier, or null when there is none.</summary>
        VaultEntry Entry(string uuid);

        /// <summary>
        /// An entry's tag, which is its fields' and its attachments' too, or null when there is
        /// none. Asked as <see cref="GroupTag"/> is, and of the same moment as its content.
        /// </summary>
        string EntryTag(string uuid);

        /// <summary>
        /// An entry and everything it holds, with its strings when their names and values
        /// together come to no more than <paramref name="most"/> characters, or null when there is
        /// none.
        /// </summary>
        /// <remarks>
        /// **Measured before anything is decrypted, in the same read**, as
        /// <see cref="Attachment"/> is: KeePass keeps a protected string's length beside its
        /// encrypted bytes, so an entry over the ceiling comes back with its size and no strings,
        /// and a caller refusing it never pays for the plaintext.
        /// </remarks>
        VaultEntryDetail Read(string uuid, int most);

        /// <summary>The entries in a group, one level down, or null when it is not there.</summary>
        IReadOnlyList<VaultEntry> Entries(string groupUuid);

        /// <summary>Every entry in the database, the recycle bin aside, in tree order, or null when
        /// the database has no tree to read.</summary>
        IReadOnlyList<VaultEntry> Entries();

        /// <summary>The names of an entry's strings, never their values, or null when it is not
        /// there.</summary>
        IReadOnlyList<VaultField> Fields(string entryUuid);

        /// <summary>
        /// A string's value when it comes, with its name, to no more than
        /// <paramref name="most"/> characters, its size either way, or null when the entry or the
        /// name is not there.
        /// </summary>
        /// <remarks>Measured before anything is decrypted, as <see cref="Read"/> is.</remarks>
        VaultValue Field(string entryUuid, string name, int most);

        /// <summary>The names and sizes of an entry's attachments, never their contents, or null
        /// when it is not there.</summary>
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

        /// <summary>Puts a new group inside another, answering with its identifier.</summary>
        /// <param name="name">
        /// Not empty. The routing table's lengths keep an empty one from every client, and a vault
        /// answers one as a failure on this side rather than make a group nobody can name.
        /// </param>
        /// <param name="asking">
        /// As <see cref="UpdateGroup"/> takes it, about the group the new group goes into. A
        /// group or an entry created deals in no tag.
        /// </param>
        VaultWrite CreateGroup(string parentUuid, string name, string notes, VaultAsking asking);

        /// <summary>Renames a group or changes its notes, leaving whichever is null as it is.</summary>
        /// <param name="asking">
        /// Whether the caller may make the change, asked of the node the change finds, inside its
        /// turn and before anything else about that node is answered: a change refused there is
        /// <see cref="VaultWrite.Refused"/>, and one handed no asking is refused as well. Every
        /// change asks about the node its target answers for, a field's and an attachment's
        /// being their entry, and a create's being the group it goes into.
        ///
        /// It answers besides whether the change is made only while the node still holds what
        /// `If-Match` named, and whether its answer carries the tag it leaves. The precondition
        /// is asked under the writer's lock, against the node as the change finds it, and after
        /// every refusal that does not depend on it, as RFC 9110 section 13.2.1 orders them, so
        /// a node that is gone is still `404`, one that is taken or not empty still `409`, and
        /// one under an open dialog still `503 busy`. Answered
        /// <see cref="VaultWrite.PreconditionFailed"/> when it does not hold, and an update
        /// naming nothing to change is asked it as well.
        /// </param>
        VaultWrite UpdateGroup(string uuid, string name, string notes, VaultAsking asking);

        /// <summary>Removes a group, which is refused while it still holds anything.</summary>
        /// <remarks>
        /// A recursive delete would let one grant on a parent destroy entries that were never
        /// individually decided or recorded, which is the opposite of what a per-node rule is for.
        /// A removal answers no tag whatever <paramref name="asking"/> asks, since it leaves
        /// nothing to tag.
        /// </remarks>
        VaultWrite DeleteGroup(string uuid, VaultAsking asking);

        /// <summary>
        /// Puts a new entry in a group, holding the five standard fields whether or not they are
        /// named.
        /// </summary>
        /// <param name="expiry">
        /// When the new entry expires, or null, as <see cref="VaultExpiry.Never"/>, for an entry
        /// that does not expire.
        /// </param>
        /// <param name="asking">As <see cref="CreateGroup"/> takes it, about the group.</param>
        VaultWrite CreateEntry(
            string groupUuid,
            IReadOnlyDictionary<string, string> fields,
            VaultAsking asking,
            VaultExpiry expiry = null);

        /// <summary>
        /// Sets the strings named on an entry, and one it already holds keeps the protection it had.
        /// </summary>
        /// <param name="expiry">
        /// When the entry expires from now on, or null to leave its expiry as it is.
        /// <see cref="VaultExpiry.Auto"/> is a create's, and a vault given it here answers a failure
        /// on this side, as it does an empty name for a new field.
        /// </param>
        /// <param name="asking">As <see cref="UpdateGroup"/> takes it, about the entry.</param>
        VaultWrite UpdateEntry(
            string uuid,
            IReadOnlyDictionary<string, string> fields,
            VaultAsking asking,
            VaultExpiry expiry = null);

        /// <summary>Removes an entry, to the recycle bin when the database keeps one.</summary>
        /// <param name="asking">As <see cref="DeleteGroup"/> takes it, about the entry.</param>
        VaultWrite DeleteEntry(string uuid, VaultAsking asking);

        /// <summary>Adds a string that is not there, and refuses one that is.</summary>
        /// <param name="name">Not empty, for the reason <see cref="CreateGroup"/> gives.</param>
        /// <param name="isProtected">
        /// Whether the request marks the string protected. A name the database protects is stored
        /// protected whichever way this is, and that is settled inside the change, so the rules
        /// are asked about what is stored and never about what a caller read beforehand.
        /// </param>
        /// <param name="asking">
        /// As <see cref="UpdateGroup"/> takes it, about the entry, which answers for the string. A
        /// string's removal answers with the entry's tag too, since the entry is still there.
        /// </param>
        VaultWrite CreateField(
            string entryUuid, string name, string value, bool isProtected, VaultAsking asking);

        /// <summary>Changes a string that is there, and refuses one that is not.</summary>
        VaultWrite UpdateField(string entryUuid, string name, string value, VaultAsking asking);

        VaultWrite DeleteField(string entryUuid, string name, VaultAsking asking);

        /// <summary>Adds an attachment that is not there, and refuses one that is.</summary>
        /// <param name="name">Not empty, for the reason <see cref="CreateGroup"/> gives.</param>
        VaultWrite CreateAttachment(string entryUuid, string name, byte[] content, VaultAsking asking);

        VaultWrite UpdateAttachment(string entryUuid, string name, byte[] content, VaultAsking asking);

        VaultWrite DeleteAttachment(string entryUuid, string name, VaultAsking asking);
    }
}
