using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KeePassGate.Gate;
using KeePassGate.Spec;
using KeePassGate.Store;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The words the routing document declares for a `503` are the words the code can send, and
    /// `frozen`, which it declares for a later gate, `STORE.md` §656.
    /// </summary>
    /// <remarks>
    /// Read off the description of the `Unavailable` header, which names every word, and off the
    /// constants every word is sent from, the store's read off `StoreWords` whole, so a word added
    /// on either side alone fails here.
    /// </remarks>
    [TestFixture]
    public sealed class UnavailableWordsTests
    {
        /// <summary>Declared so that a client knows it, and sent by no gate of this version, `STORE.md` §652.</summary>
        private const string Declared = "frozen";

        [Test]
        public void The_document_names_every_word_the_code_sends_and_no_other()
        {
            var store = typeof(StoreWords)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue());
            var sent = new[] { LockedTarget.Error, HeldBackTarget.Undecided, "busy" }.Concat(store);

            Assert.That(HeaderWords(), Is.EquivalentTo(sent.Concat(new[] { Declared })));
        }

        private static string[] HeaderWords()
        {
            string document;
            using (var stream = typeof(SpecDocument).Assembly.GetManifestResourceStream("KeePassGate.Spec.openapi.yaml"))
            using (var reader = new StreamReader(stream))
            {
                document = reader.ReadToEnd().Replace("\r\n", "\n");
            }

            var headers = document.IndexOf("\n  headers:\n", StringComparison.Ordinal);
            var start = document.IndexOf("\n    Unavailable:\n      description: |\n", headers, StringComparison.Ordinal);
            var end = document.IndexOf("\n      required:", start, StringComparison.Ordinal);
            Assert.That(headers, Is.GreaterThan(0));
            Assert.That(start, Is.GreaterThan(headers), "the Unavailable header is declared");

            var description = document.Substring(start, end - start);
            return Regex.Matches(description, "`([a-z]+)`")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .Where(word => word != "head" && word != "get" && word != "error")
                .Distinct()
                .ToArray();
        }
    }
}
