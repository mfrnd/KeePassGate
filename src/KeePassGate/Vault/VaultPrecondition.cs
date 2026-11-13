using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Vault
{
    /// <summary>
    /// What an `If-Match` asks: that the node still holds what one of these tags says, or that it
    /// holds anything at all.
    /// </summary>
    /// <remarks>
    /// **Compared strongly, as RFC 9110 section 13.1.1 asks of `If-Match`.** A weak tag never
    /// matches, and this gate hands out none, so one in a request is a tag from somewhere else.
    ///
    /// **A field this cannot read matches nothing.** Section 13.1.1 leaves a malformed one to the
    /// server, and every way of ignoring it ends in the write going ahead, which is the one thing a
    /// client that sent a precondition did not ask for. So it fails the way a precondition that
    /// does not hold fails, `412`, and the client finds out by reading again, which is what it
    /// would have to do anyway. An empty field is one of those.
    ///
    /// **What arrives is already plain ASCII**, with the spaces and tabs around it gone: the
    /// request reader refuses a head holding anything else and trims every value it keeps. So the
    /// `obs-text` the grammar allows inside a tag never reaches this, and nothing here is written
    /// for it.
    /// </remarks>
    public sealed class VaultPrecondition
    {
        /// <summary>Why a node is refused under a precondition naming tags: it holds none of them.</summary>
        private const string Changed = "the node no longer holds what If-Match named";

        private readonly bool _any;
        private readonly IReadOnlyCollection<string> _tags;
        private readonly string _refusal;

        private VaultPrecondition(bool any, IReadOnlyCollection<string> tags, string refusal)
        {
            _any = any;
            _tags = tags;
            _refusal = refusal;
        }

        /// <summary>Why a node is refused under this precondition, for the operator's log.</summary>
        /// <remarks>
        /// **Three of the four reasons are about the request and not the node**: a field that will
        /// not read, a field naming no strong tag, and a caller the gate hands no tag, each met by
        /// no node whatever it holds. One sentence for all four told the operator that the node
        /// had changed, which sent somebody looking for an edit nobody made while a program was
        /// sending a condition nothing could meet.
        /// </remarks>
        public string Refusal
        {
            get { return _refusal; }
        }

        /// <summary>
        /// What a request's `If-Match` says, or null when it carried none, which asks for nothing.
        /// </summary>
        public static VaultPrecondition Read(string field)
        {
            if (field == null)
            {
                return null;
            }

            if (string.Equals(field, "*", StringComparison.Ordinal))
            {
                return new VaultPrecondition(true, Array.Empty<string>(), Changed);
            }

            var tags = new List<string>();
            var at = 0;

            while (true)
            {
                at = Skip(field, at);
                if (at == field.Length)
                {
                    break;
                }

                if (field[at] == ',')
                {
                    // An empty element, which the list syntax of RFC 9110 section 5.6.1 allows.
                    at++;
                    continue;
                }

                var weak = false;
                if (at + 1 < field.Length && field[at] == 'W' && field[at + 1] == '/')
                {
                    weak = true;
                    at += 2;
                }

                string opaque;
                if (!Quoted(field, ref at, out opaque))
                {
                    return Nothing();
                }

                if (!weak)
                {
                    tags.Add(opaque);
                }

                at = Skip(field, at);
                if (at < field.Length && field[at] != ',')
                {
                    return Nothing();
                }
            }

            // A field holding only commas and spaces names no tag, and a precondition naming none is
            // one no node meets.
            return tags.Count == 0
                ? new VaultPrecondition(false, tags, "If-Match names no strong tag, so no node meets it")
                : new VaultPrecondition(false, tags, Changed);
        }

        /// <summary>
        /// Whether a write or a read made under this precondition is refused, for a node carrying
        /// this tag. No precondition refuses nothing; a node that is not there meets none.
        /// </summary>
        /// <remarks>
        /// The one place the question is answered, for every vault and for the read routes alike,
        /// so that what a missing precondition means cannot come to differ between them.
        /// </remarks>
        public static bool Fails(VaultPrecondition precondition, string tag)
        {
            return precondition != null && !precondition.Admits(tag);
        }

        /// <summary>
        /// <see cref="Fails(VaultPrecondition, string)"/> for a node that is there, whose tag
        /// <paramref name="tag"/> takes, asked only when the answer depends on it.
        /// </summary>
        /// <remarks>
        /// **For a change, which asks under the writer's lock on KeePass's thread**, where a tag is
        /// a digest over every string and attachment its caller may read. Three answers need
        /// none: no
        /// precondition refuses nothing, `*` is met by every node that is there, and a precondition
        /// naming no tag, which a field that will not read and a caller handed no tags both ask, is
        /// met by none.
        /// </remarks>
        public static bool Fails(VaultPrecondition precondition, Func<string> tag)
        {
            if (tag == null)
            {
                throw new ArgumentNullException(nameof(tag));
            }

            if (precondition == null || precondition._any)
            {
                return false;
            }

            return precondition._tags.Count == 0 || !precondition.Admits(tag());
        }

        /// <summary>Whether a node carrying this tag meets the precondition. A node not there never does.</summary>
        public bool Admits(string tag)
        {
            if (tag == null)
            {
                return false;
            }

            return _any || _tags.Contains(tag, StringComparer.Ordinal);
        }

        /// <summary>A precondition no node meets, which is what a field that will not read asks.</summary>
        public static VaultPrecondition Nothing()
        {
            return new VaultPrecondition(
                false, Array.Empty<string>(), "If-Match does not read as a list of tags, so no node meets it");
        }

        /// <summary>
        /// A precondition no node meets, which is what a caller the gate hands no tag asks, whatever
        /// its field names: a tag it could meet a node with was never handed to it.
        /// </summary>
        public static VaultPrecondition Withheld()
        {
            return new VaultPrecondition(
                false, Array.Empty<string>(), "this client is handed no tags, so no node meets its If-Match");
        }

        private static int Skip(string field, int at)
        {
            while (at < field.Length && (field[at] == ' ' || field[at] == '\t'))
            {
                at++;
            }

            return at;
        }

        /// <summary>One `opaque-tag`: a quote, the characters RFC 9110 section 8.8.3 allows, a quote.</summary>
        private static bool Quoted(string field, ref int at, out string opaque)
        {
            opaque = null;
            if (at >= field.Length || field[at] != '"')
            {
                return false;
            }

            var start = ++at;
            while (at < field.Length && field[at] != '"')
            {
                // A tag character is `%x21 / %x23-7E / obs-text`, and `obs-text` cannot arrive here.
                var c = field[at];
                if (c != 0x21 && (c < 0x23 || c > 0x7E))
                {
                    return false;
                }

                at++;
            }

            if (at >= field.Length)
            {
                return false;
            }

            opaque = field.Substring(start, at - start);
            at++;
            return true;
        }
    }
}
