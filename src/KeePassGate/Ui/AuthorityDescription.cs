using System;
using System.Collections.Generic;
using System.Globalization;
using KeePassGate.Authority;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the KPG tab says about an authority, and which buttons that leaves available. Separate
    /// from the panel because wording is worth testing and a form is not worth testing it through.
    /// </summary>
    public sealed class AuthorityDescription
    {
        /// <summary>
        /// How long before an expiry is worth mentioning. `CA.md` §219: ninety days for a
        /// host or the authority, thirty for a client.
        /// </summary>
        public static readonly TimeSpan WarningWindow = TimeSpan.FromDays(90);

        public const string NoDatabaseHeadline = "No database is open on this dialog.";

        public const string AbsentHeadline =
            "This database has no authority yet, so nothing can pair with it.";

        public const string PresentHeadline =
            "This database is its own certificate authority.";

        public const string DamagedHeadline =
            "This database holds an authority that cannot be used.";

        private readonly string _headline;
        private readonly List<Fact> _facts;
        private readonly List<string> _details;
        private readonly bool _canCreate;
        private readonly bool _canRecreate;
        private readonly bool _canDelete;
        private readonly bool _isServed;

        private AuthorityDescription(
            string headline,
            IEnumerable<Fact> facts,
            IEnumerable<string> details,
            bool canCreate,
            bool canRecreate,
            bool canDelete)
            : this(headline, facts, details, canCreate, canRecreate, canDelete, false)
        {
        }

        private AuthorityDescription(
            string headline,
            IEnumerable<Fact> facts,
            IEnumerable<string> details,
            bool canCreate,
            bool canRecreate,
            bool canDelete,
            bool isServed)
        {
            _headline = headline;
            _facts = facts == null ? new List<Fact>() : new List<Fact>(facts);
            _details = details == null ? new List<string>() : new List<string>(details);
            _canCreate = canCreate;
            _canRecreate = canRecreate;
            _canDelete = canDelete;
            _isServed = isServed;
        }

        public string Headline
        {
            get { return _headline; }
        }

        /// <summary>
        /// What this authority is, as a table: a name and a value each, in the order they should be
        /// read. Empty when there is no authority to describe.
        /// </summary>
        public IReadOnlyList<Fact> Facts
        {
            get { return _facts; }
        }

        /// <summary>
        /// What is wrong or about to be, one sentence each. Separate from the facts because these
        /// are read rather than scanned, and because most of the time there are none.
        /// </summary>
        public IReadOnlyList<string> Details
        {
            get { return _details; }
        }

        public bool CanCreate
        {
            get { return _canCreate; }
        }

        public bool CanRecreate
        {
            get { return _canRecreate; }
        }

        public bool CanDelete
        {
            get { return _canDelete; }
        }

        /// <summary>
        /// Whether the gate would serve this database as it stands.
        /// </summary>
        /// <remarks>
        /// True for a whole authority and for nothing else, which is the same test
        /// `OpenDatabaseTargets` applies before it builds a target. Separate from the buttons on
        /// purpose: `CanRecreate` is true for a damaged authority as well, so a line that asked it
        /// this question told the operator a database was reachable that nothing could reach.
        /// </remarks>
        public bool IsServed
        {
            get { return _isServed; }
        }

        public static AuthorityDescription NoDatabase()
        {
            return new AuthorityDescription(NoDatabaseHeadline, null, null, false, false, false);
        }

        public static AuthorityDescription Of(AuthorityReading reading, DateTimeOffset now)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            switch (reading.Presence)
            {
                case AuthorityPresence.None:
                    // Delete is offered when records outlived the authority that admitted
                    // them, which happens after a merge or a partial restore. They can never
                    // validate again, and with the button disabled the only way to be rid of
                    // them was to create an authority and immediately recreate it.
                    return new AuthorityDescription(
                        AbsentHeadline, null, RecordsOnly(reading), true, false,
                        reading.ClientRecordCount > 0);

                case AuthorityPresence.Damaged:
                    return new AuthorityDescription(
                        DamagedHeadline, null, Damage(reading), false, true, true);

                default:
                    return new AuthorityDescription(
                        PresentHeadline, FactsOf(reading), Warnings(reading, now), false, true, true, true);
            }
        }

        private static IEnumerable<string> RecordsOnly(AuthorityReading reading)
        {
            var lines = new List<string>();
            if (reading.ClientRecordCount > 0)
            {
                lines.Add(Records(reading.ClientRecordCount) +
                          " left without an authority to make sense of them, so every one of them is inert.");
            }

            return lines;
        }

        private static IEnumerable<string> Damage(AuthorityReading reading)
        {
            var lines = new List<string>(reading.Problems);
            lines.Add("Nothing is served while this is true. Recreating replaces it, and every client pairs again.");
            if (reading.ClientRecordCount > 0)
            {
                lines.Add(Records(reading.ClientRecordCount) + " would be removed with it.");
            }

            return lines;
        }

        /// <summary>
        /// What there is to know, one name and one value at a time. A reader asking when this stops
        /// working should be answered by looking rather than by parsing a sentence.
        /// </summary>
        private static IEnumerable<Fact> FactsOf(AuthorityReading reading)
        {
            var material = reading.Material;

            // Four rows rather than five. The tab is 449 by 314 and every row costs the same
            // seventeen pixels, so the table has to say which facts are worth one. The date the
            // authority was created is the one that is not: it answers no question an operator
            // opens this tab with, while the fingerprint says which authority this is and the two
            // expiry dates say how long it keeps working.
            return new List<Fact>
            {
                new Fact("Fingerprint", CertificateDigest.Shorten(material.AuthorityDigest)),
                new Fact("Authority valid until", Stamp(material.Authority.NotAfter)),
                new Fact("Host valid until", Stamp(material.Host.NotAfter)),
                new Fact("Admission records", Count(reading.ClientRecordCount))
            };
        }

        private static IEnumerable<string> Warnings(AuthorityReading reading, DateTimeOffset now)
        {
            var material = reading.Material;

            var warnings = new List<string>();
            Expiry(warnings, "Authority", material.Authority.NotAfter, now);
            Expiry(warnings, "Host certificate", material.Host.NotAfter, now);

            if (warnings.Count == 0)
            {
                return warnings;
            }

            // One line however many there are, because this is the line that has to be read and
            // separate sentences are a paragraph nobody reads.
            return new List<string> { string.Join(" ", warnings.ToArray()) };
        }

        private static void Expiry(ICollection<string> lines, string what, DateTime notAfter, DateTimeOffset now)
        {
            var expires = new DateTimeOffset(notAfter.ToUniversalTime(), TimeSpan.Zero);
            if (expires <= now)
            {
                lines.Add(what + " expired on " + Stamp(notAfter) + ".");
                return;
            }

            var left = expires - now;
            if (left <= WarningWindow)
            {
                var days = (int)Math.Ceiling(left.TotalDays);
                lines.Add(what + " expires in " + days + (days == 1 ? " day." : " days."));
            }
        }

        /// <summary>
        /// The longest this line can be, so that the tab is measured against what it will ever
        /// have to show rather than against what it happens to show today.
        /// </summary>
        /// <remarks>
        /// A damaged reading, not an expiring one. The expiry warnings are one sentence and
        /// this used to return them, which measured the tab against a state strictly shorter
        /// than the one it actually has to survive: `AuthorityStore` reports damage a problem
        /// at a time, and they are shown joined.
        ///
        /// Kept in step by hand, because the problems are composed where they are found. If a
        /// longer one is added there, it belongs here too, and no test notices when it is not.
        /// </remarks>
        public static string LongestDetail()
        {
            return string.Join(" ", new[]
            {
                "The host certificate could not be checked against this authority on this " +
                "machine. Nothing here says the authority is wrong; recreating it would not help.",
                "Authority expired on 2036-08-12 09:41:07.",
                "Host certificate expires in 88 days."
            });
        }

        /// <summary>The widest the table can be, for the same reason.</summary>
        public static IReadOnlyList<Fact> LongestFacts()
        {
            return new List<Fact>
            {
                new Fact("Fingerprint", "0123456789abcdef"),
                new Fact("Authority valid until", "2036-08-12 09:41:07"),
                new Fact("Host valid until", "2031-08-14 09:41:07"),
                new Fact("Admission records", "16")
            };
        }

        /// <summary>The count on its own, for the table, where the noun is the column heading.</summary>
        private static string Count(int count)
        {
            return count.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The count with its noun, for the sentences, where there is no heading.</summary>
        private static string Records(int count)
        {
            return count == 1 ? "One admission record" : count + " admission records";
        }

        /// <summary>
        /// A moment, to the second, in UTC.
        /// </summary>
        /// <remarks>
        /// The time is there because a date on its own is ambiguous about the thing an operator
        /// uses these for: a certificate that expires today has either already stopped working or
        /// has hours left, and the date cannot say which. Written the same way in every language,
        /// which a test pins by switching culture, and in UTC because these are certificate dates
        /// and a certificate has no local time.
        /// </remarks>
        private static string Stamp(DateTime moment)
        {
            return Moments.OnScreen(moment);
        }
    }
}
