using System;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// What the operator is told, once the renewal dialog has closed, of a standing answer that
    /// was not kept as given.
    /// </summary>
    /// <remarks>
    /// **Two cases and no others**, `PAIRING.md` §312. An answer the writer did not take, or took
    /// and took back out because its save did not land, holds for this session instead, and the
    /// operator is told so with the writer's reason. An answer whose save ran over a file that
    /// does not hold what KeePass wrote stays in the open database, and the operator is told that
    /// the save was not confirmed. Each names no client: the dialog that was just answered is
    /// what the message is about.
    /// </remarks>
    internal static class RenewalAnswerNotice
    {
        /// <summary>The start of the message for an answer that was not written.</summary>
        public const string NotWrittenLead = "The answer to the renewal was not written: ";

        /// <summary>The second line of that message.</summary>
        public const string StaysRefused = "Renewal stays refused until KeePass exits.";

        /// <summary>The start of the message for an answer whose save was not confirmed.</summary>
        public const string NotConfirmedLead = "The save of the answer to the renewal could not be confirmed: ";

        /// <summary>The message for an answer that holds for this session instead.</summary>
        /// <param name="reason">Why the writer did not take it, as the log names it.</param>
        public static string NotWritten(string reason)
        {
            return NotWrittenLead + Sentence(reason) + Environment.NewLine + StaysRefused;
        }

        /// <summary>The message for an answer left in the database over a file that disagrees.</summary>
        public static string NotConfirmed(string reason)
        {
            return NotConfirmedLead + Sentence(reason);
        }

        /// <summary>A reason ended as a sentence, and a missing reason reported as missing.</summary>
        private static string Sentence(string reason)
        {
            var text = string.IsNullOrWhiteSpace(reason) ? "nothing was reported" : reason.Trim();
            return text.EndsWith(".", StringComparison.Ordinal) ? text : text + ".";
        }
    }
}
