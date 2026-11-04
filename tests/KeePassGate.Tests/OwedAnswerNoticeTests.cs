using KeePassGate.Authority;
using KeePassGate.Pairing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the operator is told as KeePass exits about standing answers no database took.
    /// </summary>
    /// <remarks>
    /// The message gave a cause and a consequence that memory does not record: "because KeePass
    /// was busy whenever it was given and nothing was written after it", false for an answer owed
    /// after a save that failed, and "the dialog comes up again", for clients it did not name.
    /// </remarks>
    [TestFixture]
    public sealed class OwedAnswerNoticeTests
    {
        private static readonly string First = new string('a', 64);
        private static readonly string Second = new string('b', 64);
        private static readonly string Third = new string('c', 64);
        private static readonly string Here = new string('d', 64);
        private static readonly string There = new string('e', 64);

        [Test]
        public void One_answer_is_named_by_its_certificate_and_given_no_cause()
        {
            var message = OwedAnswerNotice.Message(new[] { Owed(First, Here) });

            Assert.That(message, Is.EqualTo(
                "A standing answer to renewing a certificate was never saved to its database and is lost now: "
                + CertificateDigest.Shorten(First) + ". That client may be asked about renewing again."));
        }

        [Test]
        public void Several_answers_are_named_in_one_sentence()
        {
            var message = OwedAnswerNotice.Message(new[] { Owed(First, Here), Owed(Second, Here), Owed(Third, Here) });

            Assert.That(message, Is.EqualTo(
                "3 standing answers to renewing certificates were never saved to their database and are lost now: "
                + CertificateDigest.Shorten(First) + ", " + CertificateDigest.Shorten(Second) + " and "
                + CertificateDigest.Shorten(Third) + ". Those clients may be asked about renewing again."));
        }

        [Test]
        public void Answers_owed_to_two_databases_are_told_as_owed_to_their_databases()
        {
            var message = OwedAnswerNotice.Message(new[] { Owed(First, Here), Owed(Second, There) });

            Assert.That(message, Does.Contain("never saved to their databases"));
        }

        [Test]
        public void The_log_names_each_certificate_and_the_authority_it_is_under()
        {
            var line = OwedAnswerNotice.LogLine(new[] { Owed(First, Here), Owed(Second, There) });

            Assert.That(line, Is.EqualTo(
                "2 standing answers to renewing certificates never reached their databases, and go now that "
                + "KeePass exits: " + CertificateDigest.Shorten(First) + " under authority "
                + CertificateDigest.Shorten(Here) + " and " + CertificateDigest.Shorten(Second)
                + " under authority " + CertificateDigest.Shorten(There)));
        }

        private static OwedAnswer Owed(string admissionKey, string databaseId)
        {
            return new OwedAnswer(databaseId, admissionKey, RenewalRefusal.Forever(), null);
        }
    }
}
