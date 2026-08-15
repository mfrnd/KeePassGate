using System;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>A node's grants in a field rather than in a dialog.</summary>
    /// <remarks>
    /// One of these rather than one per fixture. Four had grown across three files: two that
    /// remembered a document, one that refused every write and one that answered a fixed document
    /// and refused. Doubles of the same seam in different files are how two tests come to
    /// disagree about what the real thing does.
    ///
    /// A refusal is a field rather than a subclass, because what a refusing store does differently
    /// is throw, and which exception it throws is the whole of what a caller can tell apart:
    /// `GrantPanel` catches the reflection family and the user interface one, and a test about that
    /// has to say which it means.
    /// </remarks>
    public sealed class FakeGrantStore : IGrantStore
    {
        public FakeGrantStore()
            : this(null)
        {
        }

        public FakeGrantStore(string document)
        {
            Current = document;
        }

        /// <summary>What it holds, which is what it was last written.</summary>
        public string Current { get; private set; }

        /// <summary>How many writes it took, so that a test can say one happened rather than none.</summary>
        public int Writes { get; private set; }

        /// <summary>What a write throws, or null to take it.</summary>
        public Exception Refusal { get; set; }

        public string Read()
        {
            return Current;
        }

        public void Write(string document)
        {
            if (Refusal != null)
            {
                throw Refusal;
            }

            Current = document;
            Writes++;
        }
    }
}
