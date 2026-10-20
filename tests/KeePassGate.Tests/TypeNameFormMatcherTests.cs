using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TypeNameFormMatcherTests
    {
        [Test]
        public void It_matches_the_exact_full_name()
        {
            var matcher = new TypeNameFormMatcher(typeof(TestDialogForm).FullName);

            using (var form = new TestDialogForm())
            {
                Assert.That(matcher.Matches(form), Is.True);
            }
        }

        [Test]
        public void Either_of_two_names_matches_when_they_are_combined()
        {
            // The grant editor goes on two dialogs that have nothing in common but the tab it puts
            // there, so one decorator watches for both.
            var matcher = new AnyFormMatcher(
                new TypeNameFormMatcher(typeof(TestDialogForm).FullName),
                new TypeNameFormMatcher(typeof(Form).FullName));

            using (var one = new TestDialogForm())
            using (var other = new Form())
            {
                Assert.That(matcher.Matches(one), Is.True);
                Assert.That(matcher.Matches(other), Is.True);
            }
        }

        [Test]
        public void A_combination_of_nothing_matches_nothing()
        {
            using (var form = new TestDialogForm())
            {
                Assert.That(new AnyFormMatcher().Matches(form), Is.False);
                Assert.That(new AnyFormMatcher(null).Matches(form), Is.False);
                Assert.That(new AnyFormMatcher(new IFormMatcher[] { null }).Matches(form), Is.False);
                Assert.That(
                    new AnyFormMatcher(new TypeNameFormMatcher(typeof(TestDialogForm).FullName)).Matches(null),
                    Is.False);
            }
        }

        [Test]
        public void It_does_not_match_another_form()
        {
            var matcher = new TypeNameFormMatcher(typeof(TestDialogForm).FullName);

            using (var form = new Form())
            {
                Assert.That(matcher.Matches(form), Is.False);
            }
        }

        [Test]
        public void It_does_not_match_a_name_that_only_differs_in_case()
        {
            var matcher = new TypeNameFormMatcher(typeof(TestDialogForm).FullName.ToUpperInvariant());

            using (var form = new TestDialogForm())
            {
                Assert.That(matcher.Matches(form), Is.False);
            }
        }

        [Test]
        public void A_missing_form_is_not_a_match_and_not_an_error()
        {
            var matcher = new TypeNameFormMatcher("KeePass.Forms.DatabaseSettingsForm");

            Assert.That(matcher.Matches(null), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        public void A_matcher_without_a_type_name_is_refused(string typeName)
        {
            Assert.Throws<ArgumentException>(() => new TypeNameFormMatcher(typeName));
        }

        [Test]
        public void It_names_itself_for_the_log()
        {
            Assert.That(new TypeNameFormMatcher("KeePass.Forms.DatabaseSettingsForm").ToString(),
                Is.EqualTo("KeePass.Forms.DatabaseSettingsForm"));
        }
    }
}
