using System.Threading;
using System.Windows.Forms;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Taking Enter and Escape away from a dialog while something on it needs them.
    /// </summary>
    /// <remarks>
    /// This is what stops the grant picker closing the dialog it is on. A form with an accept
    /// button treats Enter as that button wherever the focus is, so choosing verbs in a dropdown
    /// pressed OK on the group dialog, and Escape pressed Cancel and threw the rule away.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class DialogKeysTests
    {
        [Test]
        public void While_suspended_the_form_has_no_accept_or_cancel_button()
        {
            using (var form = new Form())
            {
                var ok = new Button();
                var cancel = new Button();
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                using (var keys = DialogKeys.Suspend(ok))
                {
                    Assert.That(keys.Suspended, Is.True);
                    Assert.That(form.AcceptButton, Is.Null);
                    Assert.That(form.CancelButton, Is.Null);
                }

                Assert.That(form.AcceptButton, Is.SameAs(ok), "the dialog gets its keys back");
                Assert.That(form.CancelButton, Is.SameAs(cancel));
            }
        }

        [Test]
        public void A_form_with_no_buttons_to_take_is_left_as_it_was()
        {
            using (var form = new Form())
            {
                var control = new Button();
                form.Controls.Add(control);

                using (DialogKeys.Suspend(control))
                {
                    Assert.That(form.AcceptButton, Is.Null);
                }

                Assert.That(form.AcceptButton, Is.Null);
            }
        }

        [Test]
        public void A_control_on_no_form_suspends_nothing_and_does_not_throw()
        {
            // Which is every test that builds a panel without showing it, and the path the picker
            // takes when the grid has never been drawn.
            using (var orphan = new Button())
            using (var keys = DialogKeys.Suspend(orphan))
            {
                Assert.That(keys.Suspended, Is.False);
            }

            Assert.DoesNotThrow(() => DialogKeys.Suspend(null).Dispose());
        }

        [Test]
        public void Giving_them_back_twice_gives_them_back_once()
        {
            using (var form = new Form())
            {
                var ok = new Button();
                form.Controls.Add(ok);
                form.AcceptButton = ok;

                // Let go of by hand, twice, which is what this is about. The `using` only covers a
                // failure on the way, since a token already let go of ignores being let go of again.
                using (var keys = DialogKeys.Suspend(ok))
                {
                    keys.Dispose();

                    form.AcceptButton = null;
                    keys.Dispose();

                    Assert.That(form.AcceptButton, Is.Null,
                        "a second release must not put back what somebody has since changed");
                }
            }
        }

        [Test]
        public void Overlapping_suspensions_give_the_keys_back_once_both_are_let_go_in_either_order()
        {
            // A picker lets go of its token a message after it closes, and the next picker can
            // open in between. Each token recording the buttons for itself, the first gave OK and
            // Cancel back under the second picker, and the second then put back the nothing it
            // had found, which left the dialog answering neither key.
            using (var form = new Form())
            {
                var ok = new Button();
                var cancel = new Button();
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                // Let go of by hand, in the order this is about. The `using` statements only cover a
                // failure on the way, since a token already let go of ignores being let go of again.
                using (var first = DialogKeys.Suspend(ok))
                using (var second = DialogKeys.Suspend(ok))
                {
                    first.Dispose();

                    Assert.That(form.AcceptButton, Is.Null,
                        "the keys came back while the second still held them");
                    Assert.That(form.CancelButton, Is.Null);

                    second.Dispose();

                    Assert.That(form.AcceptButton, Is.SameAs(ok));
                    Assert.That(form.CancelButton, Is.SameAs(cancel));
                }
            }
        }
    }
}
