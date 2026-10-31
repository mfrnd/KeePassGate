using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a timeout means when the work was posted onto somebody else's message queue, and where
    /// the work's own failure ends up.
    /// </summary>
    /// <remarks>
    /// **The answer has to be "it did not happen", not "it has not happened yet".** `BeginInvoke`
    /// offers no way to take something back out of the queue, and the queue is exactly where work
    /// sits while a user interface is too busy to answer. Everything that marshals here has already
    /// given its own answer by the time the timeout expires: pairing has refused the client and
    /// destroyed the certificate, so a write arriving afterwards saves an admission record for a
    /// certificate nobody holds, and an approval dialog arriving afterwards asks the operator about
    /// a client that was turned away minutes ago.
    ///
    /// **And a failure belongs to the caller, not to the user interface thread.** Work posted with
    /// `BeginInvoke` that throws is handed to `Application.OnThreadException` there as well as
    /// being stored for `EndInvoke`, so the two tests about it watch that route rather than only
    /// what the caller was told.
    ///
    /// Driven against a real `Control` on a real message queue, because the whole subject is the
    /// queue: a fake that runs work inline or refuses to run it at all cannot have this bug.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class UiThreadTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        [Test]
        public void Work_the_user_interface_never_reached_is_called_off_rather_than_run_later()
        {
            using (var form = new Form())
            {
                Handle(form);

                var ui = new ControlUiThread(form);
                var ran = 0;
                var answered = false;
                var answer = true;

                var caller = new Thread(() =>
                {
                    answer = ui.Run(() => Interlocked.Increment(ref ran), TimeSpan.FromMilliseconds(150));
                    Volatile.Write(ref answered, true);
                });

                caller.IsBackground = true;
                caller.Start();

                // The user interface is busy: nothing pumps, so the posted work sits in the queue
                // for longer than the caller is prepared to wait.
                Assert.That(caller.Join(Patience), Is.True, "the caller never gave up");
                Assert.That(answered, Is.True);
                Assert.That(answer, Is.False, "a timeout has to be reported as one");

                // And now it pumps. This is the moment the abandoned work used to run.
                Drain(form);

                Assert.That(Volatile.Read(ref ran), Is.Zero,
                    "work the caller was told did not happen must not happen afterwards");
            }
        }

        [Test]
        public void Work_that_gets_through_in_time_runs_once_and_is_reported_as_having_run()
        {
            using (var form = new Form())
            {
                Handle(form);

                var ui = new ControlUiThread(form);
                var ran = 0;
                var answer = false;

                var caller = new Thread(() => answer = ui.Run(() => Interlocked.Increment(ref ran), Patience));
                caller.IsBackground = true;
                caller.Start();

                // Pump until the caller is satisfied, which is what a user interface that is not
                // stuck does on its own.
                var until = DateTime.UtcNow + Patience;
                while (!caller.Join(10) && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                }

                Assert.That(caller.Join(Patience), Is.True);
                Assert.That(answer, Is.True);
                Assert.That(Volatile.Read(ref ran), Is.EqualTo(1));
            }
        }

        [Test]
        public void A_control_with_no_window_yet_answers_no_rather_than_throwing()
        {
            using (var form = new Form())
            {
                Assert.That(new ControlUiThread(form).Run(() => { }, Patience), Is.False);
            }
        }

        [Test]
        public void Work_that_throws_fails_the_caller_rather_than_the_user_interface_thread()
        {
            using (var form = new Form())
            {
                Handle(form);

                var ui = new ControlUiThread(form);
                var thrown = new NullReferenceException("a defect inside marshaled work");
                Exception caught = null;
                var answered = false;

                var caller = new Thread(() =>
                {
                    caught = Thrown.By(() =>
                    {
                        ui.Run(() => throw thrown, Patience);
                        Volatile.Write(ref answered, true);
                    });
                });

                caller.IsBackground = true;
                caller.Start();

                var escapes = PumpWatchingForEscapes(caller);

                Assert.That(escapes.Unhandled, Is.Null,
                    "a marshaled failure reaching `Application.ThreadException` is what puts " +
                    "WinForms' own error dialog inside KeePass, which registers no handler for it");
                Assert.That(escapes.Loose, Is.Null,
                    "and it must not come out of the message pump either");
                Assert.That(caught, Is.SameAs(thrown),
                    "the calling thread is the one that can turn it into a refusal");
                Assert.That(answered, Is.False, "work that failed has no answer to report");
            }
        }

        [Test]
        public void Work_that_throws_after_the_timeout_fails_the_caller_that_waited_for_it()
        {
            using (var form = new Form())
            {
                Handle(form);

                var ui = new ControlUiThread(form);
                var thrown = new NullReferenceException("a defect in work that had already begun");
                Exception caught = null;

                var caller = new Thread(() =>
                {
                    caught = Thrown.By(() =>
                    {
                        // Long enough to be under way when the timeout expires, which is the path
                        // that cannot be withdrawn and is waited for instead. The timeout is a
                        // second rather than a moment because the pump has to start the work
                        // inside it, and on a loaded machine a moment is not always enough.
                        ui.Run(
                            () =>
                            {
                                Thread.Sleep(TimeSpan.FromSeconds(2));
                                throw thrown;
                            },
                            TimeSpan.FromSeconds(1));
                    });
                });

                caller.IsBackground = true;
                caller.Start();

                var escapes = PumpWatchingForEscapes(caller);

                Assert.That(escapes.Unhandled, Is.Null, "the same on the path that waits");
                Assert.That(escapes.Loose, Is.Null, "the same on the path that waits");
                Assert.That(caught, Is.SameAs(thrown),
                    "a late failure is still the caller's, and answering false would hide it");
            }
        }

        /// <summary>
        /// Runs the message loop until the caller is finished, watching where a failure goes.
        /// </summary>
        /// <remarks>
        /// The watch itself is shared with the fixture over `Unattended`, because the two are
        /// pinning the same escape from either side of it.
        /// </remarks>
        private static Escapes PumpWatchingForEscapes(Thread caller)
        {
            var escapes = Escapes.PumpUntil(() => caller.Join(10), Patience);

            Assert.That(caller.Join(Patience), Is.True, "the caller never came back");
            return escapes;
        }

        /// <summary>Forces the window into existence, which is what `BeginInvoke` needs.</summary>
        private static void Handle(Control control)
        {
            control.CreateControl();
            Assert.That(control.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(control.IsHandleCreated, Is.True);
        }

        /// <summary>Lets the message queue run, which is what a user interface stops doing.</summary>
        private static void Drain(Control control)
        {
            for (var i = 0; i < 20; i++)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }

            GC.KeepAlive(control);
        }
    }
}
