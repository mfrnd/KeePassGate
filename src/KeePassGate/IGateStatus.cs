namespace KeePassGate
{
    // Deliberately in the root namespace and not in `Ui`. It is implemented by the transport and
    // read by the tab, and putting it under `Ui` made `GateListener` take a `using` on the user
    // interface to describe itself. The root is visible from every namespace in this assembly
    // without one.
    /// <summary>Whether the gate is listening, where, and why not.</summary>
    public interface IGateStatus
    {
        bool IsListening { get; }

        int Port { get; }

        /// <summary>
        /// Why nothing is listening, or null when something is.
        /// </summary>
        /// <remarks>
        /// **A gate that could not take its port stays down until KeePass starts again**, because
        /// the port is bound once and never rebound. That makes this a fact about the whole
        /// session rather than a line that scrolls past in a log, and the window an operator opens
        /// to ask why nothing can reach them is where it has to be readable.
        ///
        /// The rest of the plugin is unaffected: every setting can still be changed, every
        /// authority can still be created, and what is lost is only that no client can connect
        /// until the next start.
        /// </remarks>
        string Refusal { get; }
    }
}
