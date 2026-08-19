using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        /// <summary>A client is a smaller thing to reissue, so it is mentioned later.</summary>
        public static readonly TimeSpan ClientWarningWindow = TimeSpan.FromDays(30);

        public const string NoDatabaseHeadline = "No database is open on this dialog.";

        public const string AbsentHeadline =
            "This database has no authority yet, so nothing can pair with it.";

        public const string PresentHeadline =
            "This database is its own certificate authority.";

        public const string DamagedHeadline =
            "This database holds an authority that cannot be used.";

        public const string UndecidedHeadline =
            "This database holds more than one authority, so nothing is served.";

        private readonly string _headline;
        private readonly List<Fact> _facts;
        private readonly List<string> _details;
        private readonly bool _canCreate;
        private readonly bool _canRecreate;
        private readonly bool _canDelete;
        private readonly bool _isServed;
        private readonly bool _hasExpired;
        private readonly bool _hostHasExpired;

        private AuthorityDescription(
            string headline,
            IEnumerable<Fact> facts,
            IEnumerable<string> details,
            bool canCreate,
            bool canRecreate,
            bool canDelete)
            : this(headline, facts, details, canCreate, canRecreate, canDelete, false, false, false)
        {
        }

        private AuthorityDescription(
            string headline,
            IEnumerable<Fact> facts,
            IEnumerable<string> details,
            bool canCreate,
            bool canRecreate,
            bool canDelete,
            bool isServed,
            bool hasExpired,
            bool hostHasExpired)
        {
            _headline = headline;
            _facts = facts == null ? new List<Fact>() : new List<Fact>(facts);
            _details = details == null ? new List<string>() : new List<string>(details);
            _canCreate = canCreate;
            _canRecreate = canRecreate;
            _canDelete = canDelete;
            _isServed = isServed;
            _hasExpired = hasExpired;
            _hostHasExpired = hostHasExpired;
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

        /// <summary>The details as the page shows them: a line each.</summary>
        /// <remarks>
        /// Joined here rather than where they are drawn, so that what the tab is measured against
        /// is joined the same way. It was not: the longest detail was one paragraph joined with
        /// spaces, and a paragraph wraps to fewer rows than the same sentences a line each.
        /// </remarks>
        public string DetailText
        {
            get { return string.Join(Environment.NewLine, _details); }
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
        /// Whether there are several authorities to choose between, which is the one state the
        /// page offers the chooser in, in the place Create has.
        /// </summary>
        public bool CanChoose { get; private set; }

        /// <summary>
        /// Whether the authority's own dates have passed, which is a state an operator can only
        /// leave by replacing it.
        /// </summary>
        /// <remarks>
        /// False where there is nothing to ask about, so this is never the reason an absent or a
        /// damaged authority cannot be invited to. Those two have reasons of their own and the
        /// page already gives them.
        /// </remarks>
        public bool HasExpired
        {
            get { return _hasExpired; }
        }

        /// <summary>
        /// Whether the host certificate this database answers with has run out, which it does
        /// five years into the authority's ten.
        /// </summary>
        /// <remarks>
        /// Apart from <see cref="HasExpired"/> because it is a different date, and the page says
        /// which one passed. What it costs is the same: every connection is served with this
        /// certificate, so a client paired now could never connect, and `CA.md` §219 has no
        /// reissue, so recreating the authority is the way out here too.
        /// </remarks>
        public bool HostHasExpired
        {
            get { return _hostHasExpired; }
        }

        /// <summary>
        /// Whether a code minted now could ever be redeemed, which is a narrower question than
        /// any of the three buttons on the first page answer.
        /// </summary>
        /// <remarks>
        /// Narrower in both directions that matter. A damaged authority and one past its own
        /// dates are both authorities recreating is the answer to, and neither can sign anything,
        /// so the button that offers to recreate has to be alive in exactly the two states where
        /// the button that hands out a code must not be. A host certificate past its dates is a
        /// third such state. `PAIRING.md` §311 refuses a pairing request on an expired authority
        /// or host certificate before the operator is disturbed.
        /// </remarks>
        public bool CanInvite
        {
            get { return _isServed && !_hasExpired && !_hostHasExpired; }
        }

        public static AuthorityDescription NoDatabase()
        {
            return new AuthorityDescription(NoDatabaseHeadline, null, null, false, false, false);
        }

        /// <summary>
        /// What to say about this authority, given the records it admits and how far each gets
        /// with the gate before its dates.
        /// </summary>
        /// <remarks>
        /// The records are a parameter rather than an overload that defaults them away. One existed
        /// and every caller that took it silently lost the client expiry warnings, which is the
        /// failure this class was given records to prevent: a client stops being admitted one
        /// morning with nothing having said so. Null still means "not asked for", and now a caller
        /// has to write it down. The same goes for the reaches, where null counts every record.
        /// </remarks>
        /// <param name="reaches">
        /// What the gate asks before a record's dates, by admission key, as the clients page is
        /// told it: a record the gate refuses before its dates is left out of the expiry
        /// warnings, which exist to say that a client is about to stop being admitted.
        /// </param>
        public static AuthorityDescription Of(
            AuthorityReading reading,
            AdmissionListing clients,
            IDictionary<string, ClientReach> reaches,
            DateTimeOffset now)
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

                case AuthorityPresence.Undecided:
                    // Recreating and deleting both still mean what they say: every one of the
                    // candidates is retired either way, so both are offered. Creating is not,
                    // because there is no absence to fill.
                    return new AuthorityDescription(
                        UndecidedHeadline, null, Undecided(reading), false, true, true)
                    {
                        CanChoose = true
                    };

                default:
                    return new AuthorityDescription(
                        PresentHeadline, FactsOf(reading), Warnings(reading, clients, reaches, now),
                        false, true, true, true,
                        CertificateValidity.HasExpired(reading.Material.Authority, now),
                        CertificateValidity.HasExpired(reading.Material.Host, now));
            }
        }

        /// <summary>
        /// What is true of a database holding several authorities, and what to do about it.
        /// </summary>
        /// <remarks>
        /// Why before what, because the state is surprising and nothing the operator did on this
        /// machine explains it: two copies of the file each made an authority of their own, and a
        /// merge brought both back. What it costs is that nothing is served, which is the part a
        /// client somewhere is already finding out.
        /// </remarks>
        private static IEnumerable<string> Undecided(AuthorityReading reading)
        {
            return new List<string> { UndecidedLine(reading.Candidates.Count) };
        }

        private static string UndecidedLine(int candidates)
        {
            return candidates.ToString(CultureInfo.InvariantCulture) +
                   " authorities were made separately in copies of this database that were later " +
                   "merged. Nothing is served until one of them is kept: choose it below.";
        }

        private static IEnumerable<string> RecordsOnly(AuthorityReading reading)
        {
            var lines = new List<string>();
            if (reading.ClientRecordCount > 0)
            {
                lines.Add(RecordsOnlyLine(reading.ClientRecordCount));
            }

            return lines;
        }

        private static string RecordsOnlyLine(int records)
        {
            return Records(records) +
                   " left without an authority to make sense of them, so every one of them is inert.";
        }

        /// <summary>
        /// What is wrong with an authority that will not read, and what that costs.
        /// </summary>
        /// <remarks>
        /// **The problems are cut, because two of them carry somebody else's exception message.**
        /// `AuthorityStore.Read` and `AuthorityService.Read` both compose one as
        /// `The authority could not be read: ` followed by whatever threw, and the length of that
        /// is not this plugin's to choose. The detail label has `AutoScroll` off like everything
        /// else on the tab, so an untrimmed one grew the page past it and pushed the three buttons
        /// out of reach with no way to scroll to them. The whole message is in the log.
        ///
        /// The two lines added under them are this plugin's own words at a length it chose, so
        /// they are not cut, and <see cref="LongestDescriptions"/> holds them whole by asking this
        /// for them.
        /// </remarks>
        private static IEnumerable<string> Damage(AuthorityReading reading)
        {
            var lines = new List<string>();
            foreach (var problem in reading.Problems)
            {
                lines.Add(Clipped.Message(problem));
            }

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

        private static IEnumerable<string> Warnings(
            AuthorityReading reading,
            AdmissionListing clients,
            IDictionary<string, ClientReach> reaches,
            DateTimeOffset now)
        {
            var material = reading.Material;

            var warnings = new List<string>();
            Expiry(warnings, "Authority", material.Authority.NotAfter, now);
            Expiry(warnings, "Host certificate", material.Host.NotAfter, now);
            Clients(warnings, clients == null ? null : clients.Records, reaches, now);
            Unreadable(warnings, clients, reading.ClientRecordCount);

            return OneLine(warnings);
        }

        /// <summary>
        /// One line however many there are, because this is the line that has to be read and
        /// separate sentences are a paragraph nobody reads.
        /// </summary>
        private static List<string> OneLine(List<string> warnings)
        {
            if (warnings.Count == 0)
            {
                return warnings;
            }

            return new List<string> { string.Join(" ", warnings.ToArray()) };
        }

        private static void Expiry(ICollection<string> lines, string what, DateTime notAfter, DateTimeOffset now)
        {
            var expires = new DateTimeOffset(notAfter.ToUniversalTime(), TimeSpan.Zero);
            if (CertificateValidity.HasExpired(expires, now))
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
        /// The longest the page can be under each headline, so that the tab is measured against
        /// what it will ever have to show rather than against what it happens to show today.
        /// </summary>
        /// <remarks>
        /// **Composed by what composes the page, and joined the way the page joins it.** This was
        /// one paragraph written out by hand, with a remark saying `GateTabFitsTests` kept it in
        /// step, which that test cannot do: it measures what it is given, and cut to six characters
        /// every test stayed green. It held neither of the two lines `Damage` adds, mixed damage
        /// with warnings that are never on screen with it, and was joined with spaces where the
        /// page puts each detail on a line of its own. Now each is what the page's own composer
        /// says for the longest input it can be given, and <see cref="DetailText"/> joins it.
        ///
        /// **What is still written down is which inputs can arrive together**, each beside the
        /// code that decides it: `AuthorityStore.LongestProblems` for the problems one authority
        /// can be reported with at once, and here for the warnings, where a listing that could
        /// not be made is said in place of the records it could not read, so the longer of those
        /// two is taken rather than both.
        ///
        /// **The problem that ends in an exception's message is a cut one**, which is what bounds
        /// a damaged page at all: `Damage` cuts it at `Clipped.Limit`, and the message given here
        /// is long enough to be cut, in words of ordinary length so that it wraps the way a real
        /// one does.
        /// </remarks>
        public static IReadOnlyList<AuthorityDescription> LongestDescriptions()
        {
            var most = Counts.Max();

            List<string> damage;
            using (var damaged = AuthorityReading.Damaged(AuthorityStore.LongestProblems(Thrown), most, true))
            {
                damage = Damage(damaged).ToList();
            }

            // Expired rather than expiring, because a date is wider than a count of days, and
            // every date is written as wide as every other.
            var date = new DateTime(2036, 8, 12, 9, 41, 7, DateTimeKind.Utc);
            var after = new DateTimeOffset(date).AddSeconds(1);

            var warnings = new List<string>();
            Expiry(warnings, "Authority", date, after);
            Expiry(warnings, "Host certificate", date, after);
            warnings.Add(Longest(Expired));
            warnings.Add(Longest(Expiring));
            warnings.Add(new[] { Longest(Unreadable), CouldNotList }.OrderByDescending(line => line.Length).First());

            return new List<AuthorityDescription>
            {
                new AuthorityDescription(DamagedHeadline, null, damage, false, true, true),
                new AuthorityDescription(PresentHeadline, LongestFacts(), OneLine(warnings), false, true, true),
                new AuthorityDescription(UndecidedHeadline, null, new[] { Longest(UndecidedLine) }, false, true, true)
                {
                    CanChoose = true
                },
                new AuthorityDescription(AbsentHeadline, null, new[] { Longest(RecordsOnlyLine) }, true, false, true)
            };
        }

        /// <summary>What stands in for an exception's message where the longest page is composed.</summary>
        private const string Thrown =
            "the key set for this certificate is not available on this machine, and the profile it " +
            "would have been read from could not be loaded either. Ask whoever administers this computer.";

        /// <summary>
        /// What is about to stop working. A client that expires is not a failure anybody sees
        /// coming otherwise: it simply stops being admitted one morning.
        /// </summary>
        /// <remarks>
        /// **Only records the gate would get as far as the dates of.** One from another
        /// authority, one issued for something other than a client, and one not valid yet are
        /// refused before their end matters, and the clients page names them so; warning that one
        /// of them expires within the month had the operator looking to pair again a program the
        /// other page said had never reached the database at all.
        /// </remarks>
        private static void Clients(
            ICollection<string> lines,
            IReadOnlyList<AdmissionRecord> records,
            IDictionary<string, ClientReach> reaches,
            DateTimeOffset now)
        {
            if (records == null || records.Count == 0)
            {
                return;
            }

            var expired = 0;
            var soon = 0;

            foreach (var record in records)
            {
                ClientReach reach;
                if (reaches != null && reaches.TryGetValue(record.AdmissionKey, out reach)
                    && reach != ClientReach.Reaches)
                {
                    continue;
                }

                using (var certificate = record.Certificate())
                {
                    // Through `CertificateValidity`, so the tab cannot call a client expired at an
                    // instant the gate is still serving it.
                    if (CertificateValidity.HasExpired(certificate, now))
                    {
                        expired++;
                    }
                    else if (CertificateValidity.RemainingOn(certificate, now) <= ClientWarningWindow)
                    {
                        soon++;
                    }
                }
            }

            // Short, because these share one line with the authority's own expiries and that line
            // is the tallest thing the tab ever has to fit. An expired client is refused, which is
            // what expired means here and does not need saying twice.
            if (expired > 0)
            {
                lines.Add(Expired(expired));
            }

            if (soon > 0)
            {
                lines.Add(Expiring(soon));
            }
        }

        /// <summary>
        /// The records the table counts and the warnings could not look at.
        /// </summary>
        /// <remarks>
        /// Without this the tab counts one thing and warns about another: the fact is every stored
        /// key, and only the readable ones can be checked for expiry, so a damaged record made the
        /// two numbers disagree with nothing on screen to explain it. The client it belonged to is
        /// refused, which is the safe half, and this is the half an operator can act on: the
        /// sentence says where, because the clients page lists such a record by its key, with an
        /// X that takes it away, where it used to be listed nowhere and the one way to be rid of it
        /// on this tab was to recreate the authority and have every client pair again.
        /// </remarks>
        private static void Unreadable(
            ICollection<string> lines, AdmissionListing clients, int stored)
        {
            if (clients == null)
            {
                return;
            }

            if (!clients.Complete)
            {
                // **The difference is not damage when the listing could not be made.** The count
                // and the records are two reads of the same dictionary; if the one that produced
                // the records failed, every stored record looks unreadable, and the tab would tell
                // the operator that intact records admit nobody. That points at Recreate, which
                // destroys the authority and every record with it.
                lines.Add(CouldNotList);
                return;
            }

            if (clients.Records.Count >= stored)
            {
                return;
            }

            lines.Add(Unreadable(stored - clients.Records.Count));
        }

        /// <summary>What is said when the admission records could not be listed at all.</summary>
        private const string CouldNotList =
            "The admission records could not be read, so this says nothing about who is admitted.";

        private static string Expired(int clients)
        {
            return clients == 1 ? "One client has expired." : clients + " clients have expired.";
        }

        private static string Expiring(int clients)
        {
            var days = ((int)ClientWarningWindow.TotalDays).ToString(CultureInfo.InvariantCulture);
            return (clients == 1 ? "One client expires within " : clients + " clients expire within ")
                   + days + " days.";
        }

        /// <summary>
        /// The longest a counted line can be, which is not the line for any one count.
        /// `Expiring(1)` spells "One client" and is a character longer than `Expiring(16)`; a
        /// fourth digit is longer again. Measuring one representative count therefore
        /// under-measures twice, on a tab whose whole contract is that it never scrolls.
        /// </summary>
        private static readonly int[] Counts = { 1, 16, 9999 };

        private static string Longest(Func<int, string> line)
        {
            return Counts.Select(line).OrderByDescending(text => text.Length).First();
        }

        private static string Unreadable(int records)
        {
            return records == 1
                ? Records(records) + " cannot be read and admits nobody. The Clients page lists it."
                : Records(records) + " cannot be read and admit nobody. The Clients page lists them.";
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
