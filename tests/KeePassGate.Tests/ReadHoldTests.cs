using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The one queue of turns on KeePass's thread: a read holding it still, work waiting for it to
    /// be idle, and what either is told when that does not happen.
    /// </summary>
    /// <remarks>
    /// Driven against a real `Control` on a real message queue, because the subject is when the
    /// queue gets round to things. Most pump by hand, a round of `DoEvents` and then Idle as a
    /// loop raises it; the one about a timer runs a loop of its own, since what it pins is what a
    /// real loop does with nobody driving it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ReadHoldTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        /// <summary>No merge is running, which is every test here that is not about one.</summary>
        private static readonly Func<bool> NoMerge = () => false;

        [Test]
        public void A_read_holds_KeePass_s_thread_still_for_its_whole_length()
        {
            using (var form = Made())
            using (var ui = new ControlUiThread(form, NoMerge))
            {
                var moved = 0;
                var seen = -1;

                var reader = Start(() => seen = ui.Hold(() =>
                {
                    // What KeePass would do next, asked for now. It may not happen until the read
                    // is over, which is what keeps the tree still under it.
                    form.BeginInvoke((MethodInvoker)(() => Interlocked.Increment(ref moved)));
                    Thread.Sleep(200);
                    return Volatile.Read(ref moved);
                }));

                PumpUntil(() => !reader.IsAlive);
                Drain();

                Assert.That(seen, Is.Zero, "nothing ran on KeePass's thread while the read went on");
                Assert.That(Volatile.Read(ref moved), Is.EqualTo(1), "and it ran once the read let go");
            }
        }

        [Test]
        public void A_read_is_held_inside_DoEvents_so_a_long_save_does_not_hold_reads_up()
        {
            using (var form = Made())
            using (var ui = new ControlUiThread(form, NoMerge))
            {
                var seen = 0;

                var reader = Start(() => seen = ui.Hold(() => 42));

                var until = DateTime.UtcNow + Patience;
                while (reader.IsAlive && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }

                Assert.That(seen, Is.EqualTo(42), "served on the fallback timer, with no Idle raised at all");
            }
        }

        [Test]
        public void A_read_that_cannot_be_held_in_time_is_answered_as_unsettled_and_never_runs()
        {
            using (var form = Made())
            using (var ui = new ControlUiThread(form, NoMerge, TimeSpan.FromMilliseconds(150), ControlUiThread.HoldLimit))
            {
                var ran = 0;
                Exception thrown = null;

                // Nothing pumps, which is KeePass busy with something that does not.
                var reader = Start(() => thrown = Thrown.By(() => ui.Hold(() => Interlocked.Increment(ref ran))));
                Assert.That(reader.Join(Patience), Is.True, "the read never gave up");

                // And now it pumps. This is the moment a read the caller gave up on would run.
                Drain();

                Assert.That(thrown, Is.InstanceOf<VaultUnsettled>(), "answered as busy, which a client can act on");
                Assert.That(Volatile.Read(ref ran), Is.Zero, "and a read the caller gave up on does not happen afterwards");
            }
        }

        [Test]
        public void A_read_that_outlasts_the_hold_is_not_believed()
        {
            using (var form = Made())
            using (var ui = new ControlUiThread(form, NoMerge, Patience, TimeSpan.FromMilliseconds(50)))
            {
                var resumed = 0;
                var during = -1;
                Exception thrown = null;

                var reader = Start(() => thrown = Thrown.By(() => ui.Hold(() =>
                {
                    form.BeginInvoke((MethodInvoker)(() => Interlocked.Increment(ref resumed)));
                    Thread.Sleep(500);
                    during = Volatile.Read(ref resumed);
                    return during;
                })));

                PumpUntil(() => !reader.IsAlive);

                Assert.That(during, Is.EqualTo(1),
                    "KeePass's thread went back to work once the limit passed, with the read still going");
                Assert.That(thrown, Is.InstanceOf<VaultUnsettled>(), "so what the read saw is not believed");
            }
        }

        [Test]
        public void Work_asked_for_by_a_thread_holding_KeePass_s_thread_is_refused_as_a_defect()
        {
            // Waited out, it would freeze KeePass's window for the length of the hold's limit and
            // then fail anyway, since the thread it asks is the one it is holding.
            using (var form = Made())
            using (var ui = new ControlUiThread(form, NoMerge))
            {
                Exception thrown = null;

                var reader = Start(() => thrown = Thrown.By(() => ui.Hold(() => ui.Run(() => { }, Patience))));
                PumpUntil(() => !reader.IsAlive);

                Assert.That(thrown, Is.InstanceOf<HoldMisused>());
            }
        }

        [Test]
        public void A_hold_inside_a_hold_or_on_KeePass_s_own_thread_is_run_where_it_is()
        {
            using (var form = Made())
            using (var ui = new ControlUiThread(form, NoMerge))
            {

                Assert.That(ui.Hold(() => ui.Hold(() => 5)), Is.EqualTo(5),
                    "on KeePass's own thread there is nothing to wait for");

                var seen = 0;
                var reader = Start(() => seen = ui.Hold(() => ui.Hold(() => 7)));
                PumpUntil(() => !reader.IsAlive);

                Assert.That(seen, Is.EqualTo(7),
                    "a hold inside a hold is part of it rather than a turn of its own, which it could never get");
            }
        }

        [Test]
        public void KeePass_s_timers_keep_their_time_under_a_stream_of_reads()
        {
            // What KeePass's inactivity lock and its clipboard clearing run on. Turns posted one
            // after another held a timer off for as long as the stream lasted, because Windows
            // makes a timer's message only once nothing else is queued; started on Idle, they
            // leave every timer on time. Measured at 125 ms between ticks of a 100 ms timer with
            // thirty-two readers, against 112 ms with none.
            using (var loop = new UiLoop())
            using (var ui = loop.Own(() => new ControlUiThread(loop.Control, NoMerge)))
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
                var holds = 0;
                var readers = Enumerable.Range(0, 8).Select(_ => Start(() =>
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        ui.Value.Hold(() => Spin(TimeSpan.FromMilliseconds(10)));
                        Interlocked.Increment(ref holds);
                    }
                })).ToList();

                Thread.Sleep(TimeSpan.FromSeconds(2));
                Volatile.Write(ref stop, 1);
                readers.ForEach(reader => reader.Join(Patience));

                double longest = 0;
                loop.Invoke(() =>
                {
                    ticker.Value.Stop();
                    longest = gaps.Count == 0 ? double.MaxValue : gaps.Max();
                });

                Assume.That(Volatile.Read(ref holds), Is.GreaterThan(50), "the reads really were holding the thread");
                Assert.That(longest, Is.LessThan(400), "the longest gap between ticks, in milliseconds");
            }
        }

        [Test]
        public void A_read_asked_for_inside_a_merge_waits_for_the_merge_to_end()
        {
            // The merge pumps from its status logger between a group or an entry and the next,
            // which is `DoEvents`, and the timer ticks inside it. A read started there would be
            // handed a tree part way between two databases.
            using (var form = Made())
            {
                var merging = 1;
                using (var ui = new ControlUiThread(form, () => Volatile.Read(ref merging) == 1))
                {
                    var seen = 0;
                    var reader = Start(() => seen = ui.Hold(() => 42));

                    PumpWithoutIdle(TimeSpan.FromMilliseconds(300));
                    Assert.That(reader.IsAlive, Is.True, "nothing was read while the merge went on");

                    Volatile.Write(ref merging, 0);
                    var until = DateTime.UtcNow + Patience;
                    while (reader.IsAlive && DateTime.UtcNow < until)
                    {
                        Application.DoEvents();
                        Thread.Sleep(1);
                    }

                    Assert.That(seen, Is.EqualTo(42), "and the read was served on the tick after it, still without Idle");
                }
            }
        }

        [Test]
        public void A_read_a_merge_outlasts_is_answered_as_unsettled_and_never_runs()
        {
            using (var form = Made())
            {
                var merging = 1;
                using (var ui = new ControlUiThread(
                    form,
                    () => Volatile.Read(ref merging) == 1,
                    TimeSpan.FromMilliseconds(150),
                    ControlUiThread.HoldLimit))
                {
                    var ran = 0;
                    Exception thrown = null;

                    var reader = Start(() => thrown = Thrown.By(() => ui.Hold(() => Interlocked.Increment(ref ran))));
                    var until = DateTime.UtcNow + Patience;
                    while (reader.IsAlive && DateTime.UtcNow < until)
                    {
                        Pump();
                        Thread.Sleep(1);
                    }

                    Volatile.Write(ref merging, 0);
                    Drain();

                    Assert.That(thrown, Is.InstanceOf<VaultUnsettled>(), "answered as busy, which a client can act on");
                    Assert.That(Volatile.Read(ref ran), Is.Zero, "and it does not happen once the merge is over");
                }
            }
        }

        [Test]
        public void Work_does_not_start_inside_a_merge_even_on_Idle()
        {
            // A WinForms dialog shown from inside a merge runs a loop of its own, which raises
            // Idle with the merge still on the stack underneath it.
            using (var form = Made())
            {
                var merging = 1;
                using (var ui = new ControlUiThread(form, () => Volatile.Read(ref merging) == 1))
                {
                    var ran = 0;
                    var worker = Start(() => ui.Run(() => Interlocked.Increment(ref ran), Patience));

                    var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
                    while (DateTime.UtcNow < until)
                    {
                        Pump();
                        Thread.Sleep(1);
                    }

                    Assert.That(Volatile.Read(ref ran), Is.Zero, "Idle came round and nothing was started");

                    Volatile.Write(ref merging, 0);
                    PumpUntil(() => !worker.IsAlive);

                    Assert.That(Volatile.Read(ref ran), Is.EqualTo(1), "and it ran once the merge was over");
                }
            }
        }

        [Test]
        public void A_thread_holding_KeePass_s_thread_may_wait_for_a_file_no_more_than_that_thread_may()
        {
            // What the log and the audit ask before a line is written. A read's hold keeps
            // KeePass's window waiting, so a line logged inside a hold is queued as a line logged
            // on KeePass's own thread is, and the same thread waits for the file as any
            // connection does before the hold and after it.
            using (var form = Made())
            using (var ui = new ControlUiThread(form, NoMerge))
            {
                var mayNotWait = ControlUiThread.MayNotWait(Thread.CurrentThread);
                var before = true;
                var inside = false;
                var nested = false;
                var after = true;

                var reader = Start(() =>
                {
                    before = mayNotWait();
                    inside = ui.Hold(mayNotWait);
                    nested = ui.Hold(() => ui.Hold(mayNotWait) && mayNotWait());
                    after = mayNotWait();
                });

                PumpUntil(() => !reader.IsAlive);

                Assert.That(mayNotWait(), Is.True, "KeePass's own thread");
                Assert.That(before, Is.False, "a connection's thread, holding nothing");
                Assert.That(inside, Is.True, "the same thread inside a hold");
                Assert.That(nested, Is.True, "and still inside the outer hold once an inner hold is over");
                Assert.That(after, Is.False, "and no longer once the hold is over");
            }
        }

        [Test]
        public void With_nobody_to_ask_about_a_merge_the_timer_starts_no_read()
        {
            // A KeePass whose merge cannot be looked for. A read the timer started could be
            // inside one unseen, so reads wait for Idle, as work does.
            using (var form = Made())
            using (var ui = new ControlUiThread(form, null))
            {
                var seen = 0;
                var reader = Start(() => seen = ui.Hold(() => 42));

                PumpWithoutIdle(TimeSpan.FromMilliseconds(300));
                Assert.That(reader.IsAlive, Is.True, "the timer ticked and served nothing");

                PumpUntil(() => !reader.IsAlive);
                Assert.That(seen, Is.EqualTo(42), "and Idle served it");
            }
        }

        [Test]
        public void Whether_a_merge_is_running_is_not_asked_with_nothing_waiting()
        {
            // The question reads the thread's whole stack, which nothing should pay for on every
            // round of Idle in a KeePass nobody is talking to.
            using (var form = Made())
            {
                var asked = 0;
                using (var ui = new ControlUiThread(form, () => Interlocked.Increment(ref asked) < 0))
                {
                    var reader = Start(() => ui.Hold(() => 1));
                    PumpUntil(() => !reader.IsAlive);

                    var after = Volatile.Read(ref asked);
                    Assert.That(after, Is.GreaterThan(0), "asked before the read was served");

                    Drain();
                    Assert.That(Volatile.Read(ref asked), Is.EqualTo(after), "and not again once the queue was empty");
                }
            }
        }

        private static Form Made()
        {
            var form = new Form();
            form.CreateControl();
            Assert.That(form.Handle, Is.Not.EqualTo(IntPtr.Zero));
            return form;
        }

        private static Thread Start(Action work)
        {
            var thread = new Thread(() => work()) { IsBackground = true };
            thread.Start();
            return thread;
        }

        private static int Spin(TimeSpan length)
        {
            var clock = Stopwatch.StartNew();
            var turns = 0;
            while (clock.Elapsed < length)
            {
                turns++;
            }

            return turns;
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
        /// Pumps as an operation of KeePass's does while it runs: the queue drained again and
        /// again, timers and all, and Idle never raised.
        /// </summary>
        private static void PumpWithoutIdle(TimeSpan length)
        {
            var until = DateTime.UtcNow + length;
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
        }

        private static void Drain()
        {
            for (var i = 0; i < 20; i++)
            {
                Pump();
                Thread.Sleep(5);
            }
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
