using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The store's format number, held beside a description of the form it numbers, `STORE.md`
    /// §650.
    /// </summary>
    /// <remarks>
    /// **The description is the table as a person reads it, written out here**, and the test
    /// makes the same text from what the code reads by: <see cref="StoreKinds"/> and the constants
    /// of <see cref="StoreFormat"/>. A change to a row, a member, a form, a ceiling, the check or
    /// the layout of an identifier fails this test until the text below is changed with it, and
    /// the text is changed together with <see cref="StoreFormat.Number"/>, or with a stated reason
    /// why the form is the same. Until release 0.3.0 the number stays 1 while the first form is
    /// built, `STORE.md` §658. A pull request that brings a kind's meaning adds that kind's
    /// further members to its line.
    /// </remarks>
    [TestFixture]
    public sealed class StoreKindsTests
    {
        private const int Number = 1;

        private static readonly string[] Described =
        {
            "check: HMAC-SHA256 under the store key, as lowercase hexadecimal, over KPG.Kind, a line feed and KPG.Data, then for each KPG.S. string in ordinal order of the names a line feed, the name, a line feed and the value",
            "identifier: tag 3 bytes, subject 5 bytes, number 2 bytes big-endian, check 6 bytes",
            "ceiling: a string of the store, 1048576 characters",
            "ceiling: a content or an item, 32 members",
            "ceiling: an item's name, KPG.S. and 4 digits",
            "kind authority: subject authority: and id; number none; members id text, storeKey hex; beside a deletion record out of force; restated not",
            "kind retired: subject authority: and authority; number none; members authority text, storeKey hex where present; beside a deletion record counts; restated by an item with the same authority; item authority text",
            "kind client: subject client: and client; number none; members client text; beside a deletion record out of force; restated not",
            "kind revoked: subject client: and client; number none; members client text; beside a deletion record counts; restated by an item with the same client; item client text",
            "kind withdrawn: subject certificate: and certificate; number none; members certificate hex; beside a deletion record counts; restated by an item with the same certificate; item certificate hex",
            "kind profile: subject profile: and name; number none; members name text, after identifiers; beside a deletion record out of force; restated not",
            "kind removed: subject profile: and name; number none; members name text; beside a deletion record counts; restated by an item with the same name; item name text, id text",
            "kind version: subject the object; number none; members object text, after identifiers, doc text; beside a deletion record out of force; restated by an item whose id is its identifier; item id text, object text, after identifiers, doc text; objects of the types rule, switch, renew",
            "kind kept: subject key: and key; number none; members key key name, after identifiers; beside a deletion record counts; restated by an item whose id is its identifier; item id text, key key name, after identifiers",
            "kind format: subject format: and format; number format; members format number, plugin version; beside a deletion record counts; restated by the item format of a summary entry stating a number at least its own; item format number, plugin version",
            "kind head: subject store; number height; members height number, after identifiers, and every item; beside a deletion record out of force; restated not",
        };

        [Test]
        public void The_format_number_is_held_beside_a_description_of_the_form_it_numbers()
        {
            Assert.That(StoreFormat.Number, Is.EqualTo(Number), "the format number moved: change the description with it");
            Assert.That(Description(), Is.EqualTo(Described), "the form changed: change the description, and the number with it or state why not");
        }

        [Test]
        public void A_kind_the_table_does_not_hold_is_not_found()
        {
            Assert.That(StoreKinds.Find("future"), Is.Null);
            Assert.That(StoreKinds.Find("Head"), Is.Null, "kinds are compared by ordinal");
            Assert.That(StoreKinds.Find(null), Is.Null);
            Assert.That(StoreKinds.Find("head").Name, Is.EqualTo("head"));
        }

        /// <summary>The description, made from the table the code reads by and the code's own constants.</summary>
        private static IEnumerable<string> Description()
        {
            yield return "check: HMAC-SHA256 under the store key, as lowercase hexadecimal, over "
                         + StoreFormat.KindField + ", a line feed and " + StoreFormat.DataField
                         + ", then for each " + StoreFormat.ItemPrefix
                         + " string in ordinal order of the names a line feed, the name, a line feed and the value";
            yield return "identifier: tag " + StoreFormat.TagLength + " bytes, subject " + StoreFormat.SubjectLength
                         + " bytes, number " + StoreFormat.HeightLength + " bytes big-endian, check "
                         + StoreFormat.CheckLength + " bytes";
            yield return "ceiling: a string of the store, " + StoreFormat.MaxStringLength + " characters";
            yield return "ceiling: a content or an item, " + KeePassGate.Transport.JsonBody.MaxMembers + " members";
            yield return "ceiling: an item's name, " + StoreFormat.ItemPrefix + " and " + StoreFormat.ItemDigits + " digits";

            foreach (var kind in StoreKinds.All)
            {
                yield return Line(kind);
            }
        }

        /// <summary>A kind's line of the description, its parts joined.</summary>
        private static string Line(StoreKind kind)
        {
            var subject = kind.SubjectMember == null
                ? kind.SubjectPrefix
                : kind.SubjectPrefix.Length == 0 ? "the " + kind.SubjectMember : kind.SubjectPrefix + " and " + kind.SubjectMember;

            return string.Concat(
                "kind ", kind.Name, ": subject ", subject,
                "; number ", kind.Number.ToString().ToLowerInvariant(),
                "; members ", Members(kind.Members),
                kind.Name == StoreKinds.Head ? ", and every item" : string.Empty,
                "; beside a deletion record ", kind.CountsBesideDeletion ? "counts" : "out of force",
                "; restated ", Restated(kind),
                kind.Item != null ? "; item " + Members(kind.Item) : string.Empty,
                kind.Name == StoreKinds.Version ? "; objects of the types " + string.Join(", ", StoreKinds.ObjectTypes) : string.Empty);
        }

        private static string Members(IEnumerable<StoreMember> members)
        {
            return string.Join(", ", members.Select(member =>
                member.Name + " " + Form(member.Form) + (member.Optional ? " where present" : string.Empty)));
        }

        private static string Form(StoreForm form)
        {
            switch (form)
            {
                case StoreForm.KeyName:
                    return "key name";
                default:
                    return form.ToString().ToLowerInvariant();
            }
        }

        private static string Restated(StoreKind kind)
        {
            switch (kind.Restated)
            {
                case StoreRestated.Not:
                    return "not";
                case StoreRestated.BySameMember:
                    return "by an item with the same " + kind.RestatedBy;
                case StoreRestated.ById:
                    return "by an item whose id is its identifier";
                case StoreRestated.ByFormat:
                    return "by the item format of a summary entry stating a number at least its own";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind.Restated, "a way of restating nobody described");
            }
        }
    }
}
