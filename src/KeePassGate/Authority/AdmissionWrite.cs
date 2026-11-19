namespace KeePassGate.Authority
{
    /// <summary>
    /// Whether an admission record reached the database, and why not when it did not.
    /// </summary>
    /// <remarks>
    /// It exists because pairing may not hand a certificate over until the record behind it is
    /// durable. A record that was written and not saved, or written into the database's own custom
    /// data while the Database Maintenance dialog holds a copy of it to put back, is lost after its
    /// certificate has been handed over, so every one of those has to come back as a refusal rather
    /// than as a silence.
    ///
    /// The reason is for the operator's log. What the client is told is a status and nothing else,
    /// because an unpaired caller has no business learning what is open on somebody's screen.
    /// </remarks>
    public sealed class AdmissionWrite
    {
        private readonly bool _ok;
        private readonly bool _busy;
        private readonly bool _unserved;
        private readonly string _reason;

        private AdmissionWrite(bool ok, bool busy, bool unserved, string reason)
        {
            _ok = ok;
            _busy = busy;
            _unserved = unserved;
            _reason = reason ?? string.Empty;
        }

        public bool Ok
        {
            get { return _ok; }
        }

        /// <summary>
        /// True when the answer would be different in a minute, as it is while the Database
        /// Maintenance dialog holds its copy of the records or another pairing holds the writer.
        /// Told apart from a failure because it is worth retrying and a failure is not.
        /// </summary>
        public bool IsBusy
        {
            get { return _busy; }
        }

        /// <summary>
        /// True when the authority that issued the certificate is not served and no decision of
        /// the operator's can serve it, found before anything was touched or asked: the store is
        /// whole and serves another authority or none, or several authorities stand and it is not
        /// among them. The authority the operator answered under was retired since, by a Recreate,
        /// a Delete or a choice between authorities, here or in a copy merged in, and no retry
        /// under it can be written, `PAIRING.md` §311. A store holding the gate back, and several
        /// authorities standing with the issuer among them, are answered <see cref="Word"/>
        /// instead.
        /// </summary>
        public bool IsUnserved
        {
            get { return _unserved; }
        }

        /// <summary>
        /// The word the write met the database with, or null: the word the plugin's store holds
        /// the gate back with, `STORE.md` §656, or `undecided` where several authorities stand,
        /// the issuer among them, answered under `503` as a connection is, with nothing touched
        /// or asked.
        /// </summary>
        public string Word { get; private set; }

        public string Reason
        {
            get { return _reason; }
        }

        public static AdmissionWrite Done()
        {
            return new AdmissionWrite(true, false, false, "written and saved");
        }

        public static AdmissionWrite Failed(string reason)
        {
            return new AdmissionWrite(false, false, false, reason);
        }

        public static AdmissionWrite Busy(string reason)
        {
            return new AdmissionWrite(false, true, false, reason);
        }

        public static AdmissionWrite Unserved(string reason)
        {
            return new AdmissionWrite(false, false, true, reason);
        }

        /// <summary>
        /// The database is answered this word as a connection is, `STORE.md` §656: the store's
        /// word, or `undecided` where several authorities stand, the issuer among them.
        /// </summary>
        public static AdmissionWrite Unavailable(string word)
        {
            return new AdmissionWrite(false, false, false, "the database is answered " + word) { Word = word };
        }
    }
}
