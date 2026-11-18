using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The authority chooser in every state that changes its shape.
    /// </summary>
    /// <remarks>
    /// The dialog owns its own size, like the approval dialog, and two lists decide how tall it
    /// is: the authorities, which is two in the ordinary case and more after several merges, and
    /// the clients of the selected one, which is where a long list scrolls while the rest of the
    /// dialog stays where it is. One candidate that will not read changes the line under the
    /// fingerprint into the reason it cannot be kept.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class ChooserDialogGallery
    {
        private static readonly DateTimeOffset Admitted = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        [Test]
        public void Screenshot()
        {
            // Two copies that each recreated, one client on each: the ordinary case.
            Shoot("chooser-01-two-authorities", "Invented Vault", 2, new[] { 1, 1 }, false, true);

            // Three, with a crowd on the first, so its list scrolls.
            Shoot("chooser-02-three-and-a-crowd", "Invented Vault", 3, new[] { 14, 0, 2 }, false, true);

            // One that will not read, selected, next to one that does.
            Shoot("chooser-03-one-unreadable", null, 1, new[] { 1 }, true, true);

            // No audit to read, so every last request is unknown.
            Shoot("chooser-04-no-audit", "Invented Vault", 2, new[] { 3, 1 }, false, false);
        }

        private static void Shoot(
            string name, string databaseName, int authorities, int[] clients, bool unreadable, bool audit)
        {
            var records = new MemoryStore();
            var service = new AuthorityService(records, null);

            // Each authority beside those before it, which is what copies that each recreated
            // leave behind once they are merged.
            var digests = new List<string> { StoreSeeds.Authority(records) };
            for (var i = 1; i < authorities; i++)
            {
                digests.Add(StoreSeeds.Beside(records));
            }

            if (unreadable)
            {
                StoreSeeds.Damaged(records);
            }

            for (var i = 0; i < digests.Count; i++)
            {
                for (var c = 0; c < clients[i]; c++)
                {
                    Admit(records, digests[i], "agent-" + i + "-" + c.ToString("00", CultureInfo.InvariantCulture));
                }
            }

            AuthorityChoice choice;
            using (var reading = service.Read())
            {
                choice = AuthorityChoice.From(
                    reading,
                    StoreSeeds.Listed(records),
                    keys => audit
                        ? keys.Take((keys.Count + 1) / 2).ToDictionary(k => k, k => Admitted.AddDays(19))
                        : null,
                    null);
            }

            using (var dialog = new AuthorityChooserForm(choice, databaseName))
            {
                // The unreadable one when there is one, and otherwise the one with the most clients,
                // which is the list that decides how the lower half looks.
                dialog.Select(unreadable
                    ? StoreSeeds.DamagedId
                    : choice.Options.OrderByDescending(o => o.Clients.Count).First().Fingerprint);

                Gallery.Shot(dialog, name);
            }
        }

        private static void Admit(MemoryStore records, string digest, string client)
        {
            var authority = StoreSeeds.KeyOf(records, digest);
            using (var certificate = new AuthorityIssuer().IssueClient(authority, client, new[] { "ops", "reader" }).Read())
            {
                StoreSeeds.Client(records, AdmissionRecord.For(certificate, Admitted, new[] { "ops", "reader" }, digest).Members());
            }
        }
    }
}
