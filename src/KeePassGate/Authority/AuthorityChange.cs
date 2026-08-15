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

        private AuthorityChange(bool succeeded, string message, int recordsRemoved)
        {
            _succeeded = succeeded;
            _message = message ?? string.Empty;
            _recordsRemoved = recordsRemoved;
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

        public static AuthorityChange Done(string message)
        {
            return new AuthorityChange(true, message, 0);
        }

        public static AuthorityChange Done(string message, int recordsRemoved)
        {
            return new AuthorityChange(true, message, recordsRemoved);
        }

        public static AuthorityChange Refused(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw new ArgumentException("A refusal says why.", nameof(reason));
            }

            return new AuthorityChange(false, reason, 0);
        }
    }
}
