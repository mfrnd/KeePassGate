using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Transport;
using Newtonsoft.Json;

namespace KeePassGate.Acl
{
    /// <summary>
    /// The profiles a database knows about: the names an operator may grant, and a client may be
    /// approved for.
    /// </summary>
    /// <remarks>
    /// **A profile had no home before this.** A name became real by being approved at a pairing and
    /// meant something by being named in a rule, and nothing anywhere held the set. That works
    /// until somebody has to answer "what may I grant here", and the honest answer was "whatever
    /// you type, spelled exactly as somebody else typed it". A typo made a profile that existed and
    /// granted nothing, and no screen could tell it from the one that was meant.
    ///
    /// So the set is written down, on the database, in this plugin's own namespace. It is a list
    /// of names and nothing else: what a name reaches is on the nodes, and who holds it is in the
    /// admission records. Three places, three questions, none of them duplicated.
    ///
    /// **One flat object of strings and arrays of strings**, the same grammar the rules use and the
    /// wire already carries, so no parser arrives with it:
    ///
    /// <code>
    /// {"profiles":["reader","ops"]}
    /// </code>
    ///
    /// **Nothing is granted by this list, and nothing is granted without it.** A client holds the
    /// names its record carries that are also here, so taking a name off the list takes it from
    /// every client at once and adding it back returns it, with no record and no rule touched.
    ///
    /// A document that will not read is an empty set rather than a refusal. That costs every client
    /// its access until the list is written again, which is the direction to fail in: a damaged
    /// list read as some other list would be granting on a guess.
    /// </remarks>
    public sealed class ProfileNames
    {
        /// <summary>The key on the database's custom data. This plugin's own namespace.</summary>
        public const string CustomDataKey = "KPG.Profiles";

        /// <summary>The one member the document has.</summary>
        public const string ProfilesKey = "profiles";

        /// <summary>
        /// As many as the reader allows in one array, which is far more than anybody will name.
        /// </summary>
        public const int Most = JsonBody.MaxItems;

        private readonly List<string> _names;

        private ProfileNames(IEnumerable<string> names)
        {
            _names = new List<string>();

            foreach (var name in names ?? Array.Empty<string>())
            {
                Add(name);
            }
        }

        /// <summary>An empty set, which is what a database starts with.</summary>
        public static ProfileNames Empty()
        {
            return new ProfileNames(null);
        }

        /// <summary>Every name, sorted, because a list somebody reads is a list in an order.</summary>
        public IReadOnlyList<string> Names
        {
            get
            {
                var sorted = new List<string>(_names);
                sorted.Sort(StringComparer.Ordinal);
                return sorted;
            }
        }

        /// <summary>
        /// Whether a stored list reads as one: absent and empty are a list naming nothing, and
        /// anything else has to be the object this writes.
        /// </summary>
        /// <remarks>
        /// <see cref="Read"/> answers a list that will not read as one naming nothing, the safe
        /// direction for every reader that only asks what is named. This is for the reader that
        /// writes what it reads, and must not write that nothing.
        /// </remarks>
        public static bool Readable(string document)
        {
            return string.IsNullOrEmpty(document) || JsonBody.Read(document) != null;
        }

        /// <summary>
        /// Reads a document, or answers with an empty set when there is nothing readable there.
        /// </summary>
        public static ProfileNames Read(string document)
        {
            if (string.IsNullOrEmpty(document))
            {
                return Empty();
            }

            var body = JsonBody.Read(document);
            if (body == null)
            {
                return Empty();
            }

            return new ProfileNames(body.Array(ProfilesKey));
        }

        /// <summary>
        /// What to store, or null when there is nothing left to store, which is what takes the key
        /// away rather than leaving an empty document behind.
        /// </summary>
        public string Write()
        {
            if (_names.Count == 0)
            {
                return null;
            }

            // Written by `Json.NET`, the same one that reads it back. A profile name is a
            // string somebody typed into a dialog, and it travels into a certificate claim from
            // here, so nothing about what it looks like escaped is decided twice.
            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            using (var json = new JsonTextWriter(text) { Formatting = Formatting.None })
            {
                json.WriteStartObject();
                json.WritePropertyName(ProfilesKey);
                json.WriteStartArray();

                foreach (var name in Names)
                {
                    json.WriteValue(name);
                }

                json.WriteEndArray();
                json.WriteEndObject();
                json.Flush();

                return text.ToString();
            }
        }

        /// <summary>Whether this set already holds a name, compared exactly.</summary>
        public bool Holds(string name)
        {
            return name != null && _names.Contains(name);
        }

        /// <summary>
        /// Adds a name. Answers with why it could not, or null when it did or was already there.
        /// </summary>
        /// <remarks>
        /// The same rule the issuer applies, and only that rule, because a profile that cannot
        /// travel as a claim is a profile no client can ever hold: it would be granted here and
        /// refused at the certificate. That includes `default`, which the issuer already refuses
        /// in any case, so there is no second reservation written here to disagree with it.
        /// </remarks>
        public string Add(string name)
        {
            if (!AuthorityIssuer.IsUsableProfileName(name))
            {
                return "A profile name is letters, digits, dash and underscore, up to 64 of them, "
                    + "and not `" + AclDocument.DefaultKey + "`.";
            }

            if (_names.Contains(name))
            {
                return null;
            }

            if (_names.Count >= Most)
            {
                return "This database already names " + Most + " profiles.";
            }

            _names.Add(name);
            return null;
        }

        /// <summary>Takes a name out. False when it was not there.</summary>
        public bool Remove(string name)
        {
            return name != null && _names.Remove(name);
        }

        /// <summary>
        /// Combines an edit made against one version of this list with a version somebody else
        /// wrote in the meantime.
        /// </summary>
        /// <remarks>
        /// **This key has two writers and they write it differently.** The KPG tab holds its
        /// version until the dialog is accepted; a pairing writes the live database and saves it,
        /// because the profile the operator just approved has to be named by the same write as the
        /// admission record. Replaying the tab's version wholesale therefore threw the pairing's
        /// name away, and the client the operator had just approved was left holding a name the
        /// list did not, which grants nothing.
        ///
        /// **What is replayed is the operator's difference, not their snapshot**, which is the
        /// same rule the buffer follows for every other key by only replaying the ones that were
        /// touched. A name the operator added goes in; a name they took out comes out; anything
        /// else the list holds now was put there by somebody else and stays.
        ///
        /// **Unless the list is full by then, and then what it holds already stays.** The page
        /// stops the operator at <see cref="Most"/>, but a pairing can have named a profile of its
        /// own meanwhile, and the two together can come to one more than the list holds. The names
        /// already there went into the file with the admission records that needed them, so taking
        /// one out would take a profile from a client the operator has just approved, while a
        /// name typed on the page can be typed again once there is room. It is not dropped
        /// quietly: every name left out is handed back, for the caller to say so.
        ///
        /// A document that will not read is an empty set, here as everywhere else this reads one.
        /// </remarks>
        /// <param name="baseline">What the key held when the edit began.</param>
        /// <param name="current">What it holds now.</param>
        /// <param name="edited">The edited version, or null when the edit was to remove the key.</param>
        /// <param name="unkept">The names the operator added that the full list could not take.</param>
        /// <returns>What to store, or null when nothing is left to store.</returns>
        public static string Merge(string baseline, string current, string edited, out IReadOnlyList<string> unkept)
        {
            var before = Read(baseline);
            var mine = Read(edited);
            var merged = Read(current);
            var left = new List<string>();

            // Removals first, so that a name the operator took out makes room for one they put in.
            foreach (var name in before.Names.Where(name => !mine.Holds(name)))
            {
                merged.Remove(name);
            }

            foreach (var name in mine.Names.Where(name => !before.Holds(name)))
            {
                var refused = merged.Add(name);
                if (refused != null)
                {
                    left.Add(name);
                }
            }

            unkept = left;
            return merged.Write();
        }
    }
}
