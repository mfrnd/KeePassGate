using System;
using System.Collections.Generic;
using System.Threading;
using KeePassGate.Pairing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The operator, as a test decides them. It records what it was shown, because what the dialog
    /// is given is as much a part of the design as what it answers.
    /// </summary>
    internal sealed class FakePairingApproval : IPairingApproval
    {
        private readonly Func<PairingRequest, PairingApproval> _answer;

        public FakePairingApproval(PairingApproval answer)
            : this(request => answer)
        {
        }

        public FakePairingApproval(Func<PairingRequest, PairingApproval> answer)
        {
            _answer = answer;
        }

        public int Asked { get; private set; }

        public PairingRequest LastRequest { get; private set; }

        public TimeSpan LastTimeout { get; private set; }

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

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
        {
            Asked++;
            LastRequest = request;
            LastTimeout = timeout;

            if (Thinks > TimeSpan.Zero)
            {
                Thread.Sleep(Thinks);
            }

            if (Throws != null)
            {
                throw Throws;
            }

            return _answer(request);
        }
    }

    /// <summary>An operator who approves exactly what was asked for, whatever that is.</summary>
    internal sealed class AgreeableOperator : IPairingApproval
    {
        public int Asked { get; private set; }

        public IReadOnlyList<string> LastProfiles { get; private set; }

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
        {
            Asked++;
            LastProfiles = request.Profiles;
            return PairingApproval.Approved(request.Profiles);
        }
    }
}
