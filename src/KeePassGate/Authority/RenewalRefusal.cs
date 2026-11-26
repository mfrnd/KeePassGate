using System;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The operator's standing answer to a client's renewals: forever, in this session, or not
    /// before a moment.
    /// </summary>
    /// <remarks>
    /// **Given when the operator refuses a renewal and means more than once**, from the Refuse
    /// button's menu, and asked before the dialog is raised for the next renewal by any
    /// certificate of that client, which is then answered without one for as long as the answer
    /// stands. `PAIRING.md` §312 has the route.
    ///
    /// **Forever and a moment are written down; this session is not.** Those two are versions of
    /// `renew:` and the client key in the plugin's store, composed and read by `RenewalText`,
    /// `STORE.md` §618. This session is held in the plugin's memory, in `SessionRefusals`, and goes
    /// when KeePass exits, which is what it means: an answer written into the database would
    /// outlast the session it names, in every copy of the file.
    ///
    /// **A moment rather than a date**, written as every moment on the wire is, so that the answer
    /// a client is given and the one stored are the same text. The dialog asks for a day, and that
    /// day begins at midnight where the operator is: the first midnight, on a day whose clocks
    /// fall back at midnight and so pass it twice.
    ///
    /// **Every one is an answer about a client**, every certificate it holds, a certificate a
    /// renewal issued among them. Pairing again is a new client with a client key of its own,
    /// which nothing here refers to.
    /// </remarks>
    public sealed class RenewalRefusal
    {
        private readonly bool _thisSession;
        private readonly DateTimeOffset? _notBefore;

        private RenewalRefusal(bool thisSession, DateTimeOffset? notBefore)
        {
            _thisSession = thisSession;
            _notBefore = notBefore;
        }

        /// <summary>Every renewal of the client refused from now on, without asking.</summary>
        public static RenewalRefusal Forever()
        {
            return new RenewalRefusal(false, null);
        }

        /// <summary>Every renewal of the client refused without asking until KeePass exits.</summary>
        public static RenewalRefusal ThisSession()
        {
            return new RenewalRefusal(true, null);
        }

        /// <summary>
        /// Every renewal of the client refused without asking until this moment, kept to the
        /// second as the wire writes it.
        /// </summary>
        public static RenewalRefusal Until(DateTimeOffset notBefore)
        {
            var utc = notBefore.ToUniversalTime();
            return new RenewalRefusal(
                false, new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero));
        }

        /// <summary>Whether this refuses every renewal of the client, whenever it is asked.</summary>
        public bool IsForever
        {
            get { return !_thisSession && !_notBefore.HasValue; }
        }

        /// <summary>
        /// Whether this refuses every renewal of the client until KeePass exits, which no version
        /// of the store holds.
        /// </summary>
        public bool IsThisSession
        {
            get { return _thisSession; }
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
    }
}
