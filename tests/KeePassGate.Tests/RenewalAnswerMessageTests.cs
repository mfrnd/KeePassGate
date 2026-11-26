using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The message of a standing answer to a renewal that was not kept as given, from the
    /// connection thread the renewal handler tells it on to the operator's screen.
    /// </summary>
    /// <remarks>
    /// Driven against a real window on a real message queue, with the message asked for from
    /// another thread, because that is where the handler asks for it once the dialog has closed.
    /// The end to end tier shows it over KeePass's own window, `LiveRefusalNotWrittenTests`.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class RenewalAnswerMessageTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        [Test]
        public void An_answer_not_kept_is_told_under_the_plugin_s_name_on_the_window_s_own_thread()
        {
            using (var form = new Form())
            {
                Handle(form);
                var screen = new RecordingPrompt();
                var prompt = Prompt(form, screen, () => null);
                var news = RenewalAnswerNotice.NotWritten("a dialog is open on the database");

                FromAnotherThread(() => prompt.Tell(news));
                var escapes = Escapes.PumpUntil(() => screen.Told.Count > 0, Patience);

                Assert.That(screen.Told, Is.EqualTo(new[] { GateName.Full + "|" + news }));
                Assert.That(screen.On, Is.EqualTo(Environment.CurrentManagedThreadId), "shown on the window's thread");
                Assert.That(escapes.Unhandled, Is.Null);
                Assert.That(escapes.Loose, Is.Null);
            }
        }

        [Test]
        public void While_KeePass_is_busy_the_message_waits_for_an_idle_at_which_it_is_free()
        {
            // A message shown inside KeePass's own save stands inside that save, so it waits.
            using (var form = new Form())
            {
                Handle(form);
                var screen = new RecordingPrompt();
                string busy = "KeePass is in the middle of an operation of its own";
                var prompt = Prompt(form, screen, () => busy);

                FromAnotherThread(() => prompt.Tell(RenewalAnswerNotice.NotWritten("the database is busy")));
                Escapes.PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
                Assert.That(screen.Told, Is.Empty, "nothing was shown while KeePass was busy");

                busy = null;
                Escapes.PumpUntil(() => screen.Told.Count > 0, Patience);
                Escapes.PumpUntil(() => false, TimeSpan.FromMilliseconds(200));

                Assert.That(screen.Told, Has.Count.EqualTo(1), "shown once KeePass was free, and once");
            }
        }

        [Test]
        public void A_prompt_with_nothing_to_show_messages_leaves_the_log_to_tell()
        {
            var log = new RecordingGateLog();
            var prompt = new PairingApprovalPrompt(new FakeUiThread(), log);

            Assert.DoesNotThrow(() => prompt.Tell(RenewalAnswerNotice.NotWritten("anything")));
            Assert.That(log.Entries, Has.Some.Matches<RecordingGateLog.Entry>(
                entry => entry.Message.StartsWith(PairingApprovalPrompt.Untold, StringComparison.Ordinal)));
        }

        [Test]
        public void A_window_that_has_gone_leaves_the_message_in_the_log()
        {
            // KeePass's exit leaves a connection thread inside a renewal: the post is refused on
            // that thread, and the refusal stands all the same.
            var log = new RecordingGateLog();
            var screen = new RecordingPrompt();

            Form gone;
            using (var form = new Form())
            {
                Handle(form);
                gone = form;
            }

            var prompt = new PairingApprovalPrompt(new FakeUiThread(), log)
            {
                Messages = new FreeMessage(gone, screen, () => null, null, log),
            };

            Assert.DoesNotThrow(() => FromAnotherThread(() => prompt.Tell(RenewalAnswerNotice.NotWritten("anything"))));
            Assert.That(screen.Told, Is.Empty);
            Assert.That(log.Entries, Has.Some.Matches<RecordingGateLog.Entry>(
                entry => entry.Message.StartsWith(PairingApprovalPrompt.Untold, StringComparison.Ordinal)));
        }

        [Test]
        public void The_words_are_the_words_chosen()
        {
            Assert.That(RenewalAnswerNotice.NotWritten("another change is already being written"), Is.EqualTo(
                "The answer to the renewal was not written: another change is already being written." +
                Environment.NewLine + "Renewal stays refused until KeePass exits."));
            Assert.That(RenewalAnswerNotice.NotConfirmed("the database closed as its save ended"), Is.EqualTo(
                "The save of the answer to the renewal could not be confirmed: the database closed as its save ended."));
            Assert.That(RenewalAnswerNotice.NotWritten(null), Does.Contain("nothing was reported."));
        }

        private static PairingApprovalPrompt Prompt(Control window, IOperatorPrompt screen, Func<string> busy)
        {
            return new PairingApprovalPrompt(new FakeUiThread(), null)
            {
                Messages = new FreeMessage(window, screen, busy, null, null),
            };
        }

        private static void Handle(Control control)
        {
            control.CreateControl();
            Assume.That(control.Handle, Is.Not.EqualTo(IntPtr.Zero));
        }

        private static void FromAnotherThread(Action work)
        {
            Exception thrown = null;
            var thread = new Thread(() => thrown = Thrown.By(work));
            thread.Start();
            Assert.That(thread.Join(Patience), Is.True, "the other thread did not finish");

            if (thrown != null)
            {
                throw new AssertionException("the other thread threw: " + thrown);
            }
        }

        /// <summary>The operator's screen, recording each message as its title and its text.</summary>
        private sealed class RecordingPrompt : IOperatorPrompt
        {
            public List<string> Told { get; } = new List<string>();

            public int On { get; private set; } = -1;

            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
                Told.Add(title + "|" + news);
                On = Environment.CurrentManagedThreadId;
            }
        }
    }
}
