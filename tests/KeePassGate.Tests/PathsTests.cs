using System;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A path joined from parts, where `Path.Combine` would let a later part replace the earlier
    /// ones.
    /// </summary>
    [TestFixture]
    public sealed class PathsTests
    {
        [Test]
        public void Parts_are_joined_with_one_separator_between_each()
        {
            Assert.That(Paths.Join(@"C:\data", "KeePassGate", "plugin.log"), Is.EqualTo(@"C:\data\KeePassGate\plugin.log"));
            Assert.That(Paths.Join(@"C:\data\", "plugin.log"), Is.EqualTo(@"C:\data\plugin.log"));
            Assert.That(Paths.Join("C:/data/", "plugin.log"), Is.EqualTo("C:/data/plugin.log"));
        }

        [Test]
        public void An_empty_part_adds_nothing()
        {
            Assert.That(Paths.Join(@"C:\data", string.Empty, "plugin.log"), Is.EqualTo(@"C:\data\plugin.log"));
            Assert.That(Paths.Join(string.Empty, "plugin.log"), Is.EqualTo("plugin.log"));
        }

        [Test]
        public void A_drive_takes_the_next_part_with_no_separator_as_path_combine_has_it()
        {
            Assert.That(Paths.Join("C:", "plugin.log"), Is.EqualTo("C:plugin.log"));
        }

        [TestCase(@"C:\Windows")]
        [TestCase(@"\Windows")]
        [TestCase("/Windows")]
        [TestCase(@"\\server\share")]
        [TestCase("C:Windows")]
        public void A_rooted_part_after_the_first_is_refused_rather_than_taken(string rooted)
        {
            // Where `Path.Combine` answers with the rooted part alone.
            Assert.That(() => Paths.Join(@"C:\data", "logs", rooted), Throws.ArgumentException);
        }

        [TestCase("..")]
        [TestCase(@"..\Windows")]
        [TestCase(@"logs\..\..\Windows")]
        [TestCase("logs/../..")]
        public void A_part_that_climbs_out_of_the_one_before_it_is_refused(string climbing)
        {
            Assert.That(() => Paths.Join(@"C:\data", climbing), Throws.ArgumentException);
        }

        [Test]
        public void Dots_that_are_part_of_a_name_are_a_name()
        {
            Assert.That(Paths.Join(@"C:\data", "..cache", "a..b"), Is.EqualTo(@"C:\data\..cache\a..b"));
        }

        [Test]
        public void The_first_part_is_the_base_and_is_taken_as_it_is()
        {
            Assert.That(Paths.Join(@"..\data", "plugin.log"), Is.EqualTo(@"..\data\plugin.log"));
        }

        [Test]
        public void A_missing_part_is_refused()
        {
            Assert.That(() => Paths.Join(null, "plugin.log"), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => Paths.Join(@"C:\data", "logs", null), Throws.TypeOf<ArgumentNullException>());
        }
    }
}
