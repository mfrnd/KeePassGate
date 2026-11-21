using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Where an OK on a node's tab writes, as a test sees it: every change handed over, and an
    /// answer of the test's choosing.
    /// </summary>
    internal sealed class FakeNodeRules : INodeRules
    {
        /// <summary>Every set of changes handed over, in order.</summary>
        public List<RuleChanges> Written { get; } = new List<RuleChanges>();

        /// <summary>What a write answers: null for every change kept, or what the operator is told of changes not kept.</summary>
        public string Refusal { get; set; }

        /// <summary>What a write throws, or null for nothing.</summary>
        public Exception Thrown { get; set; }

        public string Write(RuleChanges changes)
        {
            if (Thrown != null)
            {
                throw Thrown;
            }

            Written.Add(changes);
            return Refusal;
        }
    }

    /// <summary>A dialog source handing every dialog the same tab.</summary>
    internal sealed class FakeNodeDialogs : INodeDialogSource
    {
        private readonly NodeTab _tab;

        public FakeNodeDialogs(NodeTab tab)
        {
            _tab = tab;
        }

        public NodeTab TabFor(Form dialog)
        {
            return _tab;
        }
    }
}
