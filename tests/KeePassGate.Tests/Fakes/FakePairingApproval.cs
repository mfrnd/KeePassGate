using System;
using System.Collections.Generic;
using System.Threading;
using KeePassGate.Pairing;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The operator, as a test decides them, on the plugin's own screen, `OnScreen`. It records
    /// what it was shown, because what the dialog is given is as much a part of the design as what
    /// it answers.
    /// </summary>
    internal sealed class FakePairingApproval : IPairingApproval
    {
        private readonly Func<PairingRequest, PairingApproval> _answer;
        private readonly KeePassGate.Ui.PairingApprovalPrompt _screen;

        public FakePairingApproval(PairingApproval answer)
            : this(request => answer)
        {
        }

        public FakePairingApproval(Func<PairingRequest, PairingApproval> answer)
        {
            _answer = answer;
            _screen = OnScreen.Screen(Shown, null);
        }

        public int Asked { get; private set; }

        public PairingRequest LastRequest { get; private set; }

        /// <summary>Every request a dialog was built from, in the order the dialogs came up.</summary>
        public List<PairingRequest> Requests { get; } = new List<PairingRequest>();

        public TimeSpan LastTimeout { get; private set; }

        /// <summary>How long the dialog was told it may stand at most, for the budget.</summary>
        public TimeSpan LastLeft { get; private set; }

        /// <summary>The same for every dialog, in the order the dialogs came up.</summary>
        public List<TimeSpan> Lefts { get; } = new List<TimeSpan>();

        /// <summary>Set to have the screen throw rather than put anything up.</summary>
        public Exception Throws { get; set; }

        /// <summary>
        /// How long the operator takes to answer, which for a real one reading a dialog is never
        /// nothing and is the whole reason a pairing connection has a budget of its own.
        /// </summary>
        public TimeSpan Thinks { get; set; }

        public static FakePairingApproval Approving(params string[] profiles)
        {
            return new FakePairingApproval(PairingApproval.Approved(profiles));
        }

        public static FakePairingApproval Refusing()
        {
            return new FakePairingApproval(PairingApproval.Refused());
        }

        public static FakePairingApproval Absent()
        {
            return new FakePairingApproval(PairingApproval.TimedOut());
        }

        /// <summary>What happens to the store while the request waits for the screen, before its reading.</summary>
        public Action BeforeShowing { get; set; }

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
        {
            if (BeforeShowing != null)
            {
                BeforeShowing();
            }

            LastTimeout = timeout;
            LastLeft = left;
            Lefts.Add(left);
            if (Throws != null)
            {
                throw Throws;
            }

            return _screen.Ask(request, timeout, left);
        }

        /// <summary>The dialog, as the screen puts it up: the operator reads it and answers.</summary>
        private PairingApproval Shown(PairingRequest shown, TimeSpan allowed)
        {
            Asked++;
            LastRequest = shown;
            Requests.Add(shown);

            if (Thinks > TimeSpan.Zero)
            {
                Thread.Sleep(Thinks);
            }

            return _answer(shown);
        }
    }

    /// <summary>An operator who approves exactly what was asked for, whatever that is.</summary>
    internal sealed class AgreeableOperator : IPairingApproval
    {
        private readonly KeePassGate.Ui.PairingApprovalPrompt _screen;

        public AgreeableOperator()
        {
            _screen = OnScreen.Screen(
                (shown, allowed) =>
                {
                    Asked++;
                    LastProfiles = shown.Profiles;
                    return PairingApproval.Approved(shown.Profiles);
                },
                null);
        }

        public int Asked { get; private set; }

        public IReadOnlyList<string> LastProfiles { get; private set; }

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
        {
            return _screen.Ask(request, timeout, left);
        }
    }
}
