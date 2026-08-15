using System;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The invitations a settings dialog makes, which are good at once and taken back unless the
    /// dialog is accepted.
    /// </summary>
    /// <remarks>
    /// An invitation never reaches the database, so the buffer the rest of the tab writes into
    /// cannot hold it back, and Cancel used to leave a copied code good for its whole lifetime.
    /// What these settle is that Cancel takes back exactly what was made on that dialog, that OK
    /// keeps it, and that a code somebody already used stays used.
    /// </remarks>
    [TestFixture]
    public sealed class PendingInvitationsTests
    {
        private const string Database = "aaaabbbbccccdddd";

        private InvitationBook _book;

        [SetUp]
        public void SetUp()
        {
            _book = new InvitationBook(TimeSpan.FromMinutes(10), null, new RecordingGateLog());
        }

        [TearDown]
        public void TearDown()
        {
            _book.Dispose();
        }

        [Test]
        public void A_code_made_on_the_dialog_is_good_before_the_dialog_is_answered()
        {
            var pending = new PendingInvitations(_book);

            var invitation = pending.Invite(Database);

            Assert.That(_book.Matching(invitation.NewName()), Has.Count.EqualTo(1),
                "a program given the code while the dialog is open can use it");
        }

        [Test]
        public void Discarding_takes_back_what_was_made_on_the_dialog()
        {
            var pending = new PendingInvitations(_book);
            pending.Invite(Database);

            Assert.That(pending.Discard(), Is.EqualTo(1));

            Assert.That(_book.Outstanding(), Is.Empty);
        }

        [Test]
        public void Applying_keeps_it_and_a_later_discard_takes_nothing()
        {
            var pending = new PendingInvitations(_book);
            var invitation = pending.Invite(Database);

            pending.Apply();

            Assert.That(pending.Discard(), Is.EqualTo(0));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
            Assert.That(_book.Outstanding()[0].Id, Is.EqualTo(invitation.Id));
        }

        [Test]
        public void A_code_made_elsewhere_is_left_alone()
        {
            // Made on an earlier dialog that was accepted, or for another database on another
            // dialog. Only what this dialog made is this dialog's to take back.
            var earlier = _book.Invite("some other database");
            var pending = new PendingInvitations(_book);
            pending.Invite(Database);

            pending.Discard();

            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
            Assert.That(_book.Outstanding()[0].Id, Is.EqualTo(earlier.Id));
        }

        [Test]
        public void A_code_somebody_already_used_stays_used()
        {
            var pending = new PendingInvitations(_book);
            var invitation = pending.Invite(Database);
            Assert.That(_book.Burn(invitation.Id), Is.True);

            Assert.That(pending.Discard(), Is.EqualTo(0),
                "the pairing it led to is the database's, and a revocation is what takes it back");
        }

        [Test]
        public void Renewing_and_then_discarding_leaves_no_code_at_all()
        {
            // The code renewing replaced does not come back: its operator has stopped expecting
            // anybody to use it.
            var before = _book.Invite(Database);
            var pending = new PendingInvitations(_book);
            pending.Invite(Database);

            pending.Discard();

            Assert.That(_book.Outstanding(), Is.Empty);
            Assert.That(before.Token, Is.Null);
        }
    }
}
