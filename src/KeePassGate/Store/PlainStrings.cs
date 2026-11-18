namespace KeePassGate.Store
{
    /// <summary>
    /// The strings of a record held as plain text: a record composed and not yet in a database,
    /// read as a version beside the entries a database holds.
    /// </summary>
    internal sealed class PlainStrings : IStoreStrings
    {
        public static readonly PlainStrings Instance = new PlainStrings();

        private PlainStrings()
        {
        }

        public int Length(object value)
        {
            var text = value as string;
            return text == null ? 0 : text.Length;
        }

        public string Read(object value)
        {
            return value as string;
        }
    }
}
