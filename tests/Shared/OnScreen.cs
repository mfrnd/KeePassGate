using System;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Ui;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The plugin's own screen, `PairingApprovalPrompt`, with an operator a test stands in for in
    /// place of its dialog: the reading as the dialog comes up, no dialog over a reading that does
    /// not serve, no dialog shown again where no row differs, and the approval held to what the
    /// dialog showed, each by the product's own members.
    /// </summary>
    /// <remarks>
    /// **What is left here is the operator and KeePass's thread.** A test's answer is a value, so
    /// an approval it gives without what the dialog showed is made again through
    /// `PairingRequest.Approve`, the member the dialog's Approve answers through; and the turn on
    /// KeePass's thread is run where it is asked, as the real thread runs it once it is idle.
    /// </remarks>
    internal static class OnScreen
    {
        /// <summary>
        /// The screen, putting each request the dialog would come up for to this operator.
        /// </summary>
        public static PairingApprovalPrompt Screen(Func<PairingRequest, TimeSpan, PairingApproval> dialog, IGateLog log)
        {
            if (dialog == null)
            {
                throw new ArgumentNullException(nameof(dialog));
            }

            return new PairingApprovalPrompt(new Inline(), log)
            {
                Dialog = (shown, allowed) => Held(dialog(shown, allowed), shown)
            };
        }

        /// <summary>What the screen answers an operator who answers each shown request as this decides.</summary>
        public static PairingApproval Ask(PairingRequest request, Func<PairingRequest, PairingApproval> answer)
        {
            return Screen((shown, allowed) => answer(shown), null).Ask(request, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// The operator's answer as the dialog's Approve gives it: an approval the test made of its
        /// profiles and lifetime alone, held to what the dialog showed.
        /// </summary>
        private static PairingApproval Held(PairingApproval answer, PairingRequest shown)
        {
            return answer != null && answer.Decision == PairingDecision.Approved && answer.Against == null
                ? shown.Approve(answer.Profiles, answer.Lifetime)
                : answer;
        }

        /// <summary>KeePass's thread, idle: the turn runs where it is asked.</summary>
        private sealed class Inline : IUiThread
        {
            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                work();
                return true;
            }
        }
    }
}
