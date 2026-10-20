using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>Records what the host asked for, and can be told to fail the way a real one might.</summary>
    internal sealed class RecordingFormDecorator : IFormDecorator
    {
        private readonly List<Form> _attached = new List<Form>();
        private readonly List<Form> _detached = new List<Form>();

        public IReadOnlyList<Form> Attached
        {
            get { return _attached; }
        }

        public IReadOnlyList<Form> Detached
        {
            get { return _detached; }
        }

        public int DetachAllCount { get; private set; }

        public Exception AttachThrows { get; set; }

        public Exception DetachAllThrows { get; set; }

        public bool Attach(Form form)
        {
            if (AttachThrows != null)
            {
                throw AttachThrows;
            }

            _attached.Add(form);
            return true;
        }

        public void Detach(Form form)
        {
            _detached.Add(form);
        }

        public void DetachAll()
        {
            DetachAllCount++;
            if (DetachAllThrows != null)
            {
                throw DetachAllThrows;
            }
        }
    }
}
