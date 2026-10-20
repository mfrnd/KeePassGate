using System;
using System.Threading;
using KeePassGate.Pairing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The renewal dialog in every state that changes its shape, and the question of a day that
    /// its Refuse button can ask.
    /// </summary>
    /// <remarks>
    /// The pairing dialog's sibling, so the same things vary: how much the list holds and how long
    /// the names are. What it adds is a line for the profiles not carried over and the arrow on
    /// Refuse. The menu the arrow opens is a window of its own, which a drawing of the dialog does
    /// not hold; the question of a day is drawn on its own, for a certificate with weeks left and
    /// for one ending within the week, where the day it starts on is the certificate's last day.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class RenewalDialogGallery
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

        private static readonly DateTime Today = new DateTime(2026, 10, 3);

        [Test]
        public void Screenshot()
        {
            // The ordinary renewal: one profile, asking for nothing, with months left.
            Shoot("renewal-01-one-profile", Asking(new[] { "reader" }, Array.Empty<string>(), 120, null));

            // Several, a lifetime asked for, and one name the database no longer holds.
            Shoot(
                "renewal-02-a-profile-not-carried-over",
                Asking(new[] { "reader", "ops" }, new[] { "retired" }, 20, TimeSpan.FromDays(30)));

            // Nothing left to carry, which renews into a certificate that reaches nothing.
            Shoot("renewal-03-nothing-held", Asking(Array.Empty<string>(), new[] { "retired" }, 20, null));

            // More than the list shows, and names at the length the issuer still allows.
            Shoot(
                "renewal-04-crowded",
                new RenewalRequest(
                    "aaaabbbbccccdddd",
                    "A Database With A Name Somebody Really Did Type Into The Box",
                    "an-agent-with-a-name-that-somebody-really-did-type",
                    new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero),
                    Crowd(14),
                    new[] { "a-profile-named-at-length", "another-one-just-as-long" },
                    "127.0.0.1:60001",
                    TimeSpan.FromDays(120)));

            // The question of a day, with weeks to choose from and a week on already chosen.
            Ask("renewal-05-a-day-weeks-ahead", 40);

            // And with three days left, where the week on is past the end and the last day is chosen.
            Ask("renewal-06-a-day-within-the-week", 3);
        }

        private static void Shoot(string name, RenewalRequest request)
        {
            using (var dialog = new RenewalApprovalForm(request, Timeout, null, Today, null))
            {
                Gallery.Shot(dialog, name);
            }
        }

        private static void Ask(string name, int daysLeft)
        {
            var first = Today.AddDays(1);
            var last = Today.AddDays(daysLeft);
            var week = first.AddDays(6);

            using (var asking = new RefuseUntilForm(first, last, week <= last ? week : last, null))
            {
                Gallery.Shot(asking, name);
            }
        }

        private static RenewalRequest Asking(string[] held, string[] dropped, int daysLeft, TimeSpan? lifetime)
        {
            return new RenewalRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "agent-invented",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(Today.AddDays(daysLeft).AddHours(12), TimeSpan.Zero),
                held,
                dropped,
                "127.0.0.1:60001",
                lifetime);
        }

        private static string[] Crowd(int count)
        {
            var many = new string[count];

            for (var i = 0; i < count; i++)
            {
                many[i] = "profile-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            }

            return many;
        }
    }
}
