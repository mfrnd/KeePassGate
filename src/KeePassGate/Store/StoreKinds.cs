using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>What bytes 9 and 10 of a record's identifier hold, `STORE.md` §609.</summary>
    public enum StoreNumber
    {
        /// <summary>Zero.</summary>
        None,

        /// <summary>The height of a summary entry, as its content states it.</summary>
        Height,

        /// <summary>The format number a version record states.</summary>
        Format,

        /// <summary>
        /// For a version whose object is of the type `rule` and holds `/`, the tag of the name
        /// after its first `/`, the subject ending before it; zero for every other version,
        /// `STORE.md` §609. `StoreFormat.SubjectOf` makes the subject and the text tagged.
        /// </summary>
        ProfileTag,
    }

    /// <summary>The form a member the core reads has to take, `STORE.md` §658.</summary>
    public enum StoreForm
    {
        /// <summary>A text the core compares and does not look into.</summary>
        Text,

        /// <summary>64 lowercase hexadecimal characters: a store key, or an admission key.</summary>
        Hex,

        /// <summary>The name of a store key, `STORE.md` §649: 64 lowercase hexadecimal characters.</summary>
        KeyName,

        /// <summary>Identifiers of 32 lowercase hexadecimal characters, separated by single spaces.</summary>
        Identifiers,

        /// <summary>A whole number from 1 to 65,535 in decimal, with no sign and no leading zero.</summary>
        Number,

        /// <summary>Three whole numbers, each from 0, joined by periods.</summary>
        Version,

        /// <summary>
        /// The first 16 characters of an identifier, lowercase hexadecimal: its tag and subject
        /// hash, `STORE.md` §609.
        /// </summary>
        TagAndSubject,

        /// <summary>A single identifier, 32 lowercase hexadecimal characters, `STORE.md` §609.</summary>
        Identifier,
    }

    /// <summary>How a summary entry restates a record of a kind, `STORE.md` §658.</summary>
    public enum StoreRestated
    {
        /// <summary>Not at all.</summary>
        Not,

        /// <summary>By an item of the kind that holds the same value of the member named.</summary>
        BySameMember,

        /// <summary>By an item of the kind whose `id` is the record's identifier.</summary>
        ById,

        /// <summary>By the item `format` of a summary entry that states a number at least the record's.</summary>
        ByFormat,

        /// <summary>
        /// By an item of the kind whose `id` is the record's identifier and which holds the same
        /// value of every member the record's row names, a member absent equal only to a member
        /// absent: each record a statement of its own, `STORE.md` §648.
        /// </summary>
        ByStatement,
    }

    /// <summary>A member the core reads, with its form.</summary>
    public sealed class StoreMember
    {
        public StoreMember(string name, StoreForm form, bool optional = false)
        {
            Name = name;
            Form = form;
            Optional = optional;
        }

        public string Name { get; private set; }

        public StoreForm Form { get; private set; }

        /// <summary>Read where it is present, and a content without it still reads as its kind.</summary>
        public bool Optional { get; private set; }
    }

    /// <summary>One row of the table of `STORE.md` §658: what the core reads of a kind.</summary>
    public sealed class StoreKind
    {
        public StoreKind(
            string name,
            string subjectPrefix,
            string subjectMember,
            StoreNumber number,
            IReadOnlyList<StoreMember> members,
            bool countsBesideDeletion,
            StoreRestated restated,
            string restatedBy,
            IReadOnlyList<StoreMember> item,
            IReadOnlyList<StoreMember> unnamed = null)
        {
            Name = name;
            SubjectPrefix = subjectPrefix;
            SubjectMember = subjectMember;
            Number = number;
            Members = members;
            CountsBesideDeletion = countsBesideDeletion;
            Restated = restated;
            RestatedBy = restatedBy;
            Item = item;
            Unnamed = unnamed;
        }

        /// <summary>The kind, as `KPG.Kind` and an item's `kind` name it.</summary>
        public string Name { get; private set; }

        /// <summary>What the subject begins with, the member's value following it.</summary>
        public string SubjectPrefix { get; private set; }

        /// <summary>The member whose value ends the subject, or null where the subject is the prefix alone.</summary>
        public string SubjectMember { get; private set; }

        public StoreNumber Number { get; private set; }

        /// <summary>The members of a content of this kind the core reads, each in its form.</summary>
        public IReadOnlyList<StoreMember> Members { get; private set; }

        /// <summary>
        /// Whether a record of this kind counts beside a deletion record naming it, where the
        /// other kinds go out of force.
        /// </summary>
        public bool CountsBesideDeletion { get; private set; }

        public StoreRestated Restated { get; private set; }

        /// <summary>The member whose value a record of the kind takes away, which its item holds the same of, or null.</summary>
        public string RestatedBy { get; private set; }

        /// <summary>
        /// The members of an item of this kind in a summary entry, beside `kind`, or null for a
        /// kind no summary entry holds an item of.
        /// </summary>
        public IReadOnlyList<StoreMember> Item { get; private set; }

        /// <summary>
        /// The members of an item of this kind that names no subject, beside `kind`, or null for a
        /// kind restated by its subject alone: a decision a deletion record tells of whose subject
        /// the file names nowhere, `STORE.md` §611.
        /// </summary>
        public IReadOnlyList<StoreMember> Unnamed { get; private set; }
    }

    /// <summary>
    /// Every kind of record the store's first format holds, as `STORE.md` §658 has them: the
    /// table the reading reads every version by, and the guard tells a record by.
    /// </summary>
    /// <remarks>
    /// **Data and nothing else.** No method here describes the table: `StoreKindsTests` makes a
    /// description of it, holds that to a text written out beside the format number, and so puts
    /// any change to a row in front of whoever makes it, `STORE.md` §650. A kind, a kind of item,
    /// a type of object or a changed row is a new form of the store once a release has written
    /// the first.
    ///
    /// **A kind missing here is a kind the plugin does not know**, which stops the gate for the
    /// database whose store holds a record of it, `STORE.md` §621, and is a record whose
    /// identifier the guard cannot tell by its tag.
    /// </remarks>
    public static class StoreKinds
    {
        public const string Authority = "authority";
        public const string Retired = "retired";
        public const string Client = "client";
        public const string Revoked = "revoked";
        public const string Withdrawn = "withdrawn";
        public const string Profile = "profile";
        public const string Removed = "removed";
        public const string Version = "version";
        public const string Kept = "kept";
        public const string Format = "format";
        public const string Head = "head";

        /// <summary>The member of an item that names its kind, `STORE.md` §611.</summary>
        public const string ItemKind = "kind";

        /// <summary>The member of an authority's or a retirement's content that holds the store key.</summary>
        public const string StoreKey = "storeKey";

        /// <summary>The member of a summary entry's content and of a version that names what it replaces.</summary>
        public const string After = "after";

        /// <summary>
        /// The member of a version, and of its item, that names the lost versions at its place it
        /// settles for its own object, absent where it names none, `STORE.md` §617.
        /// </summary>
        public const string Lost = "lost";

        /// <summary>The member of a summary entry's content that holds its height.</summary>
        public const string Height = "height";

        /// <summary>
        /// The member every content holds beside its own, `StoreFormat.NonceLength` random bytes
        /// as hexadecimal, `STORE.md` §607.
        /// </summary>
        public const string Nonce = "nonce";

        /// <summary>The member of an item that names the record it restates by identifier.</summary>
        public const string Id = "id";

        /// <summary>
        /// The member of an item of a revocation or a retirement that names no subject, holding the
        /// keyed tag and subject hash of the records it restates, `STORE.md` §611.
        /// </summary>
        public const string Hash = "hash";

        /// <summary>The member of a version, and of its item, naming what it is a version of.</summary>
        public const string ObjectMember = "object";

        /// <summary>The member of a version, and of its item, holding the text of the version, `STORE.md` §617.</summary>
        public const string Doc = "doc";

        /// <summary>The member of a keep record, and of its item, naming the key not kept.</summary>
        public const string Key = "key";

        /// <summary>The member of a version record, and of its item, holding the format number.</summary>
        public const string FormatMember = "format";

        /// <summary>The member of a version record, and of its item, holding the plugin's version.</summary>
        public const string PluginMember = "plugin";

        /// <summary>The member of a retirement, and of its item, naming the authority it retires.</summary>
        public const string AuthorityMember = "authority";

        /// <summary>The member of an admission and of a revocation, and of its item, holding the client key.</summary>
        public const string ClientMember = "client";

        /// <summary>The member of a profile addition and of a removal, and of its item, holding the name.</summary>
        public const string NameMember = "name";

        /// <summary>
        /// The member of a record taking a certificate away, and of its item, naming the
        /// certificate; and of an admission, naming the certificate it admits, `STORE.md` §615.
        /// </summary>
        public const string Certificate = "certificate";

        /// <summary>
        /// The member of a record taking a certificate away, and of its item, naming the renewals
        /// from that certificate its writer had read, `STORE.md` §648.
        /// </summary>
        public const string Renewed = "renewed";

        /// <summary>
        /// The member of a record taking a certificate away, and of its item, naming the
        /// certificate the certificate taken away was renewed from, `STORE.md` §648; and of an
        /// admission a renewal writes, naming the certificate it was renewed from, `STORE.md` §615.
        /// </summary>
        public const string From = "from";

        /// <summary>The member of an admission holding the certificate itself, base64 of its DER, `STORE.md` §615.</summary>
        public const string Der = "der";

        /// <summary>The member of an admission naming the authority that issued its certificate, `STORE.md` §615.</summary>
        public const string Issuer = "issuer";

        /// <summary>The member of an admission holding the approved profile names, `STORE.md` §615.</summary>
        public const string Profiles = "profiles";

        /// <summary>The member of an admission holding the certificate's subject as issued, `STORE.md` §615.</summary>
        public const string IssuedSubject = "subject";

        /// <summary>The member of an admission holding the moment of the approval, `STORE.md` §615.</summary>
        public const string Admitted = "admitted";

        /// <summary>The subject of a summary entry, `STORE.md` §609.</summary>
        public const string StoreSubject = "store";

        /// <summary>What separates the type of a version's object from the rest, `STORE.md` §658.</summary>
        public const char ObjectTypeEnd = ':';

        /// <summary>The type of the object of a profile's rule at a node, `STORE.md` §617.</summary>
        public const string RuleType = "rule";

        /// <summary>The type of the object of a node's switch, `STORE.md` §617.</summary>
        public const string SwitchType = "switch";

        /// <summary>The type of the object of the standing answers to a client's renewals, `STORE.md` §618.</summary>
        public const string RenewType = "renew";

        /// <summary>
        /// The types of object a version is of: a profile's rule at a node, a node's switch, and
        /// an answer to a renewal. A version of another type is of a kind the plugin does not know.
        /// </summary>
        public static readonly IReadOnlyList<string> ObjectTypes = Array.AsReadOnly(new[] { RuleType, SwitchType, RenewType });

        /// <summary>The rows of `STORE.md` §658, in its order.</summary>
        public static readonly IReadOnlyList<StoreKind> All = Array.AsReadOnly(new[]
        {
            new StoreKind(
                Authority, "authority:", Id, StoreNumber.None,
                Members(new StoreMember(Id, StoreForm.Text), new StoreMember(StoreKey, StoreForm.Hex)),
                false, StoreRestated.Not, null, null),
            new StoreKind(
                Retired, "authority:", "authority", StoreNumber.None,
                Members(new StoreMember("authority", StoreForm.Text), new StoreMember(StoreKey, StoreForm.Hex, true)),
                true, StoreRestated.BySameMember, "authority",
                Members(new StoreMember("authority", StoreForm.Text)),
                Members(new StoreMember(Hash, StoreForm.TagAndSubject))),
            // What an admission's meaning reads is held where present and as a text, so a member
            // out of the form `STORE.md` §615 gives it admits nobody and stops nothing, §658.
            new StoreKind(
                Client, "client:", "client", StoreNumber.None,
                Members(
                    new StoreMember("client", StoreForm.Text),
                    new StoreMember(Certificate, StoreForm.Text, true),
                    new StoreMember(Der, StoreForm.Text, true),
                    new StoreMember(Issuer, StoreForm.Text, true),
                    new StoreMember(Profiles, StoreForm.Text, true),
                    new StoreMember(IssuedSubject, StoreForm.Text, true),
                    new StoreMember(Admitted, StoreForm.Text, true),
                    new StoreMember(From, StoreForm.Text, true)),
                false, StoreRestated.Not, null, null),
            new StoreKind(
                Revoked, "client:", "client", StoreNumber.None,
                Members(new StoreMember("client", StoreForm.Text)),
                true, StoreRestated.BySameMember, "client",
                Members(new StoreMember("client", StoreForm.Text)),
                Members(new StoreMember(Hash, StoreForm.TagAndSubject))),
            new StoreKind(
                Withdrawn, "certificate:", Certificate, StoreNumber.None,
                Members(
                    new StoreMember(Certificate, StoreForm.Hex),
                    new StoreMember(Renewed, StoreForm.Text, true),
                    new StoreMember(From, StoreForm.Text, true)),
                true, StoreRestated.ByStatement, Certificate,
                Members(
                    new StoreMember(Id, StoreForm.Text),
                    new StoreMember(Certificate, StoreForm.Hex),
                    new StoreMember(Renewed, StoreForm.Text, true),
                    new StoreMember(From, StoreForm.Text, true))),
            new StoreKind(
                Profile, "profile:", "name", StoreNumber.None,
                Members(new StoreMember("name", StoreForm.Text), new StoreMember(After, StoreForm.Identifiers)),
                false, StoreRestated.Not, null, null),
            new StoreKind(
                Removed, "profile:", "name", StoreNumber.None,
                Members(new StoreMember("name", StoreForm.Text)),
                true, StoreRestated.BySameMember, "name",
                Members(new StoreMember("name", StoreForm.Text), new StoreMember(Id, StoreForm.Text)),
                Members(new StoreMember(Id, StoreForm.Identifier))),
            new StoreKind(
                Version, string.Empty, ObjectMember, StoreNumber.ProfileTag,
                Members(
                    new StoreMember(ObjectMember, StoreForm.Text),
                    new StoreMember(After, StoreForm.Identifiers),
                    new StoreMember(Doc, StoreForm.Text),
                    new StoreMember(Lost, StoreForm.Identifiers, true)),
                false, StoreRestated.ById, null,
                Members(
                    new StoreMember(Id, StoreForm.Text),
                    new StoreMember(ObjectMember, StoreForm.Text),
                    new StoreMember(After, StoreForm.Identifiers),
                    new StoreMember(Doc, StoreForm.Text),
                    new StoreMember(Lost, StoreForm.Identifiers, true))),
            new StoreKind(
                Kept, "key:", Key, StoreNumber.None,
                Members(new StoreMember(Key, StoreForm.KeyName), new StoreMember(After, StoreForm.Identifiers)),
                true, StoreRestated.ById, null,
                Members(
                    new StoreMember(Id, StoreForm.Text),
                    new StoreMember(Key, StoreForm.KeyName),
                    new StoreMember(After, StoreForm.Identifiers))),
            new StoreKind(
                Format, "format:", FormatMember, StoreNumber.Format,
                Members(new StoreMember(FormatMember, StoreForm.Number), new StoreMember("plugin", StoreForm.Version)),
                true, StoreRestated.ByFormat, null,
                Members(new StoreMember(FormatMember, StoreForm.Number), new StoreMember("plugin", StoreForm.Version))),
            new StoreKind(
                Head, StoreSubject, null, StoreNumber.Height,
                Members(new StoreMember(Height, StoreForm.Number), new StoreMember(After, StoreForm.Identifiers)),
                false, StoreRestated.Not, null, null),
        });

        /// <summary>The row of this kind, or null for a kind the plugin does not know.</summary>
        public static StoreKind Find(string name)
        {
            return All.FirstOrDefault(kind => string.Equals(kind.Name, name, StringComparison.Ordinal));
        }

        private static IReadOnlyList<StoreMember> Members(params StoreMember[] members)
        {
            return Array.AsReadOnly(members);
        }
    }
}
