using System.Threading;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>The label every page and dialog of the plugin writes its text into.</summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TextLabelTests
    {
        [Test]
        public void An_ampersand_in_its_text_is_no_access_key()
        {
            // It draws an ampersand as an ampersand, so it may not act on one either: a database
            // named `R&D` made Alt+D a key on the approval dialogs that nothing on screen showed.
            using (var label = new TextLabel { Text = "R&D" })
            {
                Assert.That(label.UseMnemonic, Is.False);
            }
        }
    }
}
