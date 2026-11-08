using System.Collections.Generic;
using System.Globalization;
using KeePassGate.Transport;
using KeePassLib.Utility;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What a request body may carry, held to what KeePass writes into a `.kdbx`: a character
    /// `StrUtil.SafeXmlString` takes out of a string is a character a body is refused for.
    /// </summary>
    /// <remarks>
    /// The agreement was measured once, by calling that method on every code point and comparing,
    /// and nothing held the two to it afterwards, so a KeePass that changed what it drops would
    /// have changed nothing here: a body carrying the character would be answered `201` and come
    /// back short from the file. Against the pinned KeePass, every code point of the basic
    /// multilingual plane.
    /// </remarks>
    [TestFixture]
    public sealed class JsonBodyAgainstKeePassTests
    {
        [Test]
        public void Every_code_point_inside_a_string_is_refused_exactly_where_KeePass_drops_it()
        {
            var disagreements = new List<string>();
            for (var code = 0; code <= 0xFFFF; code++)
            {
                var text = "a" + (char)code + "b";
                if (JsonBody.IsUsable(text) != Kept(text))
                {
                    disagreements.Add(code.ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            Assert.That(disagreements, Is.Empty);
        }

        [Test]
        public void Alone_only_whitespace_is_refused_beyond_what_KeePass_drops()
        {
            // A string of whitespace and nothing else is written and then read back as nothing,
            // which is the reader's doing rather than the writer's, so `SafeXmlString` keeps the
            // four characters XML counts as whitespace and the body refuses them alone.
            var disagreements = new List<string>();
            for (var code = 0; code <= 0xFFFF; code++)
            {
                var text = ((char)code).ToString();
                if (JsonBody.IsUsable(text) != Kept(text))
                {
                    disagreements.Add(code.ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            Assert.That(disagreements, Is.EquivalentTo(new[] { "0009", "000A", "000D", "0020" }));
        }

        [Test]
        public void A_surrogate_pair_is_kept_by_both_and_half_of_one_by_neither()
        {
            var pair = "a" + char.ConvertFromUtf32(0x1FFFE) + "b";
            var half = "a" + char.ConvertFromUtf32(0x1FFFE).Substring(0, 1) + "b";

            Assert.That(Kept(pair), Is.True);
            Assert.That(JsonBody.IsUsable(pair), Is.True);
            Assert.That(Kept(half), Is.False);
            Assert.That(JsonBody.IsUsable(half), Is.False);
        }

        /// <summary>Whether KeePass writes a string into a `.kdbx` as it is.</summary>
        private static bool Kept(string text)
        {
            return StrUtil.SafeXmlString(text) == text;
        }
    }
}
