using System;
using KeePassGate.Gate;
using KeePassGate.Transport;
using KeePassGate.Vault;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// The database this connection reached, as the API's handlers use it.
    /// </summary>
    /// <remarks>
    /// It travels in <see cref="IGateContext.Items"/> for the same reason a pairing connection
    /// does: the transport knows which database a name resolved to, and the handlers need it, and
    /// the seam between them stays the shape `DESIGN.md` §103 describes. A route that has no
    /// business touching a database simply never asks for this.
    ///
    /// Absent on a connection to a locked database, which the router refuses before any handler
    /// runs, and absent in a test that is about something else.
    /// </remarks>
    public sealed class ServedDatabase
    {
        public const string Key = "kpg.database";

        private readonly IVault _vault;
        private readonly IGateTarget _target;

        public ServedDatabase(IVault vault)
            : this(vault, null)
        {
        }

        /// <param name="vault">
        /// What the API's routes read and change, or null where the database is served without one,
        /// which a test about the transport is.
        /// </param>
        /// <param name="target">
        /// The database as the transport serves it, which is what holds its admission records and
        /// writes them. Null where a test is about the vault alone. One of the two is required.
        /// </param>
        public ServedDatabase(IVault vault, IGateTarget target)
        {
            if (vault == null && target == null)
            {
                throw new ArgumentNullException(nameof(vault));
            }

            _vault = vault;
            _target = target;
        }

        public IVault Vault
        {
            get { return _vault; }
        }

        /// <summary>The database's records, for a route about the caller's own admission.</summary>
        public IGateTarget Target
        {
            get { return _target; }
        }

        public void PutOn(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Items[Key] = this;
        }

        /// <summary>
        /// The vault for this request, or null when there is none.
        /// </summary>
        /// <remarks>
        /// Null rather than an exception, because a handler reaching for one that is not there has
        /// to answer rather than fail: what it answers is that there is nothing there, which is
        /// what every other absence answers too.
        /// </remarks>
        public static IVault Of(IGateContext context)
        {
            if (context == null)
            {
                return null;
            }

            object found;
            if (!context.Items.TryGetValue(Key, out found))
            {
                return null;
            }

            var served = found as ServedDatabase;
            return served == null ? null : served.Vault;
        }

        /// <summary>The database's records for this request, or null when there are none.</summary>
        public static IGateTarget TargetOf(IGateContext context)
        {
            if (context == null)
            {
                return null;
            }

            object found;
            if (!context.Items.TryGetValue(Key, out found))
            {
                return null;
            }

            var served = found as ServedDatabase;
            return served == null ? null : served.Target;
        }

        /// <summary>
        /// Answers `503 locked` when the database this request reached has locked or closed since,
        /// and says whether it did.
        /// </summary>
        /// <remarks>
        /// `openapi.yaml` §502 is the rule. The gate answers a database that is locked when a
        /// request arrives before routing it, and a lock or a close can land at any later step:
        /// while the request is still being read, before the router looks its node up, or between
        /// the router's decision and the handler's read. Every lookup then finds nothing, and a
        /// node in a database that has locked has not gone, so a client told `404` would conclude
        /// something untrue. It is answered as the gate answers it, in the same word, and nothing
        /// about it is recorded, because no rule decided it.
        ///
        /// Asked only once a lookup has found nothing, since that is the only moment the question
        /// changes an answer. Where no database was reached, as on a route that reads none, there
        /// is nothing to ask and nothing is answered.
        /// </remarks>
        public static bool TryAnswerLocked(IGateContext context)
        {
            var vault = Of(context);
            if (vault == null || vault.IsOpen)
            {
                return false;
            }

            context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
            return true;
        }
    }
}
