using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ControlTreeTests
    {
        [Test]
        public void Descendants_of_null_is_empty()
        {
            Assert.That(ControlTree.Descendants(null), Is.Empty);
        }

        [Test]
        public void Descendants_excludes_the_root_and_includes_every_generation()
        {
            using (var root = new Panel { Name = "root" })
            {
                var child = new Panel { Name = "child" };
                var grandchild = new Label { Name = "grandchild" };
                child.Controls.Add(grandchild);
                root.Controls.Add(child);

                var names = ControlTree.Descendants(root).Select(c => c.Name).ToList();

                Assert.That(names, Is.EqualTo(new[] { "child", "grandchild" }));
            }
        }

        [Test]
        public void Descendants_visits_a_parent_before_its_children_and_siblings_in_order()
        {
            using (var root = new Panel { Name = "root" })
            {
                var first = new Panel { Name = "first" };
                first.Controls.Add(new Label { Name = "first.a" });
                var second = new Panel { Name = "second" };
                second.Controls.Add(new Label { Name = "second.a" });
                root.Controls.Add(first);
                root.Controls.Add(second);

                var names = ControlTree.Descendants(root).Select(c => c.Name).ToList();

                Assert.That(names, Is.EqualTo(new[] { "first", "first.a", "second", "second.a" }));
            }
        }

        [Test]
        public void FindByName_reaches_a_control_several_containers_deep()
        {
            using (var form = Fakes.Dialogs.WithNestedTabControl("General"))
            {
                var found = ControlTree.FindByName<TabControl>(form, KeePassContract.DatabaseSettingsTabControlName);

                Assert.That(found, Is.Not.Null);
                Assert.That(found.Name, Is.EqualTo(KeePassContract.DatabaseSettingsTabControlName));
            }
        }

        [Test]
        public void FindByName_ignores_a_matching_name_of_the_wrong_type()
        {
            using (var form = new Form())
            {
                form.Controls.Add(new Panel { Name = "m_tabMain" });

                Assert.That(ControlTree.FindByName<TabControl>(form, "m_tabMain"), Is.Null);
            }
        }

        [Test]
        public void FindByName_is_case_sensitive_because_control_names_are()
        {
            using (var form = Fakes.Dialogs.WithNestedTabControl("General"))
            {
                Assert.That(ControlTree.FindByName<TabControl>(form, "m_tabMain".ToUpperInvariant()), Is.Null);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        public void FindByName_without_a_name_finds_nothing(string name)
        {
            using (var form = Fakes.Dialogs.WithNestedTabControl("General"))
            {
                Assert.That(ControlTree.FindByName<TabControl>(form, name), Is.Null);
            }
        }

        [Test]
        public void FindFirst_returns_the_earliest_in_visit_order()
        {
            using (var form = new Form())
            {
                var left = new Panel { Name = "left" };
                left.Controls.Add(new TabControl { Name = "leftTabs" });
                var right = new Panel { Name = "right" };
                right.Controls.Add(new TabControl { Name = "rightTabs" });
                form.Controls.Add(left);
                form.Controls.Add(right);

                Assert.That(ControlTree.FindFirst<TabControl>(form).Name, Is.EqualTo("leftTabs"));
            }
        }

        [Test]
        public void FindFirst_returns_null_when_there_is_none()
        {
            using (var form = Fakes.Dialogs.WithoutTabControl())
            {
                Assert.That(ControlTree.FindFirst<TabControl>(form), Is.Null);
            }
        }
    }
}
