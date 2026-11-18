using KeePassGate.Diagnostics;
using KeePassGate.Pipeline;
using KeePassGate.Store;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The words a store that holds the gate back is answered with, `STORE.md` §656, and what a
    /// connection is told where its store could not be read.
    /// </summary>
    [TestFixture]
    public sealed class StoreWordsTests
    {
        private TestAuthority _target;

        [SetUp]
        public void SetUp()
        {
            _target = TestAuthority.New();
        }

        [TearDown]
        public void TearDown()
        {
            _target.Dispose();
        }

        [TestCase(StoreState.None, null)]
        [TestCase(StoreState.Split, "split")]
        [TestCase(StoreState.Outdated, "outdated")]
        [TestCase(StoreState.Stopped, "stopped")]
        [TestCase(StoreState.Whole, null)]
        public void Each_state_is_answered_its_word(StoreState state, string word)
        {
            Assert.That(StoreWords.Of(state), Is.EqualTo(word));
        }

        [TestCase(StoreState.None, "lost")]
        [TestCase(StoreState.Split, "split")]
        [TestCase(StoreState.Outdated, "outdated")]
        [TestCase(StoreState.Stopped, "stopped")]
        [TestCase(StoreState.Whole, null)]
        public void The_gate_answers_a_store_with_no_key_lost_and_every_other_state_as_a_write_is_refused(StoreState state, string word)
        {
            // `STORE.md` §656: a file with no store key is `lost` to a client, while a write and
            // the tab take it for a database that has no store yet, which a Create starts.
            Assert.That(StoreWords.Answered(state), Is.EqualTo(word));
        }

        [TestCase("split")]
        [TestCase("outdated")]
        [TestCase("stopped")]
        public void A_connection_to_a_database_whose_store_holds_it_back_is_answered_the_word(string word)
        {
            _target.Unavailable = word;
            using (var certificate = _target.IssueAdmittedClient("reader"))
            {
                var arriving = ClientCredentials.For(certificate, _target, null);
                Assert.That(arriving.UnavailableError, Is.EqualTo(word));
                Assert.That(arriving.IsPaired, Is.False);

                _target.Unavailable = null;
                var paired = ClientCredentials.For(certificate, _target, null);
                Assume.That(paired.IsPaired, Is.True);

                _target.Unavailable = word;
                Assert.That(ClientCredentials.Again(paired, _target, null).UnavailableError, Is.EqualTo(word),
                    "a store that stopped while a request waited answers that request");
            }
        }

        [Test]
        public void A_store_that_could_not_be_read_is_busy_and_never_a_stranger()
        {
            // A stranger may pair, so a reading that could not be made must never fall into the
            // answer every failure to judge a certificate gets.
            _target.UnavailableThrows = new VaultUnsettled("KeePass's thread could not be held still for a read");

            using (var certificate = _target.IssueAdmittedClient("reader"))
            {
                var arriving = ClientCredentials.For(certificate, _target, null);
                Assert.That(arriving.UnavailableError, Is.EqualTo("busy"));
                Assert.That(arriving.IsPaired, Is.False);
                Assert.That(arriving.Refusal, Is.Null, "not refused as a certificate with no record");

                var stranger = ClientCredentials.For(null, _target, null);
                Assert.That(stranger.UnavailableError, Is.EqualTo("busy"), "nor a stranger who could pair");
            }
        }

        [Test]
        public void A_store_that_could_not_be_read_while_a_request_waited_is_thrown_on_to_be_answered_busy()
        {
            using (var certificate = _target.IssueAdmittedClient("reader"))
            {
                var paired = ClientCredentials.For(certificate, _target, null);
                Assume.That(paired.IsPaired, Is.True);

                _target.UnavailableThrows = new VaultUnsettled("KeePass's thread could not be held still for a read");
                Assert.Throws<VaultUnsettled>(() => ClientCredentials.Again(paired, _target, null));
            }
        }

        [Test]
        public void A_database_that_shut_while_its_store_was_read_is_locked()
        {
            _target.UnavailableThrows = new DatabaseShut("the database closed under this connection");

            using (var certificate = _target.IssueAdmittedClient("reader"))
            {
                Assert.That(ClientCredentials.For(certificate, _target, null).IsLocked, Is.True);
                Assert.That(ClientCredentials.For(null, _target, null).IsLocked, Is.True);
            }
        }

        [Test]
        public void A_database_that_shut_while_a_request_waited_is_locked()
        {
            using (var certificate = _target.IssueAdmittedClient("reader"))
            {
                var paired = ClientCredentials.For(certificate, _target, null);
                _target.UnavailableThrows = new DatabaseShut("the database closed under this connection");

                Assert.That(ClientCredentials.Again(paired, _target, null).IsLocked, Is.True);
            }
        }
    }
}
