namespace KeePassGate
{
    // Deliberately in the root namespace and not in `Ui`, for the reason `IGateStatus` gives
    // beside it: this is implemented by the bridge to KeePass's configuration and read by a
    // window, and putting it under `Ui` made `Bridge/KeePassSettings.cs` take a `using` on the
    // user interface in order to say what it is.
    /// <summary>
    /// The one thing about the gate an operator can change: which port it listens on.
    /// </summary>
    /// <remarks>
    /// **It belongs to the installation rather than to a database.** One KeePass runs one gate, and
    /// that gate serves every database open in it, so the port cannot sensibly be a property of any
    /// one of them: two databases open at once would carry two answers to a question with one.
    /// That is why this is edited on the plugin's own window rather than on the tab inside a
    /// database's settings, and why it is stored in KeePass's configuration rather than in a
    /// `.kdbx`.
    ///
    /// An interface because the storage is KeePass's and everything above the boundary is written
    /// against seams small enough to test without a password manager in the room.
    /// </remarks>
    public interface IGateSettings
    {
        /// <summary>The port the operator has chosen, or zero when they have chosen none.</summary>
        int Port { get; }

        /// <summary>
        /// Remembers a port for the next time KeePass starts. Answers with why it could not, or
        /// null when it did.
        /// </summary>
        string Save(int port);
    }

    /// <summary>What a port has to be before anything tries to listen on it.</summary>
    public static class GatePort
    {
        /// <summary>The lowest a person may choose. Below this is where the system's own live.</summary>
        public const int Lowest = 1024;

        public const int Highest = 65535;

        /// <summary>
        /// Whether a number is a port this may use, and why not when it is not.
        /// </summary>
        /// <remarks>
        /// Checked before the socket rather than by it, because a socket that refuses says so in
        /// terms of the socket, and the operator typing a number wants to be told about the number.
        /// The floor is a convention rather than a rule on loopback, and it is the one that keeps
        /// somebody from typing 80 and wondering why their web server stopped working.
        /// </remarks>
        public static string Refuse(int port)
        {
            if (port < Lowest || port > Highest)
            {
                return "A port has to be between " + Lowest + " and " + Highest + ".";
            }

            return null;
        }
    }
}
