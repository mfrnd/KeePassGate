namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Something on a window that changes on its own, and the moment it will hold still for.
    /// </summary>
    /// <remarks>
    /// A screenshot here is solved from two exposures a quarter of a second apart, so anything that
    /// moves between them is a picture of two different windows, and anything that differs between
    /// runs is a picture that cannot be regenerated. A caret was one and was answered by taking the
    /// focus away; a countdown cannot be, because it is what the dialog is saying.
    ///
    /// So the shutter is aimed instead. <see cref="WaitForIt"/> blocks until the instant with the
    /// most of a second in front of it, and <see cref="ConfirmItHeld"/> is asked afterwards whether
    /// that held. Two calls rather than one, because the second is the half that makes it honest:
    /// without it, an exposure that fell either side of a change would be committed and nothing
    /// would say so.
    ///
    /// It is an interface so that <see cref="Screenshot"/> goes on knowing nothing about pairing.
    /// </remarks>
    internal interface ISteadyMoment
    {
        /// <summary>Blocks until the moment to open the shutter.</summary>
        void WaitForIt();

        /// <summary>Fails the test if it did not hold for as long as the shutter was open.</summary>
        void ConfirmItHeld();
    }
}
