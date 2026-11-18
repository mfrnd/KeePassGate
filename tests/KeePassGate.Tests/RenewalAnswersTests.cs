using System;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The operator's standing answers to a renewal, kept in the database's custom data beside the
    /// profile names, and what takes an answer away.
    /// </summary>
    [TestFixture]
    public sealed class RenewalAnswersTests
    {
        private InMemoryKeyValueStore _values;
        private RecordingGateLog _log;
        private RenewalAnswers _answers;

        [SetUp]
        public void SetUp()
        {
            _values = new InMemoryKeyValueStore();
            _log = new RecordingGateLog();
            _answers = new RenewalAnswers(_values, _log);
        }

        [Test]
        public void A_standing_answer_to_a_renewal_reads_back_as_it_was_written()
        {
            var key = new string('a', 64);
            Assume.That(_answers.StandingRefusal(key), Is.Null, "nothing until the operator answers");

            _answers.RefuseRenewal(key, RenewalRefusal.Forever());

            Assert.That(_answers.StandingRefusal(key).IsForever, Is.True);
            Assert.That(_values.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.EqualTo("forever"));
        }

        [Test]
        public void A_later_standing_answer_replaces_the_one_before_it()
        {
            // The property the key's being changed rather than only added rests on: the dialog
            // asks again once an until-date has passed, and the answer it takes then is the answer
            // that stands.
            var key = new string('a', 64);
            _answers.RefuseRenewal(key, RenewalRefusal.Until(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero)));

            _answers.RefuseRenewal(key, RenewalRefusal.Forever());

            Assert.That(_answers.StandingRefusal(key).IsForever, Is.True);
            Assert.That(_values.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.EqualTo("forever"));
        }

        [Test]
        public void A_refusal_in_this_session_is_never_written_to_the_database()
        {
            // It names this session, and a key in the file would outlast it in every copy.
            var key = new string('a', 64);

            Assert.That(() => _answers.RefuseRenewal(key, RenewalRefusal.ThisSession()), Throws.ArgumentException);
            Assert.That(_values.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Null);
        }

        [TestCase("next Tuesday")]
        [TestCase("")]
        public void A_standing_answer_that_will_not_read_is_answered_as_forever_and_said(string written)
        {
            // Only a hand at Database Maintenance or a later version writes one of these, and the
            // operator had answered: answering it as nothing would raise a dialog they did
            // not want, where answering it as forever refuses, which is the safe direction. An
            // empty value is one of them, since this plugin never writes such a value.
            var key = new string('a', 64);
            _values.Plant(AuthorityKeys.RenewalRefusalKey(key), written);

            Assert.That(_answers.StandingRefusal(key).IsForever, Is.True);
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("could not be read"));
        }

        [Test]
        public void Allow_renewal_takes_a_standing_answer_away_and_says_whether_there_was_one()
        {
            var key = new string('a', 64);
            _values.Plant(AuthorityKeys.RenewalRefusalKey(key), "next Tuesday");

            Assert.That(_answers.LiftRenewalRefusal(key), Is.True, "an answer that will not read is lifted like any other");
            Assert.That(_values.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Null);
            Assert.That(_answers.LiftRenewalRefusal(key), Is.False);
        }

        [Test]
        public void A_prune_takes_every_answer_about_a_certificate_no_admission_names_and_says_what_it_took()
        {
            var kept = new string('a', 64);
            var gone = new string('b', 64);
            _answers.RefuseRenewal(kept, RenewalRefusal.Forever());
            _answers.RefuseRenewal(gone, RenewalRefusal.Until(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero)));
            _values.Set("KPG.Profiles", "{}");

            var taken = _answers.Prune(new[] { kept });

            Assert.That(taken.Select(pair => pair.Key), Is.EqualTo(new[] { AuthorityKeys.RenewalRefusalKey(gone) }));
            Assert.That(taken[0].Value, Is.EqualTo(RenewalRefusal.Until(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero)).Write()),
                "what a write that does not land puts back");
            Assert.That(_answers.StandingRefusal(kept).IsForever, Is.True, "an answer about a certificate an admission names stays");
            Assert.That(_values.Get("KPG.Profiles"), Is.EqualTo("{}"), "and no key of another kind goes");
        }

        [Test]
        public void A_prune_that_is_told_nothing_takes_nothing()
        {
            var key = new string('a', 64);
            _answers.RefuseRenewal(key, RenewalRefusal.Forever());

            Assert.That(_answers.Prune(null), Is.Empty);
            Assert.That(_answers.StandingRefusal(key), Is.Not.Null);
        }
    }
}
