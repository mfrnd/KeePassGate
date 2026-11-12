using System;
using System.Collections.Generic;

namespace KeePassGate.Vault
{
    /// <summary>
    /// How far into an entry a caller may read, beside the strings nothing protects.
    /// </summary>
    /// <remarks>
    /// The two switches a rule holds, answered for `read` about the entry a view is taken of.
    /// Decided above the vault, which knows nothing of rules, and handed to it as the answer to
    /// a question the vault asks once it has found the entry: <see cref="IVault.View"/>.
    /// </remarks>
    public sealed class VaultSight
    {
        /// <summary>
        /// Every string and every attachment: the sight a tag handed out is taken with.
        /// </summary>
        public static readonly VaultSight Whole = new VaultSight(true, true);

        /// <summary>
        /// The strings nothing protects and no more, which is what a question nobody answered
        /// comes to.
        /// </summary>
        public static readonly VaultSight Least = new VaultSight(false, false);

        public VaultSight(bool reachesProtected, bool reachesAttachments)
        {
            Protected = reachesProtected;
            Attachments = reachesAttachments;
        }

        /// <summary>Whether the values of protected strings are in sight.</summary>
        public bool Protected { get; private set; }

        /// <summary>Whether the entry's attachments are in sight.</summary>
        public bool Attachments { get; private set; }
    }

    /// <summary>
    /// One of an entry's strings as a view holds it: named, described, and quoted only when it is
    /// in sight.
    /// </summary>
    /// <remarks>
    /// **The value is read the first time it is asked for**, so a view taken to answer for one
    /// string, or to measure an entry, decrypts nothing else.
    ///
    /// **A withheld string holds nothing its value could be read with.** The constructor drops
    /// the reader for a protected string out of sight, so no later mistake above the vault can
    /// reach the value through the view: there is nothing behind <see cref="Value"/> to reach.
    /// </remarks>
    public sealed class VaultViewField
    {
        private readonly int _length;
        private Func<string> _read;
        private string _value;

        /// <summary>A string of an entry, as far as <paramref name="sight"/> reaches it.</summary>
        /// <param name="isProtected">
        /// Whether it is kept protected, by the string's own flag or by the database's setting
        /// for a field of that name.
        /// </param>
        /// <param name="length">
        /// The value's length in characters, known without decrypting it.
        /// </param>
        /// <param name="sight">
        /// What the caller may read, where null is <see cref="VaultSight.Least"/>.
        /// </param>
        /// <param name="read">What reads the value, kept only for a string in sight.</param>
        public VaultViewField(string name, bool isProtected, int length, VaultSight sight, Func<string> read)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Name = name;
            IsProtected = isProtected;
            _length = length;
            IsWithheld = isProtected && (sight == null || !sight.Protected);

            if (!IsWithheld)
            {
                if (read == null)
                {
                    throw new ArgumentNullException(nameof(read));
                }

                _read = read;
            }
        }

        public string Name { get; private set; }

        /// <summary>
        /// Whether it is kept protected in memory, as a password is, either because the string
        /// carries the flag or because the database protects a field of that name.
        /// </summary>
        public bool IsProtected { get; private set; }

        /// <summary>
        /// Whether the value is out of the caller's sight, and so not in this view.
        /// </summary>
        public bool IsWithheld { get; private set; }

        /// <summary>
        /// The string's name and value together, in characters, measured without decrypting
        /// anything, and the same whether or not the value is in sight.
        /// </summary>
        public int Characters
        {
            get { return Name.Length + _length; }
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
    }

    /// <summary>
    /// One of an entry's attachments as a view holds it: named and sized, with its bytes and
    /// their digest read when they are asked for.
    /// </summary>
    /// <remarks>
    /// The size is known without decrypting anything, because a client has to decide whether to
    /// ask for an attachment before it asks, and an attachment can be megabytes.
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

        /// <summary>An attachment of an entry whose attachments are in sight.</summary>
        /// <param name="size">Its length in bytes, known without decrypting it.</param>
        /// <param name="read">What reads its bytes.</param>
        /// <param name="digest">What takes the SHA-256 of its bytes.</param>
        public VaultViewAttachment(string name, int size, Func<byte[]> read, Func<byte[]> digest)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            if (digest == null)
            {
                throw new ArgumentNullException(nameof(digest));
            }

            Name = name;
            Size = size;
            _read = read;
            _digest = digest;
        }

        public string Name { get; private set; }

        public int Size { get; private set; }

        /// <summary>
        /// What it holds, read the first time it is asked for, or null for an attachment larger
        /// than <see cref="MostServed"/>, which is measured and never decrypted.
        /// </summary>
        public byte[] Content
        {
            get
            {
                if (Size > MostServed)
                {
                    return null;
                }

                return _content ?? (_content = _read());
            }
        }

        /// <summary>
        /// The SHA-256 of what it holds, or null for an attachment larger than
        /// <see cref="MostServed"/>, whose bytes no route serves and no tag covers.
        /// </summary>
        public byte[] Digest
        {
            get { return Size > MostServed ? null : _digest(); }
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
    /// its place and holds no value, and attachments out of sight are no list at all, so nothing
    /// rendered from a view can hand out what the sight left behind.
    ///
    /// **Nothing is decrypted to build it.** Names, flags and sizes are kept beside the encrypted
    /// values, so an entry is found, measured against <see cref="MostCharacters"/> and refused
    /// with every value still encrypted. Values, attachments' bytes, their digests and the tag are
    /// each read when first asked for.
    /// </remarks>
    public sealed class VaultEntryView
    {
        /// <summary>
        /// The most characters an entry's strings, names and values together, may come to and
        /// still be read whole or written to.
        /// </summary>
        /// <remarks>
        /// Here rather than with the routes, so that the vault, which measures an entry and refuses
        /// a write taking an entry past it, and the routes, which refuse to read an entry past it,
        /// read the same number. `VaultHandlers.MaxEntryCharacters` gives the reason for this
        /// number.
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
        /// Every attachment, dropped here when the sight does not reach them.
        /// </param>
        /// <param name="tagger">What condenses a view into its tag.</param>
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
            Fields = fields;
            Attachments = sight != null && sight.Attachments ? attachments : null;
            _tagger = tagger;

            var characters = 0;
            var guarded = new List<string>();
            foreach (var field in fields)
            {
                characters += field.Characters;

                if (field.IsProtected)
                {
                    guarded.Add(field.Name);
                }
            }

            Characters = characters;
            Protected = guarded;
        }

        public VaultEntry Entry { get; private set; }

        /// <summary>
        /// Every string the entry holds, by KeePass's own names, in the order KeePass keeps them,
        /// which is the order `read` writes them in.
        /// </summary>
        public IReadOnlyList<VaultViewField> Fields { get; private set; }

        /// <summary>
        /// Every attachment the entry holds, or null when the caller's sight does not reach
        /// attachments. Never empty for that: an empty list is an entry holding none.
        /// </summary>
        public IReadOnlyList<VaultViewAttachment> Attachments { get; private set; }

        /// <summary>
        /// What the entry's strings, names and values together, come to in characters, measured
        /// without decrypting anything, and counting every string whether or not it is in sight.
        /// </summary>
        public int Characters { get; private set; }

        /// <summary>
        /// Which of the strings are kept protected, by the string's own flag or by the database's
        /// setting for a field of that name.
        /// </summary>
        public IReadOnlyList<string> Protected { get; private set; }

        /// <summary>
        /// The tag of this view, taken the first time it is asked for, so a request that hands
        /// out no tag digests nothing.
        /// </summary>
        /// <remarks>
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
        /// The attachment carrying this name byte for byte, or null when the entry holds none or
        /// attachments are out of sight.
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
