using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a pairing request answers the dialog showing it: which rows changed and how, which
    /// rows open checked or a Refresh unchecks, and what an approval of it is held to, each over
    /// readings of a store, as the dialog reads them.
    /// </summary>
    [TestFixture]
    public sealed class PairingRequestTests
    {
        private MemoryStore _store;
        private List<X509Certificate2> _certificates;

        [SetUp]
        public void SetUp()
        {
            _store = new MemoryStore();
            StoreSeeds.Authority(_store);
            _certificates = new List<X509Certificate2>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var certificate in _certificates)
            {
                certificate.Dispose();
            }
        }

        [Test]
        public void A_Refresh_that_changes_no_row_refreshes_nothing_and_unchecks_nothing()
        {
            StoreSeeds.Name(_store, "reader");
            var shown = Request("reader", "fresh").Showing(Read("reader", "fresh"));

            var refreshed = shown.Refreshed(Read("reader", "fresh"));

            Assert.That(refreshed.Refreshes, Is.False);
            Assert.That(refreshed.Profiles.Select(refreshed.Changed), Is.All.False);
            Assert.That(refreshed.Profiles.Select(refreshed.Unchecks), Is.All.False);
        }

        [Test]
        public void A_Refresh_unchecks_a_row_for_which_Approve_does_more_and_highlights_every_row_it_changed()
        {
            // `on`: on the list, then restored. `back`: restored, then on the list. `more`: regained
            // by a client, then by a second. `fewer`: regained by two clients, then by a single
            // client. `same`: added, then added.
            var asked = new[] { "on", "back", "more", "fewer", "same" };
            StoreSeeds.Name(_store, "on");
            StoreSeeds.Name(_store, "back");
            Admitted(_store, "agent-first", "more", "fewer");
            var second = Admitted(_store, "agent-second", "fewer");
            var copy = _store.Copy();
            Admitted(copy, "agent-third", "more");
            Taken("back", "more", "fewer");
            var shown = Request(asked).Showing(Read(asked));
            Assert.That(asked.Select(shown.MarkOf), Is.EqualTo(new[]
            {
                ProfileMark.Listed, ProfileMark.Removed, ProfileMark.Removed, ProfileMark.Removed, ProfileMark.New
            }));

            StoreSeeds.Unname(_store, "on");
            StoreSeeds.Name(_store, "back");
            StoreSeeds.Revoke(_store, CertificateDigest.Of(second));
            _store.Synchronize(copy);
            var refreshed = shown.Refreshed(Read(asked));

            Assert.That(asked.Select(refreshed.MarkOf), Is.EqualTo(new[]
            {
                ProfileMark.Removed, ProfileMark.Listed, ProfileMark.Removed, ProfileMark.Removed, ProfileMark.New
            }));
            Assert.That(refreshed.Returning("more").Count, Is.EqualTo(2), "a client more");
            Assert.That(refreshed.Returning("fewer").Count, Is.EqualTo(1), "a client fewer");
            Assert.That(refreshed.Refreshes, Is.True);
            Assert.That(asked.Select(refreshed.Changed), Is.EqualTo(new[] { true, true, true, true, false }), "highlighted");
            Assert.That(asked.Select(refreshed.Unchecks), Is.EqualTo(new[] { true, false, true, false, false }), "unchecked");

            var again = refreshed.Refreshed(Read(asked));
            Assert.That(again.Refreshes, Is.False, "a Refresh after it changes nothing");
            Assert.That(asked.Select(again.Unchecks), Is.All.False);
            Assert.That(asked.Select(again.Changed), Is.EqualTo(new[] { true, true, true, true, false }), "a highlight stays");
        }

        [Test]
        public void A_dialog_shown_again_highlights_and_unchecks_a_row_a_client_the_dialog_before_did_not_show_regains()
        {
            // `ops` regained by `agent-first` before and by `agent-second` as well now: a client the
            // operator was not shown. `audit` regained by both before and by `agent-first` alone
            // now: narrower, highlighted as a row of a dialog shown again, and checked as approved.
            var asked = new[] { "ops", "audit" };
            Admitted(_store, "agent-first", "ops", "audit");
            var second = Admitted(_store, "agent-second", "audit");
            var copy = _store.Copy();
            Admitted(copy, "agent-third", "ops");
            Taken("ops", "audit");
            var before = Read(asked);
            StoreSeeds.Revoke(_store, CertificateDigest.Of(second));
            _store.Synchronize(copy);

            var shown = Request(asked, PairingApproval.Approved(asked, TimeSpan.FromDays(30)), before).Showing(Read(asked));

            Assert.That(shown.Again, Is.True);
            Assert.That(asked.Select(shown.MarkOf), Is.All.EqualTo(ProfileMark.Removed), "restored, as before");
            Assert.That(asked.Select(shown.Changed), Is.EqualTo(new[] { true, true }));
            Assert.That(asked.Select(shown.Checks), Is.EqualTo(new[] { false, true }));
            Assert.That(shown.Differs, Is.True);
        }

        [Test]
        public void A_dialog_shown_again_in_which_no_row_differs_gives_the_approval_before_again_over_its_own_reading()
        {
            var asked = new[] { "ops", "fresh" };
            Admitted(_store, "agent-first", "ops");
            Taken("ops");
            var before = Read(asked);
            var reading = Read(asked);

            var shown = Request(asked, PairingApproval.Approved(new[] { "ops" }, TimeSpan.FromDays(7)), before).Showing(reading);
            var answer = shown.AsBefore();

            Assert.That(shown.Differs, Is.False);
            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
            Assert.That(answer.Shown, Is.False, "asked of nobody");
            Assert.That(answer.Profiles, Is.EqualTo(new[] { "ops" }));
            Assert.That(answer.Lifetime, Is.EqualTo(TimeSpan.FromDays(7)));
            Assert.That(answer.Against, Is.SameAs(reading));
        }

        [Test]
        public void Approve_is_held_to_what_the_dialog_showing_the_request_was_built_from()
        {
            var reading = Read("ops");
            var shown = Request("ops").Showing(reading);

            var answer = shown.Approve(new[] { "ops" }, TimeSpan.FromDays(30));

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
            Assert.That(answer.Shown, Is.True);
            Assert.That(answer.Profiles, Is.EqualTo(new[] { "ops" }));
            Assert.That(answer.Lifetime, Is.EqualTo(TimeSpan.FromDays(30)));
            Assert.That(answer.Against, Is.SameAs(reading));
        }

        [Test]
        public void A_request_put_to_the_screen_is_built_with_its_reading()
        {
            Assert.Throws<ArgumentNullException>(() => new PairingRequest(
                "db", null, "abcdef01", "agent-fictitious", new[] { "ops" }, "127.0.0.1:60001", null, null, null, null, null));
        }

        [Test]
        public void A_dialog_ends_with_the_store_s_word_only_over_a_store_that_reads_and_no_longer_serves()
        {
            var ended = PairingApproval.NoLongerServed(AskedProfiles.Unserved(HeldBackTarget.Undecided));
            var gone = PairingApproval.NoLongerServed(AskedProfiles.Unserved(null));

            Assert.That(ended.Decision, Is.EqualTo(PairingDecision.Unserved));
            Assert.That(ended.Word, Is.EqualTo(HeldBackTarget.Undecided));
            Assert.That(ended.Shown, Is.True, "the dialog stood");
            Assert.That(gone.Word, Is.Null, "answered 409 no invitation");
            Assert.Throws<ArgumentException>(() => PairingApproval.NoLongerServed(AskedProfiles.Unreadable()));
            Assert.Throws<ArgumentException>(() => PairingApproval.NoLongerServed(Read("ops")));
            Assert.Throws<ArgumentException>(() => PairingApproval.NoLongerServed(null));
        }

        /// <summary>A request for these profiles, as the handler makes it, reading this test's store.</summary>
        private PairingRequest Request(params string[] profiles)
        {
            return Request(profiles, null, null);
        }

        private PairingRequest Request(string[] profiles, PairingApproval earlier, AskedProfiles before)
        {
            return new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", profiles, "127.0.0.1:60001", null,
                earlier, before, null, () => Read(profiles));
        }

        /// <summary>What a dialog for these profiles reads of the store now.</summary>
        private AskedProfiles Read(params string[] profiles)
        {
            using (var reading = _store.Read())
            {
                return AskedProfiles.Of(reading, profiles, DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// A client admitted into this store holding these names, each put on the list there first
        /// where it is not, as a pairing's write puts it.
        /// </summary>
        private X509Certificate2 Admitted(MemoryStore store, string name, params string[] profiles)
        {
            var certificate = Minted.Named("CN=" + name).SelfSigned().Read();
            _certificates.Add(certificate);
            StoreSeeds.Admit(store, certificate, DateTimeOffset.UtcNow, profiles);
            return certificate;
        }

        /// <summary>These names taken off the list, each put on it first where it is not.</summary>
        private void Taken(params string[] profiles)
        {
            foreach (var profile in profiles)
            {
                using (var reading = _store.Read())
                {
                    if (!KeePassGate.Acl.ProfileNames.Of(reading).Holds(profile))
                    {
                        StoreSeeds.Name(_store, profile);
                    }
                }

                StoreSeeds.Unname(_store, profile);
            }
        }
    }
}
