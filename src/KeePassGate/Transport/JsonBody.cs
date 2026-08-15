using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KeePassGate.Transport
{
    /// <summary>
    /// A request body, read strictly: one flat JSON object whose values are strings or arrays of
    /// strings, and nothing else at all.
    /// </summary>
    /// <remarks>
    /// **The grammar is Json.NET's and the policy is ours**, and the split is the point. Parsing
    /// JSON correctly is a solved problem, solved better elsewhere than it would ever be solved
    /// here; deciding what this wire is allowed to carry is a decision, and it belongs where it can
    /// be read and argued with. Writing a tokenizer to avoid a dependency would be the wrong trade
    /// on the wire an unauthenticated caller reaches first. `DESIGN.md` §118 records why
    /// Json.NET rather than one of the alternatives, which is one assembly against nine.
    ///
    /// What the policy refuses: nesting, numbers, booleans, null, duplicate names, anything after
    /// the closing brace, more members, items or characters than the limits below, and any
    /// character or string a `.kdbx` could not hold, which <see cref="IsUsable"/> says more
    /// about. A body that is not exactly one of these objects is not read, it is refused, which is
    /// the same answer an admission record gives to anything that is not exactly a record.
    ///
    /// **What it does not refuse is a trailing comma, a comment inside the object, a string or a
    /// name in single quotes, a name in no quotes at all, or `\'` inside a string**, because
    /// Json.NET accepts all of them and none carries a member or changes what the object says.
    /// Fighting a parser to reject punctuation that means nothing is how owning a grammar starts,
    /// and each is pinned so that the leniency is a decision rather than something nobody noticed.
    /// A comment **after** the closing brace is refused with everything else that follows one:
    /// measured, the reader hands it back as a token like any other, and telling it from a second
    /// document means reading past the object, which is what that refusal exists to prevent.
    ///
    /// **Depth is bounded while parsing**, at two, which is one level deeper than this wire
    /// carries. So one level of nesting is built and then refused by the value check, because a
    /// member's value here is a string or an array of strings and nothing else; only the second
    /// level is refused before anything is built. The bound worth having is the transport's, which
    /// caps a body at a megabyte before a byte of it is read.
    ///
    /// Dates are the trap worth naming: Json.NET turns anything that looks like one into a
    /// `DateTime` by default, so a value would come back reformatted rather than as it was sent.
    /// `DateParseHandling.None` turns that off, and a test pins it.
    /// </remarks>
    public sealed class JsonBody
    {
        /// <summary>
        /// More than any request this server accepts, and a bound rather than a target. Every limit
        /// here exists because the body arrives from a caller that has proved nothing yet.
        /// </summary>
        public const int MaxMembers = 32;

        /// <summary>As long as the longest list this protocol has: the profiles asked for at pairing.</summary>
        public const int MaxItems = 32;

        /// <summary>
        /// A backstop rather than a policy, which is what it went back to being when attachments
        /// arrived.
        /// </summary>
        /// <remarks>
        /// It was 16 KB, chosen as comfortably longer than the longest string this wire carried,
        /// which was a base64 PKCS#12 in a pairing answer. An attachment is base64 too and is not
        /// comfortably anything, so that number would have capped an upload at about 12 KB and made
        /// the create and update verbs on attachments a fiction.
        ///
        /// So it is the same megabyte the transport allows a whole body, which means it can never
        /// be the thing that refuses a request: a string cannot be longer than the body holding it.
        /// **What actually limits a string is the `maxLength` its route declares**, which
        /// `BodyCheck` enforces per operation. TODO(step 11): routes declare their bodies, and
        /// `BodyCheck` checks them, from spec-first routing on.
        /// </remarks>
        public const int MaxStringLength = 1024 * 1024;

        /// <summary>One object of values, so a nested one is refused before it is built.</summary>
        private const int MaxDepth = 2;

        /// <summary>
        /// The two code points Unicode guarantees are never characters, written as escapes.
        /// </summary>
        /// <remarks>
        /// Named rather than spelled, because either of them typed into this file is a byte
        /// sequence no editor renders and no reviewer can see. XML 1.0 excludes both, and so does
        /// the KeePass method that writes a string into a `.kdbx`.
        /// </remarks>
        private const char NotACharacter = '\uFFFE';

        private const char NotACharacterEither = '\uFFFF';

        private readonly Dictionary<string, string> _text;
        private readonly Dictionary<string, IReadOnlyList<string>> _arrays;

        private JsonBody(Dictionary<string, string> text, Dictionary<string, IReadOnlyList<string>> arrays)
        {
            _text = text;
            _arrays = arrays;
        }

        public int Count
        {
            get { return _text.Count + _arrays.Count; }
        }

        /// <summary>Null for anything that is not exactly one flat object of strings and arrays.</summary>
        public static JsonBody Read(string body)
        {
            var parsed = Parse(body);
            return parsed == null ? null : Check(parsed);
        }

        /// <summary>The value of a string member, or null when it is absent or is an array.</summary>
        public string Text(string name)
        {
            string value;
            return name != null && _text.TryGetValue(name, out value) ? value : null;
        }

        /// <summary>The value of an array member, or null when it is absent or is a string.</summary>
        public IReadOnlyList<string> Array(string name)
        {
            IReadOnlyList<string> value;
            return name != null && _arrays.TryGetValue(name, out value) ? value : null;
        }

        public bool Has(string name)
        {
            return name != null && (_text.ContainsKey(name) || _arrays.ContainsKey(name));
        }

        /// <summary>
        /// Every member that arrived, so a check can refuse one the document did not declare.
        /// </summary>
        /// <remarks>
        /// Reading a body is asking it for what was expected; checking one is asking it what it
        /// brought. Without this, a member nobody declared would be a member nobody looked at.
        /// </remarks>
        public IEnumerable<string> Names
        {
            get
            {
                foreach (var name in _text.Keys)
                {
                    yield return name;
                }

                foreach (var name in _arrays.Keys)
                {
                    yield return name;
                }
            }
        }

        /// <summary>
        /// One object, or null when the text is not one.
        /// </summary>
        /// <remarks>
        /// Every setting here turns off something Json.NET would otherwise do quietly: a duplicate
        /// name replaces the one before it, a date shaped value is converted, and text after the
        /// object is ignored. None of those is acceptable on a wire where a member nobody declared
        /// is a member nobody looked at.
        /// </remarks>
        private static JObject Parse(string body)
        {
            if (body == null)
            {
                return null;
            }

            // Before the parser sees it. Json.NET's reader reports a NUL after the object as the
            // end of its input, so one sent there passed the check below that refuses anything
            // after the closing brace. Nothing this reads holds one anywhere else either: inside a
            // string it is a control character, which the value check refuses.
            if (body.IndexOf('\0') >= 0)
            {
                return null;
            }

            try
            {
                using (var text = new StringReader(body))
                using (var reader = new JsonTextReader(text))
                {
                    reader.MaxDepth = MaxDepth;
                    reader.DateParseHandling = DateParseHandling.None;

                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                    {
                        return null;
                    }

                    var settings = new JsonLoadSettings
                    {
                        // Discarded rather than carried. A comment holds no member and reaches
                        // nothing, so a body with one says exactly what the same body without one
                        // says, and refusing it would mean scanning for something that changes
                        // nothing.
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    };

                    var parsed = JObject.Load(reader, settings);

                    // Anything after the closing brace is a second document, and a reader that
                    // stopped at the first would be reading half of what arrived.
                    return reader.Read() ? null : parsed;
                }
            }
            catch (JsonException)
            {
                // Malformed, too deep, or a duplicate name. All of them mean the same thing to a
                // caller, which is that this is not a body this server reads.
                return null;
            }
        }

        /// <summary>What this wire is allowed to carry, which is the part worth writing here.</summary>
        private static JsonBody Check(JObject parsed)
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            var arrays = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            if (parsed.Count > MaxMembers)
            {
                return null;
            }

            foreach (var member in parsed.Properties())
            {
                if (!IsUsable(member.Name))
                {
                    return null;
                }

                var value = member.Value;

                if (value.Type == JTokenType.String)
                {
                    var text = value.Value<string>();
                    if (!IsUsable(text))
                    {
                        return null;
                    }

                    strings[member.Name] = text;
                    continue;
                }

                if (value.Type != JTokenType.Array)
                {
                    // A number, a boolean, null or an object. None of them is ever expected, so
                    // none of them is ever guessed at.
                    return null;
                }

                var items = new List<string>();
                foreach (var item in (JArray)value)
                {
                    if (items.Count >= MaxItems || item.Type != JTokenType.String)
                    {
                        return null;
                    }

                    var text = item.Value<string>();
                    if (!IsUsable(text))
                    {
                        return null;
                    }

                    items.Add(text);
                }

                arrays[member.Name] = items;
            }

            return new JsonBody(strings, arrays);
        }

        /// <summary>
        /// Whether a name or a value is one this server will carry any further.
        /// </summary>
        /// <remarks>
        /// **A value may hold a line break**, and has to: an entry's notes are a field like any
        /// other and people write paragraphs in them. What keeps a line break out of a log is
        /// escaping on the way out, which every answer and every audit line goes through, rather
        /// than refusing it on the way in.
        ///
        /// **What it may not hold is a character the database would drop**, which is the other
        /// half of the same question and the half this used to get wrong. A `.kdbx` is XML, and
        /// KeePass writes every unprotected string through `StrUtil.SafeXmlString`, which removes
        /// what XML 1.0 cannot represent: `\b` and `\f` and the rest of the control characters
        /// other than tab, carriage return and line feed, `U+FFFE` and `U+FFFF`, and any surrogate
        /// without its pair. So a body carrying one of those was accepted, answered `200` or `201`
        /// with the value reported as stored, and came back short from the file: the client
        /// believed it had written a password it had not. Measured against KeePass 2.61.1 over
        /// every code point in the basic multilingual plane, and this rule and that method agree
        /// on all 65,536 of them.
        ///
        /// **A carriage return is let through, and comes back changed**, which that measurement
        /// could not see because it asked only what the writer keeps. The reader is the other
        /// half: an XML reader turns a carriage return, or a carriage return and line feed, into
        /// one line feed, as section 2.11 of XML 1.0 requires, so a value holding `\r` is stored
        /// as sent and reads back with `\n` once KeePass has opened the file again. That is
        /// KeePass's behavior and it is left as KeePass's, rather than refused or normalized here:
        /// refusing it would turn away every note pasted from a Windows editor, and normalizing it
        /// would make this wire change a value KeePass itself would have kept until the next open.
        ///
        /// **Nor may a string be whitespace and nothing else.** KeePass writes an unprotected
        /// string as the text of an element, and its reader drops text that is only whitespace,
        /// so a user name of one space was answered `201` and read back empty once the file had
        /// been opened again, and a custom field named with a space came back named nothing, which
        /// no route can address. Measured against KeePass 2.61.1, with a value of spaces, one of
        /// line breaks and tabs, and a name. Whitespace means the four characters XML counts, and
        /// a string with anything else in it keeps all of them, as a protected one does.
        ///
        /// **Asked of every string on this wire**, rather than only of the ones bound for an
        /// unprotected field. Whether a given field is protected is the database's own setting
        /// and a flag the operator can change afterwards, so a rule that let a character in
        /// wherever it happens to survive today would be a rule that a later change to Database
        /// Settings turns back into silent truncation. Nothing else this wire carries wants one:
        /// the pairing bodies are base64 and hex, and an attachment's bytes travel as base64 too,
        /// so what is refused here is a name or a value nothing could have stored.
        ///
        /// **The one thing it cannot reach is an escaped surrogate half**, because Json.NET
        /// substitutes `U+FFFD` for `\uD800` while it is reading and this policy is handed the
        /// substitute. Owning that would mean reading the text before the parser does, which is
        /// the grammar this deliberately does not own; what a caller asked to store there was not
        /// a character in the first place, and the replacement is what every other reader would
        /// have made of it.
        /// </remarks>
        private static bool IsUsable(string text)
        {
            if (text == null || text.Length > MaxStringLength)
            {
                return false;
            }

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];

                if (char.IsHighSurrogate(character))
                {
                    // The pair is one character, and half of one is not a character at all.
                    // Nothing off the wire arrives this way: a body is UTF-8 decoded strictly, and
                    // an escaped half is turned into `U+FFFD` by Json.NET before this sees it. A
                    // document read back out of a `.kdbx` this plugin did not write is the caller
                    // that can carry one, and refusing there is the fail-closed direction.
                    if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    {
                        return false;
                    }

                    index++;
                    continue;
                }

                if (char.IsLowSurrogate(character)
                    || character == NotACharacter
                    || character == NotACharacterEither)
                {
                    return false;
                }

                if (character < ' ' && character != '\t' && character != '\n' && character != '\r')
                {
                    return false;
                }
            }

            return !IsWhitespaceAlone(text);
        }

        /// <summary>
        /// Whether a string is not empty and holds nothing but the four characters XML counts as
        /// whitespace.
        /// </summary>
        private static bool IsWhitespaceAlone(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            foreach (var character in text)
            {
                if (character != ' ' && character != '\t' && character != '\n' && character != '\r')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
