using System;
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

        public ServedDatabase(IVault vault)
        {
            if (vault == null)
            {
                throw new ArgumentNullException(nameof(vault));
            }

            _vault = vault;
        }

        public IVault Vault
        {
            get { return _vault; }
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
    }
}
