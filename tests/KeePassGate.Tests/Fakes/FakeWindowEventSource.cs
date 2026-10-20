using System;
using System.Windows.Forms;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Stands in for KeePass announcing its dialogs, and counts subscriptions so a test can tell
    /// a double start from a harmless one.
    /// </summary>
    internal sealed class FakeWindowEventSource : IWindowEventSource
    {
        private EventHandler<GateWindowEventArgs> _added;
        private EventHandler<GateWindowEventArgs> _removed;

        public event EventHandler<GateWindowEventArgs> WindowAdded
        {
            add
            {
                _added += value;
                AddedSubscriberCount++;
            }

            remove
            {
                _added -= value;
                AddedSubscriberCount--;
            }
        }

        public event EventHandler<GateWindowEventArgs> WindowRemoved
        {
            add
            {
                _removed += value;
                RemovedSubscriberCount++;
            }

            remove
            {
                _removed -= value;
                RemovedSubscriberCount--;
            }
        }

        public int AddedSubscriberCount { get; private set; }

        public int RemovedSubscriberCount { get; private set; }

        public void RaiseWindowAdded(Form form)
        {
            var handler = _added;
            if (handler != null)
            {
                handler(this, new GateWindowEventArgs(form));
            }
        }

        public void RaiseWindowRemoved(Form form)
        {
            var handler = _removed;
            if (handler != null)
            {
                handler(this, new GateWindowEventArgs(form));
            }
        }

        public void RaiseWindowAddedRaw(GateWindowEventArgs e)
        {
            var handler = _added;
            if (handler != null)
            {
                handler(this, e);
            }
        }
    }
}
