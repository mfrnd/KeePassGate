using System;
using KeePassGate.Ui;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A prompt that fails rather than asks.
    /// </summary>
    /// <remarks>
    /// The fixtures here build panels that are never meant to reach a confirmation. Passing null
    /// instead would put the real one there, and the real one is a modal `MessageBox`: a test that
    /// reached it would stop dead with a dialog nobody is there to answer, on a build machine as
    /// much as on a desk. Failing loudly is what says the test went somewhere it was not supposed
    /// to go.
    /// </remarks>
    internal sealed class NeverAsked : IOperatorPrompt
    {
        public bool Confirm(string title, string question)
        {
            throw new InvalidOperationException(
                "This fixture does not expect a confirmation, and was asked: " + title);
        }

        public void Tell(string title, string news)
        {
            throw new InvalidOperationException(
                "This fixture does not expect to be told anything, and was told: " + title);
        }
    }
}
