using System;
using System.Globalization;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The operator's standing answer to a client renewing one certificate: forever, in this
    /// session, or not before a moment.
    /// </summary>
    /// <remarks>
    /// **Given when the operator refuses a renewal and means more than once**, from the Refuse
    /// button's menu, and asked before the dialog is raised for the next renewal of that
    /// certificate, which is then answered without one for as long as the answer stands.
    /// `PAIRING.md` §312 has the route.
    ///
    /// **Forever and a moment are written down; this session is not.** Those two are kept under
    /// `KPG.RenewalRefused.` and the certificate's admission key, `CA.md` §204. This session is held
    /// in the plugin's memory, in `SessionRefusals`, and goes when KeePass exits, which is what it
    /// means: an answer written into the database would outlast the session it names, in every
    /// copy of the file.
    ///
    /// **A moment rather than a date**, written as every moment on the wire is, so that the answer
    /// a client is given and the one stored are the same text. The dialog asks for a day, and that
    /// day begins at midnight where the operator is: the first midnight, on a day whose clocks
    /// fall back at midnight and so pass it twice.
    ///
    /// **Every one is an answer about this certificate, not this client.** Pairing again is a new
    /// certificate with a record of its own, which nothing here refers to.
    /// </remarks>
    public sealed class RenewalRefusal
    {
        /// <summary>What a refusal forever holds.</summary>
        public const string ForeverValue = "forever";

        private readonly bool _thisSession;
        private readonly DateTimeOffset? _notBefore;

        private RenewalRefusal(bool thisSession, DateTimeOffset? notBefore)
        {
            _thisSession = thisSession;
            _notBefore = notBefore;
        }

        /// <summary>Every renewal of this certificate refused from now on, without asking.</summary>
        public static RenewalRefusal Forever()
        {
            return new RenewalRefusal(false, null);
        }

        /// <summary>
        /// Every renewal of this certificate refused without asking until KeePass exits.
        /// </summary>
        public static RenewalRefusal ThisSession()
        {
            return new RenewalRefusal(true, null);
        }

        /// <summary>
        /// Every renewal of this certificate refused without asking until this moment, kept to the
        /// second as the wire writes it.
        /// </summary>
        public static RenewalRefusal Until(DateTimeOffset notBefore)
        {
            var utc = notBefore.ToUniversalTime();
            return new RenewalRefusal(
                false, new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero));
        }

        /// <summary>Whether this refuses every renewal of the certificate, whenever it is asked.</summary>
        public bool IsForever
        {
            get { return !_thisSession && !_notBefore.HasValue; }
        }

        /// <summary>Whether this refuses every renewal of the certificate until KeePass exits.</summary>
        public bool IsThisSession
        {
            get { return _thisSession; }
        }

        /// <summary>
        /// Whether this is kept in the database, which a refusal in this session never is.
        /// </summary>
        public bool IsWritten
        {
            get { return !_thisSession; }
        }

        /// <summary>
        /// The moment renewals are asked about again, or null for one refused forever or in this
        /// session.
        /// </summary>
        public DateTimeOffset? NotBefore
        {
            get { return _notBefore; }
        }

        /// <summary>Whether a renewal asked for at this moment is refused without a dialog.</summary>
        public bool Refuses(DateTimeOffset now)
        {
            return !_notBefore.HasValue || now < _notBefore.Value;
        }

        /// <summary>The value the key holds.</summary>
        /// <exception cref="InvalidOperationException">For a refusal in this session, which no key holds.</exception>
        public string Write()
        {
            if (_thisSession)
            {
                throw new InvalidOperationException(
                    "A renewal refused in this session is held in memory and never written to a database.");
            }

            return IsForever ? ForeverValue : Moments.Wire(_notBefore.Value);
        }

        /// <summary>A stored value read back, or null when it is not one this plugin writes.</summary>
        public static RenewalRefusal Read(string written)
        {
            if (string.Equals(written, ForeverValue, StringComparison.Ordinal))
            {
                return Forever();
            }

            DateTimeOffset notBefore;
            if (written != null && DateTimeOffset.TryParseExact(
                    written,
                    Moments.WireFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out notBefore))
            {
                return Until(notBefore);
            }

            return null;
        }
    }
}
