using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Who a database's store admits, `STORE.md` §615: the admissions, read by their meaning from
    /// what is in force under the store key.
    /// </summary>
    /// <remarks>
    /// **Read from the reading of the hold, and kept nowhere**, `STORE.md` §620: the gate asks it
    /// once for each connection, from the reading every question of that hold is answered from,
    /// and a decision that is in the gate's copy when the hold begins holds in that hold.
    ///
    /// **A certificate is admitted** by a single admission in force that reads, whose client key
    /// no revocation names, whose certificate was not taken away, whose issuer no retirement names
    /// and is the authority served, in a store that is whole, and whose chain of origins passes
    /// the rules of `STORE.md` §648. **What does not read admits nobody**, and is counted among no
    /// admissions of a certificate, and neither is an admission a decision refuses (S24).
    ///
    /// **Total, and no certificate is parsed but those asked about**: a hold parses the admission
    /// of the certificate on the connection and the admissions of origin its chain leads to, and a
    /// listing every admission's, for a page or a dialog.
    /// </remarks>
    internal static class StoreAdmissions
    {
        /// <summary>
        /// The admission of this certificate where it counts, else null: no admission, several,
        /// an admission refused by a decision, an admission under an authority that is not served,
        /// or a renewal that does not count, `STORE.md` §648.
        /// </summary>
        /// <remarks>
        /// **Every admission in force is compared**, the whole key and in fixed time, so nothing
        /// tells how much of a key a certificate somebody else chose matched.
        /// </remarks>
        public static AdmissionRecord Admitted(StoreReading reading, string admissionKey)
        {
            if (reading == null || reading.InForce == null || string.IsNullOrEmpty(admissionKey))
            {
                return null;
            }

            var served = StoreAuthorities.Served(reading);
            if (served == null)
            {
                return null;
            }

            AdmissionRecord found = null;
            var count = 0;
            foreach (var record in reading.InForce.Admissions.Where(record => record.InForce))
            {
                var certificate = record.Form.Member(StoreKinds.Certificate);
                if (!FixedTime.Equals(certificate ?? string.Empty, admissionKey))
                {
                    continue;
                }

                // A record that does not read is no admission anywhere, and an admission that a
                // decision refuses is not counted among several, so the answer is the same before
                // the write that deletes it and after, `STORE.md` §615 and §627.
                var admission = AdmissionRecord.Read(record.Hex, record.Form.Members);
                if (admission == null || Refused(reading.InForce, admission))
                {
                    continue;
                }

                count++;
                found = admission;
            }

            if (count != 1 || !string.Equals(found.Issuer, served, StringComparison.Ordinal))
            {
                return null;
            }

            // Given the admission read here, which is the single admission of its certificate that
            // counts, so the chain does not read it again.
            var origins = new Origins(reading.InForce, served);
            origins.Know(found.AdmissionKey, found);
            return origins.Pass(found.AdmissionKey) ? found : null;
        }

        /// <summary>
        /// Every admission in force that reads and that no decision refuses, newest first, each
        /// with what it comes to; the records in force that do not read; and how many clients a
        /// decision refuses whose admissions wait to be deleted.
        /// </summary>
        /// <remarks>
        /// **A store that cannot be judged lists nobody and reports it could not**: under several
        /// keys, a form the plugin does not read, past the ceiling on keys. A database whose file
        /// holds no store key lists nobody and states so, since nobody can be admitted there.
        /// </remarks>
        public static AdmissionListing Listing(StoreReading reading)
        {
            return Listing(reading, reading == null ? null : StoreAuthorities.Served(reading));
        }

        /// <summary>
        /// The same, as it would stand were this authority the authority served: for the choice
        /// between several authorities, where the clients of a candidate are the admissions that
        /// would count were it kept, `STORE.md` §614.
        /// </summary>
        public static AdmissionListing Listing(StoreReading reading, string served)
        {
            if (reading == null)
            {
                return new AdmissionListing(Array.Empty<AdmissionRecord>(), false);
            }

            var inForce = reading.InForce;
            if (inForce == null)
            {
                return new AdmissionListing(Array.Empty<AdmissionRecord>(), reading.State == StoreState.None);
            }

            var read = new List<AdmissionRecord>();
            var unreadable = new List<UnreadAdmission>();
            var waiting = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in inForce.Admissions.Where(record => record.InForce))
            {
                var admission = AdmissionRecord.Read(record.Hex, record.Form.Members);
                if (admission == null)
                {
                    var client = record.Form.Member(StoreKinds.ClientMember);
                    var certificate = record.Form.Member(StoreKinds.Certificate);
                    if (RefusedByText(inForce, client, certificate, record.Form.Member(StoreKinds.Issuer)))
                    {
                        waiting.Add(CertificateDigest.IsDigest(client) ? client : record.Hex);
                        continue;
                    }

                    unreadable.Add(new UnreadAdmission(record.Hex, client, certificate));
                    continue;
                }

                if (Refused(inForce, admission))
                {
                    waiting.Add(admission.ClientKey);
                    continue;
                }

                read.Add(admission);
            }

            var named = read.GroupBy(admission => admission.AdmissionKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var standing = new Dictionary<AdmissionRecord, AdmissionStanding>();

            // A single reading of the chains for the whole listing, so that each certificate along
            // them is worked out once however many renewals lead to it, given what was read above:
            // of each certificate, its single admission that counts, or none.
            var origins = new Origins(inForce, served);
            foreach (var admission in read)
            {
                origins.Know(
                    admission.AdmissionKey,
                    named[admission.AdmissionKey] == 1 && string.Equals(admission.Issuer, served, StringComparison.Ordinal)
                        ? admission
                        : null);
            }

            foreach (var admission in read)
            {
                if (named[admission.AdmissionKey] > 1)
                {
                    standing[admission] = AdmissionStanding.Twice;
                }
                else if (!string.Equals(admission.Issuer, served, StringComparison.Ordinal))
                {
                    standing[admission] = AdmissionStanding.NotServed;
                }
                else if (!origins.Pass(admission.AdmissionKey))
                {
                    standing[admission] = AdmissionStanding.Uncounted;
                }
            }

            var ordered = read.OrderByDescending(admission => admission.Admitted)
                .ThenBy(admission => admission.Entry, StringComparer.Ordinal)
                .ToList();
            return new AdmissionListing(ordered, standing, unreadable, waiting.Count, true);
        }

        /// <summary>
        /// The certificates the records of kind `client` in force name, by the texts the core
        /// keeps, with what a write deletes taken out and what it adds put in: what the standing
        /// answers to a renewal are kept for once the write lands, `STORE.md` §618.
        /// </summary>
        public static ISet<string> Named(StoreReading reading, StoreWrite write)
        {
            var named = new HashSet<string>(StringComparer.Ordinal);
            if (reading != null && reading.InForce != null)
            {
                var deleted = new HashSet<string>(write == null ? Enumerable.Empty<string>() : write.Delete, StringComparer.Ordinal);
                named.UnionWith(reading.InForce.Admissions
                    .Where(record => record.InForce && !deleted.Contains(record.Hex))
                    .Select(record => record.Form.Member(StoreKinds.Certificate))
                    .Where(certificate => certificate != null));
            }

            if (write != null)
            {
                named.UnionWith(write.Add
                    .Where(record => record.Kind == StoreKinds.Client)
                    .Select(record => record.Member(StoreKinds.Certificate))
                    .Where(certificate => certificate != null));
            }

            return named;
        }

        /// <summary>
        /// Whether a decision refuses an admission: a revocation of its client key, its certificate
        /// taken away, or a retirement of its issuer, by record, item or deletion record.
        /// </summary>
        private static bool Refused(StoreInForce inForce, AdmissionRecord admission)
        {
            return RefusedByText(inForce, admission.ClientKey, admission.AdmissionKey, admission.Issuer);
        }

        private static bool RefusedByText(StoreInForce inForce, string client, string certificate, string issuer)
        {
            if (client != null && inForce.Taken(StoreKinds.Revoked).Contains(client))
            {
                return true;
            }

            if (certificate != null && inForce.Taken(StoreKinds.Withdrawn).Contains(certificate))
            {
                return true;
            }

            return issuer != null && inForce.Taken(StoreKinds.Retired).Contains(issuer);
        }

        /// <summary>
        /// Whether the chain of origins of a certificate passes, `STORE.md` §648 rules 3 to 5,
        /// asked of a single reading.
        /// </summary>
        /// <remarks>
        /// **The certificates a certificate was renewed from**: for a certificate taken away, the
        /// `from` of every statement that took it away, and the `from` of each admission of it
        /// still in force that reads, each of which has to pass; for any other certificate, the
        /// `from` of its single admission that counts, or none for a pairing's. **A step from a
        /// certificate to its origin passes** where the origin was taken away and every statement
        /// that took it away names the certificate in `renewed`, or where the origin was not taken
        /// away and is admitted by a single admission in force that reads, no decision refuses and
        /// the authority served issued; and the origin's own chain passes in turn. An origin
        /// several admissions name is admitted by none of them (S24), and an origin that is
        /// nowhere is neither admitted nor taken away, so neither passes. A certificate met a
        /// second time along its own chain does not pass.
        ///
        /// **A loop with what it has met, and never a recursion**: a chain grows a step with every
        /// renewal a client makes and is as long as a store can hold, so it is followed by an
        /// explicit stack, each certificate worked out once for as long as this object is kept,
        /// in time linear in the admissions and statements present. A recursion that deep ends
        /// KeePass.
        ///
        /// **Kept for a single question or a single listing, and never longer.** `Admitted` makes
        /// its own for the certificate on the connection, given the admission it read, and lets it
        /// go with its answer; `Listing` makes its own for the whole listing, given every admission
        /// it read. What either was given is not read again.
        /// </remarks>
        private sealed class Origins
        {
            private readonly StoreInForce _inForce;
            private readonly string _served;

            /// <summary>Whether each certificate worked out passes.</summary>
            private readonly Dictionary<string, bool> _passes = new Dictionary<string, bool>(StringComparer.Ordinal);

            /// <summary>
            /// The certificates on the stack: a chain that meets any of them again leads back into
            /// itself.
            /// </summary>
            private readonly HashSet<string> _met = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>
            /// The single admission that counts of each certificate looked at, or null for none.
            /// </summary>
            private readonly Dictionary<string, AdmissionRecord> _single =
                new Dictionary<string, AdmissionRecord>(StringComparer.Ordinal);

            public Origins(StoreInForce inForce, string served)
            {
                _inForce = inForce;
                _served = served;
            }

            /// <summary>
            /// The single admission of this certificate that counts, or null for none, as the
            /// caller already read it, so that the chain does not read it again.
            /// </summary>
            public void Know(string certificate, AdmissionRecord single)
            {
                _single[certificate] = single;
            }

            /// <summary>Whether this certificate's chain of origins passes.</summary>
            public bool Pass(string certificate)
            {
                bool known;
                if (_passes.TryGetValue(certificate, out known))
                {
                    return known;
                }

                var stack = new Stack<Step>();
                Enter(stack, certificate);
                while (stack.Count > 0)
                {
                    var step = stack.Peek();
                    if (step.Failed || step.Next >= step.Origins.Count)
                    {
                        stack.Pop();
                        _met.Remove(step.Certificate);
                        _passes[step.Certificate] = !step.Failed;
                        if (step.Failed && stack.Count > 0)
                        {
                            stack.Peek().Failed = true;
                        }

                        continue;
                    }

                    var origin = step.Origins[step.Next++];
                    if (origin == null)
                    {
                        // A pairing's, which was renewed from nothing.
                        continue;
                    }

                    if (_met.Contains(origin) || !Leads(step.Certificate, origin))
                    {
                        step.Failed = true;
                        continue;
                    }

                    bool passes;
                    if (_passes.TryGetValue(origin, out passes))
                    {
                        step.Failed = !passes;
                        continue;
                    }

                    Enter(stack, origin);
                }

                return _passes[certificate];
            }

            /// <summary>
            /// Puts a certificate on the stack with the certificates it was renewed from.
            /// </summary>
            private void Enter(Stack<Step> stack, string certificate)
            {
                _met.Add(certificate);
                var origins = RenewedFrom(certificate);
                stack.Push(new Step(certificate, origins ?? new List<string>(), origins == null));
            }

            /// <summary>
            /// The certificates this certificate was renewed from, an absent `from` as null, or
            /// null where it is neither taken away nor admitted.
            /// </summary>
            private List<string> RenewedFrom(string certificate)
            {
                var statements = _inForce.StatementsOf(certificate);
                if (statements.Count > 0)
                {
                    // Taken away: what each statement holds, and what each admission of it still
                    // in force holds, as a copy holds it that received the statement by a merge
                    // and has not written since.
                    var origins = statements.Select(statement => statement.From).ToList();
                    origins.AddRange(_inForce.AdmissionsOf(certificate)
                        .Where(record => record.InForce)
                        .Select(record => AdmissionRecord.Read(record.Hex, record.Form.Members))
                        .Where(admission => admission != null)
                        .Select(admission => admission.From));
                    return origins;
                }

                var single = Single(certificate);
                return single == null ? null : new List<string> { single.From };
            }

            /// <summary>
            /// Whether a certificate renewed from this origin may count, before the origin's own
            /// chain is asked.
            /// </summary>
            private bool Leads(string certificate, string origin)
            {
                var statements = _inForce.StatementsOf(origin);
                if (statements.Count > 0)
                {
                    return statements.All(statement => statement.Names(certificate));
                }

                return Single(origin) != null;
            }

            /// <summary>
            /// The single admission of a certificate in force that reads, that no decision refuses
            /// and that the authority served issued, or null where there is none or there are
            /// several.
            /// </summary>
            private AdmissionRecord Single(string certificate)
            {
                AdmissionRecord found;
                if (_single.TryGetValue(certificate, out found))
                {
                    return found;
                }

                found = null;
                var count = 0;
                foreach (var record in _inForce.AdmissionsOf(certificate).Where(record => record.InForce))
                {
                    var admission = AdmissionRecord.Read(record.Hex, record.Form.Members);
                    if (admission == null || Refused(_inForce, admission))
                    {
                        continue;
                    }

                    count++;
                    found = admission;
                }

                found = count == 1 && string.Equals(found.Issuer, _served, StringComparison.Ordinal) ? found : null;
                _single[certificate] = found;
                return found;
            }

            /// <summary>
            /// A certificate on the stack, with its origins and how far through them it is.
            /// </summary>
            private sealed class Step
            {
                public Step(string certificate, List<string> origins, bool failed)
                {
                    Certificate = certificate;
                    Origins = origins;
                    Failed = failed;
                }

                public string Certificate { get; private set; }

                public List<string> Origins { get; private set; }

                public int Next { get; set; }

                public bool Failed { get; set; }
            }
        }
    }
}
