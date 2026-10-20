using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateStatusPanelTests
    {
        [Test]
        public void It_shows_the_name_the_version_and_the_status()
        {
            using (var panel = new GateStatusPanel("1.2.3", "nothing is served yet"))
            {
                Assert.That(LabelText(panel, GateTab.TitleLabelName), Is.EqualTo("KeePassGate"));
                Assert.That(LabelText(panel, GateTab.VersionLabelName), Is.EqualTo("Version 1.2.3"));
                Assert.That(LabelText(panel, GateTab.StatusLabelName), Is.EqualTo("nothing is served yet"));
            }
        }

        [Test]
        public void It_carries_the_names_that_ui_automation_looks_for()
        {
            using (var panel = new GateStatusPanel("1.2.3", "status"))
            {
                Assert.That(panel.Name, Is.EqualTo(GateTab.PanelControlName));

                // Left unset on purpose. `AccessibleName` replaces what a control reports as its
                // name, so setting it to the name is both redundant and the thing `CONVENTIONS.md`
                // forbids: automation points at `Name`, which is the `AutomationId`, and a screen
                // reader is what the other one is for.
                Assert.That(panel.AccessibleName, Is.Null.Or.Empty);
            }
        }

        [TestCase(null, "status")]
        [TestCase("1.2.3", null)]
        public void It_refuses_to_be_built_without_its_text(string version, string status)
        {
            Assert.Throws<ArgumentNullException>(() => new GateStatusPanel(version, status));
        }

        private static string LabelText(Control panel, string name)
        {
            return ControlTree.FindByName<Label>(panel, name).Text;
        }
    }

    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateStatusPanelFactoryTests
    {
        [Test]
        public void Each_call_builds_a_separate_panel_because_a_control_belongs_to_one_dialog()
        {
            var factory = new GateStatusPanelFactory("1.2.3", "status");

            using (var first = factory.CreatePanel())
            using (var second = factory.CreatePanel())
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(first, Is.InstanceOf<GateStatusPanel>());
            }
        }

        [Test]
        public void The_default_panel_reports_the_plugin_version_and_says_nothing_is_served()
        {
            using (var panel = (GateStatusPanel)new GateStatusPanelFactory().CreatePanel())
            {
                Assert.That(LabelTextOf(panel, GateTab.VersionLabelName), Is.EqualTo("Version " + GateVersion.Current));
                Assert.That(LabelTextOf(panel, GateTab.StatusLabelName), Is.EqualTo(GateStatusPanelFactory.DefaultStatus));
            }
        }

        [Test]
        public void The_default_status_does_not_claim_to_serve_anything()
        {
            Assert.That(GateStatusPanelFactory.DefaultStatus, Does.Contain("no").IgnoreCase);
            Assert.That(GateStatusPanelFactory.DefaultStatus, Does.Contain("loaded"));
        }

        [TestCase(null, "status")]
        [TestCase("1.2.3", null)]
        public void It_refuses_to_be_built_without_its_text(string version, string status)
        {
            Assert.Throws<ArgumentNullException>(() => new GateStatusPanelFactory(version, status));
        }

        private static string LabelTextOf(Control panel, string name)
        {
            return ControlTree.FindByName<Label>(panel, name).Text;
        }
    }

    [TestFixture]
    public sealed class GateVersionTests
    {
        [Test]
        public void It_reports_something_a_person_can_read()
        {
            Assert.That(GateVersion.Current, Is.Not.Null.And.Not.Empty);
            Assert.That(GateVersion.Current, Does.Not.Contain("+"));
            Assert.That(GateVersion.Current, Does.Match(@"^\d+\.\d+\.\d+"));
        }
    }
}
