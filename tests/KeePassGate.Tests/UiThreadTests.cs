using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a timeout means when the work was queued for somebody else's thread, and where the
    /// work's own failure ends up.
    /// </summary>
    /// <remarks>
    /// **The answer has to be "it did not happen", not "it has not happened yet".** The queue is
    /// exactly where work sits while a user interface is too busy to answer. Everything that marshals here has already
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
            using (var ui = new ControlUiThread(form))
            {
                Handle(form);

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
            using (var ui = new ControlUiThread(form))
            {
                Handle(form);

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
                    Pump();
                }

                Assert.That(caller.Join(Patience), Is.True);
                Assert.That(answer, Is.True);
                Assert.That(Volatile.Read(ref ran), Is.EqualTo(1));
            }
        }

        [Test]
        public void A_control_with_no_window_is_named_as_that_rather_than_answered_as_a_timeout()
        {
            // False means the timeout passed, and every caller words it so: an exit, which is when
            // a connection thread meets a window that has gone, was written down as KeePass not
            // answering in time, or as a dialog nobody answered that was never shown.
            var ran = false;

            using (var form = new Form())
            using (var thread = new ControlUiThread(form))
            {
                Assert.That(() => thread.Run(() => ran = true, Patience), Throws.InstanceOf<ObjectDisposedException>(),
                    "a window that was never made");

                form.CreateControl();
                form.Dispose();

                var thrown = Assert.Throws<ObjectDisposedException>(() => thread.Run(() => ran = true, Patience),
                    "a window that has gone");
                Assert.That(Expected.OfUserInterface(thrown), Is.True, "every caller already catches it by name");
            }

            Assert.That(ran, Is.False);
        }

        [Test]
        public void Work_that_throws_fails_the_caller_rather_than_the_user_interface_thread()
        {
            using (var form = new Form())
            using (var ui = new ControlUiThread(form))
            {
                Handle(form);

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
            using (var ui = new ControlUiThread(form))
            {
                Handle(form);

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

        [Test]
        public void Work_waits_for_KeePass_s_thread_to_be_idle_and_never_starts_inside_DoEvents()
        {
            // How KeePass pumps while it saves, imports, synchronizes or exports: `DoEvents` in a
            // loop, which never raises Idle. Work posted with `BeginInvoke` started there, inside
            // the operation; queued, it waits for the operation to end.
            using (var form = new Form())
            using (var ui = new ControlUiThread(form))
            {
                Handle(form);

                var ran = 0;
                var answer = false;

                var caller = Start(() => answer = ui.Run(() => Interlocked.Increment(ref ran), Patience));

                var until = DateTime.UtcNow.AddMilliseconds(500);
                while (DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }

                var inside = Volatile.Read(ref ran);
                PumpUntil(() => !caller.IsAlive);

                Assert.That(inside, Is.Zero, "nothing started while KeePass was pumping for itself");
                Assert.That(answer, Is.True);
                Assert.That(Volatile.Read(ref ran), Is.EqualTo(1), "and it ran once KeePass was idle");
            }
        }

        [Test]
        public void KeePass_s_timers_keep_their_time_under_a_stream_of_work()
        {
            // What KeePass's inactivity lock and its clipboard clearing run on. Work posted one
            // after another held a timer off for as long as the stream lasted, because Windows
            // makes a timer's message only once nothing else is queued; started on Idle, it leaves
            // every timer on time.
            using (var loop = new UiLoop())
            using (var ui = loop.Own(() => new ControlUiThread(loop.Control)))
            using (var ticker = loop.Own(() => new System.Windows.Forms.Timer { Interval = 100 }))
            {
                var clock = Stopwatch.StartNew();
                var gaps = new List<double>();
                var last = 0.0;

                loop.Invoke(() =>
                {
                    ticker.Value.Tick += delegate
                    {
                        var now = clock.Elapsed.TotalMilliseconds;
                        if (last > 0)
                        {
                            gaps.Add(now - last);
                        }

                        last = now;
                    };
                    ticker.Value.Start();
                });

                var stop = 0;
                var turns = 0;
                var writers = Enumerable.Range(0, 8).Select(_ => Start(() =>
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        ui.Value.Run(() => Spin(TimeSpan.FromMilliseconds(10)), Patience);
                        Interlocked.Increment(ref turns);
                    }
                })).ToList();

                Thread.Sleep(TimeSpan.FromSeconds(2));
                Volatile.Write(ref stop, 1);
                writers.ForEach(writer => writer.Join(Patience));

                double longest = 0;
                loop.Invoke(() =>
                {
                    ticker.Value.Stop();
                    longest = gaps.Count == 0 ? double.MaxValue : gaps.Max();
                });

                Assume.That(Volatile.Read(ref turns), Is.GreaterThan(50), "the work really was holding the thread");
                Assert.That(longest, Is.LessThan(400), "the longest gap between ticks, in milliseconds");
            }
        }

        [Test]
        public void A_message_box_on_KeePass_s_thread_is_said_before_anything_is_queued()
        {
            // A native dialog's loop never raises Idle, so work queued under one waits for it to
            // close. Said at once instead, so that the writer and the prompt can answer at once.
            using (var loop = new UiLoop())
            using (var ui = loop.Own(() => new ControlUiThread(loop.Control)))
            {
                uint thread = 0;
                loop.Invoke(() => thread = NativeMethods.GetWindowThreadProcessId(loop.Control.Handle));

                Assert.That(ui.Value.Stalled, Is.Null, "nothing is open yet");

                loop.Control.BeginInvoke((MethodInvoker)(() => MessageBox.Show("an invented question", "KeePassGate test")));
                var reported = Eventually(() => ui.Value.Stalled != null);

                var box = IntPtr.Zero;
                NativeMethods.EnumThreadWindows(thread, (window, parameter) =>
                {
                    if (NativeMethods.IsWindowVisible(window) && NativeMethods.GetClassName(window) == "#32770")
                    {
                        box = window;
                        return false;
                    }

                    return true;
                });

                // WM_CLOSE, which answers a message box with only an OK as its OK.
                User32.PostMessage(box, 0x0010, IntPtr.Zero, IntPtr.Zero);
                var cleared = Eventually(() => ui.Value.Stalled == null);

                Assert.That(reported, Is.True, "a message box open on KeePass's thread is reported");
                Assert.That(box, Is.Not.EqualTo(IntPtr.Zero), "and it is the window Windows calls a dialog");
                Assert.That(cleared, Is.True, "and once it closes, nothing is");
            }
        }

        private static Thread Start(Action work)
        {
            var thread = new Thread(() => work()) { IsBackground = true };
            thread.Start();
            return thread;
        }

        private static void Spin(TimeSpan length)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < length)
            {
                // Busy, as a turn doing real work is: a wait would give the thread back.
            }
        }

        private static bool Eventually(Func<bool> condition)
        {
            var until = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < until)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return condition();
        }

        /// <summary>Pumps until the condition holds, as a loop that is not stuck does.</summary>
        private static void PumpUntil(Func<bool> done)
        {
            var until = DateTime.UtcNow + Patience;
            while (!done() && DateTime.UtcNow < until)
            {
                Pump();
                Thread.Sleep(1);
            }

            Assert.That(done(), Is.True, "the caller never came back");
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
                Pump();
                Thread.Sleep(5);
            }

            GC.KeepAlive(control);
        }

        /// <summary>
        /// One round of a message loop: the queue drained, then Idle, which a real loop raises once
        /// nothing is left in it and `DoEvents` never does.
        /// </summary>
        private static void Pump()
        {
            Application.DoEvents();
            Application.RaiseIdle(EventArgs.Empty);
        }
    }
}
