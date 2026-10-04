using System;
using System.Collections.Generic;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Pairing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The approval dialog in every state that changes its shape.
    /// </summary>
    /// <remarks>
    /// This is the one window an operator is interrupted by, and the only one they answer under
    /// time pressure, so what it looks like when the list is long or a name is unreasonable matters
    /// more here than anywhere else on the tab. It is also the one surface here that owns its own
    /// size rather than being handed one, so what varies is how much has to fit inside a window
    /// that decides for itself how big to be.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class ApprovalDialogGallery
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

        [Test]
        public void Screenshot()
        {
            // One profile, and every one of them already named by this database: the ordinary
            // request, and the one the others are read against.
            Shoot("approval-01-one-profile", Asking(new[] { "reader" }, new[] { "reader", "ops" }));

            // Several, which is what the list is for.
            Shoot(
                "approval-02-several-profiles",
                Asking(new[] { "reader", "ops", "analyst" }, new[] { "reader", "ops", "analyst" }));

            // One this database has never named. Approving it does two things rather than one, and
            // the mark is the only thing on screen that says so.
            Shoot(
                "approval-03-a-new-profile",
                Asking(new[] { "reader", "invented" }, new[] { "reader" }));

            // Nothing asked for at all, which is a client asking to be admitted to nothing.
            Shoot("approval-04-nothing-asked", Asking(Array.Empty<string>(), new[] { "reader" }));

            // More than the list can show, so it scrolls while the rest of the dialog does not
            // move: the countdown and the buttons are pinned and the list takes what is left.
            Shoot("approval-05-scrolling", Asking(Crowd(14), Crowd(14)));

            // Names at the length the issuer still allows, against a dialog 460 wide.
            Shoot(
                "approval-06-long-names",
                new PairingRequest(
                    "aaaabbbbccccdddd",
                    "A Database With A Name Somebody Really Did Type Into The Box",
                    "abcdef0123456789",
                    "an-agent-with-a-name-that-somebody-really-did-type",
                    new[] { "a-profile-named-at-length", "another-one-just-as-long" },
                    new[] { "a-profile-named-at-length" },
                    "127.0.0.1:60001"));

            // No name for the database, so it is named by its fingerprint alone, which is the state
            // a database that has never been given one is in.
            Shoot(
                "approval-07-unnamed-database",
                new PairingRequest(
                    "aaaabbbbccccdddd",
                    "abcdef01",
                    "agent-invented",
                    new[] { "reader" },
                    "127.0.0.1:60001"));

            // One this database named and removed, which clients still hold. Approving it gives
            // it back to all of them, and the line under the list names each one.
            using (var authority = Fakes.TestAuthority.New())
            using (var first = authority.IssueClient("agent-nonexistent"))
            using (var second = authority.IssueClient("ci-fictitious"))
            {
                var holding = new List<AdmissionRecord>
                {
                    AdmissionRecord.For(first, DateTimeOffset.UtcNow, new[] { "retired" }),
                    AdmissionRecord.For(second, DateTimeOffset.UtcNow, new[] { "retired" })
                };

                Shoot(
                    "approval-08-a-removed-profile",
                    new PairingRequest(
                        "aaaabbbbccccdddd",
                        "Invented Vault",
                        "abcdef01",
                        "agent-invented",
                        new[] { "reader", "retired" },
                        new[] { "reader" },
                        "127.0.0.1:60001",
                        new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "retired", holding } }));
            }

            // A lifetime asked for that is none of the presets, offered as a choice of its own and
            // marked as the one asked for: three digits and the mark, as long as a choice gets.
            Shoot(
                "approval-09-a-lifetime-of-its-own",
                new PairingRequest(
                    "aaaabbbbccccdddd",
                    "Invented Vault",
                    "abcdef01",
                    "agent-invented",
                    new[] { "reader" },
                    new[] { "reader" },
                    "127.0.0.1:60001",
                    null,
                    TimeSpan.FromDays(120)));
        }

        private static void Shoot(string name, PairingRequest request)
        {
            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                Gallery.Shot(dialog, name);
            }
        }

        private static PairingRequest Asking(string[] wanted, string[] known)
        {
            return new PairingRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "abcdef01",
                "agent-invented",
                wanted,
                known,
                "127.0.0.1:60001");
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
