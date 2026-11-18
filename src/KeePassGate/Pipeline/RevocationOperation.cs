using KeePassGate.Audit;
using KeePassGate.Transport;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// `revokeCertificate`: a client giving up its own certificate, the certificate it presented,
    /// `STORE.md` §648.
    /// </summary>
    /// <remarks>
    /// **Nothing to name, because the certificate on the connection is the only one a client can
    /// be trusted to speak for.** The router has already checked it chains, carries `clientAuth`,
    /// is current and is admitted, so what is taken away here is exactly what was proved, and a
    /// client cannot take away anybody else's.
    ///
    /// **A record of kind `withdrawn` for that single certificate**, made through the single writer
    /// rather than a dialog's buffer since nobody is going to press OK on it, and answered in the
    /// vault writes' words: `200` once it is saved and the file read back, `503 busy` for
    /// everything that would be different in a minute, Database Maintenance open on the database
    /// among them, and `500` with what was left behind. The certificate a renewal issued keeps
    /// working, since the record names it.
    ///
    /// **A certificate no longer admitted when the write is made is answered `401 unpaired`**,
    /// which is what this certificate will be told on every connection from now on. That is the
    /// operator revoking its client in the instant between this connection and its write, and
    /// what the client asked for has happened either way.
    ///
    /// **Written to the audit once the record is in the database**: answered `200`, or
    /// `500 not verified`, which keeps it in the database KeePass holds, so the certificate is
    /// refused from then on whatever the file holds. A record taken back out, or never made, took
    /// nothing away and is not written down, and nor is a record whose database closed inside its
    /// save before KeePass reported a save of it that landed, which no database holds any more
    /// and the file may not. A database that closes takes this connection with it, so the client
    /// is answered nothing then, whatever the write decided. **The writer writes the line**, on
    /// its own path for a record left in force and before anything drawn from the store is told,
    /// so a defect there cannot lose it; this hands it the line and takes the client's turn for it
    /// once the write returns.
    /// </remarks>
    public sealed class RevocationOperation
    {
        private readonly AdmissionAudit _audit;

        /// <summary>
        /// One for every database the gate serves, since the one a revocation is about arrives with
        /// its request.
        /// </summary>
        /// <param name="audit">Where a revocation that took is written down, or null for nowhere.</param>
        public RevocationOperation(AdmissionAudit audit)
        {
            _audit = audit ?? new AdmissionAudit(NullGateAudit.Instance);
        }

        public void Handle(IGateContext context)
        {
            var client = context.Client;
            if (!client.IsPaired || string.IsNullOrEmpty(client.AdmissionKey))
            {
                // The router refuses a caller with no admission before any handler runs, so this
                // is a defect if it is reached, and it is answered as the caller would be anyway.
                context.Respond(401, HttpResponse.ErrorBody(Unpaired));
                return;
            }

            var target = ServedDatabase.TargetOf(context);
            if (target == null)
            {
                // A connection with no database behind it. A locked one never reaches a route,
                // so this is this plugin failing rather than the request being wrong.
                context.Respond(500, HttpResponse.ErrorBody(InternalError));
                return;
            }

            var databaseId = target.DatabaseId;
            var key = client.AdmissionKey;
            var clientKey = client.ClientKey;
            var written = target.GiveUp(key, () => _audit.RevokedItself(databaseId, key, clientKey));
            if (written == null)
            {
                context.Respond(500, HttpResponse.ErrorBody(InternalError));
                return;
            }

            if (written.Ok)
            {
                _audit.TakeTurn(clientKey);
                context.Respond(200, "{\"ok\":true}");
                return;
            }

            if (written.IsBusy)
            {
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return;
            }

            if (written.IsUnsaved)
            {
                context.Respond(500, HttpResponse.ErrorBody("not saved"));
                return;
            }

            if (written.IsUnverified)
            {
                _audit.TakeTurn(clientKey);
                context.Respond(500, HttpResponse.ErrorBody("not verified"));
                return;
            }

            if (written.IsFailed)
            {
                context.Respond(500, HttpResponse.ErrorBody(InternalError));
                return;
            }

            // Nothing there to revoke, which is the record having gone since this connection
            // was admitted. The certificate is refused from here on, as asked.
            context.Respond(401, HttpResponse.ErrorBody(Unpaired));
        }

        /// <summary>What every caller with no admission is told, on every path there is.</summary>
        private const string Unpaired = "unpaired";

        private const string InternalError = "internal error";
    }
}
