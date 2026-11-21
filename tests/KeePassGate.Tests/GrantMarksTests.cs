using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the grant table draws on a row beyond its texts, worked out from the rows the sheet
    /// hands over: which rows are bound, where the bracket closes, which letters are bold and which
    /// background a row takes.
    /// </summary>
    [TestFixture]
    public sealed class GrantMarksTests
    {
        [Test]
        public void Two_versions_are_bound_closed_at_the_first_and_the_last_and_bold_where_they_differ()
        {
            var rows = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("ops", "row:R")), null).Rows;
            var marks = GrantMarks.Of(rows, true);

            Assert.That(rows.Select(row => row.Profile + " " + row.Mask), Is.EqualTo(new[]
            {
                "ops .R......", "reader .R...L..", "reader CR...L..",
            }));

            Assert.That(Shape(marks), Is.EqualTo(new[] { "alone", "first", "last" }));
            Assert.That(marks[0].Bold, Is.Empty, "a row standing alone has nothing to differ from");
            Assert.That(marks[1].Bold, Is.EqualTo(new[] { 0 }));
            Assert.That(marks[2].Bold, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void Three_versions_with_a_version_that_takes_the_rule_off_are_bold_wherever_any_differs()
        {
            var rows = Sheets.Of(
                Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "off:R")),
                Sheets.Arriving(("reader", "CRUL"))).Rows;
            var marks = GrantMarks.Of(rows, true);

            Assert.That(rows.Select(row => row.Mask + " " + row.Kind), Is.EqualTo(new[]
            {
                ".R...... Version", ".R...L.. Version", "CR...L.. Version",
            }));
            Assert.That(rows[0].States, Is.EqualTo(new[] { GrantState.Removed }), "the version that took the rule off");

            Assert.That(Shape(marks), Is.EqualTo(new[] { "first", "middle", "last" }));
            foreach (var mark in marks)
            {
                Assert.That(mark.Bold, Is.EqualTo(new[] { 0, 5 }));
            }
        }

        [Test]
        public void A_limit_is_bound_under_its_inherited_rule_with_the_letters_the_limit_takes_away_in_bold()
        {
            var rows = Sheets.Of(Rules.Stored(("reader", "off:R")), Sheets.Arriving(("reader", "CRUL"))).Rows;
            var marks = GrantMarks.Of(rows, true);

            Assert.That(rows.Select(row => row.Mask + " " + row.Kind), Is.EqualTo(new[]
            {
                "CRU..L.. Inherited", ".R...... Limit",
            }));
            Assert.That(Shape(marks), Is.EqualTo(new[] { "first", "last" }));
            Assert.That(marks[0].Bold, Is.EqualTo(new[] { 0, 2, 5 }));
            Assert.That(marks[1].Bold, Is.EqualTo(new[] { 0, 2, 5 }));
        }

        [Test]
        public void Versions_that_hold_the_same_letters_are_bound_with_no_letter_in_bold()
        {
            var marks = GrantMarks.Of(Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", "off:R")), null).Rows, true);

            Assert.That(Shape(marks), Is.EqualTo(new[] { "first", "last" }));
            Assert.That(marks.SelectMany(mark => mark.Bold), Is.Empty);
        }

        [Test]
        public void Two_groups_side_by_side_are_two_brackets()
        {
            var marks = GrantMarks.Of(Sheets.Of(
                Rules.Stored(("ops", "row:R"), ("ops", "row:RL"), ("reader", "row:C"), ("reader", "row:U")),
                null).Rows,
                true);

            Assert.That(Shape(marks), Is.EqualTo(new[] { "first", "last", "first", "last" }));
            Assert.That(marks[0].Bold, Is.EqualTo(new[] { 5 }));
            Assert.That(marks[2].Bold, Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void A_first_showing_marks_no_row_as_changed_and_a_press_marks_the_rows_it_changed()
        {
            var sheet = Sheets.Of(
                Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "row:R"), ("ops", "row:R")),
                null);

            Assert.That(GrantMarks.Of(sheet.Rows, true).Any(mark => mark.Changed), Is.False);

            var removed = sheet.Rows.First(row => row.Profile == "reader").Version;
            Assert.That(sheet.RemoveVersion("reader", removed), Is.True);

            var marks = GrantMarks.Of(sheet.Rows, true);
            Assert.That(Shape(marks), Is.EqualTo(new[] { "alone", "first", "last" }));
            Assert.That(marks.Select(mark => mark.Changed), Is.EqualTo(new[] { false, true, true }));
        }

        [Test]
        public void A_changed_row_takes_the_light_yellow_over_the_light_red_and_no_color_where_none_is_given()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("ops", "row:R"), ("deploy", "row:R")), null);
            Assert.That(sheet.Set("deploy", ".R...L.."), Is.Null);
            Assert.That(sheet.RemoveVersion("reader", sheet.Rows.First(row => row.Profile == "reader").Version), Is.True);
            Assert.That(sheet.Set("reader", "C......."), Is.Null);

            var tints = RowTints.For(false);
            var rows = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("ops", "row:R")), null).Rows;
            var bound = GrantMarks.Of(rows, true);
            Assert.That(bound.Select(mark => mark.Tint(tints)), Is.EqualTo(new[] { Color.Empty, RowTints.BoundColor, RowTints.BoundColor }));

            var changed = GrantMarks.Of(sheet.Rows, true);
            Assert.That(sheet.Rows.Select(row => row.Profile), Is.EqualTo(new[] { "deploy", "ops", "reader" }));
            Assert.That(changed.Select(mark => mark.Tint(tints)), Is.EqualTo(new[] { RowTints.ChangedColor, Color.Empty, RowTints.ChangedColor }));

            Assert.That(bound.Concat(changed).Select(mark => mark.Tint(RowTints.For(true))).Distinct(), Is.EqualTo(new[] { Color.Empty }));
        }

        [Test]
        public void A_row_bound_and_changed_at_once_takes_the_light_yellow()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "row:R")), null);
            Assert.That(sheet.RemoveVersion("reader", sheet.Rows[0].Version), Is.True);

            var marks = GrantMarks.Of(sheet.Rows, true);
            Assert.That(marks.All(mark => mark.Bound && mark.Changed), Is.True);
            Assert.That(marks.Select(mark => mark.Tint(RowTints.Standard)).Distinct(), Is.EqualTo(new[] { RowTints.ChangedColor }));
        }

        [Test]
        public void A_change_no_OK_can_write_is_marked_as_no_change_and_a_bound_row_keeps_the_light_red()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "row:R"), ("ops", "row:R")), null);
            Assert.That(sheet.RemoveVersion("reader", sheet.Rows.First(row => row.Profile == "reader").Version), Is.True);
            Assert.That(sheet.Set("ops", "C......."), Is.Null);
            sheet.Rebase(
                Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "row:R"), ("ops", "row:R")), null, Sheets.Names, GrantTab.StoreUnusable);

            Assert.That(sheet.CanEdit, Is.False);
            Assert.That(sheet.Rows.All(row => row.Changed), Is.True, "every press stays on the table");

            var marks = GrantMarks.Of(sheet.Rows, sheet.CanEdit);
            Assert.That(marks.Any(mark => mark.Changed), Is.False);
            Assert.That(Shape(marks), Is.EqualTo(new[] { "alone", "first", "last" }));
            Assert.That(marks.Select(mark => mark.Tint(RowTints.Standard)), Is.EqualTo(new[] { Color.Empty, RowTints.BoundColor, RowTints.BoundColor }));
        }

        [Test]
        public void The_bracket_takes_a_gap_and_a_line_as_wide_as_the_tight_space_and_both_grow_with_the_text()
        {
            using (var designed = new Font("Microsoft Sans Serif", 8.25f))
            using (var twice = new Font("Microsoft Sans Serif", 16.5f))
            {
                Assert.That(GrantMarks.Room(designed), Is.GreaterThanOrEqualTo(Spacing.Gap));
                Assert.That(GrantMarks.Stroke(designed), Is.GreaterThanOrEqualTo(Spacing.Tight));
                Assert.That(GrantMarks.Room(twice), Is.EqualTo(2 * GrantMarks.Room(designed)).Within(2));
                Assert.That(GrantMarks.Stroke(twice), Is.EqualTo(2 * GrantMarks.Stroke(designed)).Within(1));
                Assert.That(GrantMarks.Room(designed), Is.GreaterThanOrEqualTo(3 * GrantMarks.Stroke(designed)),
                    "a line, its arms and the air before the name");
            }
        }

        [Test]
        public void The_text_of_a_row_stays_legible_on_each_color()
        {
            // The contrast as the web's accessibility guidelines count it, for the window's text
            // and for the gray of an inherited rule, on each color a row can take; at least what
            // the guidelines ask of ordinary text.
            foreach (var tint in new[] { RowTints.ChangedColor, RowTints.BoundColor })
            {
                foreach (var text in new[] { SystemColors.ControlText, SystemColors.GrayText })
                {
                    var contrast = Contrast(tint, text);
                    TestContext.Out.WriteLine(Hex(text) + " on " + Hex(tint) + ": " + contrast.ToString("0.00", CultureInfo.InvariantCulture));
                    Assert.That(contrast, Is.GreaterThanOrEqualTo(4.5), Hex(text) + " on " + Hex(tint));
                }
            }

            Assert.That(RowTints.ChangedColor, Is.EqualTo(Color.FromArgb(0xFF, 0xF3, 0xB0)));
        }

        [Test]
        public void A_high_contrast_setting_takes_no_fixed_color()
        {
            var high = RowTints.For(true);
            Assert.That(high.Changed.IsEmpty && high.Bound.IsEmpty, Is.True);

            var ordinary = RowTints.For(false);
            Assert.That(new[] { ordinary.Changed, ordinary.Bound }, Is.EqualTo(new[] { RowTints.ChangedColor, RowTints.BoundColor }));
        }

        [Test]
        public void A_lost_version_beside_two_equal_versions_puts_no_letter_in_bold()
        {
            var marks = GrantMarks.Of(Sheets.Of(Rules.Stored(("reader", null), ("reader", "row:RL"), ("reader", "row:RL")), null).Rows, true);

            Assert.That(Shape(marks), Is.EqualTo(new[] { "first", "middle", "last" }));
            Assert.That(marks.SelectMany(mark => mark.Bold), Is.Empty, "the versions that read agree");
        }

        [Test]
        public void A_lost_version_beside_a_single_version_puts_no_letter_in_bold()
        {
            var marks = GrantMarks.Of(Sheets.Of(Rules.Stored(("reader", null), ("reader", "row:CRL")), null).Rows, true);

            Assert.That(Shape(marks), Is.EqualTo(new[] { "first", "last" }));
            Assert.That(marks.SelectMany(mark => mark.Bold), Is.Empty, "a single mask that reads differs from nothing");
        }

        [Test]
        public void An_unreadable_version_gets_no_bold_and_counts_for_nothing_in_what_the_others_differ_in()
        {
            var rows = Sheets.Of(Rules.Stored(("reader", "row:x"), ("reader", "row:R"), ("reader", "row:RL")), null).Rows;
            var marks = GrantMarks.Of(rows, true);

            Assert.That(rows.Select(row => row.Kind), Is.EqualTo(new[] { GrantRowKind.Lost, GrantRowKind.Version, GrantRowKind.Version }));
            Assert.That(marks.Select(mark => string.Join(",", mark.Bold)), Is.EqualTo(new[] { string.Empty, "5", "5" }));
        }

        [Test]
        public void Versions_for_a_name_the_database_no_longer_names_put_no_letter_in_bold()
        {
            var rows = Sheets.Of(Rules.Stored(("retired", "row:R"), ("retired", "row:RL")), null).Rows;
            var marks = GrantMarks.Of(rows, true);

            Assert.That(rows.All(row => row.States.Contains(GrantState.NoProfile)), Is.True, "the masks are struck through");
            Assert.That(Shape(marks), Is.EqualTo(new[] { "first", "last" }));
            Assert.That(marks.SelectMany(mark => mark.Bold), Is.Empty);
        }

        /// <summary>Where each row stands in its bracket: alone, first, middle or last.</summary>
        private static string[] Shape(System.Collections.Generic.IList<GrantMarks> marks)
        {
            return marks.Select(mark => !mark.Bound
                    ? "alone"
                    : mark.First ? (mark.Last ? "both" : "first") : mark.Last ? "last" : "middle")
                .ToArray();
        }

        private static double Contrast(Color first, Color second)
        {
            var lighter = Math.Max(Luminance(first), Luminance(second));
            var darker = Math.Min(Luminance(first), Luminance(second));
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double Luminance(Color color)
        {
            return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
        }

        private static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private static string Hex(Color color)
        {
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture)
                   + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
