using System;
using System.Globalization;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// An approval dialog's countdown, and the one second a picture of it can be taken in.
    /// </summary>
    /// <remarks>
    /// **Read, not written.** A label paints from its managed text and never reads the window's,
    /// which is why `Steadied` cannot put a fixed sentence here. Reading one is another matter: UI
    /// automation reports a label's text across a process boundary, and this label carries no
    /// `AccessibleName`, so what it reports is the sentence itself.
    ///
    /// **Composed rather than parsed.** The sentence comes from
    /// <see cref="GateTab.ApprovalCountdownFormat"/>, which the dialog formats too, so the wait is a
    /// string comparison and a reworded countdown moves one constant instead of quietly failing to
    /// match. Hunting digits out of English prose would have tied the picture to the wording.
    ///
    /// **The transition, not the value.** Waiting until the text reads the wanted second can arrive
    /// nine tenths of the way through it. So the reading is checked once before the wait begins:
    /// if it is already there the margin was too small, which is a fact about how long everything
    /// before the shutter took rather than a defect in what is being screenshotted, and it says so.
    /// Everything after that is the tick itself, with a whole second behind it.
    ///
    /// **And it is asked again afterwards.** Two exposures either side of a tick would solve to a
    /// picture of two different dialogs, and one taken in a different second than the last run is a
    /// picture that cannot be regenerated. Neither is committed: the run fails and says which
    /// second it opened on and which it closed on.
    /// </remarks>
    internal sealed class Countdown : ISteadyMoment
    {
        /// <summary>
        /// How often to look. Short against a second, and each look is one call across a process
        /// boundary, so the shutter opens within a twentieth of a second of the tick.
        /// </summary>
        private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// How long to keep looking. Generous, because the only way to wait this long is for the
        /// wanted second to have gone past already, and the message for that is worth reaching.
        /// </summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private readonly AutomationElement _label;
        private readonly string _wanted;

        public Countdown(AutomationElement dialog, int seconds)
            : this(dialog, seconds, GateTab.ApprovalCountdownName, GateTab.ApprovalCountdownFormat)
        {
        }

        /// <summary>The same for a dialog whose countdown is called and worded otherwise.</summary>
        public Countdown(AutomationElement dialog, int seconds, string label, string format)
        {
            _label = Uia.ById(dialog, ControlType.Text, label);
            Assert.That(_label, Is.Not.Null, "the approval dialog has no countdown on it");

            _wanted = string.Format(CultureInfo.InvariantCulture, format, seconds);
        }

        public void WaitForIt()
        {
            var already = Reading();
            if (string.Equals(already, _wanted, StringComparison.Ordinal))
            {
                Assert.Fail(
                    "the countdown already said \"" + _wanted + "\" before the wait began, so a "
                    + "picture taken now would be part way through that second and a later run "
                    + "would catch a different part of it. Everything between the dialog appearing "
                    + "and the shutter has to fit inside the margin: give it a wider one.");
            }

            var deadline = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < deadline)
            {
                if (string.Equals(Reading(), _wanted, StringComparison.Ordinal))
                {
                    return;
                }

                Thread.Sleep(Look);
            }

            Assert.Fail(
                "the countdown never said \"" + _wanted + "\". It says \"" + Reading() + "\", "
                + "so the second being waited for is behind it and the margin is wrong.");
        }

        public void ConfirmItHeld()
        {
            var now = Reading();
            if (string.Equals(now, _wanted, StringComparison.Ordinal))
            {
                return;
            }

            Assert.Fail(
                "the countdown ticked while the picture was being taken: it said \"" + _wanted
                + "\" when the shutter opened and \"" + now + "\" when it closed. The two "
                + "exposures are of different windows, so nothing is committed.");
        }

        private string Reading()
        {
            try
            {
                return _label.Current.Name ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                return string.Empty;
            }
        }
    }
}
