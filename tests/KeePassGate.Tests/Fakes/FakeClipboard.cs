using System;
using System.Collections.Generic;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A clipboard that remembers instead of one the developer is using.
    /// </summary>
    /// <remarks>
    /// The real clipboard is shared with everything on the desktop, so a test that wrote to it
    /// would take whatever the person running the tests had copied.
    /// </remarks>
    internal sealed class FakeClipboard : IClipboard
    {
        private readonly List<string> _copied = new List<string>();

        /// <summary>When set, every copy fails with this. What a busy clipboard looks like.</summary>
        public Func<Exception> Fails { get; set; }

        public IReadOnlyList<string> Copied
        {
            get { return _copied; }
        }

        public string Last
        {
            get { return _copied.Count == 0 ? null : _copied[_copied.Count - 1]; }
        }

        public void Set(string text)
        {
            if (Fails != null)
            {
                throw Fails();
            }

            _copied.Add(text);
        }
    }
}
