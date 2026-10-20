using KeePassGate.Authority;
using KeePassGate.Transport;
using Newtonsoft.Json;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// `getStatus`: that the gate is serving, and which database this client reached.
    /// </summary>
    /// <remarks>
    /// **It reads nothing out of the database, and that is the whole design of it.** Which is
    /// why it declares `x-kpg-verb: none`, as the two pairing routes do: no grant could allow or
    /// refuse it, because it reaches nothing a grant is about. It exists to prove a connection
    /// rather than to be useful. What it answers is what the connection already
    /// established: the gate is listening, this certificate is still admitted, and the database is
    /// unlocked, because a locked one never reaches an operation at all.
    ///
    /// The database is named by the first characters of its authority's digest, which says which
    /// database without saying anything about it.
    /// </remarks>
    public sealed class StatusOperation
    {
        private readonly string _version;

        public StatusOperation(string version)
        {
            _version = version ?? string.Empty;
        }

        public void Handle(IGateContext context)
        {
            var database = CertificateDigest.Shorten(context.Client.DatabaseId ?? string.Empty);

            context.Respond(200,
                "{\"ok\":true,\"database\":" + JsonConvert.ToString(database) +
                ",\"version\":" + JsonConvert.ToString(_version) + "}");
        }
    }
}
