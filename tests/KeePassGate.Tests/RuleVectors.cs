using System.IO;
using KeePassGate.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The vectors the rules are held to, printed from the measured model and never from this side:
    /// `tests/Shared/StoreRecordVectors.json` for the texts and the objects, and
    /// `tests/Shared/StoreMeaningVectors.json` for what a store gives at each node.
    /// </summary>
    internal static class RuleVectors
    {
        /// <summary>The records, with the texts in form and out of form under `texts`.</summary>
        public static JObject Records
        {
            get { return Read("StoreRecordVectors.json"); }
        }

        /// <summary>The stores, with what the measured reading answers for each under `meanings`.</summary>
        public static JObject Meanings
        {
            get { return Read("StoreMeaningVectors.json"); }
        }

        private static JObject Read(string name)
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", name);
                if (File.Exists(path))
                {
                    // Read as text: a member that looks like a date stays the text it is.
                    using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None })
                    {
                        return JObject.Load(reader);
                    }
                }
            }

            throw new FileNotFoundException("tests/Shared/" + name + " is not above the test's directory");
        }
    }
}
