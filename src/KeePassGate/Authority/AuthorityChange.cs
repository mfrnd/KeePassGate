using System;

namespace KeePassGate.Authority
{
    /// <summary>
    /// What happened when the operator asked for something. A result rather than an exception,
    /// because every one of these is reported in a dialog and none of them may reach KeePass.
    /// </summary>
    public sealed class AuthorityChange
    {
        private readonly bool _succeeded;
        private readonly string _message;
        private readonly int _recordsRemoved;
        private readonly bool _behindAnotherWrite;

        private AuthorityChange(bool succeeded, string message, int recordsRemoved, bool behindAnotherWrite)
        {
            _succeeded = succeeded;
            _message = message ?? string.Empty;
            _recordsRemoved = recordsRemoved;
            _behindAnotherWrite = behindAnotherWrite;
        }

        public bool Succeeded
        {
            get { return _succeeded; }
        }

        /// <summary>What to show the operator. Never empty.</summary>
        public string Message
        {
            get { return _message; }
        }

        /// <summary>How many clients lost their admission, which a message has to be able to state.</summary>
        public int RecordsRemoved
        {
            get { return _recordsRemoved; }
        }

        /// <summary>
        /// Whether this was refused because another write held the writer, which lets it go once
        /// its save ends, so that asking again is all it takes.
        /// </summary>
        public bool IsBehindAnotherWrite
        {
            get { return _behindAnotherWrite; }
        }

        public static AuthorityChange Done(string message)
        {
            return new AuthorityChange(true, message, 0, false);
        }

        public static AuthorityChange Done(string message, int recordsRemoved)
        {
            return new AuthorityChange(true, message, recordsRemoved, false);
        }

        public static AuthorityChange Refused(string reason)
        {
            return new AuthorityChange(false, Reason(reason), 0, false);
        }

        /// <summary>Refused because another write held the writer, <see cref="IsBehindAnotherWrite"/>.</summary>
        public static AuthorityChange BehindAnotherWrite(string reason)
        {
            return new AuthorityChange(false, Reason(reason), 0, true);
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
