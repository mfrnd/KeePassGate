using System.Text;
using KeePassGate.Bridge;

namespace KeePassGate.Host.Tests
{
    /// <summary>The tags a vault in these tests is built with.</summary>
    /// <remarks>
    /// Keyed from fixed bytes rather than from a host certificate, because what a test of the
    /// vault asks about a tag is how it follows the content, and the key only has to be the same
    /// one throughout. `NodeTagsTests` asks the key's own questions.
    /// </remarks>
    internal static class TestTags
    {
        public static NodeTags Fixed
        {
            get { return new NodeTags(NodeTags.KeyFrom(Encoding.UTF8.GetBytes("a host certificate for tests"))); }
        }
    }
}
