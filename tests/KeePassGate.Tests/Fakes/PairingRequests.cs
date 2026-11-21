using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Store;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Pairing requests as the handler makes them, each reading a store a test composed, and shown
    /// as the screen shows a request as its dialog comes up: what the dialog's tests build their
    /// dialogs from, so that a dialog standing in a test reads the store on its ticks as the
    /// product's dialog does.
    /// </summary>
    /// <remarks>
    /// **The composing is the product's own acts**, `StoreSeeds`: a name on the list is a name
    /// added, a name off it that the dialog shows restored is a name removed or held by an
    /// admission, and a name off it that nothing holds and nothing removed is a name the store has
    /// never known.
    /// </remarks>
    internal static class PairingRequests
    {
        /// <summary>A store serving an authority, with these names on its list.</summary>
        public static MemoryStore Listing(params string[] listed)
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            foreach (var name in listed)
            {
                StoreSeeds.Name(store, name);
            }

            return store;
        }

        /// <summary>
        /// This client admitted holding these names, as a pairing's write admits it, which puts
        /// each name on the list where the list does not hold it.
        /// </summary>
        public static void Holding(IGateStore store, X509Certificate2 client, params string[] names)
        {
            StoreSeeds.Admit(store, client, DateTimeOffset.UtcNow, names);
        }

        /// <summary>
        /// These names taken off the list, as the Profiles page takes a name off it, each put on it
        /// first where it is not, so that the store holds a removal of each.
        /// </summary>
        public static void Taken(IGateStore store, params string[] names)
        {
            foreach (var name in names)
            {
                using (var reading = store.Read())
                {
                    if (!ProfileNames.Of(reading).Holds(name))
                    {
                        StoreSeeds.Name(store, name);
                    }
                }

                StoreSeeds.Unname(store, name);
            }
        }

        /// <summary>
        /// What a dialog for these profiles reads of this store now, under the authority the store
        /// serves now.
        /// </summary>
        public static AskedProfiles Read(IGateStore store, IReadOnlyCollection<string> profiles)
        {
            return Read(store, StoreSeeds.Served(store), profiles);
        }

        /// <summary>
        /// What a dialog for these profiles reads of this store now under this authority, judged
        /// by the member the handler's target judges a reading by, `StoreReadings.Asked`: a store
        /// that no longer serves the authority answers its word, as the handler's reading does.
        /// </summary>
        private static AskedProfiles Read(IGateStore store, string authority, IReadOnlyCollection<string> profiles)
        {
            using (var reading = store.Read())
            {
                return StoreReadings.Asked(reading, authority, profiles, DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// The request for these profiles as the handler makes it, under the authority the store
        /// serves as the request is made, every reading of it a reading of this store, and not yet
        /// shown.
        /// </summary>
        public static PairingRequest Asking(
            IGateStore store,
            IReadOnlyList<string> profiles,
            string name = "agent-fictitious",
            TimeSpan? lifetime = null,
            PairingApproval earlier = null,
            AskedProfiles before = null,
            string databaseName = "Invented Vault",
            string invitationId = "abcdef01")
        {
            var asked = (profiles ?? Array.Empty<string>()).ToArray();
            var authority = StoreSeeds.Served(store);
            return new PairingRequest(
                "aaaabbbbccccdddd", databaseName, invitationId, name, asked, "127.0.0.1:60001", lifetime, earlier, before, null,
                () => Read(store, authority, asked));
        }

        /// <summary>
        /// The same request shown: its content read once, as the screen reads it in the turn that
        /// puts the dialog up. A dialog shown again comes up only where a row differs from the
        /// dialog before, `PairingRequest.Differs`, so that a request shown again in which no row
        /// differs is refused here, as the product never puts such a dialog up.
        /// </summary>
        public static PairingRequest Shown(
            IGateStore store,
            IReadOnlyList<string> profiles,
            string name = "agent-fictitious",
            TimeSpan? lifetime = null,
            PairingApproval earlier = null,
            AskedProfiles before = null,
            string databaseName = "Invented Vault",
            string invitationId = "abcdef01")
        {
            var request = Asking(store, profiles, name, lifetime, earlier, before, databaseName, invitationId);
            var shown = request.Showing(request.Read());
            if (shown.Again && !shown.Differs)
            {
                throw new InvalidOperationException("A dialog is shown again only where a row differs from the dialog before.");
            }

            return shown;
        }
    }
}
