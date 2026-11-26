using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The acts that write a standing answer to a renewal into a database's store, `STORE.md`
    /// §618: a refusal, forever or until a moment, and a lift, each a single record of kind
    /// `version` of `renew:` and the client key, composed under the key of the reading it is
    /// composed over from what its writer was shown.
    /// </summary>
    /// <remarks>
    /// **What a version names is what its writer was shown**, and nothing that arrived since. A
    /// version names in `after` each standing version of the client's answer that its writer was
    /// shown and that still stands, with everything those name in `after`; a lift names in `lost`
    /// each lost version it was shown that is still lost, with everything the versions it
    /// replaces name in `lost`, none of them in `after`. A version that arrived by a merge while
    /// the dialog stood is named by nothing, stands beside the new version, and the stricter of the
    /// two is the answer: a lift made without having seen a refusal loses to it.
    ///
    /// **A refusal holds no `lost`.** A refusal is given only where nothing refuses, so it never
    /// settles a lost version. **`lost` replaces nothing**: a version settled there that turns up
    /// again with its content stands beside the lift, and refuses.
    ///
    /// **A lift with nothing left to name comes to nothing**, as a certificate taken away with
    /// nothing left to end does: every version its press was shown was replaced since. It is
    /// composed again over the store as it stands at each read of the KPG tab's buffer and at OK,
    /// `StoreAct.Again`.
    ///
    /// **A version is composed only where its content and its item fit** the plugin's ceiling of a
    /// string, `STORE.md` §627, so a write is never refused for an answer that could not be
    /// restated.
    /// </remarks>
    internal static class RenewalActs
    {
        /// <summary>What the log calls a refusal's act.</summary>
        public const string RefuseName = "refusing a client's renewals";

        /// <summary>What the log calls a lift's act.</summary>
        public const string LiftName = "allowing a client's renewal";

        /// <summary>The length of an identifier as hexadecimal.</summary>
        private const int IdentifierLength = 32;

        /// <summary>
        /// An act of a single version refusing every renewal of the client forever or until a
        /// moment, naming in `after` the versions in <paramref name="shown"/> that still stand and
        /// what they name there; null with the reason in <paramref name="refused"/> where the
        /// reading holds no store key or the version would not fit.
        /// </summary>
        /// <param name="shown">The identifiers of the standing versions the reading before the dialog held.</param>
        public static StoreAct Refuse(StoreReading reading, string client, RenewalRefusal refusal, IEnumerable<string> shown, out string refused)
        {
            if (!CertificateDigest.IsDigest(client))
            {
                throw new ArgumentException("An answer is about a client by its client key.", nameof(client));
            }

            var doc = RenewalText.Of(refusal);
            return Composed(reading, RefuseName, client, doc, shown, Enumerable.Empty<string>(), null, out refused);
        }

        /// <summary>
        /// An act of a single version `ask` naming in `after` the versions in
        /// <paramref name="standing"/> that still stand and what they name there, and in `lost`
        /// the versions in <paramref name="lost"/> that are still lost and what the versions it
        /// replaces name there, or null where it names nothing, where the reading holds no store
        /// key, or where the version would not fit. Composed again under the key of each later
        /// reading.
        /// </summary>
        /// <param name="standing">The identifiers of the standing versions the press was shown.</param>
        /// <param name="lost">The identifiers of the lost versions the press was shown.</param>
        public static StoreAct Lift(StoreReading reading, string client, IReadOnlyCollection<string> standing, IReadOnlyCollection<string> lost)
        {
            if (!CertificateDigest.IsDigest(client))
            {
                throw new ArgumentException("An answer is about a client by its client key.", nameof(client));
            }

            string refused;
            return Composed(reading, LiftName, client, RenewalText.Ask, standing, lost, again => Lift(again, client, standing, lost), out refused);
        }

        /// <summary>The client key a lift is about, or null for every other act.</summary>
        public static string About(StoreAct act)
        {
            if (act == null || !string.Equals(act.Name, LiftName, StringComparison.Ordinal))
            {
                return null;
            }

            foreach (var record in act.Records.Where(record => record.Kind == StoreKinds.Version))
            {
                string client;
                if (RenewalText.ReadObject(record.Member(StoreKinds.ObjectMember), out client))
                {
                    return client;
                }
            }

            return null;
        }

        /// <summary>
        /// A press of a lift about a client that takes the place of an earlier press about the
        /// same client: composed again under each later reading, it names every version either
        /// press was shown, so that a version only the earlier press was shown is not left
        /// standing by the later; the later press as it is where the two are not about a single
        /// client.
        /// </summary>
        /// <remarks>
        /// **What a press was shown is read off the version it composed**: its `after` holds each
        /// standing version it was shown that still stood, with what those name, and its `lost`
        /// each lost version it was shown that was still lost. Composed again, every one of them
        /// that does not stand or is not lost then names nothing, so naming more of what a press
        /// was shown than stands changes nothing.
        /// </remarks>
        public static StoreAct Folded(StoreAct earlier, StoreAct later)
        {
            var client = About(later);
            if (client == null || !string.Equals(About(earlier), client, StringComparison.Ordinal))
            {
                return later;
            }

            var standing = Shown(earlier, StoreKinds.After).Concat(Shown(later, StoreKinds.After))
                .Distinct(StringComparer.Ordinal).ToList();
            var lost = Shown(earlier, StoreKinds.Lost).Concat(Shown(later, StoreKinds.Lost))
                .Distinct(StringComparer.Ordinal).ToList();
            var key = later.Key();
            try
            {
                return new StoreAct(later.Name, key, later.Against, later.Records, null, again => Lift(again, client, standing, lost));
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>The identifiers a member of the version of an act lists.</summary>
        private static IEnumerable<string> Shown(StoreAct act, string member)
        {
            return act.Records.Where(record => record.Kind == StoreKinds.Version)
                .SelectMany(record => Listed(record.Member(member)));
        }

        private static StoreAct Composed(
            StoreReading reading,
            string name,
            string client,
            string doc,
            IEnumerable<string> shownStanding,
            IEnumerable<string> shownLost,
            Func<StoreReading, StoreAct> again,
            out string refused)
        {
            refused = null;
            if (reading == null || reading.InForce == null)
            {
                refused = "the store holds no key to write under";
                return null;
            }

            // A lift alone is composed again; a refusal holds no `lost`, whatever it replaces holds.
            var lift = again != null;
            var inForce = reading.InForce;
            var objectText = RenewalText.ObjectOf(client);
            var shown = new HashSet<string>(shownStanding ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var replaced = inForce.VersionsOf(objectText).Where(version => shown.Contains(version.Id)).ToList();
            var key = (byte[])inForce.Key.Bytes.Clone();
            try
            {
                var place = Place(key, objectText);
                var after = OfObject(inForce, objectText, place, replaced.SelectMany(version => new[] { version.Id }.Concat(Listed(version.After))));
                var lost = (IReadOnlyList<string>)Array.Empty<string>();
                if (lift)
                {
                    var lostNow = new HashSet<string>(inForce.LostOf(objectText), StringComparer.Ordinal);
                    var settling = (shownLost ?? Enumerable.Empty<string>()).Where(lostNow.Contains)
                        .Concat(replaced.SelectMany(version => Listed(version.Lost)));
                    var replacing = new HashSet<string>(after, StringComparer.Ordinal);
                    lost = OfObject(inForce, objectText, place, settling.Where(id => !replacing.Contains(id)));
                }

                // A lift that names nothing was shown only versions replaced since.
                if (lift && after.Count == 0 && lost.Count == 0)
                {
                    return null;
                }

                var members = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.ObjectMember, objectText },
                    { StoreKinds.After, string.Join(" ", after) },
                    { StoreKinds.Doc, doc },
                };
                if (lost.Count > 0)
                {
                    members[StoreKinds.Lost] = string.Join(" ", lost);
                }

                var record = StoreRecord.Compose(key, StoreKinds.Version, members);
                if (!Fits(record))
                {
                    refused = "the answer names more versions than a record of the store holds";
                    return null;
                }

                return new StoreAct(name, key, inForce.Key.Name, new[] { record }, null, again);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// These identifiers, each once and ascending, less every identifier the reading carries
        /// as a version of another object or as a record of another kind, and every identifier
        /// that does not begin with the place of a version of this object under the key.
        /// </summary>
        private static IReadOnlyList<string> OfObject(StoreInForce inForce, string objectText, string place, IEnumerable<string> identifiers)
        {
            return identifiers
                .Where(id => id != null && id.Length == IdentifierLength)
                .Distinct(StringComparer.Ordinal)
                .Where(id =>
                {
                    StoreNaming carried;
                    if (inForce.Versions.TryGetValue(id, out carried))
                    {
                        return string.Equals(carried.Object, objectText, StringComparison.Ordinal);
                    }

                    return !inForce.Others.Contains(id) && id.StartsWith(place, StringComparison.Ordinal);
                })
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The first bytes of the identifier of every version of this object under the key, as
        /// hexadecimal: the tag of `version`, the subject hash of the whole object, and 0,
        /// `STORE.md` §609 and §618.
        /// </summary>
        private static string Place(byte[] key, string objectText)
        {
            return Hex.Of(StoreFormat.Tag(key, StoreKinds.Version))
                   + Hex.Of(StoreFormat.Subject(key, objectText))
                   + 0.ToString("x4", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether a version's content and the item that restates it each fit the ceiling of a
        /// string: the item is the content with the member `nonce` giving way to `id` and `kind`,
        /// none of whose names and values needs an escape, as for a rule's version.
        /// </summary>
        private static bool Fits(StoreRecord record)
        {
            if (record.Strings.Values.Any(value => value.Length > StoreFormat.MaxWritten))
            {
                return false;
            }

            var item = record.Content.Length
                       - Written(StoreKinds.Nonce, record.Member(StoreKinds.Nonce))
                       + Written(StoreKinds.Id, record.Hex)
                       + Written(StoreKinds.ItemKind, StoreKinds.Version);
            return item <= StoreFormat.MaxWritten;
        }

        /// <summary>The characters a member needing no escape takes in a flat object, with its comma.</summary>
        private static int Written(string name, string value)
        {
            return name.Length + value.Length + "\"\":\"\",".Length;
        }

        /// <summary>The identifiers a list holds, none for a list that is empty or absent.</summary>
        private static IEnumerable<string> Listed(string list)
        {
            return string.IsNullOrEmpty(list) ? Enumerable.Empty<string>() : list.Split(' ');
        }
    }
}
