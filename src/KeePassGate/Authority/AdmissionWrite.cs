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
        /// True when the authority that issued the certificate is not the authority the store
        /// serves alone, found by the write's plan before anything was touched or asked: an
        /// authority retired by a Recreate or a Delete after the operator answered, an
        /// authority standing beside another, or a store that holds the gate back,
        /// `PAIRING.md` §311.
        /// </summary>
        public bool IsUnserved
        {
            get { return _unserved; }
        }

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
    }
}
