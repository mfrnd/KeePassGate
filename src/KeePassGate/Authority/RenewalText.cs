using System;
using System.Globalization;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>What the text of a version of a standing answer to a renewal reads as, `STORE.md` §618.</summary>
    internal enum RenewalTextKind
    {
        /// <summary>No text the plugin composes: refuses every renewal of its client, and stops nothing.</summary>
        OutOfForm,

        /// <summary>`ask`: the lift, by which the operator is asked again.</summary>
        Ask,

        /// <summary>`refuse:forever`.</summary>
        Forever,

        /// <summary>`refuse:until:` and a moment.</summary>
        Until,
    }

    /// <summary>
    /// The texts of the versions of a standing answer to a renewal and the object they are
    /// versions of, `STORE.md` §618: composed and read in this single place, strictly, and total.
    /// </summary>
    /// <remarks>
    /// **Strict, because a text read generously is an answer nobody gave.** A text is in form only
    /// where composing what was read gives back the same bytes: a moment that is no date, a
    /// fraction of a second, an offset, a letter in lower case, a space or anything after the text
    /// is out of form, and a text out of form refuses its client forever.
    ///
    /// **Total, because the store holds texts nobody of the plugin wrote.** Nothing here throws on
    /// any text, a null among them, and an object that holds anything but a client key is the
    /// object of nothing.
    ///
    /// **A moment rather than a date**, written as the wire writes a moment, `Moments.Wire`, in
    /// UTC to the second, so that the answer a client is given and the text stored are the same
    /// characters, 20 of them.
    /// </remarks>
    internal static class RenewalText
    {
        /// <summary>The whole text of a version that refuses every renewal of its client.</summary>
        public const string Forever = "refuse:forever";

        /// <summary>What the text of a version that refuses until a moment begins with, the moment following it.</summary>
        public const string UntilStart = "refuse:until:";

        /// <summary>The whole text of the lift: the operator is asked at the client's next renewal.</summary>
        public const string Ask = "ask";

        /// <summary>The length of a client key as an object writes it.</summary>
        private const int ClientKeyLength = 64;

        /// <summary>The length of a moment as `Moments.Wire` writes it.</summary>
        private const int MomentLength = 20;

        /// <summary>What an object of an answer begins with: the type `renew` and the end of a type.</summary>
        private static readonly string ObjectStart = StoreKinds.RenewType + StoreKinds.ObjectTypeEnd;

        /// <summary>The object of the standing answers about a client: `renew:` and the client key.</summary>
        public static string ObjectOf(string clientKey)
        {
            return ObjectStart + clientKey;
        }

        /// <summary>
        /// Whether a text is the object of the answers about a client, `renew:` and 64 lowercase
        /// hexadecimal characters, with the client key in <paramref name="clientKey"/>: false for
        /// any other text, which is the object of nothing; total.
        /// </summary>
        public static bool ReadObject(string objectText, out string clientKey)
        {
            clientKey = null;
            if (objectText == null
                || objectText.Length != ObjectStart.Length + ClientKeyLength
                || !objectText.StartsWith(ObjectStart, StringComparison.Ordinal)
                || !IsClientKey(objectText, ObjectStart.Length))
            {
                return false;
            }

            clientKey = objectText.Substring(ObjectStart.Length);
            return true;
        }

        /// <summary>
        /// The text of a refusal that stands: `refuse:forever`, or `refuse:until:` and the moment
        /// as the wire writes it.
        /// </summary>
        /// <exception cref="ArgumentException">For a refusal in this session, which no version holds.</exception>
        public static string Of(RenewalRefusal refusal)
        {
            if (refusal == null || refusal.IsThisSession)
            {
                throw new ArgumentException("Only a refusal forever or until a moment is a version of the store.", nameof(refusal));
            }

            return refusal.NotBefore.HasValue ? UntilStart + Moments.Wire(refusal.NotBefore.Value) : Forever;
        }

        /// <summary>
        /// What a text reads as, with the moment of a refusal until a moment in
        /// <paramref name="moment"/>, and the minimum for every other text; total.
        /// </summary>
        public static RenewalTextKind Read(string text, out DateTimeOffset moment)
        {
            moment = DateTimeOffset.MinValue;
            if (text == null)
            {
                return RenewalTextKind.OutOfForm;
            }

            if (string.Equals(text, Forever, StringComparison.Ordinal))
            {
                return RenewalTextKind.Forever;
            }

            if (string.Equals(text, Ask, StringComparison.Ordinal))
            {
                return RenewalTextKind.Ask;
            }

            DateTimeOffset read;
            if (text.Length == UntilStart.Length + MomentLength
                && text.StartsWith(UntilStart, StringComparison.Ordinal)
                && DateTimeOffset.TryParseExact(
                    text.Substring(UntilStart.Length),
                    Moments.WireFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out read)
                && string.Equals(UntilStart + Moments.Wire(read), text, StringComparison.Ordinal))
            {
                moment = read;
                return RenewalTextKind.Until;
            }

            return RenewalTextKind.OutOfForm;
        }

        /// <summary>Whether 64 lowercase hexadecimal characters begin at <paramref name="start"/>.</summary>
        private static bool IsClientKey(string text, int start)
        {
            if (text.Length < start + ClientKeyLength)
            {
                return false;
            }

            for (var at = start; at < start + ClientKeyLength; at++)
            {
                var c = text[at];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
