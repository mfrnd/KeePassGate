using System;

namespace KeePassGate.Store
{
    /// <summary>
    /// A record as the reading found it under a store key, `STORE.md` §621: the entry, the
    /// version of it that passes, where that version is in the entry, and whether a deletion record
    /// names the entry.
    /// </summary>
    internal sealed class FoundRecord
    {
        public FoundRecord(string hex, StoreChecked form, int index, bool besideDeletion)
        {
            Hex = hex;
            Form = form;
            Index = index;
            BesideDeletion = besideDeletion;
        }

        /// <summary>The entry's identifier, as lowercase hexadecimal.</summary>
        public string Hex { get; private set; }

        /// <summary>The checked form of the version that passes.</summary>
        public StoreChecked Form { get; private set; }

        /// <summary>
        /// Which version passes: 0 for the current, and for an item of the history its place
        /// counted from the newest, as the versions are walked, `STORE.md` §621.
        /// </summary>
        public int Index { get; private set; }

        /// <summary>Whether the version that passes is an item of the entry's history, `STORE.md` §622.</summary>
        public bool ByHistory
        {
            get { return Index > 0; }
        }

        /// <summary>Whether a deletion record names the entry, `STORE.md` §623 step 1.</summary>
        public bool BesideDeletion { get; private set; }

        /// <summary>
        /// Whether it is in force: a record of a kind that counts beside a deletion record naming
        /// it always, and any other only where no deletion record names it, as its row of
        /// `STORE.md` §658 has it.
        /// </summary>
        public bool InForce
        {
            get { return (Form.Row != null && Form.Row.CountsBesideDeletion) || !BesideDeletion; }
        }

        /// <summary>Whether it reads as a record of this kind.</summary>
        public bool Is(string kind)
        {
            return Form.ReadsAsKind && string.Equals(Form.Kind, kind, StringComparison.Ordinal);
        }
    }
}
