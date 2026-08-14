using System;
using System.Windows.Forms;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>A panel factory that hands out a plain control, and counts how often.</summary>
    internal sealed class StubPanelFactory : IGatePanelFactory
    {
        public int CreatedCount { get; private set; }

        public Control Last { get; private set; }

        public Control CreatePanel()
        {
            CreatedCount++;
            Last = new Panel { Name = "StubPanel" };
            return Last;
        }
    }

    /// <summary>A panel factory that fails, the way a later step's panel could.</summary>
    internal sealed class ThrowingPanelFactory : IGatePanelFactory
    {
        public static readonly string Message = "the panel could not be built";

        public Control CreatePanel()
        {
            throw new InvalidOperationException(Message);
        }
    }

    /// <summary>A panel factory that quietly returns nothing, which must not produce an empty tab.</summary>
    internal sealed class NullPanelFactory : IGatePanelFactory
    {
        public Control CreatePanel()
        {
            return null;
        }
    }
}
