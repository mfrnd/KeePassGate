using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Ui
{
    /// <summary>A row of the seal's dialog: what was deleted, what it is about, what the seal makes of it.</summary>
    internal sealed class SealRow
    {
        public SealRow(string kind, string of, string after, params string[] identifiers)
        {
            Kind = kind;
            Of = of;
            After = after;
            Identifiers = identifiers;
        }

        /// <summary>The kind of what was deleted, never a name: the first column.</summary>
        public string Kind { get; private set; }

        /// <summary>What it is about: the second column.</summary>
        public string Of { get; private set; }

        /// <summary>What the seal makes of it: the third column.</summary>
        public string After { get; private set; }

        /// <summary>
        /// The whole identifier of every deletion record that tells of what the row is about, or of
        /// the entry it tells of, for its tooltip.
        /// </summary>
        public IReadOnlyList<string> Identifiers { get; private set; }

        /// <summary>The row's tooltip: each whole identifier on a line of its own, and the sentence the third column stands for.</summary>
        public string Tip
        {
            get { return string.Join(Environment.NewLine, Identifiers) + Environment.NewLine + SealRows.Sentence(After); }
        }
    }

    /// <summary>
    /// What the seal's dialog lists of a stopped store, `STORE.md` §625: a row for each loss the
    /// listing holds, each a kind, what it is about and what the seal makes of it, and the line
    /// that counts what the seal keeps.
    /// </summary>
    /// <remarks>
    /// **Apart from the dialog**, so that every string it shows is held by a test with no window,
    /// as the authority page's are by `AuthorityDescription`.
    /// </remarks>
    internal static class SealRows
    {
        public const string SummaryEntry = "Summary entry";
        public const string RuleOfAProfile = "Rule of a profile";
        public const string Switch = "Switch";
        public const string Answer = "Answer to renewals";
        public const string Revocation = "Revocation";
        public const string Removal = "Removal of a name";
        public const string Withdrawn = "Certificate taken away";
        public const string Retirement = "Retirement";
        public const string Damaged = "Damaged entry";
        public const string SetAside = "Set aside by a start";

        public const string Replaced = "named as replaced";
        public const string EmptyRule = "an empty rule";
        public const string SwitchRefusing = "the switch refusing";
        public const string Forever = "refused forever";
        public const string Restated = "restated, stays in force";
        public const string DeletedWithRecord = "deleted with a record";
        public const string RefusesUntilSet = "refuses until set again";
        public const string StoreKept = "this store kept";

        /// <summary>What the second column of a store a start set aside holds after the key's start.</summary>
        public const string FoundNowhere = ", found nowhere";

        /// <summary>What a node the tree does not hold is called in the second column.</summary>
        public const string NodeNotHeld = "a node this database does not hold";

        /// <summary>What the second column holds for a revocation of a client the database names nowhere.</summary>
        public const string ClientNamedNowhere = "a client named nowhere";

        /// <summary>What the second column holds for a removal of a name the database holds nowhere.</summary>
        public const string NameHeldNowhere = "a name held nowhere";

        /// <summary>What the second column holds for a retirement of an authority the database names nowhere.</summary>
        public const string AuthorityNamedNowhere = "an authority named nowhere";

        /// <summary>The length of a key's start, as every short key on the tab shows it.</summary>
        private const int Short = 8;

        /// <summary>The rows of what a stopped store lost, in the order the dialog lists them.</summary>
        /// <param name="leftOut">The objects the seal's act leaves out, which go on refusing.</param>
        /// <param name="nodeTitle">A node's title by its identifier, or null where the tree does not hold it.</param>
        /// <param name="clientName">A client's name by its client key, or null where no admission names it.</param>
        public static IReadOnlyList<SealRow> Of(
            StoreLosses losses, IEnumerable<string> leftOut, Func<string, string> nodeTitle, Func<string, string> clientName)
        {
            if (losses == null)
            {
                return Array.Empty<SealRow>();
            }

            var left = new HashSet<string>(leftOut ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var rows = new List<SealRow>();

            // A store a start set aside first: the key the start made, found nowhere, `STORE.md`
            // §632; the seal keeps this store over it.
            rows.AddRange(losses.SetAside.Select(stop => new SealRow(SetAside, "key " + Start(stop.Value) + FoundNowhere, StoreKept, stop.Key)));

            foreach (var head in losses.Heads)
            {
                rows.Add(new SealRow(SummaryEntry, "height " + Height(head).ToString(CultureInfo.InvariantCulture), Replaced, head));
            }

            foreach (var loss in losses.Lost)
            {
                var row = ObjectRow(loss.Object, nodeTitle, clientName, loss.Lost);
                rows.Add(left.Contains(loss.Object) ? new SealRow(row.Kind, row.Of, RefusesUntilSet, row.Identifiers.ToArray()) : row);
            }

            // A decision whose subject the database names nowhere is shown by its kind alone, with
            // every deletion record that tells of it in its tooltip, `STORE.md` §611.
            rows.AddRange(losses.Revoked.Select(client => new SealRow(Revocation, Start(client), Restated, Told(client, losses.RevokedBy))));
            rows.AddRange(Unnamed(losses, StoreKinds.Revoked, Revocation, ClientNamedNowhere));
            rows.AddRange(losses.Removed.Select(removal => new SealRow(Removal, removal.Key, Restated, removal.Value)));
            rows.AddRange(Unnamed(losses, StoreKinds.Removed, Removal, NameHeldNowhere));
            rows.AddRange(losses.Withdrawn.Select(statement => new SealRow(Withdrawn, Start(statement.Value), Restated, statement.Key)));
            rows.AddRange(losses.Retired.Select(authority => new SealRow(Retirement, Start(authority), Restated, Told(authority, losses.RetiredBy))));
            rows.AddRange(Unnamed(losses, StoreKinds.Retired, Retirement, AuthorityNamedNowhere));

            foreach (var damaged in losses.Damaged)
            {
                var title = damaged.Title();
                rows.Add(new SealRow(Damaged, string.IsNullOrEmpty(title) ? Start(damaged.Hex) : title, DeletedWithRecord, damaged.Hex));
            }

            foreach (var objectText in losses.RefusesAfter)
            {
                var row = ObjectRow(objectText, nodeTitle, clientName, Array.Empty<string>());
                rows.Add(new SealRow(row.Kind, row.Of, RefusesUntilSet, objectText));
            }

            return rows;
        }

        /// <summary>The sentence a third column's words stand for, which its tooltip holds.</summary>
        public static string Sentence(string after)
        {
            switch (after)
            {
                case Replaced:
                    return "The new summary entry names it as replaced.";
                case EmptyRule:
                    return "A rule granting nothing is written for the profile at the node.";
                case SwitchRefusing:
                    return "The node's switch is written refusing.";
                case Forever:
                    return "The client's renewals are refused forever.";
                case Restated:
                    return "It stays in force, and the new summary entry restates it.";
                case DeletedWithRecord:
                    return "The entry is deleted with a deletion record.";
                case StoreKept:
                    return "A keep record keeps this store over the key the start made. Should that key come back, both stores stand and nothing is served until the store is started again.";
                default:
                    return "It refuses until it is set again.";
            }
        }

        /// <summary>
        /// The line counting what the seal keeps over this reading of the store as the seal
        /// leaves it, whole, `SealActs.Sealed`: the authorities, the clients as the clients page
        /// counts them, the names, the ruled nodes and the standing answers, each worded for any
        /// count.
        /// </summary>
        public static string Kept(StoreReading sealedStore)
        {
            var inForce = sealedStore.InForce;
            var authorities = inForce.Authorities.Count;
            var clients = ClientReaches.Admitted(sealedStore, DateTimeOffset.UtcNow);
            var names = ProfileNames.Of(sealedStore).Names.Count;
            var nodes = inForce.Objects
                .Where(objectText => inForce.VersionsOf(objectText).Count > 0)
                .Select(NodeOf)
                .Where(node => node != null)
                .Distinct(StringComparer.Ordinal)
                .Count();
            var answers = inForce.Objects
                .Select(objectText =>
                {
                    string client;
                    return RenewalText.ReadObject(objectText, out client) ? client : null;
                })
                .Count(client => client != null && StoredRenewals.AnswerOf(inForce, client) != null);

            var parts = new List<string>
            {
                authorities == 0 ? "no authority" : Counted(authorities, "authority", "authorities"),
                Counted(clients, "client", "clients"),
                Counted(names, "profile name", "profile names"),
                Counted(nodes, "ruled node", "ruled nodes"),
                Counted(answers, "standing answer", "standing answers"),
            };
            return "Kept: " + string.Join(", ", parts) + ".";
        }

        /// <summary>The row of an object: its kind, what it is about, and what the seal writes for it.</summary>
        /// <param name="told">The deletion records that tell of the object; none, and the row carries the object's text.</param>
        private static SealRow ObjectRow(string objectText, Func<string, string> nodeTitle, Func<string, string> clientName, IReadOnlyList<string> told)
        {
            var identifiers = told.Count == 0 ? new[] { objectText } : told.ToArray();
            string node;
            string profile;
            if (RuleText.ReadObject(objectText, out node, out profile))
            {
                var title = nodeTitle == null ? null : nodeTitle(node);
                var at = string.IsNullOrEmpty(title) ? NodeNotHeld : title;
                return profile == null
                    ? new SealRow(Switch, at, SwitchRefusing, identifiers)
                    : new SealRow(RuleOfAProfile, at + ", " + profile, EmptyRule, identifiers);
            }

            string client;
            if (RenewalText.ReadObject(objectText, out client))
            {
                var name = clientName == null ? null : clientName(client);
                var of = string.IsNullOrEmpty(name) ? Start(client) : name + " (" + Start(client) + ")";
                return new SealRow(Answer, of, Forever, identifiers);
            }

            return new SealRow(RuleOfAProfile, objectText, RefusesUntilSet, identifiers);
        }

        /// <summary>
        /// The tooltip's identifiers of a revocation or a retirement: the whole client key or
        /// authority `id` the second column starts, and each deletion record that tells of it.
        /// </summary>
        private static string[] Told(string value, IReadOnlyDictionary<string, IReadOnlyList<string>> by)
        {
            IReadOnlyList<string> told;
            return new[] { value }.Concat(by != null && by.TryGetValue(value, out told) ? told : Enumerable.Empty<string>()).ToArray();
        }

        /// <summary>The rows of the decisions of a kind whose subject the database names nowhere, each with every deletion record that tells of it.</summary>
        private static IEnumerable<SealRow> Unnamed(StoreLosses losses, string kind, string rowKind, string of)
        {
            return (losses.Unnamed ?? Array.Empty<StoreUnnamed>())
                .Where(decision => decision.Kind == kind)
                .Select(decision => new SealRow(rowKind, of, Restated, decision.Told.ToArray()));
        }

        /// <summary>The node of an object of a rule or a switch, or null for any other object.</summary>
        private static string NodeOf(string objectText)
        {
            string node;
            string profile;
            return RuleText.ReadObject(objectText, out node, out profile) ? node : null;
        }

        /// <summary>Bytes 9 and 10 of an identifier written as text, a summary entry's height.</summary>
        private static int Height(string hex)
        {
            return int.Parse(
                hex.Substring((StoreFormat.TagLength + StoreFormat.SubjectLength) * 2, StoreFormat.HeightLength * 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
        }

        private static string Start(string key)
        {
            return key == null || key.Length <= Short ? key : key.Substring(0, Short);
        }

        private static string Counted(int count, string one, string many)
        {
            return count.ToString("N0", CultureInfo.CurrentCulture) + " " + (count == 1 ? one : many);
        }
    }
}
