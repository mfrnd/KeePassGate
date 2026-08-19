using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Where a failure goes when the work was posted onto a message queue and nobody stayed to
    /// collect it.
    /// </summary>
    /// <remarks>
    /// `ControlUiThread` answers this for a caller that waits: the failure is carried back and
    /// thrown on the thread that asked. The entry point's announcement has no such caller. It
    /// posts from the user interface thread, in a handler of the main window's `Shown`, and returns
    /// at once, so a `try` written around `BeginInvoke` guards the posting and leaves the work
    /// unguarded on the other side of the queue, where WinForms answers an escape with its own
    /// modal error dialog inside the password manager.
    ///
    /// Driven against a real `Control` on a real message queue and posted from the thread that
    /// owns it, because that is the arrangement the entry point is in: a fake that runs the work
    /// inline cannot have this defect.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class UnattendedTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        [Test]
        public void Work_that_throws_reaches_the_reporter_rather_than_the_message_loop()
        {
            using (var form = new Form())
            {
                Handle(form);

                var thrown = new NullReferenceException("a defect inside posted work");
                Exception reported = null;

                Unattended.Post(form, () => throw thrown, error => reported = error);

                var escapes = Escapes.PumpUntil(() => reported != null, Patience);

                Assert.That(escapes.Unhandled, Is.Null,
                    "a posted failure reaching `Application.ThreadException` is what puts " +
                    "WinForms' own error dialog inside KeePass, which registers no handler for it");
                Assert.That(escapes.Loose, Is.Null,
                    "and it must not come out of the message pump either");
                Assert.That(reported, Is.SameAs(thrown),
                    "the plugin's own handler is the only thing left that can count it");
            }
        }

        [Test]
        public void A_reporter_that_is_itself_broken_is_not_let_out_either()
        {
            using (var form = new Form())
            {
                Handle(form);

                var reached = false;

                Unattended.Post(
                    form,
                    () => throw new InvalidOperationException("a defect inside posted work"),
                    error =>
                    {
                        GC.KeepAlive(error);
                        reached = true;
                        throw new NullReferenceException("a defect inside the reporter");
                    });

                var escapes = Escapes.PumpUntil(() => reached, Patience);

                Assert.That(reached, Is.True);
                Assert.That(escapes.Unhandled, Is.Null,
                    "the last thing standing between a failure and the dialog cannot be one more " +
                    "way to raise it");
                Assert.That(escapes.Loose, Is.Null);
            }
        }

        [Test]
        public void Work_that_does_not_throw_runs_once_and_reports_nothing()
        {
            using (var form = new Form())
            {
                Handle(form);

                var ran = 0;
                Exception reported = null;

                Unattended.Post(form, () => ran++, error => reported = error);

                var escapes = Escapes.PumpUntil(() => ran > 0, Patience);

                Assert.That(ran, Is.EqualTo(1));
                Assert.That(reported, Is.Null);
                Assert.That(escapes.Unhandled, Is.Null);
                Assert.That(escapes.Loose, Is.Null);
            }
        }

        [Test]
        public void A_window_that_cannot_be_posted_to_fails_the_caller_rather_than_the_queue()
        {
            using (var form = new Form())
            {
                // No handle, so there is no queue to post onto. The caller is still standing here
                // and is the one that can say what it was unable to say, which is why the posting
                // is deliberately left outside the guard.
                Assert.That(
                    () => Unattended.Post(form, () => { }, error => GC.KeepAlive(error)),
                    Throws.InstanceOf<InvalidOperationException>());
            }
        }

        /// <summary>Forces the window into existence, which is what `BeginInvoke` needs.</summary>
        /// <remarks>
        /// Reading `Handle` is what creates one here, not `CreateControl`: that call makes a
        /// handle only for a control that is visible, and a `Form` nobody has shown is not.
        /// </remarks>
        private static void Handle(Control control)
        {
            control.CreateControl();
            Assume.That(control.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assume.That(control.IsHandleCreated, Is.True);
        }
    }
}
