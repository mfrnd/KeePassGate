using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Acl
{
    /// <summary>
    /// The profiles a database knows about, as its store keeps them, `STORE.md` §616: the names an
    /// operator may grant, and a client may be approved for.
    /// </summary>
    /// <remarks>
    /// **A profile is a name on this list, and nothing else makes a profile.** What a name reaches
    /// is on the nodes, and who holds it is in the admissions. Three places, three questions, none
    /// of them duplicated.
    ///
    /// **A name is on the list by its records.** An addition puts it there where it names every
    /// removal of that name the reading holds, and a removal takes it off every addition that had
    /// not seen it. The list is worked out from the reading of each hold and kept nowhere, so the
    /// gate, the approval dialogs, the pages of the KPG tab and the grant editor all read the same
    /// list from the same records.
    ///
    /// **Nothing is granted by this list, and nothing is granted without it.** A client holds the
    /// names its admission carries that are also here, so taking a name off the list takes it from
    /// every client at once and adding it back returns it, with no admission and no rule touched.
    ///
    /// **Total.** A name is any text, compared by ordinal; a record whose name could travel as no
    /// claim puts nothing on the list and stops nothing, and an `after` out of form has already
    /// failed its version in the core. Nothing a store holds makes this throw.
    /// </remarks>
    public sealed class ProfileNames
    {
        private readonly IReadOnlyList<string> _names;
        private readonly HashSet<string> _held;

        private ProfileNames(IEnumerable<string> names)
        {
            _held = new HashSet<string>(names, StringComparer.Ordinal);
            _names = _held.OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The names on the list of this reading, `STORE.md` §616: the name of each addition in
        /// force that names every removal of that name the reading holds, where that name is a
        /// name a claim can carry. None where the reading holds nothing in force.
        /// </summary>
        /// <remarks>
        /// **Linear in the additions and in what each names**, with the removals of each name
        /// worked out as the reading was made, `StoreInForce.RemovalsOf`, so that a hold asks no
        /// index of its own. A list has no ceiling: what copies that each added names bring when
        /// they meet is read whole.
        /// </remarks>
        public static ProfileNames Of(StoreReading reading)
        {
            var inForce = reading == null ? null : reading.InForce;
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (inForce == null)
            {
                return new ProfileNames(names);
            }

            foreach (var addition in inForce.Additions.Where(addition => addition.InForce))
            {
                var name = addition.Form.Member(StoreKinds.NameMember);
                if (names.Contains(name) || !AuthorityIssuer.IsUsableProfileName(name))
                {
                    continue;
                }

                var removals = inForce.RemovalsOf(name);
                if (removals.Count > 0)
                {
                    var seen = new HashSet<string>(
                        StoreFormat.Identifiers(addition.Form.Member(StoreKinds.After)) ?? Array.Empty<string>(), StringComparer.Ordinal);
                    if (!removals.All(seen.Contains))
                    {
                        continue;
                    }
                }

                names.Add(name);
            }

            return new ProfileNames(names);
        }

        /// <summary>Every name, in ordinal order, because a list somebody reads is a list in an order.</summary>
        public IReadOnlyList<string> Names
        {
            get { return _names; }
        }

        /// <summary>Whether a name is on the list, compared exactly.</summary>
        public bool Holds(string name)
        {
            return name != null && _held.Contains(name);
        }

        /// <summary>Why a text cannot be a profile name, or null where it can.</summary>
        /// <remarks>
        /// The same rule the issuer applies, and only that rule, because a profile that cannot
        /// travel as a claim is a profile no client can ever hold: it would be granted here and
        /// refused at the certificate. That includes `default`, which the issuer already refuses
        /// in any case, so there is no second reservation written here to disagree with it.
        /// </remarks>
        public static string Refusal(string name)
        {
            return AuthorityIssuer.IsUsableProfileName(name)
                ? null
                : "A profile name is ASCII letters, digits, hyphens and underscores, up to 64 of them, and not `default` in any case.";
        }
    }
}
