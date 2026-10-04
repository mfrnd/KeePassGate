using System;
using System.Threading;
using KeePassGate.Pairing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The operator facing a renewal, as a test decides them. It records what it was shown, as
    /// the pairing one does.
    /// </summary>
    internal sealed class FakeRenewalApproval : IRenewalApproval
    {
        private readonly Func<RenewalRequest, RenewalApproval> _answer;

        public FakeRenewalApproval(RenewalApproval answer)
            : this(request => answer)
        {
        }

        public FakeRenewalApproval(Func<RenewalRequest, RenewalApproval> answer)
        {
            _answer = answer;
        }

        public int Asked { get; private set; }

        public RenewalRequest LastRequest { get; private set; }

        /// <summary>How long the operator takes to answer.</summary>
        public TimeSpan Thinks { get; set; }

        /// <summary>Run while the operator is deciding, which is how a test changes the world under them.</summary>
        public Action Meanwhile { get; set; }

        /// <summary>An operator who approves exactly what was offered, for as long as was asked.</summary>
        public static FakeRenewalApproval Approving()
        {
            return new FakeRenewalApproval(request => RenewalApproval.Approved(request.Profiles, request.Lifetime));
        }

        public RenewalApproval Ask(RenewalRequest request, TimeSpan timeout)
        {
            Asked++;
            LastRequest = request;

            if (Thinks > TimeSpan.Zero)
            {
                Thread.Sleep(Thinks);
            }

            if (Meanwhile != null)
            {
                Meanwhile();
            }

            return _answer(request);
        }
    }
}
