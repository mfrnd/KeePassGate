using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>A database for the panel to work on, with no KeePass behind it.</summary>
    internal sealed class FakeGateDatabase : IGateDatabase
    {
        private readonly InMemoryKeyValueStore _store;

        public FakeGateDatabase()
            : this(new InMemoryKeyValueStore())
        {
        }

        public FakeGateDatabase(InMemoryKeyValueStore store)
        {
            _store = store;
            IsOpen = true;
            Name = "Invented Vault";
        }

        public bool IsOpen { get; set; }

        public int MarkedModifiedCount { get; private set; }

        public InMemoryKeyValueStore Store
        {
            get { return _store; }
        }

        /// <summary>What a screen calls it. Invented, like everything else in a test.</summary>
        public string Name { get; set; }

        /// <summary>
        /// The nodes the rules are counted over. Settable, because most tests have no tree at all
        /// and the ones that do are about what is counted in it.
        /// </summary>
        public KeePassGate.Acl.IRuledNodes Nodes { get; set; }

        public IKeyValueStore PluginCustomData
        {
            get { return _store; }
        }

        /// <summary>
        /// What marking the database as modified throws, or null for a mark that works. For the
        /// failure nobody predicted, which the handlers that catch everything have to count.
        /// </summary>
        public System.Exception MarkThrows { get; set; }

        public void MarkModified()
        {
            if (MarkThrows != null)
            {
                throw MarkThrows;
            }

            MarkedModifiedCount++;
        }
    }

    /// <summary>Hands the same database to every dialog, or none at all.</summary>
    internal sealed class FakeDatabaseSource : IGateDatabaseSource
    {
        private readonly IGateDatabase _database;

        public FakeDatabaseSource(IGateDatabase database)
        {
            _database = database;
        }

        public IGateDatabase ForDialog(Form dialog)
        {
            return _database;
        }
    }

    /// <summary>An operator who always says the same thing, and remembers being asked.</summary>
    internal sealed class FakePrompt : IOperatorPrompt
    {
        private readonly bool _answer;

        public FakePrompt(bool answer)
        {
            _answer = answer;
        }

        public int Asked { get; private set; }

        public string LastQuestion { get; private set; }

        public bool Confirm(string title, string question)
        {
            Asked++;
            LastQuestion = question;
            return _answer;
        }
    }
}
