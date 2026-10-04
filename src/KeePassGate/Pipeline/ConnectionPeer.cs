namespace KeePassGate.Pipeline
{
    /// <summary>
    /// Where this request's connection came from, for the two routes that show it to a person,
    /// pairing and renewal.
    /// </summary>
    /// <remarks>
    /// The approval dialog for a renewal says where the request came from, as the pairing one does,
    /// and only the transport knows. It travels in <see cref="IGateContext.Items"/> beside the
    /// database, for the reason <see cref="ServedDatabase"/> gives, rather than becoming part of who
    /// the caller is: an address is a fact about a connection, and the gate listens on loopback, so
    /// it says which local port rather than which machine.
    /// </remarks>
    public static class ConnectionPeer
    {
        public const string Key = "kpg.peer";

        public static void PutOn(IGateContext context, string peer)
        {
            if (context != null && !string.IsNullOrEmpty(peer))
            {
                context.Items[Key] = peer;
            }
        }

        /// <summary>The address, or empty when nobody said.</summary>
        public static string Of(IGateContext context)
        {
            object found;
            if (context == null || !context.Items.TryGetValue(Key, out found))
            {
                return string.Empty;
            }

            return found as string ?? string.Empty;
        }
    }
}
