using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KeePassGate.Transport;
using Newtonsoft.Json;

namespace KeePassGate.Acl
{
    /// <summary>What a node says when it says nothing about a profile.</summary>
    public enum AclDefault
    {
        /// <summary>Ask the group above. The answer when a document does not say.</summary>
        Inherit = 0,

        /// <summary>
        /// Stop here and refuse. What seals a subtree off from whatever was granted above it.
        /// </summary>
        Deny,
    }

    /// <summary>
    /// One node's rules: what each profile may do here, and what happens to profiles it says
    /// nothing about.
    /// </summary>
    /// <remarks>
    /// **One flat object of strings and arrays of strings**, which is exactly what `JsonBody`
    /// already reads and what this wire already carries. That was not a coincidence looking for a
    /// justification: a grant document is profiles and the verbs they hold, and nothing about that
    /// needs nesting. It also means no JSON library arrives for it, which matters in a process that
    /// holds somebody's password database.
    ///
    /// <code>
    /// {"default":"deny","reader":["read"],"editor":["read","update"]}
    /// </code>
    ///
    /// **`default` cannot collide with a profile**, because `default` was already refused as a
    /// profile name before this existed, by the issuer that puts profiles into certificates. So the
    /// one reserved word this format needs is one the rest of the system already reserved.
    ///
    /// **An empty array is a rule, not an absence.** `{"reader":[]}` says this profile may do
    /// nothing here, which is how a grant made higher up is taken away lower down without a deny
    /// syntax. A profile the document does not mention at all is a different thing: that is what
    /// `default` answers.
    ///
    /// Every failure denies. A document that will not read, a verb nobody recognizes, a default
    /// that is not one of the two: each of those is a rule somebody wrote and this cannot honor, so
    /// none of them is skipped.
    ///
    /// **The reader's ceilings are this format's ceilings**, and they are worth saying out loud
    /// because reaching one denies rather than truncates. `JsonBody.MaxMembers` allows 32, so a
    /// node carries the default and 31 profiles, and the 32nd makes the whole document unreadable
    /// and everything under that node refused. That holds whether or not a document writes the
    /// default, because <see cref="Write"/> always does. `JsonBody.MaxItems` allows 32 verbs where
    /// there are eight. Neither is a number anybody should be near, and a node that is near one is
    /// a node whose rules nobody can read anyway.
    /// </remarks>
    public sealed class AclDocument
    {
        /// <summary>The reserved member that carries the default. Never a profile name.</summary>
        public const string DefaultKey = "default";

        /// <summary>The key on a group's or an entry's custom data. This plugin's own namespace.</summary>
        public const string CustomDataKey = "KPG";

        /// <summary>
        /// How many profiles one document may name: the reader's ceiling less the member the
        /// default takes.
        /// </summary>
        /// <remarks>
        /// Counted on the profiles rather than on the members, so a document that leaves the
        /// default out is held to it too. Counted on the members alone, such a document could
        /// name 32, and <see cref="Write"/> always adds the default, so an ordinary edit of that
        /// node would write back 33 members, which the same reader refuses: every client refused
        /// at that node and under it, for having changed one rule. Written as the subtraction
        /// rather than as 31, so that raising the reader raises this with it.
        /// </remarks>
        public const int MostProfiles = JsonBody.MaxMembers - 1;

        private readonly Dictionary<string, HashSet<AclVerb>> _grants;

        private AclDocument(AclDefault fallback, Dictionary<string, HashSet<AclVerb>> grants)
        {
            Default = fallback;
            _grants = grants;
        }

        /// <summary>What this node answers for a profile it does not mention.</summary>
        public AclDefault Default { get; private set; }

        /// <summary>Every profile this node has a rule for, in no particular order.</summary>
        public IEnumerable<string> Profiles
        {
            get { return _grants.Keys; }
        }

        /// <summary>A node with no rules at all, which is what an absent document means.</summary>
        public static AclDocument Empty
        {
            get { return new AclDocument(AclDefault.Inherit, new Dictionary<string, HashSet<AclVerb>>(StringComparer.Ordinal)); }
        }

        /// <summary>
        /// Reads one, or refuses it. Null for anything that is not exactly a document, an empty
        /// value included, and the two kinds of null mean different things to the caller.
        /// </summary>
        /// <remarks>
        /// **An empty value is an absence.** It is a node that says nothing, and the callers skip
        /// it before asking, so it inherits whatever the groups above grant. Anything else that
        /// will not read, whitespace included, is a rule somebody wrote and this cannot honor, and
        /// the resolver turns that into a denial rather than into an absence.
        /// </remarks>
        public static AclDocument Read(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var body = JsonBody.Read(text);
            if (body == null)
            {
                return null;
            }

            var fallback = AclDefault.Inherit;
            var grants = new Dictionary<string, HashSet<AclVerb>>(StringComparer.Ordinal);

            foreach (var name in body.Names)
            {
                if (string.Equals(name, DefaultKey, StringComparison.Ordinal))
                {
                    var written = body.Text(name);
                    if (written == null)
                    {
                        // `"default": ["deny"]` is not a default, it is somebody guessing.
                        return null;
                    }

                    if (string.Equals(written, "inherit", StringComparison.Ordinal))
                    {
                        fallback = AclDefault.Inherit;
                        continue;
                    }

                    if (string.Equals(written, "deny", StringComparison.Ordinal))
                    {
                        fallback = AclDefault.Deny;
                        continue;
                    }

                    return null;
                }

                var verbs = body.Array(name);
                if (verbs == null)
                {
                    // A profile's rule is the list of verbs it holds, so a string here is a rule
                    // written in a shape this does not read, and reading it as something else is
                    // how a grant comes to mean what nobody wrote.
                    return null;
                }

                var held = new HashSet<AclVerb>();
                foreach (var verb in verbs.Select(AclVerbs.Read))
                {
                    if (verb == null || !AclVerbs.IsGrantable(verb.Value))
                    {
                        return null;
                    }

                    held.Add(verb.Value);
                }

                grants[name] = held;
            }

            if (grants.Count > MostProfiles)
            {
                // Only reachable when the default is left out, since `JsonBody` counts it as a
                // member when it is there. Refused rather than read, because this reader is what
                // `Write` is held to: a document it accepted has to be one it can write back.
                return null;
            }

            return new AclDocument(fallback, grants);
        }

        /// <summary>Builds one, which is what the editor will do.</summary>
        public static AclDocument Of(AclDefault fallback, IDictionary<string, IEnumerable<AclVerb>> grants)
        {
            var held = new Dictionary<string, HashSet<AclVerb>>(StringComparer.Ordinal);

            if (grants != null)
            {
                foreach (var grant in grants)
                {
                    if (string.Equals(grant.Key, DefaultKey, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            DefaultKey + " is the reserved member, not a profile.", nameof(grants));
                    }

                    var verbs = new HashSet<AclVerb>();
                    foreach (var verb in grant.Value ?? Array.Empty<AclVerb>())
                    {
                        if (!AclVerbs.IsGrantable(verb))
                        {
                            throw new ArgumentException(
                                AclVerbs.Write(verb) + " is not something a rule can grant.", nameof(grants));
                        }

                        verbs.Add(verb);
                    }

                    held[grant.Key] = verbs;
                }
            }

            return new AclDocument(fallback, held);
        }

        /// <summary>
        /// What this node says about a profile: the verbs it holds, or null when this node says
        /// nothing about it at all.
        /// </summary>
        /// <remarks>
        /// An empty set and null are the two answers that look alike and are not. The empty set is
        /// a rule saying this profile may do nothing from here down; null is silence, which is what
        /// <see cref="Default"/> answers for.
        /// </remarks>
        public AclGrant Grant(string profile)
        {
            HashSet<AclVerb> verbs;
            return profile != null && _grants.TryGetValue(profile, out verbs) ? new AclGrant(verbs) : null;
        }

        /// <summary>
        /// Writes it back as one flat object, canonically rather than as it arrived.
        /// </summary>
        /// <remarks>
        /// `default` is written first whether or not the source put it there, and the profiles
        /// follow sorted, so the same rules always produce the same bytes. A reader that wanted the bytes it sent
        /// back unchanged would be a reader relying on an order this format does not promise.
        /// </remarks>
        public string Write()
        {
            // Written by `Json.NET`, which is here for the request bodies. A profile name is a
            // string somebody typed into a dialog, so what it looks like escaped is a question
            // this file should not be answering a second time.
            var text = new StringWriter(CultureInfo.InvariantCulture);
            var json = new JsonTextWriter(text) { Formatting = Formatting.None };

            json.WriteStartObject();

            // The default first and always, so that a document read by a person says what it does
            // with anything it does not mention before it lists what it does.
            json.WritePropertyName(DefaultKey);
            json.WriteValue(Default == AclDefault.Deny ? "deny" : "inherit");

            var names = new List<string>(_grants.Keys);
            names.Sort(StringComparer.Ordinal);

            foreach (var name in names)
            {
                json.WritePropertyName(name);
                json.WriteStartArray();

                var verbs = new List<AclVerb>(_grants[name]);

                // In the order `AclVerbs.Grantable` declares, which is the order the editor shows
                // and the order a mask is read in. Sorting the enum values instead agreed with it
                // only for as long as the array happened to be in enum order, and a rule written
                // here is read by a person comparing it against what a tab showed them.
                verbs.Sort((left, right) => AclVerbs.PositionOf(left).CompareTo(AclVerbs.PositionOf(right)));

                foreach (var verb in verbs)
                {
                    json.WriteValue(AclVerbs.Write(verb));
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.Flush();

            return text.ToString();
        }
    }
}
