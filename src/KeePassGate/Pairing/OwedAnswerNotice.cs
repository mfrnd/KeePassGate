using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// What the operator is told as KeePass exits about standing answers to renewals that no
    /// database took, which go with it.
    /// </summary>
    /// <remarks>
    /// **What is known and nothing more.** Memory holds the certificate each answer is about and
    /// the authority it was given under, and not why it was never written: an answer is owed when
    /// its write did not land for any reason, KeePass busy among them. So the certificates are
    /// named, by the key the log names them by, and no cause is given; and the operator is told a
    /// renewal of each may ask again, which is all that follows, since a client that stops asking
    /// is never asked about.
    /// </remarks>
    internal static class OwedAnswerNotice
    {
        /// <summary>
        /// The line for the log, naming each certificate and the authority it is under.
        /// </summary>
        public static string LogLine(IReadOnlyList<OwedAnswer> owed)
        {
            var named = owed.Select(answer => CertificateDigest.Shorten(answer.AdmissionKey)
                                              + " under authority " + CertificateDigest.Shorten(answer.DatabaseId));
            return (owed.Count == 1
                       ? "a standing answer to renewing a certificate never reached its database, and goes"
                       : owed.Count + " standing answers to renewing certificates never reached their "
                         + "databases, and go")
                   + " now that KeePass exits: " + Listed(named);
        }

        /// <summary>The message put in front of the operator.</summary>
        public static string Message(IReadOnlyList<OwedAnswer> owed)
        {
            var keys = Listed(owed.Select(answer => CertificateDigest.Shorten(answer.AdmissionKey)));
            if (owed.Count == 1)
            {
                return "A standing answer to renewing a certificate was never saved to its database and is "
                       + "lost now: " + keys + ". That client may be asked about renewing again.";
            }

            var databases = owed.Select(answer => answer.DatabaseId).Distinct().Count();
            return owed.Count + " standing answers to renewing certificates were never saved to their "
                   + (databases == 1 ? "database" : "databases") + " and are lost now: " + keys
                   + ". Those clients may be asked about renewing again.";
        }

        /// <summary>
        /// Names in a sentence: a single name alone, or commas between all but the last two, and
        /// "and" between those.
        /// </summary>
        private static string Listed(IEnumerable<string> names)
        {
            var all = names.ToList();
            return all.Count <= 1
                ? string.Concat(all)
                : string.Join(", ", all.Take(all.Count - 1)) + " and " + all[all.Count - 1];
        }
    }
}
