using System.Linq;
using KeePassGate.Bridge;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The entries of a tree a write finds its entries in, `STORE.md` §627: a write whose records
    /// would be answered otherwise by a walk of the tree at its steps is told apart.
    /// </summary>
    [TestFixture]
    public sealed class TreeEntriesTests
    {
        private static readonly string[] None = System.Array.Empty<string>();

        [Test]
        public void A_write_that_looks_up_only_what_the_tree_held_and_keeps_is_sound()
        {
            var tree = Tree(out var held, out var other);

            Assert.That(tree.Unsound(new[] { Fresh() }, new[] { held }, new[] { other }, new[] { other }), Is.Null);
        }

        [Test]
        public void A_record_added_under_an_identifier_the_tree_holds_is_unsound()
        {
            var tree = Tree(out var held, out _);

            Assert.That(tree.Unsound(new[] { held }, None, None, None), Is.Not.Null);
        }

        [Test]
        public void A_record_the_write_adds_and_deletes_sets_back_or_protects_again_is_unsound()
        {
            var tree = Tree(out _, out _);
            var added = Fresh();

            Assert.That(tree.Unsound(new[] { added }, new[] { added }, None, None), Is.Not.Null, "deleted");
            Assert.That(tree.Unsound(new[] { added }, None, new[] { added }, None), Is.Not.Null, "set back");
            Assert.That(tree.Unsound(new[] { added }, None, None, new[] { added }), Is.Not.Null, "protected again");
        }

        [Test]
        public void A_record_the_write_deletes_and_sets_back_or_protects_again_is_unsound()
        {
            var tree = Tree(out var held, out _);

            Assert.That(tree.Unsound(None, new[] { held }, new[] { held }, None), Is.Not.Null, "set back");
            Assert.That(tree.Unsound(None, new[] { held }, None, new[] { held }), Is.Not.Null, "protected again");
        }

        /// <summary>A tree of two entries in a group, and the identifier of each as lowercase hexadecimal.</summary>
        private static TreeEntries Tree(out string held, out string other)
        {
            var root = new PwGroup(true, true, "Invented root", PwIcon.Folder);
            var group = new PwGroup(true, true, "Invented group", PwIcon.Folder);
            root.AddGroup(group, true);
            var first = new PwEntry(true, true);
            var second = new PwEntry(true, true);
            group.AddEntry(first, true);
            group.AddEntry(second, true);
            held = KeePassGate.Hex.Of(first.Uuid.UuidBytes);
            other = KeePassGate.Hex.Of(second.Uuid.UuidBytes);
            return new TreeEntries(root.GetEntries(true).ToList());
        }

        /// <summary>An identifier no entry of the tree holds, as lowercase hexadecimal.</summary>
        private static string Fresh()
        {
            return KeePassGate.Hex.Of(new PwUuid(true).UuidBytes);
        }
    }
}
