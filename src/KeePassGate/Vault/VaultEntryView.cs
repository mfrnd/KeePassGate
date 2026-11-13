using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Vault
{
    /// <summary>
    /// How much of an entry's attachments a caller has in sight.
    /// </summary>
    public enum VaultAttachmentSight
    {
        /// <summary>Nothing about them, not even whether the entry holds any.</summary>
        None,

        /// <summary>
        /// Their names and sizes and no more, which is what `list` and `attachments` in one
        /// rule open.
        /// </summary>
        Listed,

        /// <summary>
        /// Their names, sizes and bytes, which is what `read` and `attachments` in one rule
        /// open.
        /// </summary>
        Read,
    }

    /// <summary>
    /// How far into an entry a caller may read.
    /// </summary>
    /// <remarks>
    /// What the caller's rules open on the entry a view is taken of, and nothing about the route
    /// that asked: `read`, the `protected` switch in a rule with `read` or, for the strings a
    /// listing of entries shows, in a rule with `list`, and the `attachments` switch in a rule
    /// with `read` or, for names and sizes, in a rule with `list`. **Not
    /// depending on the route is what gives a caller one tag for an entry**, whichever route
    /// hands it out and whichever change answers with it.
    ///
    /// **A caller's view of an entry holds everything any route shows that caller of the
    /// entry.** A listing of entries shows a protected title or URL to a caller holding
    /// `protected` beside `list` or beside `read` in one rule, so a caller holding either
    /// beside `read` has both strings in its view: a read serves them and its tag covers them.
    /// Held otherwise, the caller was shown a title by a listing and refused the same title by
    /// a read, under a tag that did not move when the title did.
    ///
    /// Decided above the vault, which knows nothing of rules, and handed to it as the answer to
    /// a question the vault asks once it has found the entry: <see cref="IVault.View"/> for a
    /// read, <see cref="VaultAsking.Tagging"/> for a change.
    /// </remarks>
    public sealed class VaultSight
    {
        /// <summary>
        /// What no rule opens, which is what a question nobody answered comes to: no tag, no
        /// value of any string and nothing about attachments.
        /// </summary>
        public static readonly VaultSight Least = new VaultSight(false, false, VaultAttachmentSight.None);

        /// <summary>A sight, held to what `read` can open.</summary>
        /// <remarks>
        /// Protected values and attachments' bytes are read under `read` and under nothing else,
        /// so a sight that does not read is built reaching neither, whatever it was handed: no
        /// mistake above the vault makes a sight that withholds the tag and hands out the
        /// values. A view taken with such a sight holds no value of any string, protected or
        /// not, <see cref="VaultViewField.IsWithheld"/>: names and flags are what `list`
        /// opens, a value is `read`'s, and a view holds no length of a value it withholds. The
        /// listing's reach into protected strings is dropped the same way: it brings a value
        /// into a view, and a view that does not read holds none.
        /// </remarks>
        /// <param name="reads">Whether the caller holds `read` on the entry.</param>
        /// <param name="reachesProtected">
        /// Whether `read` and `protected` stand in one rule the caller holds.
        /// </param>
        /// <param name="attachments">How much of the attachments the caller's rules open.</param>
        /// <param name="listedInSight">
        /// Whether a listing of entries shows this caller a protected title or URL of the
        /// entry, which is `protected` beside `list` or beside `read` in one rule it holds: the
        /// answer of the condition the listings themselves ask.
        /// </param>
        public VaultSight(
            bool reads, bool reachesProtected, VaultAttachmentSight attachments, bool listedInSight = false)
        {
            Reads = reads;
            Protected = reads && reachesProtected;
            ListedInSight = reads && listedInSight;
            Attachments = !reads && attachments == VaultAttachmentSight.Read
                ? VaultAttachmentSight.None
                : attachments;
        }

        /// <summary>
        /// Whether the caller holds `read` on the entry, which is what a tag is handed out on.
        /// </summary>
        public bool Reads { get; private set; }

        /// <summary>Whether the values of protected strings are in sight.</summary>
        public bool Protected { get; private set; }

        /// <summary>
        /// Whether the protected strings a listing of entries shows are in sight, whatever
        /// else is: true only for a sight that reads, and true wherever
        /// <see cref="Protected"/> is, since a listing shows them on `read` with `protected`
        /// as well as on `list` with `protected`.
        /// </summary>
        public bool ListedInSight { get; private set; }

        /// <summary>How much of the entry's attachments is in sight.</summary>
        public VaultAttachmentSight Attachments { get; private set; }
    }

    /// <summary>
    /// One of an entry's strings as a view holds it: named, described, and quoted only when it is
    /// in sight.
    /// </summary>
    /// <remarks>
    /// **The value is read the first time it is asked for, and measured the first time its size
    /// is**, so no value is handed anywhere until something asks for it, and a view built for
    /// an answer refused before either is asked reads and measures nothing. The view's tag asks
    /// for every value in sight, so an answer that hands the tag out reads them all, whichever
    /// string the route was about.
    ///
    /// **Measuring a value in sight can decrypt it, inside KeePass's own type.** KeePass counts
    /// the characters of a protected string it holds as bytes, which is how a file's protected
    /// values are held once loaded, by decrypting them, the first time the count is asked, and
    /// keeps the count: `FINDINGS.md`, under "What a version of a node can be made of". The
    /// plaintext stays inside `ProtectedString`, and what comes out is a number. So a refusal
    /// for size reads no value through the view, and is not always free of a decryption.
    ///
    /// **A withheld string holds nothing its value could be read or measured with.** The
    /// constructor drops the reader and what measures the value for a string out of sight, so a
    /// withheld value is neither read nor measured, and no later mistake above the vault can
    /// reach the value, or its size, through the view: there is nothing behind
    /// <see cref="Value"/> to reach, and <see cref="Characters"/> is the name alone. The length
    /// was kept, for a ceiling measured over the whole entry, and a refusal quoting that count
    /// told a caller how long the values it was withheld were:
    /// <see cref="VaultEntryView.MostCharacters"/> has the reversal.
    ///
    /// **Out of sight is every string for a sight that does not read, and a protected string
    /// for a sight that reads without reaching protected values.** A caller that only lists is
    /// served names and flags, so its view holds those, and no value.
    ///
    /// **A protected string a listing of entries shows is in sight on the listing's own reach
    /// as well**, for a sight that reads: a caller shown a protected title by a listing is
    /// shown it by a read, <see cref="VaultSight"/>.
    /// </remarks>
    public sealed class VaultViewField
    {
        private Func<int> _measure;
        private int _length;
        private Func<string> _read;
        private readonly Func<byte[]> _stored;
        private string _value;

        /// <summary>A string of an entry, as far as <paramref name="sight"/> reaches it.</summary>
        /// <param name="isProtected">
        /// Whether it is kept protected, by the string's own flag or by the database's setting
        /// for a field of that name.
        /// </param>
        /// <param name="measure">
        /// What answers the value's length in characters, kept only for a string in sight and
        /// asked the first time the string is measured.
        /// </param>
        /// <param name="sight">
        /// What the caller may read, where null is <see cref="VaultSight.Least"/>.
        /// </param>
        /// <param name="read">What reads the value, kept only for a string in sight.</param>
        /// <param name="isListed">
        /// Whether a listing of entries shows this string, as the vault decides it: an entry's
        /// title and its URL.
        /// </param>
        /// <param name="stored">
        /// What copies out the UTF-8 form of a value the store keeps protected in memory, kept
        /// only for a string in sight, or null for a value the store keeps in the open.
        /// </param>
        public VaultViewField(
            string name,
            bool isProtected,
            Func<int> measure,
            VaultSight sight,
            Func<string> read,
            bool isListed = false,
            Func<byte[]> stored = null)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Name = name;
            IsProtected = isProtected;
            IsListed = isListed;
            IsWithheld = !InSight(sight, isProtected, isListed);

            if (!IsWithheld)
            {
                if (measure == null)
                {
                    throw new ArgumentNullException(nameof(measure));
                }

                if (read == null)
                {
                    throw new ArgumentNullException(nameof(read));
                }

                _measure = measure;
                _read = read;
                _stored = stored;
            }
        }

        /// <summary>
        /// Whether a sight reaches a string's value. No sight, and a sight that does not read,
        /// reach none.
        /// </summary>
        private static bool InSight(VaultSight sight, bool isProtected, bool isListed)
        {
            if (sight == null || !sight.Reads)
            {
                return false;
            }

            if (!isProtected || sight.Protected)
            {
                return true;
            }

            return isListed && sight.ListedInSight;
        }

        public string Name { get; private set; }

        /// <summary>
        /// Whether it is kept protected in memory, as a password is, either because the string
        /// carries the flag or because the database protects a field of that name.
        /// </summary>
        public bool IsProtected { get; private set; }

        /// <summary>
        /// Whether a listing of entries shows this string: an entry's title and its URL, as the
        /// vault finds them.
        /// </summary>
        public bool IsListed { get; private set; }

        /// <summary>
        /// Whether the value is out of the caller's sight, and so not in this view.
        /// </summary>
        public bool IsWithheld { get; private set; }

        /// <summary>
        /// What of this string the caller is handed, in characters, with no value handed out to
        /// count it: its name and its value together for a string in sight, and its name alone
        /// for a withheld string, whose name is served beside a `null` and whose value is never
        /// measured.
        /// </summary>
        /// <remarks>
        /// The value is measured the first time this is asked, and the length kept for as long
        /// as the view is. A measurement that fails is tried again by the next asking.
        /// </remarks>
        public int Characters
        {
            get
            {
                if (_measure != null)
                {
                    _length = _measure();
                    _measure = null;
                }

                return Name.Length + _length;
            }
        }

        /// <summary>
        /// The value as stored, never compiled, or null when it is withheld.
        /// </summary>
        /// <remarks>
        /// A read that fails is tried again by the next asking, and a read that succeeds is kept
        /// for as long as the view is.
        /// </remarks>
        public string Value
        {
            get
            {
                if (_read != null)
                {
                    _value = _read() ?? string.Empty;
                    _read = null;
                }

                return _value;
            }
        }

        /// <summary>
        /// The UTF-8 form of a value the store keeps protected in memory, as a copy of its own
        /// for the caller to overwrite when it is done, or null for a withheld value and for a
        /// value the store keeps in the open.
        /// </summary>
        /// <remarks>
        /// For the tag, which digests every value in sight and serves none of them: digested
        /// from this, a value nobody asked to read is never made into a string, which could not
        /// be overwritten, and the store goes on protecting it. Where this is null the value is
        /// already a string in the store's memory, and <see cref="Value"/> hands that out at
        /// no cost. The bytes are the store's, so they can hold what is not UTF-8, which
        /// <see cref="Value"/> serves with each such sequence replaced.
        /// </remarks>
        public byte[] Stored()
        {
            return _stored == null ? null : _stored();
        }
    }

    /// <summary>
    /// One of an entry's attachments as a view holds it: named and sized, with its bytes and
    /// their digest read when they are asked for, where they are in sight.
    /// </summary>
    /// <remarks>
    /// The size is known without decrypting anything, because a client has to decide whether to
    /// ask for an attachment before it asks, and an attachment can be megabytes.
    ///
    /// **An attachment that is listed and not read holds nothing its bytes could be read with**,
    /// as a withheld string holds no reader. The constructor drops what reads the bytes and what
    /// digests them unless the sight reads attachments, so a view taken for a caller that may
    /// list them serves names and sizes and has nothing else behind them.
    /// </remarks>
    public sealed class VaultViewAttachment
    {
        /// <summary>
        /// The largest attachment the gate hands out, which is also the largest it takes and the
        /// largest whose bytes a node's tag is taken over.
        /// </summary>
        /// <remarks>
        /// Here rather than with the routes, so that the view, which a tag is taken of, and the
        /// routes, which serve an attachment, read the same number.
        /// `VaultHandlers.MaxAttachmentBytes` gives the reason for this number.
        /// </remarks>
        public const int MostServed = 512 * 1024;

        private readonly Func<byte[]> _read;
        private readonly Func<byte[]> _digest;
        private byte[] _content;

        /// <summary>An attachment of an entry, as far as <paramref name="sight"/> reaches it.</summary>
        /// <param name="size">Its length in bytes, known without decrypting it.</param>
        /// <param name="sight">
        /// What the caller may read, where null is <see cref="VaultSight.Least"/>.
        /// </param>
        /// <param name="read">What reads its bytes, kept only where they are in sight.</param>
        /// <param name="digest">
        /// What takes the SHA-256 of its bytes, kept only where they are in sight.
        /// </param>
        public VaultViewAttachment(
            string name, int size, VaultSight sight, Func<byte[]> read, Func<byte[]> digest)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Name = name;
            Size = size;
            IsWithheld = sight == null || sight.Attachments != VaultAttachmentSight.Read;

            if (!IsWithheld)
            {
                if (read == null)
                {
                    throw new ArgumentNullException(nameof(read));
                }

                if (digest == null)
                {
                    throw new ArgumentNullException(nameof(digest));
                }

                _read = read;
                _digest = digest;
            }
        }

        public string Name { get; private set; }

        public int Size { get; private set; }

        /// <summary>
        /// Whether its bytes are out of the caller's sight, and so not in this view: an
        /// attachment listed for a caller that may not read attachments.
        /// </summary>
        public bool IsWithheld { get; private set; }

        /// <summary>
        /// What it holds, read the first time it is asked for, or null for an attachment that is
        /// withheld and for an attachment larger than <see cref="MostServed"/>, which is measured
        /// and never decrypted.
        /// </summary>
        public byte[] Content
        {
            get
            {
                if (IsWithheld || Size > MostServed)
                {
                    return null;
                }

                return _content ?? (_content = _read());
            }
        }

        /// <summary>
        /// The SHA-256 of what it holds, or null for an attachment that is withheld and for an
        /// attachment larger than <see cref="MostServed"/>, whose bytes no route serves and no
        /// tag covers.
        /// </summary>
        public byte[] Digest
        {
            get { return IsWithheld || Size > MostServed ? null : _digest(); }
        }
    }

    /// <summary>
    /// The whole of an entry as one caller may read it, which every answer about the entry and
    /// the entry's tag are both made from.
    /// </summary>
    /// <remarks>
    /// **One projection, so the answer and the tag cannot drift.** A tag is a digest of what a
    /// caller is served, and a tag taken by a walk of its own over the entry could come to cover
    /// something the answers leave out, or leave out something they serve. So the vault builds
    /// this once per request, filtered by the caller's <see cref="VaultSight"/>, the routes
    /// render from it, and <see cref="Tag"/> is taken of it and of nothing else.
    ///
    /// **What is out of sight is not here.** A protected string out of sight keeps its name and
    /// its place and holds no value, every string does for a sight that does not read,
    /// attachments out of sight are no list at all, and attachments that are listed and not read
    /// keep their names and sizes and hold no bytes, so nothing rendered from a view can hand
    /// out what the sight left behind. The description beside them, <see cref="Entry"/>, holds
    /// no title and no URL for the same reason: each is in the view as a field or not at all.
    ///
    /// **What is in sight is what the caller's rules open, which can be more than a route lists
    /// to it.** A caller holding `read` with `attachments` and no `list` is served no listing
    /// of the attachments, and reads any of them by its name, so they are all in its view and in
    /// its tag, `API.md` §410.
    ///
    /// **A view whose sight does not read has no tag.** A tag is handed to every caller holding
    /// `read` on the entry and to nobody else, `API.md` §410, and the view is where that is
    /// held: <see cref="Tag"/> is null for a sight that does not read, so no route and no change
    /// rendering from the view can hand a tag to a caller that may not have a tag.
    ///
    /// **Nothing is read or measured to build it, and no value is handed anywhere until it is
    /// asked for.** Names and flags are kept in the open and an attachment's size beside its
    /// encrypted bytes, so building a view asks KeePass for no value and no length. Values,
    /// their lengths, attachments' bytes, their digests and the tag are each taken when first
    /// asked for, and the tag asks for every value and digest in sight. So an entry is found,
    /// what its caller would be handed is measured against <see cref="MostCharacters"/>, and a
    /// read is refused with no value read. Measuring is not always free of a decryption:
    /// the first measurement of a protected value KeePass holds as bytes decrypts it once inside
    /// KeePass's own type, <see cref="VaultViewField"/>. A withheld value is neither read nor
    /// measured.
    ///
    /// **A view is of the moment it was taken, whenever it is asked.** The vault takes it with
    /// KeePass's thread held still and hands every field and attachment what reads the object
    /// the entry held then, an object KeePass replaces and does not change. So a read asks its
    /// view for lengths, values, bytes and the tag once that hold has ended, on its own thread,
    /// and is answered about the entry as it stood inside the hold. A view is one request's
    /// and is asked from one thread at a time.
    /// </remarks>
    public sealed class VaultEntryView
    {
        /// <summary>
        /// The most characters a caller's view of an entry may come to and still be read whole:
        /// <see cref="Characters"/>, which is what that caller would be handed.
        /// </summary>
        /// <remarks>
        /// **A ceiling on reads alone, measured over the caller's own view.** It was a ceiling
        /// on the whole entry, protected values included, held on writes as well, so that what
        /// the gate let a client write it also handed back whole. A write past it was refused
        /// with the entry's count, and a caller that may write and not read padded an entry up
        /// to the ceiling and read off the refusal how long the values it was withheld were.
        /// The operator's ruling of 2026-11-10 reverses it: no write is refused for what an
        /// entry's strings come to, and a read is held to what its caller is handed, never to a
        /// value it is withheld. `VaultHandlers.MaxEntryCharacters` gives the reason for the
        /// number and what the reversal gives up.
        ///
        /// Here rather than with the routes, beside the count it is compared with.
        /// </remarks>
        public const int MostCharacters = 512 * 1024;

        private readonly Func<VaultEntryView, string> _tagger;
        private string _tag;

        /// <summary>An entry as far as <paramref name="sight"/> reaches into it.</summary>
        /// <param name="entry">The entry as a single lookup describes it.</param>
        /// <param name="sight">
        /// What the caller may read, where null is <see cref="VaultSight.Least"/>.
        /// </param>
        /// <param name="fields">
        /// Every string, in the order KeePass keeps them, built with the same sight.
        /// </param>
        /// <param name="attachments">
        /// Every attachment, built with the same sight, dropped here when the sight reaches
        /// nothing about them.
        /// </param>
        /// <param name="tagger">
        /// What condenses a view into its tag, dropped here when the sight does not read.
        /// </param>
        public VaultEntryView(
            VaultEntry entry,
            VaultSight sight,
            IReadOnlyList<VaultViewField> fields,
            IReadOnlyList<VaultViewAttachment> attachments,
            Func<VaultEntryView, string> tagger)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            Entry = entry;
            Sight = sight ?? VaultSight.Least;
            Fields = fields;
            Attachments = Sight.Attachments == VaultAttachmentSight.None ? null : attachments;
            _tagger = Sight.Reads ? tagger : null;

            NameCharacters = fields.Sum(field => field.Name.Length);
            Protected = fields.Where(field => field.IsProtected).Select(field => field.Name).ToList();
        }

        public VaultEntry Entry { get; private set; }

        /// <summary>How far into the entry this view reaches, never null.</summary>
        public VaultSight Sight { get; private set; }

        /// <summary>
        /// Every string the entry holds, by KeePass's own names, in the order KeePass keeps them,
        /// which is the order `read` writes them in.
        /// </summary>
        public IReadOnlyList<VaultViewField> Fields { get; private set; }

        /// <summary>
        /// Every attachment the entry holds, or null when the caller's sight reaches nothing
        /// about attachments. Never empty for that: an empty list is an entry holding none.
        /// Each holds its bytes only where the sight reads attachments.
        /// </summary>
        public IReadOnlyList<VaultViewAttachment> Attachments { get; private set; }

        /// <summary>
        /// What a read of the whole entry would hand this view's caller, in characters, counted
        /// with no value handed out: the name of every string, since a withheld string is served
        /// by its name beside a `null`, and the value of every string in sight. Nothing of a
        /// withheld value is in it, its length included.
        /// </summary>
        /// <remarks>
        /// Asking it measures every value in sight that nothing has measured yet,
        /// <see cref="VaultViewField.Characters"/>, so a view nothing asks this of measures
        /// none.
        /// </remarks>
        public int Characters
        {
            get { return Fields.Sum(field => field.Characters); }
        }

        /// <summary>
        /// What the names of the entry's strings come to in characters, which is what a listing
        /// of the entry's fields hands out, whatever the sight.
        /// </summary>
        public int NameCharacters { get; private set; }

        /// <summary>
        /// Which of the strings are kept protected, by the string's own flag or by the database's
        /// setting for a field of that name.
        /// </summary>
        public IReadOnlyList<string> Protected { get; private set; }

        /// <summary>
        /// The tag of this view, taken the first time it is asked for, so a request that hands
        /// out no tag digests nothing, or null for a view whose sight does not read.
        /// </summary>
        /// <remarks>
        /// **Of what this view holds and of nothing else**, so two callers that see different
        /// things of an entry hold different tags for it, and a tag moves with nothing its
        /// holder may not read.
        ///
        /// A tag that cannot be taken throws, and the next asking takes it again.
        /// </remarks>
        public string Tag
        {
            get
            {
                if (_tag == null && _tagger != null)
                {
                    _tag = _tagger(this);
                }

                return _tag;
            }
        }

        /// <summary>
        /// The string carrying this name byte for byte, or null when the entry holds none.
        /// </summary>
        /// <remarks>
        /// Byte for byte, and not as the store compares names, for the reason the vault resolves
        /// a write's name that way: a name the store cannot tell from a stored name is not that
        /// string.
        /// </remarks>
        public VaultViewField Field(string name)
        {
            foreach (var field in Fields)
            {
                if (string.Equals(field.Name, name, StringComparison.Ordinal))
                {
                    return field;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether this view holds the value of every one of these strings that the entry
        /// carries, which is whether its caller may read every value a change naming them
        /// names. A name the entry does not carry is no value to read: writing it is a change
        /// whoever writes it. No names is the entry's own facts, its expiry among them, which
        /// a sight that reads holds.
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
        /// nothing in it to guess, so it is skipped for everybody. A group has no view, and its
        /// name and notes are read by a sight that reads.
        ///
        /// **Read off the view, and not worked out again from the sight**, so that what a
        /// caller is taken to read here is what a read serves it and what its tag covers, by
        /// construction: a protected title a listing shows this caller is a value it reads,
        /// and a rule that decided that anywhere else could come to disagree.
        /// </remarks>
        public bool Reads(IEnumerable<string> names)
        {
            if (!Sight.Reads)
            {
                return false;
            }

            return (names ?? Array.Empty<string>())
                .Select(Field)
                .All(field => field == null || !field.IsWithheld);
        }

        /// <summary>
        /// Whether this view holds the bytes of the attachment carrying this name, which is
        /// whether its caller may read what a change replacing them names:
        /// <see cref="Reads"/> has the reason it is asked.
        /// </summary>
        public bool ReadsAttachment(string name)
        {
            var attachment = Attachment(name);
            return attachment != null && !attachment.IsWithheld;
        }

        /// <summary>
        /// The attachment carrying this name byte for byte, or null when the entry holds none or
        /// nothing about attachments is in sight.
        /// </summary>
        public VaultViewAttachment Attachment(string name)
        {
            if (Attachments == null)
            {
                return null;
            }

            foreach (var attachment in Attachments)
            {
                if (string.Equals(attachment.Name, name, StringComparison.Ordinal))
                {
                    return attachment;
                }
            }

            return null;
        }
    }
}
