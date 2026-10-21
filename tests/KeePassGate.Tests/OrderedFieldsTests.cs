using System.Linq;
using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// An entry's strings in the order they were added, which is the order `read` writes them in.
    /// </summary>
    [TestFixture]
    public sealed class OrderedFieldsTests
    {
        [Test]
        public void The_strings_come_out_in_the_order_they_went_in()
        {
            var fields = new OrderedFields();
            fields.Set("Zeta brace", "1");
            fields.Set("Brace", "2");
            fields.Set("middle", "3");

            Assert.That(fields.Keys, Is.EqualTo(new[] { "Zeta brace", "Brace", "middle" }));
            Assert.That(fields.Select(pair => pair.Value), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(fields.Values, Is.EqualTo(new[] { "1", "2", "3" }));
        }

        [Test]
        public void A_string_set_again_keeps_its_place()
        {
            var fields = new OrderedFields();
            fields.Set("Title", "one");
            fields.Set("UserName", "two");
            fields.Set("Title", "three");

            Assert.That(fields.Keys, Is.EqualTo(new[] { "Title", "UserName" }));
            Assert.That(fields["Title"], Is.EqualTo("three"));
            Assert.That(fields.Count, Is.EqualTo(2));
        }

        [Test]
        public void Names_are_told_apart_by_case()
        {
            var fields = new OrderedFields();
            fields.Set("title", "lower");
            fields.Set("Title", "upper");

            string value;
            Assert.That(fields.TryGetValue("TITLE", out value), Is.False);
            Assert.That(fields.ContainsKey("title"), Is.True);
            Assert.That(fields.Count, Is.EqualTo(2));
        }
    }
}
