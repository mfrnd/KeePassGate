using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KeePassGate.Transport;

namespace KeePassGate.Spec
{
    /// <summary>
    /// One path from the document, matched against one path from the wire.
    /// </summary>
    /// <remarks>
    /// Segment by segment, because that is the only comparison that cannot be talked into matching
    /// something else. A regular expression built from a template is a second grammar to be wrong
    /// about, and the thing it would be wrong about is which route a request reaches.
    ///
    /// A template segment is either a literal, compared exactly, or `{name}`, which captures. A
    /// capture never spans a separator, so `/entries/{uuid}` cannot match `/entries/a/b`, and it is
    /// never empty, so it cannot match `/entries/`.
    ///
    /// **The path arrives as it was sent, and is decoded one segment at a time here**, which is
    /// what RFC 3986 section 2.2 asks for and the only place it can be done correctly: a `%2F`
    /// decoded before the split stops being part of a name and becomes a separator, so an
    /// attachment called `a/b` could never be addressed. Splitting first leaves the escape meaning a
    /// slash inside one segment, which is what escaping it was for.
    ///
    /// The reader has already refused a path with a segment that will not decode, one that carries
    /// a control character, and one that *is* a climb. It cannot have refused a segment that
    /// **decodes into** one, because decoding happens after the split so that an escaped separator
    /// stays inside a name, so `..%2F..%2F` reaches here as one token and comes out of the
    /// decoder as a path that climbs. A capture is where that stops being a spelling and starts
    /// being a value, so it is refused there rather than trusted here.
    ///
    /// What is left for this is comparing, and comparing what a caller meant rather than how it
    /// spelled it.
    /// </remarks>
    public sealed class PathTemplate
    {
        private readonly string[] _segments;
        private readonly bool[] _captures;

        private PathTemplate(string[] segments, bool[] captures, string text)
        {
            _segments = segments;
            _captures = captures;
            Text = text;
        }

        /// <summary>The template as the document writes it, which is what a log or a test names.</summary>
        public string Text { get; private set; }

        /// <summary>The names this template captures, in order.</summary>
        public IReadOnlyList<string> Parameters
        {
            get
            {
                var names = new List<string>();
                for (var i = 0; i < _segments.Length; i++)
                {
                    if (_captures[i])
                    {
                        names.Add(_segments[i]);
                    }
                }

                return names;
            }
        }

        /// <summary>
        /// Reads a template, or refuses it. A document that cannot be read is a startup failure
        /// rather than a route that quietly matches nothing.
        /// </summary>
        public static PathTemplate Parse(string template)
        {
            if (string.IsNullOrEmpty(template) || template[0] != '/')
            {
                throw new ArgumentException("A path in the document must be absolute: " + template, nameof(template));
            }

            var parts = template.Substring(1).Split('/');
            var segments = new string[parts.Length];
            var captures = new bool[parts.Length];
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length > 1 && part[0] == '{' && part[part.Length - 1] == '}')
                {
                    var name = part.Substring(1, part.Length - 2);
                    if (name.Length == 0 || name.IndexOf('{') >= 0 || name.IndexOf('}') >= 0)
                    {
                        throw new ArgumentException("That is not a parameter name: " + part, nameof(template));
                    }

                    if (!seen.Add(name))
                    {
                        // Two captures of one name would mean one of them silently winning.
                        throw new ArgumentException(
                            "The path captures " + name + " twice: " + template, nameof(template));
                    }

                    segments[i] = name;
                    captures[i] = true;
                    continue;
                }

                if (part.IndexOf('{') >= 0 || part.IndexOf('}') >= 0)
                {
                    // A brace in the middle of a literal is a template somebody meant to write and
                    // did not, so it is refused rather than matched literally.
                    throw new ArgumentException("A path segment is a literal or a whole parameter: " + part,
                        nameof(template));
                }

                if (part.Length == 0 && parts.Length > 1)
                {
                    throw new ArgumentException("A path has no empty segments: " + template, nameof(template));
                }

                segments[i] = part;
                captures[i] = false;
            }

            return new PathTemplate(segments, captures, template);
        }

        /// <summary>
        /// Whether a path from the wire is this route, and what it captured if it is.
        /// </summary>
        public bool TryMatch(string path, out IReadOnlyDictionary<string, string> captured)
        {
            captured = null;

            if (string.IsNullOrEmpty(path) || path[0] != '/')
            {
                return false;
            }

            var parts = path.Substring(1).Split('/');
            if (parts.Length != _segments.Length)
            {
                return false;
            }

            Dictionary<string, string> found = null;

            for (var i = 0; i < parts.Length; i++)
            {
                string part;
                if (!PercentEncoding.TryDecodeSegment(parts[i], out part))
                {
                    // The reader refused this already. Refusing it again costs nothing and means
                    // this cannot be talked into matching by whoever calls it next.
                    return false;
                }

                if (!_captures[i])
                {
                    // Compared after decoding, so an escaped separator is part of one
                    // segment rather than a route, and `/g%61te/status` is `/gate/status`. A literal in the document is what a caller meant, not
                    // how it spelled it.
                    if (!string.Equals(part, _segments[i], StringComparison.Ordinal))
                    {
                        return false;
                    }

                    continue;
                }

                if (!IsCapture(part))
                {
                    return false;
                }

                if (found == null)
                {
                    found = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                found[_segments[i]] = part;
            }

            captured = found ?? Empty;
            return true;
        }

        /// <summary>Whether a decoded value is one a capture can hold.</summary>
        /// <remarks>
        /// One rule, stated once, so that anything else needing to know whether a route could ever
        /// name a value asks this rather than a copy of it.
        ///
        /// **A control character is refused here as well as by the reader**, which refuses one in
        /// a path however it was spelled. Asked again because this is the question about the value
        /// rather than about the spelling, and a value that arrives some other way has not been
        /// through the reader.
        /// </remarks>
        public static bool IsCapture(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                // An empty capture would make `/entries/` reach the operation that reads one.
                return false;
            }

            foreach (var character in value)
            {
                if (char.IsControl(character))
                {
                    return false;
                }
            }

            // The reader refused a segment that *is* `..`; it cannot refuse one that decodes into
            // a path containing it, because it decodes after splitting so that an escaped
            // separator stays inside a name. So the check belongs where the name becomes a value
            // somebody will use. Nothing reads a capture yet, which is exactly why this is here
            // now: the first handler that does would inherit an invariant this class states and
            // did not hold.
            return !PercentEncoding.Navigates(value);
        }

        /// <summary>What a template with no captures hands back, once, for every request.</summary>
        /// <remarks>
        /// Wrapped rather than handed out bare. It is one object shared by every capture-less
        /// match in the process, and it is published into per-request state, so a caller that
        /// downcast it could write into what every other request is reading.
        /// </remarks>
        private static readonly IReadOnlyDictionary<string, string> Empty =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal));

        /// <summary>Whether some request could match both this template and another.</summary>
        /// <remarks>
        /// Two templates overlap when they have the same number of segments and every position
        /// is a capture on one side or the other, or the same literal on both. `/a/{x}` and `/a/b`
        /// overlap; `/a/{x}` and `/c/{x}` do not.
        /// </remarks>
        public bool Overlaps(PathTemplate other)
        {
            if (other == null || other._segments.Length != _segments.Length)
            {
                return false;
            }

            for (var i = 0; i < _segments.Length; i++)
            {
                if (_captures[i] || other._captures[i])
                {
                    continue;
                }

                if (!string.Equals(_segments[i], other._segments[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
