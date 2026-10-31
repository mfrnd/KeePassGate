using System;

namespace KeePassGate.Testing
{
    /// <summary>
    /// Whatever a piece of work throws, handed back rather than let out: the tests' one catch of
    /// every exception.
    /// </summary>
    /// <remarks>
    /// A test catches everything only where what was thrown is the thing it asserts on: a failure
    /// carried back from a thread to the test that started it, a client turning any failure into
    /// the reply the test reads, the side of a handshake that is not the one being measured.
    /// **This is the one place that happens**, so CodeQL finds one catch of every exception in the
    /// tests rather than one in each, and it is suppressed here with that reason.
    /// </remarks>
    internal static class Thrown
    {
        /// <summary>
        /// Runs <paramref name="work"/>, and answers what it threw, or null when it threw nothing.
        /// </summary>
        internal static Exception By(Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            try
            {
                work();
                return null;
            }
            // codeql[cs/catch-of-all-exceptions] the tests' one catch-all, handing back what a test asserts on
            catch (Exception error)
            {
                return error;
            }
        }
    }
}
