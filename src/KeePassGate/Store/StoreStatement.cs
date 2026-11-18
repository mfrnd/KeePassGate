using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>
    /// A statement that took a certificate away, `STORE.md` §648: a record of kind `withdrawn`
    /// present, an item of that kind, or a deletion record that tells of such a record and that
    /// nothing present answers for, which names no renewal and holds no origin.
    /// </summary>
    internal sealed class StoreStatement
    {
        private readonly HashSet<string> _renewed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>A statement of what a record, an item or a deletion record holds.</summary>
        /// <param name="renewed">
        /// The text of its `renewed`: admission keys separated by single spaces.
        /// </param>
        /// <param name="from">The text of its `from`, or null where it holds none.</param>
        public StoreStatement(string renewed, string from)
        {
            _renewed.UnionWith((renewed ?? string.Empty).Split(' ').Where(key => key.Length > 0));

            From = from;
        }

        /// <summary>
        /// The certificate of origin its `from` holds, or null where it holds none.
        /// </summary>
        public string From { get; private set; }

        /// <summary>
        /// Whether its `renewed` names this certificate, which lets that renewal count.
        /// </summary>
        public bool Names(string certificate)
        {
            return certificate != null && _renewed.Contains(certificate);
        }
    }
}
