using System;
using System.Collections.Generic;
using System.Globalization;
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
            bool titleProtected, bool urlProtected, DateTimeOffset? expires, bool? holdsProtected)
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
            HoldsProtected = holdsProtected;
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

        /// <summary>
        /// Whether any of the entry's strings is protected, said by a lookup of the one entry and
        /// null in a listing, which is not asked.
        /// </summary>
        /// <remarks>
        /// What decides whether a client's rule has to carry `protected` before it is handed the
        /// entry's tag. Nothing new is told by it: a client that may read the entry is told which
        /// of its fields are protected anyway.
        /// </remarks>
        public bool? HoldsProtected { get; private set; }
    }

    /// <summary>
    /// What a change found where it is about to be made: the node the rules are asked about, as
    /// it stands inside the change's own turn.
    /// </summary>
    /// <remarks>
    /// The entry for a change to an entry or to anything inside it, the group for a change to a
    /// group, and the group that would hold it for a group or an entry being created: the node
    /// the routing table has the router ask about, found again by the write itself.
    ///
    /// **A view asks with the same thing**, the entry it is about to be taken of, so that what
    /// a caller may read is answered about the entry as the read finds it:
    /// <see cref="IVault.View"/>.
    /// </remarks>
    public sealed class VaultFound
    {
        public VaultFound(VaultGroup group)
        {
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            Group = group;
        }

        public VaultFound(VaultEntry entry, bool guarded)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            Entry = entry;
            Guarded = guarded;
        }

        /// <summary>
        /// The same group, as the group a new entry would be made in.
        /// </summary>
        /// <param name="guarded">Whether a string the entry would be made with is protected.</param>
        public static VaultFound Holding(VaultGroup group, bool guarded)
        {
            return new VaultFound(group) { Guarded = guarded };
        }

        /// <summary>The group, or null when an entry was found.</summary>
        public VaultGroup Group { get; private set; }

        /// <summary>The entry, or null when a group was found.</summary>
        public VaultEntry Entry { get; private set; }

        /// <summary>
        /// Whether the change sets or takes away a string that is protected, by the database for
        /// what it is called or by the flag the string itself carries. False for a change that
        /// touches no string. For a view, whether any string of the entry is protected.
        /// </summary>
        public bool Guarded { get; private set; }

        /// <summary>The node the rules are asked about.</summary>
        public IAclNode Node
        {
            get { return Entry != null ? Entry.Node : Group.Node; }
        }
    }

    /// <summary>
    /// The questions a change asks about its caller once it has found its node, inside its own
    /// turn: whether the change may be made at all, and what it does about the node's tag.
    /// </summary>
    /// <remarks>
    /// **A change is decided where it is made.** A request waits for its turn on KeePass's thread,
    /// and whatever was decided before the wait was decided about a database the operator went on
    /// editing: a string marked protected, a rule taken away, an entry moved under a group that
    /// refuses. So the vault is handed the questions instead of their answers, and asks them of
    /// the node it found, in the turn the change is made in, where nothing else moves.
    ///
    /// **Asked before anything else is answered about the node.** A caller the rules refuse is
    /// told nothing of a name already taken, a dialog open on the entry or a tag that no longer
    /// matches, since each of those is a fact about a node it may not reach.
    ///
    /// **The tag is asked about twice**, of the node as the change found it, for the request's
    /// `If-Match`, and of the node as the change left it, for the tag in the answer: a change
    /// that makes a string protected leaves a node whose tag covers a value its own caller may no
    /// longer read. Only a caller whose rule may read everything the tag covers is handed a tag
    /// or has its `If-Match` asked, which is what keeps a tag from telling a client anything
    /// about a value its rule does not let it read, `API.md` §410.
    ///
    /// **And whether the caller may read what the change names**, which decides whether an
    /// update that would change nothing is skipped: <see cref="VaultTagging.Sees"/> has the
    /// argument.
    ///
    /// **A change handed no questions is refused**, as is a change whose questions are answered
    /// by nobody, so a caller that forgets the rules does not compile into a write that ignores
    /// them.
    /// </remarks>
    public sealed class VaultAsking
    {
        private readonly Func<VaultFound, bool> _allowed;
        private readonly Func<VaultFound, bool> _mayTag;
        private readonly Func<VaultFound, bool> _sees;
        private readonly VaultPrecondition _precondition;
        private readonly bool _answers;

        /// <summary>The questions one change asks, each of the node the change finds.</summary>
        /// <param name="allowed">Whether the caller may make this change to what was found.</param>
        /// <param name="mayTag">
        /// Whether the caller may be handed the tag of what was found and have its `If-Match`
        /// asked, or null for a change that deals in no tag.
        /// </param>
        /// <param name="sees">
        /// Whether the caller may read every value the change names on what was found, or null
        /// for a caller that may read none of them.
        /// </param>
        /// <param name="precondition">What `If-Match` asked of the node, or null.</param>
        /// <param name="answers">Whether the answer carries the tag the change leaves.</param>
        public VaultAsking(
            Func<VaultFound, bool> allowed,
            Func<VaultFound, bool> mayTag,
            Func<VaultFound, bool> sees,
            VaultPrecondition precondition,
            bool answers)
        {
            _allowed = allowed;
            _mayTag = mayTag;
            _sees = sees;
            _precondition = precondition;
            _answers = answers;
        }

        /// <summary>Whether the change may be made to what it found.</summary>
        public bool Allows(VaultFound found)
        {
            return _allowed != null && found != null && _allowed(found);
        }

        /// <summary>
        /// What the change does about the tag of the node as it stands, and whether its caller
        /// may read what it names there: asked once of the node the change found, and again of
        /// the node the change left.
        /// </summary>
        /// <remarks>
        /// **A caller that may not be handed the tag has its `If-Match` failed rather than
        /// ignored.** Ignoring it would make the write unconditional, which is the one thing a
        /// client sending a precondition did not ask for. Failed where the vault asks it, so
        /// that a node gone, taken or busy is still answered first, as RFC 9110 section 13.2.1
        /// orders it.
        /// </remarks>
        public VaultTagging Tagging(VaultFound found)
        {
            var mayTag = _mayTag != null && found != null && _mayTag(found);

            var precondition = _precondition;
            if (!mayTag && precondition != null)
            {
                precondition = VaultPrecondition.Withheld();
            }

            var sees = _sees != null && found != null && _sees(found);

            return new VaultTagging(precondition, mayTag && _answers, sees);
        }
    }

    /// <summary>
    /// What a change does about the node as the node stands at one moment, decided from who is
    /// asking: whether it is made only while the node still holds what `If-Match` named, whether
    /// it answers with the tag it leaves, and whether its caller may read the values it names.
    /// </summary>
    /// <remarks>
    /// Answered by <see cref="VaultAsking.Tagging"/> inside the change's turn, never handed in
    /// from outside it, so every part is decided from who is asking about the node as it is.
    /// A field or an attachment created answers with its entry's tag; a group or an entry
    /// created does not, since its request is sent to a collection and declares no `If-Match`,
    /// `API.md` §410.
    /// </remarks>
    public sealed class VaultTagging
    {
        public VaultTagging(VaultPrecondition precondition, bool answers, bool sees)
        {
            Precondition = precondition;
            Answers = answers;
            Sees = sees;
        }

        /// <summary>What `If-Match` asked of the node, or null when nothing is asked.</summary>
        public VaultPrecondition Precondition { get; private set; }

        /// <summary>Whether the answer carries the tag the change left.</summary>
        public bool Answers { get; private set; }

        /// <summary>
        /// Whether the caller may read every value the change names: a group's name and notes
        /// under `read`, an entry's strings and expiry under `read`, reaching protected values
        /// where a named string is protected, and an attachment's bytes under `read` reaching
        /// attachments.
        /// </summary>
        /// <remarks>
        /// **An update that would change nothing is skipped only for a caller this is true of**,
        /// the operator's ruling of 2026-11-10. A skipped update moves no time, takes no revision
        /// and saves nothing, and an update that writes does all three, so a caller that may
        /// write a value and not read it could send a guess and learn from the listing's
        /// `modified`, or from how long the answer took, whether the stored value was its guess.
        /// For such a caller the update is written like any change, so a guess that is right and
        /// a guess that is wrong do the same work. A caller that may read the value learns
        /// nothing from the skip that a read would not tell it, and a request naming nothing has
        /// nothing in it to guess, so it is skipped for everybody.
        /// </remarks>
        public bool Sees { get; private set; }
    }

    /// <summary>What happened to a change, and whether it is worth trying again.</summary>
    /// <remarks>
    /// Pairing's three answers with four more that a change to a named node can have and an
    /// admission record cannot: a create's identifier, a conflict, a node that is not there, and
    /// a node that no longer holds what `If-Match` said. `Busy` is told apart from
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

        /// <summary>
        /// The node did not hold what the request's `If-Match` said it should, so nothing was
        /// changed.
        /// </summary>
        /// <remarks>
        /// A refusal about the request, answered `412`, and decided under the writer's lock, on
        /// KeePass's thread, against the node as the change would have found it: a precondition
        /// checked on a connection thread before the write could be overtaken by a write landing in
        /// between, which is the lost update the precondition is there to prevent.
        /// </remarks>
        public bool IsPreconditionFailed { get; private set; }

        /// <summary>
        /// The change would take the entry's strings over what this gate keeps, so nothing was
        /// changed.
        /// </summary>
        /// <remarks>
        /// A refusal about the request, answered `413`, and decided under the writer's lock against
        /// the entry as the change would have found it, as <see cref="IsPreconditionFailed"/>
        /// is, so that two writes landing in turn cannot each be measured against what the entry
        /// held before the other. <see cref="VaultEntryView.MostCharacters"/> is the
        /// ceiling, and <see cref="Characters"/> what the entry would have come to.
        /// </remarks>
        public bool IsTooLarge { get; private set; }

        /// <summary>
        /// What the entry's strings, names and values together, would have come to in characters,
        /// when <see cref="IsTooLarge"/>.
        /// </summary>
        public int Characters { get; private set; }

        /// <summary>What was created, when something was.</summary>
        public string Uuid { get; private set; }

        /// <summary>
        /// The tag of the node the request was about as the change left it, when the write was
        /// asked for it, and null otherwise.
        /// </summary>
        /// <remarks>
        /// Asked for through <see cref="VaultTagging.Answers"/>, by a caller that may be handed the
        /// node's tag, and never given for a group or an entry removed, which leaves nothing to
        /// tag, or one created, whose request was sent to a collection. Taken where the change was
        /// made, before the save, so that it is the tag of exactly what was written.
        /// </remarks>
        public string Tag { get; private set; }

        /// <summary>
        /// Why, for the operator: `KdbxVault` writes it to the log when a change is refused. Never
        /// sent to a caller, who is told the kind of refusal and nothing more.
        /// </summary>
        public string Reason { get; private set; }

        public static VaultWrite Done(string tag = null)
        {
            return new VaultWrite(true, false, false, null, null) { Tag = tag };
        }

        public static VaultWrite Created(string uuid, string tag = null)
        {
            return new VaultWrite(true, false, false, uuid, null) { Tag = tag };
        }

        /// <summary>The node did not meet what `If-Match` asked, and nothing was changed.</summary>
        /// <param name="precondition">
        /// What was asked, whose <see cref="VaultPrecondition.Refusal"/> is the reason.
        /// </param>
        public static VaultWrite PreconditionFailed(VaultPrecondition precondition)
        {
            if (precondition == null)
            {
                throw new ArgumentNullException(nameof(precondition));
            }

            return new VaultWrite(false, false, false, null, precondition.Refusal)
            {
                IsPreconditionFailed = true,
            };
        }

        /// <summary>
        /// The entry would hold more than this gate keeps, and nothing was changed.
        /// </summary>
        public static VaultWrite TooLarge(int characters)
        {
            return new VaultWrite(
                false,
                false,
                false,
                null,
                "the entry would hold " + characters.ToString(CultureInfo.InvariantCulture)
                    + " characters of names and values")
            {
                IsTooLarge = true,
                Characters = characters,
            };
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

        /// <summary>
        /// The rules refuse the change, asked inside its own turn. Answered as a node that is not
        /// there is, since telling the two apart would let a client map a database by asking.
        /// </summary>
        public static VaultWrite Refused()
        {
            return new VaultWrite(false, false, false, null, "the rules refuse it");
        }
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
