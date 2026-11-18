namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The `id` of an authority nobody holds, for an admission a test composes and never puts into
    /// a store, where what issued its certificate decides nothing the test is about.
    /// </summary>
    internal static class InventedIssuer
    {
        public static readonly string Id = new string('a', 64);
    }
}
