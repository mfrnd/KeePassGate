using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The certificates a test issued to its clients, in the order issued, every certificate let
    /// go together as the test ends.
    /// </summary>
    internal sealed class IssuedClients : List<X509Certificate2>, IDisposable
    {
        /// <summary>These certificates, as they are issued.</summary>
        public IssuedClients(IEnumerable<X509Certificate2> issued)
            : base(issued)
        {
        }

        public void Dispose()
        {
            foreach (var certificate in this)
            {
                certificate.Dispose();
            }
        }
    }
}
