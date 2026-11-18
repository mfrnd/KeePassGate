using System;
using System.Globalization;
using KeePassGate.Transport;

namespace KeePassGate.Store
{
    /// <summary>
    /// The room a store has for its next write, `STORE.md` §627 and §643: the summary entry a
    /// write that adds nothing would write, held to the plugin's own ceilings.
    /// </summary>
    /// <remarks>
    /// **A write adds an item at most beside what is in force**, and names no summary entry that a
    /// write adding nothing would not name, so a store with room takes every write a client can
    /// ask for, and a store without it is served nothing until it is started again, `STORE.md`
    /// §643.
    ///
    /// **The string held to the ceiling is the summary entry's content**, which holds its `after`
    /// beside its height and its nonce, and not the `after` alone: a store whose `after` fitted
    /// and whose content did not would be served while every write into it was refused.
    /// </remarks>
    internal sealed class StoreRoom
    {
        /// <summary>
        /// The characters an identifier takes in a list, its separating space among them,
        /// `STORE.md` §607.
        /// </summary>
        private const int Listed = ((StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength + StoreFormat.CheckLength) * 2) + 1;

        public StoreRoom(int named, int height, int items)
        {
            Named = named;
            Height = height;
            Items = items;
            After = named == 0 ? 0 : (named * Listed) - 1;
            Content = After + Beside(height);
        }

        /// <summary>How many summary entries the next summary entry names in its `after`.</summary>
        public int Named { get; private set; }

        /// <summary>How high the next summary entry is, `STORE.md` §611.</summary>
        public int Height { get; private set; }

        /// <summary>The characters of the next summary entry's `after`.</summary>
        public int After { get; private set; }

        /// <summary>The characters of the next summary entry's content, which holds that `after`.</summary>
        public int Content { get; private set; }

        /// <summary>How many items the next summary entry holds, its item `format` among them.</summary>
        public int Items { get; private set; }

        /// <summary>
        /// Whether the next summary entry fits the plugin's ceiling for a string and leaves an item
        /// name for a further item.
        /// </summary>
        public bool HasRoom
        {
            get { return Content <= StoreFormat.MaxWritten && Items < StoreFormat.MaxItems; }
        }

        /// <summary>
        /// Whether the store has grown to half of either ceiling, from which the operator is told,
        /// `STORE.md` §643: an `after` of half the longest string the plugin writes, or half its
        /// item names.
        /// </summary>
        public bool PastHalf
        {
            get { return After >= StoreFormat.MaxWritten / 2 || Items >= (StoreFormat.MaxItems + 1) / 2; }
        }

        /// <summary>
        /// About how many writes still fit, each naming a summary entry more than the write before
        /// it: none where the next does not fit.
        /// </summary>
        public int Writes
        {
            get { return Content > StoreFormat.MaxWritten ? 0 : ((StoreFormat.MaxWritten - Content) / Listed) + 1; }
        }

        /// <summary>How many further items the next summary entry has names for.</summary>
        public int ItemsLeft
        {
            get { return Math.Max(0, StoreFormat.MaxItems - Items); }
        }

        /// <summary>Why the store has no room for its next write, for the log, or null where it has room.</summary>
        public string Reason
        {
            get
            {
                if (HasRoom)
                {
                    return null;
                }

                return Content > StoreFormat.MaxWritten
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "the store has no room for its next write: its summary entry would name {0} summary entries in a content of {1} characters, and the plugin writes no string of the store longer than {2}",
                        Named,
                        Content,
                        StoreFormat.MaxWritten)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "the store has no room for its next write: its summary entry would hold {0} items, and a summary entry the plugin writes has names for {1}, so none is left for a further item",
                        Items,
                        StoreFormat.MaxItems);
            }
        }

        /// <summary>How far the store has grown and what it has room for, for the log.</summary>
        public string Grown
        {
            get
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "its next summary entry names {0} summary entries in an after of {1} characters, of the {2} the plugin writes a string of the store with, and holds {3} of the {4} items a summary entry has names for: about {5} more writes fit, and {6} more items",
                    Named,
                    After,
                    StoreFormat.MaxWritten,
                    Items,
                    StoreFormat.MaxItems,
                    Writes,
                    ItemsLeft);
            }
        }

        /// <summary>
        /// The characters of a summary entry's content beside its `after`: its other members, a
        /// height and a nonce, written as every content is, `STORE.md` §607 and §611.
        /// </summary>
        private static int Beside(int height)
        {
            using (var content = new JsonWriter())
            {
                return content
                    .Text(StoreKinds.After, string.Empty)
                    .Text(StoreKinds.Height, height.ToString(CultureInfo.InvariantCulture))
                    .Text(StoreKinds.Nonce, new string('0', StoreFormat.NonceLength * 2))
                    .Done()
                    .Length;
            }
        }
    }
}
