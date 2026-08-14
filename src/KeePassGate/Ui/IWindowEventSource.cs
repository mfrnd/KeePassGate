using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>A form opening or closing in the host application.</summary>
    public sealed class GateWindowEventArgs : EventArgs
    {
        private readonly Form _form;

        public GateWindowEventArgs(Form form)
        {
            _form = form;
        }

        public Form Form
        {
            get { return _form; }
        }
    }

    /// <summary>
    /// The seam over KeePass's own window notifications. Everything that decides what to do with
    /// an opening dialog is written against this, so it can be driven by a test that raises the
    /// events itself rather than by a running KeePass.
    /// </summary>
    public interface IWindowEventSource
    {
        event EventHandler<GateWindowEventArgs> WindowAdded;

        event EventHandler<GateWindowEventArgs> WindowRemoved;
    }
}
