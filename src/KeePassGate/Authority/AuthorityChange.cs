using System;

namespace KeePassGate.Authority
{
    /// <summary>
    /// What happened when the operator asked for something: done, or refused and why. A result
    /// rather than an exception, because a refusal is put in front of the operator, on the KPG tab
    /// or by the authority chooser, and none of them may reach KeePass.
    /// </summary>
    /// <remarks>
    /// **A change that was made carries no words.** The tab shows what changed in what it draws
    /// next, and what recreating or deleting costs is in the question it asks before either, which
    /// is the only moment that number is any use to the operator. A sentence composed here for a
    /// change that was made had nobody to read it.
    /// </remarks>
    public sealed class AuthorityChange
    {
        private readonly bool _succeeded;
        private readonly string _message;
        private readonly bool _behindAnotherWrite;
        private readonly bool _overtaken;

        private AuthorityChange(bool succeeded, string message, bool behindAnotherWrite, bool overtaken)
        {
            _succeeded = succeeded;
            _message = message ?? string.Empty;
            _behindAnotherWrite = behindAnotherWrite;
            _overtaken = overtaken;
        }

        public bool Succeeded
        {
            get { return _succeeded; }
        }

        /// <summary>Why it was refused, which is what the operator is shown. Empty when it was done.</summary>
        public string Message
        {
            get { return _message; }
        }

        /// <summary>
        /// Whether this was refused because another write held the writer, which lets it go once
        /// its save ends, so that asking again is all it takes.
        /// </summary>
        public bool IsBehindAnotherWrite
        {
            get { return _behindAnotherWrite; }
        }

        /// <summary>
        /// Whether an answer to which authority to keep was refused because the database no longer
        /// holds the authorities the answer was given about, so that trying the same answer again
        /// is no use and the question has to be put again.
        /// </summary>
        public bool IsOvertaken
        {
            get { return _overtaken; }
        }

        public static AuthorityChange Done()
        {
            return new AuthorityChange(true, null, false, false);
        }

        public static AuthorityChange Refused(string reason)
        {
            return new AuthorityChange(false, Reason(reason), false, false);
        }

        /// <summary>Refused because another write held the writer, <see cref="IsBehindAnotherWrite"/>.</summary>
        public static AuthorityChange BehindAnotherWrite(string reason)
        {
            return new AuthorityChange(false, Reason(reason), true, false);
        }

        /// <summary>Refused because the authorities changed under the question, <see cref="IsOvertaken"/>.</summary>
        public static AuthorityChange Overtaken(string reason)
        {
            return new AuthorityChange(false, Reason(reason), false, true);
        }

        private static string Reason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw new ArgumentException("A refusal says why.", nameof(reason));
            }

            return reason;
        }
    }
}
