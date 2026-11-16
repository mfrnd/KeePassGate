using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What KeePass's exit tells about standing answers no database took, and when it is put in
    /// front of the operator.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-11-08: a Windows logoff or shutdown gets the log line and no
    /// box, since a box there holds the session's end up with nobody at the desk, and an exit the
    /// operator starts still shows it. Told once, because the plugin's `Dispose` runs `Terminate`
    /// as well.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class OwedAnswersAtExitTests
    {
        private const int QueryEndSession = 0x0011;
        private const int EndSession = 0x0016;

        private static readonly string Certificate = new string('a', 64);
        private static readonly string Database = new string('d', 64);

        [Test]
        public void An_exit_the_operator_starts_puts_what_is_owed_in_front_of_them()
        {
            var prompt = new FakePrompt(true);
            var log = new RecordingGateLog();
            using (var notice = new OwedAnswersAtExit(Owing, prompt))
            {
                notice.OnClosing(null, new FormClosingEventArgs(CloseReason.UserClosing, false));
                notice.Tell(log);

                Assert.That(prompt.LastNews, Is.EqualTo(OwedAnswerNotice.Message(Owing())));
                Assert.That(log.Warnings.Select(entry => entry.Message), Has.Member(OwedAnswerNotice.LogLine(Owing())));
            }
        }

        [Test]
        public void The_box_stands_without_the_main_window_behind_it()
        {
            // KeePass has closed its databases by now and not redrawn, so its window still lists
            // the closed database's groups and entries until it hides itself after the plugins.
            using (var window = OffScreenForm.New())
            {
                window.Show();
                var prompt = new WindowPrompt(window);
                using (var notice = new OwedAnswersAtExit(Owing, prompt))
                {
                    notice.Watch(window);
                    notice.OnClosing(null, new FormClosingEventArgs(CloseReason.UserClosing, false));
                    notice.Tell(new RecordingGateLog());

                    Assert.That(prompt.Told, Is.True);
                    Assert.That(prompt.WindowVisible, Is.False, "the box stood over KeePass's main window");
                }
            }
        }

        [Test]
        public void A_windows_logoff_leaves_the_main_window_alone()
        {
            using (var window = OffScreenForm.New())
            {
                window.Show();
                using (var notice = new OwedAnswersAtExit(Owing, new FakePrompt(true)))
                {
                    notice.Watch(window);
                    notice.OnClosing(null, new FormClosingEventArgs(CloseReason.WindowsShutDown, false));
                    notice.Tell(new RecordingGateLog());

                    Assert.That(window.Visible, Is.True);
                }
            }
        }

        [Test]
        public void A_windows_logoff_writes_what_is_owed_to_the_log_alone()
        {
            var prompt = new FakePrompt(true);
            var log = new RecordingGateLog();
            using (var notice = new OwedAnswersAtExit(Owing, prompt))
            {
                notice.OnClosing(null, new FormClosingEventArgs(CloseReason.WindowsShutDown, false));
                notice.Tell(log);

                Assert.That(prompt.LastNews, Is.Null, "a box at a session's end holds the logoff up");
                Assert.That(log.Warnings.Select(entry => entry.Message), Has.Member(OwedAnswerNotice.LogLine(Owing())));
            }
        }

        [Test]
        public void A_session_end_that_did_not_happen_leaves_the_next_exit_its_box()
        {
            var prompt = new FakePrompt(true);
            using (var notice = new OwedAnswersAtExit(Owing, prompt))
            {
                notice.OnClosing(null, new FormClosingEventArgs(CloseReason.WindowsShutDown, false));
                notice.OnClosing(null, new FormClosingEventArgs(CloseReason.UserClosing, false));
                notice.Tell(new RecordingGateLog());

                Assert.That(prompt.LastNews, Is.Not.Null);
            }
        }

        [Test]
        public void A_main_window_closed_by_windows_ending_the_session_is_seen_as_that()
        {
            // The two messages Windows sends a top-level window at a logoff, to a window that was
            // never shown: WinForms raises `FormClosing` on the first and `FormClosed` on the
            // second, both carrying the reason this watches for.
            using (var window = new Form())
            using (var notice = new OwedAnswersAtExit(Owing, new FakePrompt(true)))
            {
                window.CreateControl();
                notice.Watch(window);

                User32.SendMessage(window.Handle, QueryEndSession, IntPtr.Zero, IntPtr.Zero);

                Assert.That(notice.SessionEnding, Is.True);

                User32.SendMessage(window.Handle, EndSession, (IntPtr)1, IntPtr.Zero);
            }
        }

        [Test]
        public void A_window_let_go_of_is_no_longer_listened_to()
        {
            using (var window = new Form())
            {
                using (var notice = new OwedAnswersAtExit(Owing, new FakePrompt(true)))
                {
                    window.CreateControl();
                    notice.Watch(window);
                    notice.Dispose();

                    User32.SendMessage(window.Handle, QueryEndSession, IntPtr.Zero, IntPtr.Zero);

                    Assert.That(notice.SessionEnding, Is.False);
                }
            }
        }

        [Test]
        public void What_is_owed_is_told_once_however_often_it_is_asked()
        {
            var prompt = new CountingPrompt();
            var log = new RecordingGateLog();
            using (var notice = new OwedAnswersAtExit(Owing, prompt))
            {
                notice.Tell(log);
                notice.Tell(log);

                Assert.That(prompt.Told, Is.EqualTo(1));
                Assert.That(log.Warnings.Count(), Is.EqualTo(1));
            }
        }

        [Test]
        public void Nothing_owed_tells_nothing()
        {
            var prompt = new FakePrompt(true);
            var log = new RecordingGateLog();
            using (var notice = new OwedAnswersAtExit(() => Array.Empty<OwedAnswer>(), prompt))
            {
                notice.Tell(log);

                Assert.That(prompt.LastNews, Is.Null);
                Assert.That(log.Entries, Is.Empty);
            }
        }

        private static IReadOnlyList<OwedAnswer> Owing()
        {
            return new[] { new OwedAnswer(Database, Certificate, RenewalRefusal.Forever(), null) };
        }

        /// <summary>A prompt that notes whether the window was on screen when it was told.</summary>
        private sealed class WindowPrompt : IOperatorPrompt
        {
            private readonly Form _window;

            public WindowPrompt(Form window)
            {
                _window = window;
            }

            public bool Told { get; private set; }

            public bool WindowVisible { get; private set; }

            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
                Told = true;
                WindowVisible = _window.Visible;
            }
        }

        private sealed class CountingPrompt : IOperatorPrompt
        {
            public int Told { get; private set; }

            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
                Told++;
            }
        }
    }
}
