using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace KeePassGate.Store
{
    /// <summary>
    /// The checked forms of the last reading, by the identifier of the entry and the objects of
    /// each version, name for name: what a gate keeps between two holds, `STORE.md` §620.
    /// </summary>
    /// <remarks>
    /// **Nothing else is kept**: no candidate, no key that stands, no record that counts, no
    /// summary entry in force, no state and no word. A reading builds the next of these from the
    /// versions it met, so a version its entry has let go of leaves with that reading, its key
    /// overwritten, and <see cref="Clear"/> overwrites every key and drops everything, at a lock
    /// and at a close, as the gate key is, `CA.md` §208.
    ///
    /// **Found in a single lookup**, by the entry and the very object of the version's first
    /// string, and then held to every other object of it, so a version is found in the same time
    /// however long its entry's history is.
    ///
    /// **Not shared between two databases**: each served database has its own, so a form checked
    /// under one database's key is never an answer for another's.
    /// </remarks>
    internal sealed class StoreKept
    {
        private readonly Dictionary<Slot, List<StoreChecked>> _forms = new Dictionary<Slot, List<StoreChecked>>();

        /// <summary>Overwrites every key held and drops every form.</summary>
        public void Clear()
        {
            foreach (var form in _forms.Values.SelectMany(forms => forms))
            {
                form.Forget();
            }

            _forms.Clear();
        }

        /// <summary>The form kept for this version of this entry, or null where the objects differ.</summary>
        /// <remarks>
        /// More than a single form under a slot only where two versions of an entry share their
        /// first object, which is two versions of the same text.
        /// </remarks>
        internal StoreChecked Find(string entry, StoreVersion version)
        {
            List<StoreChecked> forms;
            return _forms.TryGetValue(new Slot(entry, version), out forms)
                ? forms.FirstOrDefault(form => form.Version.Same(version))
                : null;
        }

        /// <summary>Keeps a form for an entry, for the next reading.</summary>
        internal void Keep(string entry, StoreChecked form)
        {
            var slot = new Slot(entry, form.Version);
            List<StoreChecked> forms;
            if (!_forms.TryGetValue(slot, out forms))
            {
                forms = new List<StoreChecked>(1);
                _forms[slot] = forms;
            }

            forms.Add(form);
        }

        /// <summary>
        /// Overwrites the keys of every form kept here that the next forms do not hold, which is
        /// a version no entry holds any more.
        /// </summary>
        internal void Leave(StoreKept next)
        {
            var carried = new HashSet<StoreChecked>(next._forms.Values.SelectMany(forms => forms));
            foreach (var form in _forms.Values.SelectMany(forms => forms).Where(form => !carried.Contains(form)))
            {
                form.Forget();
            }

            _forms.Clear();
        }

        /// <summary>An entry, and the object of its version's first string, compared by reference.</summary>
        private struct Slot : IEquatable<Slot>
        {
            private readonly string _entry;
            private readonly object _first;

            public Slot(string entry, StoreVersion version)
            {
                _entry = entry ?? string.Empty;
                _first = version.Values.Count == 0 ? null : version.Values[0];
            }

            public bool Equals(Slot other)
            {
                return string.Equals(_entry, other._entry, StringComparison.Ordinal) && ReferenceEquals(_first, other._first);
            }

            public override bool Equals(object obj)
            {
                return obj is Slot && Equals((Slot)obj);
            }

            public override int GetHashCode()
            {
                return (StringComparer.Ordinal.GetHashCode(_entry) * 31) + RuntimeHelpers.GetHashCode(_first);
            }
        }
    }
}
