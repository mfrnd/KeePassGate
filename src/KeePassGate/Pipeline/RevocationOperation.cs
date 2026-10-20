using KeePassGate.Transport;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// `revokeCertificate`: a client giving up its own admission, by the certificate it presented.
    /// </summary>
    /// <remarks>
    /// **Nothing to name, because the certificate on the connection is the only one a client can
    /// be trusted to speak for.** The router has already checked it chains, carries `clientAuth`,
    /// is current and has a record with no revocation beside it, so what is revoked here is exactly
    /// what was proved, and a client cannot revoke anybody else.
    ///
    /// **The write is the Clients page's**, a revocation beside the record and the record emptied,
    /// made through the single writer rather than a dialog's buffer since nobody is going to press
    /// OK on it, and answered in the vault writes' words: `200` once the revocation is saved and the
    /// file read back, `503 busy` for everything that would be different in a minute, Database
    /// Maintenance open on the database among them, and `500` with what was left behind.
    ///
    /// **A record that went before the write could make it is answered `401 unpaired`**, which is
    /// what this certificate will be told on every connection from now on. That is the operator
    /// revoking it from the Clients page in the instant between this connection and its write,
    /// and what the client asked for has happened either way.
    /// </remarks>
    public static class RevocationOperation
    {
        public static void Handle(IGateContext context)
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

            var written = target.Revoke(client.AdmissionKey);
            if (written == null)
            {
                context.Respond(500, HttpResponse.ErrorBody(InternalError));
                return;
            }

            if (written.Ok)
            {
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
