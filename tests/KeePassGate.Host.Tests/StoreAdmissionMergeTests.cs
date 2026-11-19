using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Pipeline;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A certificate taken away over two copies of a real database that the product wrote apart
    /// and KeePass's own merge brings together, `STORE.md` §648.
    /// </summary>
    /// <remarks>
    /// Each sequence starts from a database whose authority admitted a client, written further
    /// where a sequence calls for it, saved and copied as a file copied to another machine is. Each
    /// copy is then written by the product's own acts, the operator's through the KPG tab's buffer
    /// and its OK, the copies are saved, and each is merged into the other by `PwDatabase.MergeIn`,
    /// under every method KeePass offers, from the other copy's file opened afresh as KeePass's
    /// import and synchronize open it. The copy merged into is asked which certificates it admits
    /// through the gate's own targets for it, as a connection is judged, and asked again after its
    /// next write of the plugin's own, which deletes what the merge brought and the reading
    /// refuses; where a sequence leaves its store stopped, that is asserted as well.
    ///
    /// **Create new IDs brings no record**: every entry of the other copy arrives under another
    /// identifier, where it is a copy the store passes over, so the copy imported into answers as
    /// it did before, `STORE.md` §621 and §640. **No method but Synchronize carries a deletion
    /// record**, so what was deleted for good where it was made reaches the other copy only by a
    /// Synchronize, `STORE.md` §639; the sequences that delete by hand state where that decides an
    /// answer.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class StoreAdmissionMergeTests
    {
        private const string Paired = "the certificate paired";
        private const string Renewed = "its renewal in the first copy";
        private const string RenewedElsewhere = "its renewal in the second copy";
        private const string Onward = "the renewal onward from that";

        private static readonly PwMergeMethod[] Methods =
        {
            PwMergeMethod.Synchronize,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.KeepExisting,
            PwMergeMethod.CreateNewUuids
        };

        [Test]
        public void A_renewal_and_the_old_certificate_given_up_in_a_copy_meet_a_copy_from_before()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                },
                second => { },
                (method, intoFirst) => Answers(Paired, false, Renewed, true));
        }

        [Test]
        public void A_renewal_made_meanwhile_in_the_other_copy_and_a_renewal_onward_from_it_are_refused()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                },
                second =>
                {
                    second.Renew(Paired, RenewedElsewhere);
                    second.Renew(RenewedElsewhere, Onward);
                },
                (method, intoFirst) => Answers(Paired, false, Renewed, true, RenewedElsewhere, false, Onward, false));
        }

        [Test]
        public void A_renewal_onward_is_refused_also_once_the_certificate_between_was_given_up_there()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                },
                second =>
                {
                    second.Renew(Paired, RenewedElsewhere);
                    second.Renew(RenewedElsewhere, Onward);
                    second.GiveUp(RenewedElsewhere);
                },
                (method, intoFirst) => Answers(Paired, false, Renewed, true, RenewedElsewhere, false, Onward, false));
        }

        [Test]
        public void The_same_certificate_taken_away_in_both_copies_each_having_read_another_renewal_admits_neither()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                },
                second =>
                {
                    second.Renew(Paired, RenewedElsewhere);
                    second.GiveUp(Paired);
                },
                (method, intoFirst) => Answers(Paired, false, Renewed, false, RenewedElsewhere, false));
        }

        [Test]
        public void The_operator_s_single_removal_in_a_copy_refuses_a_renewal_made_in_the_other()
        {
            Run(
                first => first.TakeAwayOnTheTab(Paired),
                second => second.Renew(Paired, RenewedElsewhere),
                (method, intoFirst) => Answers(Paired, false, RenewedElsewhere, false));
        }

        [Test]
        public void A_renewal_given_up_in_a_copy_after_the_other_took_its_origin_away_refuses_what_was_renewed_onward_from_it()
        {
            // The second copy renews from the paired certificate, renews onward, and gives the
            // renewal between up, which names what was renewed onward; the first copy took the
            // paired certificate away before any of that reached it, so its statement names none
            // of it, and everything renewed in the second copy is refused in both.
            Run(
                first => first.TakeAwayOnTheTab(Paired),
                second =>
                {
                    second.Renew(Paired, RenewedElsewhere);
                    second.Renew(RenewedElsewhere, Onward);
                    second.GiveUp(RenewedElsewhere);
                },
                (method, intoFirst) => Answers(Paired, false, RenewedElsewhere, false, Onward, false));
        }

        [Test]
        public void A_certificate_taken_away_after_a_statement_of_the_other_copy_arrived_names_what_that_statement_was_renewed_from_it()
        {
            // The second copy renews from the paired certificate, renews onward, and gives the
            // renewal between up. Once the copies met, the copy merged into takes the paired
            // certificate away: its statement names the renewal between by the statement that
            // arrived, so the renewal onward counts.
            Run(
                first => { },
                second =>
                {
                    second.Renew(Paired, RenewedElsewhere);
                    second.Renew(RenewedElsewhere, Onward);
                    second.GiveUp(RenewedElsewhere);
                },
                (method, intoFirst) => Answers(Paired, false, RenewedElsewhere, false, Onward, true),
                afterTheMerge: copy => copy.TakeAwayOnTheTab(Paired));
        }

        [Test]
        public void A_statement_written_after_a_merge_about_an_origin_still_admitted_leaves_what_the_other_copy_renewed_refused()
        {
            // The first copy took its renewal away before the second copy renewed from it, renewed
            // onward and gave the renewal between up. Once the copies met, the copy merged into
            // takes the paired certificate away: its statement names the renewal the first copy
            // took away, whose statement names nothing of the second copy's, so all of it stays
            // refused.
            Run(
                first => first.TakeAwayOnTheTab(Renewed),
                second =>
                {
                    second.Renew(Renewed, RenewedElsewhere);
                    second.Renew(RenewedElsewhere, Onward);
                    second.GiveUp(RenewedElsewhere);
                },
                (method, intoFirst) => Answers(Paired, false, Renewed, false, RenewedElsewhere, false, Onward, false),
                afterTheMerge: copy => copy.TakeAwayOnTheTab(Paired),
                before: first => first.Renew(Paired, Renewed));
        }

        [Test]
        public void The_operator_s_statement_and_the_holder_s_give_ups_in_the_other_copy_refuse_every_certificate_renewed_from_it()
        {
            // The first copy takes the paired certificate away; the second renews from it, renews
            // onward, and its holder gives up the renewal between and then the paired certificate,
            // naming the renewal between. The operator's statement names none of it, so all of it
            // stays refused once the copies meet.
            Run(
                first => first.TakeAwayOnTheTab(Paired),
                second =>
                {
                    second.Renew(Paired, RenewedElsewhere);
                    second.Renew(RenewedElsewhere, Onward);
                    second.GiveUp(RenewedElsewhere);
                    second.GiveUp(Paired);
                },
                (method, intoFirst) => Answers(Paired, false, RenewedElsewhere, false, Onward, false));
        }

        [TestCase(0, false)]
        [TestCase(10, false)]
        [TestCase(0, true)]
        [TestCase(10, true)]
        public void A_record_both_copies_hold_edited_in_a_copy_counts_by_the_version_written_or_stops_the_store_past_the_limit(
            int furtherEdits, bool sameSecond)
        {
            // Copied after the paired certificate was taken away, so both copies hold the record,
            // and the merges meet two versions of the same entry. Within the limit the record
            // counts by the version written wherever the edited version arrives. Past it, a store
            // left with no version of the record that passes stops, and a store that keeps the
            // version written, as its current version or in its history, stays whole. The edits
            // are a second apart, as edits by a person are, or within the second the record was
            // written, as an edit by a program can be.
            Run(
                first => first.EditTheRecord(furtherEdits, sameSecond),
                second => { },
                (method, intoFirst) => furtherEdits == 0 || !WrittenVersionLost(method, intoFirst, sameSecond)
                    ? Answers(Paired, false, Renewed, true)
                    : Answers(Paired, false, Renewed, false),
                stops: (method, intoFirst) => furtherEdits > 0 && WrittenVersionLost(method, intoFirst, sameSecond),
                before: first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                });
        }

        [Test]
        public void A_record_both_copies_hold_deleted_for_good_in_a_copy_still_refuses_in_both()
        {
            Run(
                first => first.DeleteForGood(StoreKinds.Withdrawn, null),
                second => { },
                (method, intoFirst) => Answers(Paired, false, Renewed, true),
                before: first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                });
        }

        /// <summary>
        /// Whether the copy merged into is left without the version written of an entry both
        /// copies hold, edited past the history limit in the first copy, as measured. Edited a
        /// second and more after it was written: into the first copy, OverwriteExisting writes the
        /// second copy's version over the edited entry, and every other method keeps the edited
        /// entry, whose history no longer holds the version written; into the second copy, every
        /// method but KeepExisting puts the edited entry in the place of its own, with the history
        /// the first copy kept. Edited within the second it was written, where the two versions
        /// compare as neither older nor newer: into the first copy, every method but KeepExisting
        /// keeps the version written; into the second copy, KeepExisting alone does.
        /// </summary>
        private static bool WrittenVersionLost(PwMergeMethod method, bool intoFirst, bool sameSecond)
        {
            if (sameSecond)
            {
                return intoFirst == (method == PwMergeMethod.KeepExisting);
            }

            return intoFirst ? method != PwMergeMethod.OverwriteExisting : method != PwMergeMethod.KeepExisting;
        }

        [Test]
        public void A_record_taking_a_certificate_away_deleted_for_good_is_still_restated_by_its_summary_entry()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                    first.DeleteForGood(StoreKinds.Withdrawn, null);
                },
                second => second.Renew(Paired, RenewedElsewhere),
                (method, intoFirst) => Answers(Paired, false, Renewed, true, RenewedElsewhere, false));
        }

        [Test]
        public void A_record_taking_a_certificate_away_deleted_for_good_with_its_summary_entry_reaches_only_what_Synchronize_carries()
        {
            // Both of the decision's entries deleted for good where it was made: the copy that
            // deleted them admits nobody, and the other copy admits nobody of that client once a
            // Synchronize has carried the deletion records there. A method that carries none never
            // tells the other copy, which admits what it admitted, the renewal that arrives from
            // its certificate among them, `STORE.md` §639.
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                    first.DeleteForGood(StoreKinds.Withdrawn, null);
                    first.DeleteForGood(StoreKinds.Head, null);
                },
                second => second.Renew(Paired, RenewedElsewhere),
                (method, intoFirst) => intoFirst || method == PwMergeMethod.Synchronize
                    ? Answers(Paired, false, Renewed, false, RenewedElsewhere, false)
                    : Answers(Paired, true, Renewed, true, RenewedElsewhere, true),
                stops: (method, intoFirst) => intoFirst || method == PwMergeMethod.Synchronize);
        }

        [Test]
        public void A_record_taking_a_certificate_away_edited_to_name_another_renewal_counts_by_the_version_written()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                },
                second => second.Renew(Paired, RenewedElsewhere),
                (method, intoFirst) => Answers(Paired, false, Renewed, true, RenewedElsewhere, false),
                first => first.NameInTheRecord(RenewedElsewhere, 0));
        }

        [Test]
        public void A_record_taking_a_certificate_away_edited_past_the_history_limit_admits_nobody()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                },
                second => second.Renew(Paired, RenewedElsewhere),
                (method, intoFirst) => Answers(Paired, false, Renewed, false, RenewedElsewhere, false),
                first => first.NameInTheRecord(RenewedElsewhere, 10),
                (method, intoFirst) => true);
        }

        [Test]
        public void An_admission_whose_admission_of_origin_was_deleted_by_hand_counts_only_where_its_origin_does()
        {
            // A deletion by hand is no decision: the copy that made it refuses the renewal, whose
            // certificate of origin is then nowhere, and so does the other copy once a Synchronize
            // has carried the deletion record there. A method that carries none leaves the other
            // copy admitting the certificate of origin, and the renewal counts there by it.
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.DeleteForGood(StoreKinds.Client, Paired);
                },
                second => { },
                (method, intoFirst) => intoFirst || method == PwMergeMethod.Synchronize
                    ? Answers(Paired, false, Renewed, false)
                    : Answers(Paired, true, Renewed, true));
        }

        [Test]
        public void A_revocation_of_the_client_beside_a_certificate_taken_away_refuses_every_certificate_of_it()
        {
            Run(
                first =>
                {
                    first.Renew(Paired, Renewed);
                    first.GiveUp(Paired);
                },
                second => second.Revoke(Paired),
                (method, intoFirst) => Answers(Paired, false, Renewed, false));
        }

        /// <summary>
        /// Runs a sequence: the steps in each copy, saved, then each copy merged into the other
        /// under every method, the answers compared with those expected, and compared again after
        /// the copy merged into writes.
        /// </summary>
        /// <param name="expected">
        /// The answer expected for each certificate, by method and by whether the first copy is
        /// the copy merged into; not asked for Create new IDs, under which the copy merged into
        /// answers as it did before.
        /// </param>
        /// <param name="afterTheSecond">
        /// What is done in the first copy once the second copy's steps are made, which can name a
        /// certificate the second copy admitted.
        /// </param>
        /// <param name="stops">
        /// Where the copy merged into is left stopped, by method and copy; asserted there.
        /// </param>
        /// <param name="before">What is done before the database is copied.</param>
        /// <param name="afterTheMerge">
        /// What is done in the copy merged into once the copies met, before it is asked; not done
        /// after Create new IDs, and refused as wrong where the merge leaves the store not whole.
        /// </param>
        private static void Run(
            Action<Copy> inFirst,
            Action<Copy> inSecond,
            Func<PwMergeMethod, bool, IDictionary<string, bool>> expected,
            Action<Copy> afterTheSecond = null,
            Func<PwMergeMethod, bool, bool> stops = null,
            Action<Copy> before = null,
            Action<Copy> afterTheMerge = null)
        {
            using (var copies = Copies.Made(before))
            {
                inFirst(copies.First);
                inSecond(copies.Second);
                if (afterTheSecond != null)
                {
                    afterTheSecond(copies.First);
                }

                copies.First.Database.Save();
                copies.Second.Database.Save();

                var wrong = new List<string>();
                var measured = new StringBuilder();
                foreach (var method in Methods)
                {
                    foreach (var intoFirst in new[] { true, false })
                    {
                        var into = intoFirst ? copies.First : copies.Second;
                        var from = intoFirst ? copies.Second : copies.First;
                        var named = method + (intoFirst ? ", into the first copy" : ", into the second copy");

                        using (var target = Opened(into.Database))
                        using (var source = Opened(from.Database))
                        {
                            var asBefore = copies.Answers(target);
                            target.Database.MergeIn(source.Database, method, null);
                            if (afterTheMerge != null && method != PwMergeMethod.CreateNewUuids)
                            {
                                if (State(target) == StoreState.Whole)
                                {
                                    afterTheMerge(copies.In(target));
                                }
                                else
                                {
                                    wrong.Add(named + ": the store is " + State(target) + ", and nothing was written after the merge");
                                }
                            }

                            var after = copies.Answers(target);
                            measured.AppendLine(named + ": " + State(target) + "; " + Show(after));

                            var wanted = method == PwMergeMethod.CreateNewUuids ? asBefore : expected(method, intoFirst);
                            Compare(wrong, named, wanted, after);
                            if (method != PwMergeMethod.CreateNewUuids && stops != null && stops(method, intoFirst)
                                && State(target) != StoreState.Stopped)
                            {
                                wrong.Add(named + ": the store is " + State(target) + ", not stopped");
                            }

                            if (State(target) == StoreState.Whole)
                            {
                                copies.PairAnother(target);
                                Compare(wrong, named + ", after the next write", wanted, copies.Answers(target));
                            }
                        }
                    }
                }

                TestContext.Out.WriteLine(measured.ToString());
                Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
            }
        }

        private static void Compare(
            List<string> wrong, string named, IDictionary<string, bool> wanted, IDictionary<string, bool> answered)
        {
            wrong.AddRange(wanted
                .Where(answer => answered[answer.Key] != answer.Value)
                .Select(answer => named + ": " + answer.Key + (answered[answer.Key] ? " is admitted" : " is refused")));
        }

        /// <summary>Certificates and whether each is admitted, in pairs.</summary>
        private static IDictionary<string, bool> Answers(params object[] pairs)
        {
            var answers = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (var at = 0; at < pairs.Length; at += 2)
            {
                answers[(string)pairs[at]] = (bool)pairs[at + 1];
            }

            return answers;
        }

        private static string Show(IDictionary<string, bool> answers)
        {
            return string.Join(", ", answers.Select(answer => answer.Key + (answer.Value ? " admitted" : " refused")));
        }

        private static StoreState State(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                return reading.State;
            }
        }

        /// <summary>
        /// A saved database opened afresh from a copy of its file, as KeePass's import and
        /// synchronize open the other copy, and as each merge opens the copy it merges into.
        /// </summary>
        private static TemporaryDatabase Opened(TemporaryDatabase saved)
        {
            var copy = TemporaryDatabase.New();
            System.IO.File.Copy(saved.Path, copy.Path, true);
            copy.Reopen();
            return copy;
        }

        private static KdbxStore Store(TemporaryDatabase database)
        {
            return new KdbxStore(database.Database, GateVersion.Current, NullGateLog.Instance);
        }

        /// <summary>A client certificate of the authority the store serves.</summary>
        private static X509Certificate2 Issued(KdbxStore store, string name)
        {
            return new AuthorityIssuer().IssueClient(StoreSeeds.KeyOf(store, StoreSeeds.Served(store)), name, new[] { "ops" }).Read();
        }

        /// <summary>
        /// The two copies of a sequence, and the certificates its steps admitted, by what each is
        /// to the sequence.
        /// </summary>
        private sealed class Copies : IDisposable
        {
            private readonly IDictionary<string, X509Certificate2> _certificates;

            private Copies(IDictionary<string, X509Certificate2> certificates, TemporaryDatabase first, TemporaryDatabase second)
            {
                _certificates = certificates;
                First = new Copy(certificates, first);
                Second = new Copy(certificates, second);
            }

            public Copy First { get; private set; }

            public Copy Second { get; private set; }

            /// <summary>
            /// An authority and a client paired under it, what is done before the copy, saved,
            /// and its file copied and opened again as the second copy.
            /// </summary>
            public static Copies Made(Action<Copy> before)
            {
                var certificates = new Dictionary<string, X509Certificate2>(StringComparer.Ordinal);
                var first = TemporaryDatabase.New();
                StoreSeeds.Authority(Store(first));
                var paired = Issued(Store(first), "agent-fictitious");
                certificates[Paired] = paired;
                StoreSeeds.Admit(Store(first), paired, DateTimeOffset.UtcNow, "ops");
                if (before != null)
                {
                    before(new Copy(certificates, first));
                }

                first.Save();
                return new Copies(certificates, first, Opened(first));
            }

            /// <summary>
            /// Whether the copy admits each certificate the sequence made, as the gate's targets
            /// for the copy judge a connection presenting it.
            /// </summary>
            public IDictionary<string, bool> Answers(TemporaryDatabase copy)
            {
                using (var targets = new OpenDatabaseTargets(delegate { return new[] { copy.Database }; }, null, NullGateLog.Instance))
                {
                    // The targets are their cache's, which lets them go with `targets`.
                    var current = targets.Current();
                    return _certificates.ToDictionary(
                        pair => pair.Key,
                        pair => current.Any(target => ClientCredentials.For(pair.Value, target, NullGateLog.Instance).IsPaired),
                        StringComparer.Ordinal);
                }
            }

            /// <summary>
            /// A database of the sequence as a copy, with the certificates the sequence made.
            /// </summary>
            public Copy In(TemporaryDatabase database)
            {
                return new Copy(_certificates, database);
            }

            /// <summary>
            /// A pairing of another client, as the copy's next write of the plugin's own.
            /// </summary>
            public void PairAnother(TemporaryDatabase copy)
            {
                using (var certificate = Issued(Store(copy), "agent-invented"))
                {
                    StoreSeeds.Admit(Store(copy), certificate, DateTimeOffset.UtcNow, "ops");
                }
            }

            public void Dispose()
            {
                First.Database.Dispose();
                Second.Database.Dispose();
                foreach (var certificate in _certificates.Values)
                {
                    certificate.Reset();
                }
            }
        }

        /// <summary>A copy of a sequence, and what is done in it.</summary>
        private sealed class Copy
        {
            private readonly IDictionary<string, X509Certificate2> _certificates;

            public Copy(IDictionary<string, X509Certificate2> certificates, TemporaryDatabase database)
            {
                _certificates = certificates;
                Database = database;
            }

            public TemporaryDatabase Database { get; private set; }

            /// <summary>
            /// A renewal of a certificate this copy admits, as the renewal route writes it: the
            /// same client key, and the certificate renewed from named.
            /// </summary>
            public void Renew(string from, string to)
            {
                var store = Store(Database);
                AdmissionRecord origin;
                using (var reading = store.Read())
                {
                    origin = StoreAdmissions.Admitted(reading, Key(from));
                }

                Assert.That(origin, Is.Not.Null, from + " is not admitted in this copy, and nothing renews from it");
                var certificate = Issued(store, "agent-fictitious");
                _certificates[to] = certificate;
                StoreSeeds.Admit(store, AdmissionRecord.For(
                    certificate, DateTimeOffset.UtcNow, origin.Profiles, StoreSeeds.Served(store), origin.ClientKey, origin.AdmissionKey));
            }

            /// <summary>
            /// The certificate taken away by its client giving it up, through the writer the route
            /// writes with, which takes away only a certificate still admitted.
            /// </summary>
            public void GiveUp(string certificate)
            {
                var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
                var written = writer.GiveUp(Database.Database, Key(certificate), StoreSeeds.Served(Store(Database)));
                Assert.That(written.Ok, Is.True, certificate + " was not given up: " + written.Reason);
            }

            /// <summary>
            /// The certificate taken away on the KPG tab, as the operator does: pressed, held by
            /// the dialog's buffer, and written at its OK.
            /// </summary>
            public void TakeAwayOnTheTab(string certificate)
            {
                var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
                var pending = new PendingDatabase(new OpenDatabase(Database.Database, null, writer, NullGateLog.Instance), NullGateLog.Instance);
                using (var panel = new AuthorityPanel(pending, null, NullGateLog.Instance))
                {
                    Assert.That(panel.TakeAway(Key(certificate)), Is.Null);
                }

                Assert.That(pending.Apply(), Is.True, "the OK wrote the certificate taken away");
            }

            /// <summary>The client of a certificate revoked, as the clients page revokes.</summary>
            public void Revoke(string certificate)
            {
                using (var reading = Store(Database).Read())
                {
                    StoreSeeds.Revoke(Store(Database), StoreAdmissions.Admitted(reading, Key(certificate)).ClientKey);
                }
            }

            /// <summary>
            /// The entry of this kind deleted for good, leaving a deletion record, as KeePass's
            /// delete without the recycle bin does; the entry of the certificate named, where a
            /// name is given.
            /// </summary>
            public void DeleteForGood(string kind, string certificate)
            {
                var entry = Entry(kind, certificate);
                entry.ParentGroup.Entries.Remove(entry);

                // A second after now, as a deletion made after the entry was written is: a merge
                // applies a deletion record only to an entry last changed before it.
                Database.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow.AddSeconds(1)));
            }

            /// <summary>
            /// The record that took a certificate away edited in KeePass's entry dialog to name a
            /// further renewal, then edited this many times more, each edit putting the version
            /// before it into the entry's history.
            /// </summary>
            public void NameInTheRecord(string certificate, int furtherEdits)
            {
                Edit(Key(certificate), furtherEdits, false);
            }

            /// <summary>
            /// The same, naming a certificate nothing admits: an edit of the record's content and
            /// nothing more, each edit a second after the version before it or within the second
            /// the record was written.
            /// </summary>
            public void EditTheRecord(int furtherEdits, bool sameSecond)
            {
                Edit(new string('e', 64), furtherEdits, sameSecond);
            }

            private void Edit(string named, int furtherEdits, bool sameSecond)
            {
                var entry = Entry(StoreKinds.Withdrawn, null);
                var content = JObject.Parse(entry.Strings.ReadSafe(StoreComposer.Data));
                var renewed = ((string)content[StoreKinds.Renewed] ?? string.Empty)
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Concat(new[] { named })
                    .OrderBy(key => key, StringComparer.Ordinal);
                content[StoreKinds.Renewed] = string.Join(" ", renewed);

                // Each edit a second after the version before it, or every edit in the record's own
                // second. KeePass compares modification times to the second, in a merge and at the
                // history limit, so an edit made within the second the record was written meets
                // that version as neither older nor newer; stamped, how long the run took decides
                // nothing.
                var at = entry.LastModificationTime;
                var step = sameSecond ? TimeSpan.Zero : TimeSpan.FromSeconds(1);
                entry.CreateBackup(Database.Database);
                entry.Strings.Set(StoreComposer.Data, new ProtectedString(false, content.ToString(Newtonsoft.Json.Formatting.None)));
                Touch(entry, at = at + step);

                for (var edit = 0; edit < furtherEdits; edit++)
                {
                    entry.CreateBackup(Database.Database);
                    entry.Expires = !entry.Expires;
                    Touch(entry, at = at + step);
                }
            }

            private static void Touch(PwEntry entry, DateTime at)
            {
                entry.Touch(true, false);
                entry.LastModificationTime = at;
            }

            private string Key(string certificate)
            {
                return CertificateDigest.Of(_certificates[certificate]);
            }

            private PwEntry Entry(string kind, string certificate)
            {
                return new StoredAdmissions(Database.Database).Entries(kind).Single(entry => certificate == null
                    || (string)JObject.Parse(entry.Strings.ReadSafe(StoreComposer.Data))[StoreKinds.Certificate] == Key(certificate));
            }
        }
    }
}
