using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using KeePass.Util.Spr;
using KeePassGate.Diagnostics;
using KeePassGate.Host.Tests.Live;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass's own placeholder engine makes of field references and placeholders, held to
    /// the vectors `pykeepassgate.refs` is held to.
    /// </summary>
    /// <remarks>
    /// **The client's resolver is a second copy of KeePass's grammar**, and a copy nothing checks
    /// drifts from the original the first time either changes. So the expected values in
    /// `tests/Shared/ReferenceVectors.json` are made by `SprEngine` itself, over the entries
    /// written down beside them, and `tests/pykeepassgate/test_refs.py` holds the resolver to the
    /// same file: a KeePass that compiles differently fails here, and a resolver that does fails
    /// there. They are regenerated from KeePass and never from the client, by running this test
    /// with `KPG_WRITE_REFERENCE_VECTORS` set, which rewrites what KeePass made and passes.
    ///
    /// **Compiled as a copy to the clipboard compiles, less what acts.** That is
    /// `SprCompileFlags.NonActive`: everything KeePass replaces when a field is copied, without
    /// running a command, generating a password, showing a dialog or touching the clipboard,
    /// none of which a test should do and all of which the resolver refuses by name anyway. On a
    /// value holding none of those, which is every value here, it is exactly what a copy makes.
    ///
    /// **Nothing here may depend on the machine.** The database is in memory under a relative
    /// name, so the placeholders about its file give the same answer everywhere; the one
    /// environment variable is set by the test itself; and the strings are ordered in the
    /// invariant culture, because KeePass keeps an entry's strings sorted by the current one and
    /// that order decides which of two pieces is replaced first. Placeholders whose answer is the
    /// machine's, a time, a path to a program, the clipboard, the selected group or an encrypted
    /// password, are left out of the file.
    /// </remarks>
    [TestFixture]
    public sealed class ReferenceVectorsTests
    {
        /// <summary>Set to anything, this rewrites the file with what KeePass makes.</summary>
        public const string WriteVariable = "KPG_WRITE_REFERENCE_VECTORS";

        /// <summary>The one environment variable the vectors name, set here and nowhere else.</summary>
        private const string InventedVariable = "KPG_INVENTED_VARIABLE";

        private const string InventedValue = "an invented value";

        /// <summary>A relative path, so the placeholders about the file name nobody's machine.</summary>
        private const string DatabaseFile = "invented.kdbx";

        private static string VectorsPath
        {
            get { return Paths.Join(Repository.Root, "tests", "Shared", "ReferenceVectors.json"); }
        }

        [Test]
        public void KeePass_compiles_every_value_as_the_vectors_say()
        {
            var document = JObject.Parse(File.ReadAllText(VectorsPath, Encoding.UTF8));
            var writing = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(WriteVariable));
            var found = new List<string>();

            var culture = Thread.CurrentThread.CurrentCulture;
            var variable = Environment.GetEnvironmentVariable(InventedVariable);
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                Environment.SetEnvironmentVariable(InventedVariable, InventedValue);

                if ((string)document["keepass"] != PwDefs.VersionString)
                {
                    found.Add("made by KeePass " + document["keepass"] + ", held to " + PwDefs.VersionString);
                    document["keepass"] = PwDefs.VersionString;
                }

                var database = Built(document, found);

                foreach (var item in document["cases"].Children<JObject>())
                {
                    var entry = database.RootGroup.FindEntry(Identifier((string)item["entry"]), true);
                    var value = (string)item["value"];
                    var compiled = SprEngine.Compile(
                        value, new SprContext(entry, database, SprCompileFlags.NonActive));

                    if (!string.Equals((string)item["keepass"], compiled, StringComparison.Ordinal))
                    {
                        found.Add(item["name"] + ": " + JsonConvert.ToString(value) + " compiles to " +
                                  JsonConvert.ToString(compiled) + ", and the file says " +
                                  JsonConvert.ToString((string)item["keepass"]));
                        item["keepass"] = compiled;
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(InventedVariable, variable);
                Thread.CurrentThread.CurrentCulture = culture;
            }

            if (writing && found.Count > 0)
            {
                File.WriteAllText(
                    VectorsPath,
                    document.ToString(Formatting.Indented) + Environment.NewLine,
                    new UTF8Encoding(false));
                Assert.Pass("rewrote " + found.Count + " vectors from KeePass " + PwDefs.VersionString);
            }

            Assert.That(found, Is.Empty,
                "KeePass and the vectors disagree. If KeePass is right, run this test again with " +
                WriteVariable + " set, and run the client's tests after it.");
        }

        [Test]
        public void Every_case_names_an_entry_the_vectors_hold_and_says_what_the_client_does()
        {
            var document = JObject.Parse(File.ReadAllText(VectorsPath, Encoding.UTF8));
            var entries = new HashSet<string>(
                document["entries"].Children<JObject>().Select(entry => (string)entry["uuid"]),
                StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in document["cases"].Children<JObject>())
            {
                var name = (string)item["name"];
                Assert.That(names.Add(name), Is.True, "two cases are called " + name);
                Assert.That(entries, Does.Contain((string)item["entry"]), name);
                Assert.That((string)item["resolver"], Is.AnyOf("follows", "refuses", "keeps"), name);
            }
        }

        /// <summary>
        /// The entries the file holds, in a database that is never saved, with each entry's strings
        /// written back in the order KeePass keeps them, which is the order the gate serves them in.
        /// </summary>
        private static PwDatabase Built(JObject document, List<string> found)
        {
            var database = new PwDatabase();
            database.New(IOConnectionInfo.FromPath(DatabaseFile), new CompositeKey());
            database.RootGroup.Name = "Invented";

            foreach (var item in document["entries"].Children<JObject>())
            {
                var entry = new PwEntry(false, true) { Uuid = Identifier((string)item["uuid"]) };
                var fields = (JObject)item["fields"];

                foreach (var field in fields.Properties())
                {
                    entry.Strings.Set(field.Name, new ProtectedString(
                        field.Name == PwDefs.PasswordField, (string)field.Value));
                }

                database.RootGroup.AddEntry(entry, true);

                var kept = entry.Strings.GetKeys();
                if (!fields.Properties().Select(field => field.Name).SequenceEqual(kept))
                {
                    found.Add(item["uuid"] + ": KeePass keeps its strings as " + string.Join(", ", kept));

                    var ordered = new JObject();
                    foreach (var name in kept)
                    {
                        ordered[name] = fields[name];
                    }

                    item["fields"] = ordered;
                }
            }

            return database;
        }

        private static PwUuid Identifier(string hex)
        {
            return new PwUuid(MemUtil.HexStringToByteArray(hex));
        }
    }
}
