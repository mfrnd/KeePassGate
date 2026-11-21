using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Security;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

// The measurement's own masks and this fixture's word for the box ticked and cleared.
// cspell:ignore rlap crula tickclear

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Rules changed apart in copies of a real database that the product wrote, brought together
    /// by KeePass's own merge, `STORE.md` §617 and §639: what each node gives each profile once the
    /// copies have met, held to what each copy gave just before, by capabilities.
    /// </summary>
    /// <remarks>
    /// **Written by the product, as an OK writes.** Every rule set, taken off, switched, settled or
    /// removed here is a press on the KPG tab's own sheet, `GrantSheet`, over a reading of the copy
    /// it is made in, with what arrives and the versions below as the tab reads them, and the act
    /// is composed by `RuleActs` over the reading the store's own write is planned against, with
    /// the node's parent and the tree of that write's turn, as a node dialog's OK writes it. No
    /// record is composed here, but where a sequence needs a record deleted or damaged, which no
    /// press does.
    ///
    /// **Brought together by `PwDatabase.MergeIn`**, the copy merged from opened afresh from its
    /// file as KeePass opens the other file, and the copy merged into read through its file after
    /// the merge. A single merge is played under each method KeePass has, in each direction, and
    /// under Synchronize with the entries of the store of either copy dated a day later, which
    /// decides whether a deletion record applies.
    ///
    /// **Held by capabilities, worked out here.** Before every merge the product's walk reads what
    /// each copy gives at every node for every profile, and the bound of the merged copy is the
    /// union of the capabilities of both: each verb alone, each verb that reads or writes with
    /// each switch the same rule holds, and a removal with both switches. A merge gives no
    /// capability beyond that bound, but in the sequences §639 keeps as accepted, which are pinned
    /// at exactly what the measurement gives and held to be beyond the bound, so that a change of
    /// such a residual either way fails. The end of every sequence is the end the measurement gave,
    /// or where no run gave one the end `STORE.md` §617 has, written down beside it and never read
    /// from the product.
    ///
    /// **The sequences are the measurement's**, in its own notation: `row 0 N pf rla` is copy 0's
    /// OK setting the profile's row at N to read, list and attachments, `w` standing for update
    /// as the model writes it, `-` for an empty rule and `off` for the X; `rule` sets several rows
    /// in a single OK; `switch`, `settle`, `drop` and `tickclear` press the check box, the X on a
    /// limit row, the X on a version of a bound group, and the box twice; `move` moves a group as
    /// KeePass does, each later than the last; `sync 1 0` merges copy 0 into copy 1 by
    /// Synchronize, `newer` by Overwrite if newer, and a merge marked `!` is an accepted residual;
    /// `gives` is what a copy gives at a node, `*` the copy merged into last. Every copy starts
    /// as the model's world starts: each profile granted read at the root.
    ///
    /// **After every merge, a write of the plugin's own that touches no rule**, a profile name
    /// added, leaves every answer as it was: the write that deletes what a merge brought back
    /// beside the version replacing it, `STORE.md` §627.
    /// </remarks>
    [TestFixture]
    public sealed class StoreRuleMergeTests
    {
        private static readonly string[] Two = { "pf", "pg" };

        private static readonly string[] Four = { "pf", "pg", "ph", "pi" };

        /// <summary>Every method of KeePass's merge.</summary>
        private static readonly PwMergeMethod[] Methods =
        {
            PwMergeMethod.Synchronize,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.KeepExisting,
            PwMergeMethod.CreateNewUuids,
        };

        /// <summary>
        /// The sequences of the measurement that are not held by the write vectors, in the order
        /// of the list they are taken from: single merges at a node and across levels, the hand
        /// histories, the residuals accepted, three copies, the switch in two inheriting versions,
        /// the three states of a row, moves, and a row that refuses chosen again.
        /// </summary>
        private static readonly Sequence[] Plan =
        {
            // Four profiles and every letter at a single node, `measured-rules-owed-forms` 3.
            Sequence.Merged(
                "O1: pf's row at N: attachments dropped at copy 0, protected values added at copy 1, apart",
                Four, 2, "rule 0 root crusdlap ; row 0 N pf rla ; sync 1 0 ; row 0 N pf rl ; row 1 N pf rlap",
                "N pf RL"),
            Sequence.Merged(
                "O2: pg's row at N: read with attachments at copy 0, update with attachments at copy 1, apart; met, a switch alone",
                Four, 2, "rule 0 root crusdlap ; row 0 N pg rla ; sync 1 0 ; row 0 N pg ra ; row 1 N pg ua",
                "N pg A"),
            Sequence.Merged(
                "O3: ph's row at N: the switches crossed, attachments at copy 0 and protected values at copy 1, apart",
                Four, 2, "rule 0 root crusdlap ; row 0 N ph rlap ; sync 1 0 ; row 0 N ph rla ; row 1 N ph rlp",
                "N ph RL"),
            Sequence.Merged(
                "O4: four profiles at N: pf widened with both switches and ph taken off at copy 0, pg emptied and attachments added to pi at copy 1, apart",
                Four, 2,
                "rule 0 root crusdlap ; rule 0 N pf=rl,pg=rla,ph=rlp,pi=crudl ; sync 1 0 ; row 0 N pf rlap ; row 0 N ph off ; row 1 N pg - ; row 1 N pi crudla",
                "N pf RLAP, N pg -, N ph CRUSDLAP, N pi CRUDLA"),
            Sequence.Merged(
                "O5: pf's row at N: attachments dropped at copy 0, verbs widened at copy 1, apart",
                Four, 2, "rule 0 root crusdlap ; row 0 N pf rla ; sync 1 0 ; row 0 N pf rl ; row 1 N pf crula",
                "N pf RL"),
            Sequence.Merged(
                "O8: under a refusing switch, pf's row removed at copy 0, the switch turned off at copy 1, apart",
                Four, 2,
                "rule 0 root crusdlap ; switch 0 N deny ; row 0 N pf rla ; row 0 N pg rlp ; sync 1 0 ; row 0 N pf off ; switch 1 N inherit",
                "N pf -, N pg RLP, N ph CRUSDLAP"),

            // A group between the root and N, `measured-rules-owed-forms` 4.
            Sequence.Merged(
                "L1: nearest wins, apart at two levels: pf narrowed at N at copy 0, widened at G at copy 1",
                Four, 3, "rule 0 root crusdlap ; row 0 G pf rl ; sync 1 0 ; row 0 N pf r ; row 1 G pf crudla",
                "N pf R, G pf CRUDLA"),
            Sequence.Merged(
                "L2: two profiles at two levels: pg widened at G at copy 0, pf widened at N at copy 1, apart",
                Four, 3,
                "rule 0 root crusdlap ; row 0 G pf rl ; row 0 G pg rl ; row 0 N pf rla ; sync 1 0 ; row 0 G pg rla ; row 1 N pf rlap",
                "G pg RLA, N pf RLAP, N pg RLA"),
            Sequence.Merged(
                "L3: pf's rule at N taken off at copy 0 and narrowed at copy 2, met in copy 0; pf widened at G at copy 1",
                Four, 3,
                "rule 0 root crusdlap ; row 0 G pf rl ; row 0 N pf rla ; sync 1 0 ; sync 2 0 ; row 0 N pf off ; row 2 N pf r ; sync 0 2 ; row 1 G pf crusdlap",
                "N pf R", 3),
            Sequence.Merged(
                "L4: L3 with pf narrowed at G at copy 1",
                Four, 3,
                "rule 0 root crusdlap ; row 0 G pf rl ; row 0 N pf rla ; sync 1 0 ; sync 2 0 ; row 0 N pf off ; row 2 N pf r ; sync 0 2 ; row 1 G pf l",
                "N pf -", 3),
            Sequence.Merged(
                "L5: G's switch turned on at copy 0, N's inheriting; pg's row at N taken off at copy 1, apart",
                Four, 3, "rule 0 root crusdlap ; row 0 G pf rl ; row 0 N pg r ; sync 1 0 ; switch 0 G deny ; row 1 N pg off",
                "G pg -, N pg -"),
            Sequence.Merged(
                "L6: G's switch turned on at copy 0, on and then off at copy 1, apart",
                Four, 3, "rule 0 root crusdlap ; row 0 G pf rl ; sync 1 0 ; switch 0 G deny ; switch 1 G deny ; switch 1 G inherit",
                "G pf RL, G pg -"),
            Sequence.Merged(
                "L7: under G's refusing switch, pf's row at G removed at copy 0; G's switch turned off at copy 1, apart",
                Four, 3,
                "rule 0 root crusdlap ; switch 0 G deny ; row 0 G pf rl ; row 0 G pg rl ; sync 1 0 ; row 0 G pf off ; switch 1 G inherit",
                "G pf -, N pf -"),

            // The hand histories, `S34-DESIGN` 5, as the measurement played them.
            Sequence.Played(
                "H11: the widening copy never saw the node closed: copy 0 sets an empty rule at N and takes it off, copy 1 widens the root",
                Two, 2, "row 0 N pf - ; row 0 N pf off ; row 1 root pf rw ; sync 1 0 ; gives * N pf R", bothWays: true),
            Sequence.Played(
                "H13: a node whose rule was once taken off moved into a wider group in a single copy, then settled and taken by the other copy",
                Two, 3,
                "move 0 N root ; row 0 root pf r ; row 0 G pf rw ; row 0 N pf - ; row 0 N pf off ; move 0 N G ; gives 0 N pf R ; settle 0 N pf ; gives 0 N pf RU ; sync 1 0 ; gives 1 N pf RU"),
            Sequence.Played(
                "H3 settled: a profile N never named, then the press that settles in the copy merged into, taken by the other copy",
                Two, 2,
                "switch 0 N deny ; sync 1 0 ; switch 0 N inherit ; row 1 root pg rw ; sync 1 0 ; gives 1 N pg R ; settle 1 N pg ; gives 1 N pg RU ; sync 0 1 ; gives 0 N pg RU"),
            Sequence.Played(
                "H15: after H1, the root narrowed and widened again in the copy merged into: a limit that no longer cuts is forgotten (accepted)",
                Two, 2,
                "row 0 N pf - ; sync 1 0 ; row 0 N pf off ; row 1 root pf rw ; sync 1 0 ; gives 1 N pf R ; row 1 root pf r ; gives 1 N pf R ; row 1 root pf rw ; gives 1 N pf RU"),
            Sequence.Played(
                "H20: copy 0 takes the rule off N and narrows the root to nothing; copy 1 gives G a rule, apart",
                Two, 3,
                "row 0 N pf - ; sync 1 0 ; row 0 N pf off ; row 0 root pf - ; row 1 G pf rw ; sync 1 0 ; gives * N pf - ; gives * G pf RU",
                bothWays: true),

            // The residuals accepted, `STORE.md` §639, each pinned at what the measurement gives.
            Sequence.Played(
                "H5: a rule taken off under the root, a move in a single direction and a widening apart (accepted)",
                Two, 3,
                "move 0 N root ; row 0 root pf rw ; row 0 G pf r ; row 0 N pf - ; sync 1 0 ; row 0 N pf off ; move 1 N G ; sync 0 1 ; row 1 G pf rw ; sync! 1 0 ; gives * N pf RU",
                bothWays: true),
            Sequence.Played(
                "H7: a node with no rule moved apart from a change above (accepted)",
                Two, 3, "row 0 G pf r ; sync 1 0 ; move 0 N root ; row 1 root pf rw ; sync! 1 0 ; gives * N pf RU", bothWays: true),
            Sequence.Played(
                "H16: a name put back on the list apart from a row widened, at what a client holding the name reaches (accepted)",
                Two, 2,
                "name 0 pg ; sync 1 0 ; unname 0 pg ; sync 1 0 ; name 0 pg ; row 1 root pg rw ; sync! 1 0 ; gives * root pg RU",
                bothWays: true, reach: true),

            // Three copies, `S34-REVIEW` 2.
            Sequence.Played(
                "H1 over three copies: the rule taken off in a copy, the root widened in another, both met in a third",
                Two, 2,
                "row 0 N pf - ; sync 1 0 ; sync 2 0 ; row 0 N pf off ; row 1 root pf rw ; sync 2 0 ; sync 2 1 ; gives 2 N pf R ; sync 0 2 ; sync 1 2 ; gives 0 N pf R ; gives 1 N pf R",
                copies: 3),
            Sequence.Played(
                "H3 over three copies: the switch turned off in a copy, the root widened for pg in another, both met in a third",
                Two, 2,
                "switch 0 N deny ; sync 1 0 ; sync 2 0 ; switch 0 N inherit ; row 1 root pg rw ; sync 2 1 ; sync 2 0 ; gives 2 N pg R ; sync 0 2 ; sync 1 2 ; gives 0 N pg R ; gives 1 N pg R",
                copies: 3),
            Sequence.Played(
                "Three copies: N's switch turned off seeing r, a narrowing of the root reaching that copy by a merge, a rule at G made apart in a third (accepted)",
                Two, 3,
                "switch 1 N deny ; row 2 G pg rw ; sync 2 1 ; switch 1 N inherit ; row 0 root pg off ; newer 1 0 ; sync! 2 1 ; gives * N pg R",
                copies: 3, bothWays: true),

            // The switch in two inheriting versions made apart, `S34-REVIEW` 3, M2.
            Sequence.Played(
                "M2: the switch turned off in two copies apart over different letters, met, then ticked and cleared",
                Two, 2,
                "switch 0 N deny ; sync 1 0 ; switch 0 N inherit ; row 1 root pf rw ; switch 1 N inherit ; sync 0 1 ; gives 0 N pf R ; gives 0 N pg R ; tickclear 0 N ; gives 0 N pf RU ; gives 0 N pg R ; sync 1 0 ; gives 1 N pf RU"),

            // An empty rule, a rule taken off and no rule, `STORE.md` §617.
            Sequence.Merged(
                "EOX: an empty rule at N, a rule taken off at N and no rule at G, the root widened apart",
                Two, 3, "row 0 N pf - ; row 0 N pg r ; row 0 N pg off ; sync 1 0 ; row 1 root pf rw ; row 1 root pg rw",
                "N pf -, N pg RU, G pf RU, G pg RU"),

            // A node that holds a version that lets through, moved, `STORE.md` §617 and §642.
            Sequence.Played(
                "Moved wider: a node whose rule was taken off moved into a group that gives more, then settled",
                Two, 2,
                "rule 0 root rw ; row 0 W pf crusdlap ; row 0 N pf - ; row 0 N pf off ; sync 1 0 ; move 0 N W ; gives 0 N pf RU ; sync 1 0 ; gives 1 N pf RU ; settle 1 N pf ; gives 1 N pf CRUSDLAP ; sync 0 1 ; gives 0 N pf CRUSDLAP"),
            Sequence.Played(
                "Moved narrower: a node whose rule was taken off moved into a group that gives less",
                Two, 2,
                "rule 0 root rw ; row 0 V pf r ; row 0 N pf - ; row 0 N pf off ; sync 1 0 ; move 0 N V ; gives 0 N pf R ; sync 1 0 ; gives 1 N pf R"),
            Sequence.Played(
                "A group moved with two such nodes into a group that gives more, a single node then settled",
                Two, 3,
                "rule 0 root rw ; row 0 W pf crusdlap ; row 0 N pf - ; row 0 N pf off ; row 0 N2 pf - ; row 0 N2 pf off ; sync 1 0 ; move 0 G W ; gives 0 N pf RU ; gives 0 N2 pf RU ; sync 1 0 ; gives 1 N pf RU ; gives 1 N2 pf RU ; settle 1 N pf ; gives 1 N pf CRUSDLAP ; gives 1 N2 pf RU"),
            Sequence.Played(
                "A node's place decided by a merge: moved apart in both copies, the later move kept",
                Two, 2,
                "rule 0 root rw ; row 0 W pf crusdlap ; row 0 V pf r ; row 0 N pf - ; row 0 N pf off ; sync 1 0 ; move 0 N W ; move 1 N V ; sync 0 1 ; gives 0 N pf R ; sync 1 0 ; gives 1 N pf R"),

            // A row that refuses, chosen again on the tab and merged back, `STORE.md` §617 and §657.
            Sequence.Merged(
                "Chosen again: a row that refuses for a lost version, set again on the tab, merged back",
                Two, 2, "row 0 N pf r ; sync 1 0 ; lose 0 N pf ; gives 0 N pf - ; gives 0 N pg R ; row 0 N pf rl ; gives 0 N pf RL",
                "N pf RL, N pg R"),
        };

        [TestCaseSource(nameof(VectorSequences))]
        public void A_sequence_of_the_write_vectors_answers_over_two_real_copies_what_the_model_answered_at_each_step(string name)
        {
            // The model's sequences, `tests/Shared/StoreWriteVectors.json`, played over two real
            // copies, every merge by Synchronize as the model's, and held after each step to what
            // the model's copy gave at every node for every profile. A merge is beyond both copies
            // exactly where the model's was, which pins the residual of H4 at its letters. H9 and
            // the end of W6 are wider than a removal written as an empty rule, by the press that
            // turned the switch off over `off:deny` in a copy holding it.
            var sequence = (JObject)Vectors()["sequences"][name];
            var wrong = new List<string>();
            using (var world = World.Of(sequence))
            {
                var model = new[] { ModelGives(sequence, sequence["start"]["answer"]), ModelGives(sequence, sequence["start"]["answer"]) };
                var answers = new[] { sequence["start"]["answer"], sequence["start"]["answer"] };
                var steps = (JArray)sequence["steps"];
                for (var at = 0; at < steps.Count; at++)
                {
                    var step = steps[at];
                    var copy = (int)step["copy"];
                    var where = Short(name) + ", step " + at.ToString(CultureInfo.InvariantCulture) + ", " + (string)step["command"];
                    var answer = ModelGives(sequence, step["answer"]);
                    if ((string)step["command"] == "Synchronize")
                    {
                        var into = (int)step["act"][2];
                        var from = (int)step["act"][3];
                        var met = world.Merge(into, from, PwMergeMethod.Synchronize);
                        var modelBeyond = Beyond(model[into], model[from], answer);
                        if (!met.Beyond.SequenceEqual(modelBeyond))
                        {
                            wrong.Add(where + ": beyond both copies at " + Listed(met.Beyond) + " where the model was at " + Listed(modelBeyond));
                        }

                        world.NextWrite(world.Copy(into), where, wrong);
                    }
                    else
                    {
                        var played = world.Play(VectorStep(sequence, step, answers[copy]));
                        if (played != (bool)step["played"])
                        {
                            wrong.Add(where + ": " + (played ? "written, where the model wrote nothing" : "nothing written, where the model wrote"));
                        }
                    }

                    Compare(wrong, where, answer, world.Gives(world.Copy(copy)));
                    model[copy] = answer;
                    answers[copy] = step["answer"];
                }
            }

            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        [TestCaseSource(nameof(MetVectorSequences))]
        public void A_sequence_of_the_write_vectors_merged_once_by_each_method_each_way_ends_where_the_model_ended(string name)
        {
            // The sequences of the vectors that end with the copies met both ways, W1 to W8, O6,
            // O7, L8 to L10, the same profile and different profiles changed apart, a version
            // removed and two removals made apart: everything before the last two merges played,
            // and the last merge made once instead, by each method, each way, whatever became of
            // the entries' times, ending where the model ended. W6 ends at what the copy that
            // turned the switch off over `off:deny` gave, by that press. Create new IDs leaves the
            // copy merged into as it was.
            var sequence = (JObject)Vectors()["sequences"][name];
            var steps = (JArray)sequence["steps"];
            var wrong = new List<string>();
            using (var world = World.Of(sequence))
            {
                var answers = new[] { sequence["start"]["answer"], sequence["start"]["answer"] };
                for (var at = 0; at < steps.Count - 2; at++)
                {
                    var step = steps[at];
                    var copy = (int)step["copy"];
                    if ((string)step["command"] == "Synchronize")
                    {
                        world.Merge((int)step["act"][2], (int)step["act"][3], PwMergeMethod.Synchronize);
                    }
                    else
                    {
                        world.Play(VectorStep(sequence, step, answers[copy]));
                    }

                    answers[copy] = step["answer"];
                }

                world.Cells(ModelGives(sequence, steps[steps.Count - 1]["answer"]), Short(name), wrong);
            }

            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        [TestCaseSource(nameof(PlayedSequences))]
        public void A_sequence_changed_apart_ends_where_the_measurement_ended_and_no_merge_gives_more_than_both_copies(string name)
        {
            // Every merge of the sequence held to both copies just before it, but a merge the
            // sequence marks as an accepted residual, which is held to be beyond them and pinned
            // by what follows it; every end the measurement gave; and, where the sequence ends at
            // a merge that either copy can take, the same with that merge the other way.
            var sequence = Plan.Single(found => found.Name == name);
            var wrong = new List<string>();
            Played(sequence, false, wrong);
            if (sequence.BothWays)
            {
                Played(sequence, true, wrong);
            }

            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        [TestCaseSource(nameof(MergedSequences))]
        public void A_sequence_changed_apart_merged_once_by_each_method_each_way_ends_where_the_measurement_ended(string name)
        {
            var sequence = Plan.Single(found => found.Name == name);
            var wrong = new List<string>();
            using (var world = new World(sequence.Profiles, Tree(sequence.Levels), sequence.Copies, sequence.Reach))
            {
                foreach (var step in sequence.Steps)
                {
                    world.Run(step, sequence.Short, wrong);
                }

                world.Cells(sequence.Ends, sequence.Short, wrong);
            }

            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        [TestCase(false, Hand.DeletedForGood)]
        [TestCase(false, Hand.DeletedWithARecord)]
        [TestCase(false, Hand.DeletedWithItsSummaryEntry)]
        [TestCase(false, Hand.DeletedWithItsSummaryEntryAndRecords)]
        [TestCase(false, Hand.EditedWithinTheHistoryLimit)]
        [TestCase(false, Hand.EditedPastTheHistoryLimit)]
        [TestCase(false, Hand.Duplicated)]
        [TestCase(false, Hand.DuplicatedAndTheDuplicateRemoved)]
        [TestCase(true, Hand.DeletedForGood)]
        [TestCase(true, Hand.DeletedWithARecord)]
        [TestCase(true, Hand.DeletedWithItsSummaryEntry)]
        [TestCase(true, Hand.DeletedWithItsSummaryEntryAndRecords)]
        [TestCase(true, Hand.EditedWithinTheHistoryLimit)]
        [TestCase(true, Hand.EditedPastTheHistoryLimit)]
        public void A_version_that_lets_through_changed_by_hand_in_a_copy_gives_no_more_after_any_merge(bool renewal, Hand hand)
        {
            // `STORE.md` §617, §622 and §639: a version that lets pf through at N, or the renewal
            // a widening above wrote of it, deleted, edited or duplicated by hand in a copy, which
            // no press does. While a summary entry in force carries the version its text is held,
            // so a deletion of the version alone changes nothing; with its summary entry gone
            // nothing restates what stands and the store stops; an edit counts by the version the
            // plugin wrote while the history holds it, and stops the store past the limit; a
            // duplicate is a record under an identifier its check does not give, passed over.
            // Then every merge of the two copies, each method each way: nothing beyond what both
            // copies gave, the copy merged into whole and unchanged where neither copy lost
            // anything, and the record of each cell printed.
            var wrong = new List<string>();
            var measured = new StringBuilder();
            using (var world = new World(Two, Tree(2), 2, false))
            {
                foreach (var step in Steps("row 0 N pf - ; row 0 N pf off" + (renewal ? " ; row 0 root pf rw" : string.Empty)))
                {
                    world.Run(step, hand.ToString(), wrong);
                }

                // Copy 1 takes copy 0 as it stands and writes nothing of its own, so that no
                // summary entry of its own carries the version.
                world.Merge(1, 0, PwMergeMethod.Synchronize);
                var before = world.Gives(world.Copy(0));
                Assert.That(Letters(before["N pf"]), Is.EqualTo(renewal ? "RU" : "R"), "the version lets through what it recorded");

                // The writes a minute before the change by hand, which a file keeps to the second.
                Date(world.Copy(0), DateTime.UtcNow.AddMinutes(-1));
                Date(world.Copy(1), DateTime.UtcNow.AddMinutes(-1));
                var changed = world.ByHand(world.Copy(0), hand);

                var stops = hand == Hand.DeletedWithItsSummaryEntry || hand == Hand.DeletedWithItsSummaryEntryAndRecords || hand == Hand.EditedPastTheHistoryLimit;
                Assert.That(world.State(world.Copy(0)), Is.EqualTo(stops ? StoreState.Stopped : StoreState.Whole), hand + ", in the copy changed by hand");
                if (!stops)
                {
                    Compare(wrong, hand + ", in the copy changed by hand", before, world.Gives(world.Copy(0)));
                }

                world.Copy(0).Save();
                world.Copy(1).Save();
                foreach (var cell in Cells(false))
                {
                    using (var target = Opened(world.Copy(cell.Into)))
                    using (var source = Opened(world.Copy(cell.From)))
                    {
                        var named = hand + ", " + cell;
                        var asBefore = world.Gives(target);
                        var other = world.Gives(source);
                        target.Database.MergeIn(source.Database, cell.Method, null);
                        target.SaveCloseAndReopen();
                        var after = world.Gives(target);
                        var state = world.State(target);
                        var deleted = new HashSet<string>(target.Database.DeletedObjects.Select(found => Hex.Of(found.Uuid.UuidBytes)), StringComparer.Ordinal);
                        measured.AppendLine(named + ": " + state + ", N pf " + Shown(after["N pf"]) + "; "
                                            + string.Join(", ", changed.Select(id => id.Substring(0, 8)
                                                + (target.RootGroup.FindEntry(new PwUuid(StoreComposer.BytesOf(id)), true) == null ? " absent" : " present")
                                                + (deleted.Contains(id) ? " with its record" : string.Empty))));
                        wrong.AddRange(Beyond(asBefore, other, after).Select(found => named + ": beyond both copies at " + found));
                        wrong.AddRange(Beyond(before, before, after).Select(found => named + ": beyond the copies before the change by hand at " + found));

                        // A copy merged into by new identifiers is the copy it was. A change that
                        // left the copy whole is kept by every merge, and so is a version deleted
                        // with its summary entry and no record, which the copy that holds it
                        // brings back and nothing tells of. With the records, what KeePass does
                        // with them differs by method and way, `STORE.md` §639 row 2, and so does
                        // an edit past the limit: each is held to the bound alone, its cell
                        // printed.
                        if (cell.Method == PwMergeMethod.CreateNewUuids)
                        {
                            Compare(wrong, named, asBefore, after);
                        }
                        else if (!stops || hand == Hand.DeletedWithItsSummaryEntry)
                        {
                            AssertState(wrong, named, StoreState.Whole, state);
                            Compare(wrong, named, before, after);
                        }

                        world.NextWrite(target, named, wrong);
                    }
                }
            }

            TestContext.Out.WriteLine(measured.ToString());
            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        [Test]
        public void Two_names_whose_tags_meet_both_refuse_for_a_lost_version_and_a_row_set_again_settles_it_for_its_own_row_alone()
        {
            // `STORE.md` §617: a deletion record under a profile's tag tells of every name of that
            // tag the file knows, so a version of the first name's row lost refuses both rows,
            // which is narrower, and every merge into the copy that lost it keeps both refused. A
            // row chosen again on the tab names the version lost in `lost`, which settles it for
            // its own row alone, and the version it replaced in `after`, which answers for both
            // rows the deletion record the write leaves under the same tag. So the first row set
            // again leaves the second refusing for the loss alone, and the second set again settles
            // it for itself, after which both rows stand in each copy the records reach.
            var wrong = new List<string>();
            using (var world = new World(Two, Tree(2), 2, false))
            {
                var names = world.Meeting(world.Copy(0));
                world.Also(names);
                var a = names[0];
                var b = names[1];
                foreach (var step in Steps("rule 0 N " + a + "=r," + b + "=r ; sync 1 0 ; lose 0 N " + a + " ; gives 0 N " + a + " - ; gives 0 N " + b + " - ; gives 0 N pf R"))
                {
                    world.Run(step, "tags", wrong);
                }

                world.Copy(0).Save();
                world.Copy(1).Save();
                foreach (var cell in Cells(false))
                {
                    using (var target = Opened(world.Copy(cell.Into)))
                    using (var source = Opened(world.Copy(cell.From)))
                    {
                        var asBefore = world.Gives(target);
                        var other = world.Gives(source);
                        target.Database.MergeIn(source.Database, cell.Method, null);
                        target.SaveCloseAndReopen();
                        var after = world.Gives(target);
                        wrong.AddRange(Beyond(asBefore, other, after).Select(found => cell + ": beyond both copies at " + found));
                        var refused = cell.Into == 0 || cell.Method == PwMergeMethod.Synchronize;
                        foreach (var name in new[] { a, b })
                        {
                            var wanted = refused ? string.Empty : "R";
                            if (Letters(after["N " + name]) != wanted)
                            {
                                wrong.Add(cell + ": N gives " + name + " " + Shown(after["N " + name]) + " where " + Shown(wanted) + " was expected");
                            }
                        }
                    }
                }

                // The rows set again a minute after everything either copy holds, as an operator
                // sets them, so that a merge applies the deletion records those writes leave.
                Date(world.Copy(0), DateTime.UtcNow.AddMinutes(-1));
                Date(world.Copy(1), DateTime.UtcNow.AddMinutes(-1));
                foreach (var step in Steps("row 0 N " + a + " rl ; gives 0 N " + a + " RL ; gives 0 N " + b + " - ; row 0 N " + b + " rl ; gives 0 N " + b + " RL ; gives 0 N " + a + " RL ; sync 1 0 ; gives 1 N " + a + " RL ; gives 1 N " + b + " RL"))
                {
                    world.Run(step, "tags", wrong);
                }
            }

            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        [Test]
        public void A_name_whose_tag_is_0_refuses_for_a_lost_version_of_its_row_and_the_switch_is_not_told_of()
        {
            // `STORE.md` §609 and §617: a tag of 0 is a tag. A deletion record of a version of a
            // row of such a name has 0 where a switch's has it, and the subject of the node's
            // row, so it tells of the row and never of the switch, through every merge.
            var wrong = new List<string>();
            using (var world = new World(Two, Tree(2), 2, false))
            {
                var zero = world.Zero(world.Copy(0));
                world.Also(new[] { zero });
                foreach (var step in Steps("rule 0 N " + zero + "=r,pf=rl ; switch 0 N deny ; sync 1 0 ; lose 0 N " + zero + " ; gives 0 N " + zero + " - ; gives 0 N pf RL ; gives 0 N pg - ; sync 1 0 ; gives 1 N " + zero + " - ; gives 1 N pf RL"))
                {
                    world.Run(step, "tag of 0", wrong);
                }

                foreach (var copy in new[] { 0, 1 })
                {
                    var rule = world.RuleAt(world.Copy(copy), "N");
                    Assert.That(rule.Switch.Lost, Is.Empty, "copy " + copy + ": the switch is not told of");
                    Assert.That(rule.Row(zero).Lost, Has.Count.EqualTo(1), "copy " + copy + ": the row is");
                }
            }

            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        [Test]
        public void A_lost_version_of_a_name_the_file_does_not_know_refuses_nobody_until_the_name_is_known()
        {
            // `STORE.md` §617: a deletion record under a tag that meets no name the file knows
            // tells of nothing, so a row of a name whose records are all gone refuses nobody, and
            // refuses its profile from the hold on in which the file knows the name again, as
            // where the name is added; then a rule above for the name reaches no further than N.
            var wrong = new List<string>();
            using (var world = new World(Two, Tree(2), 2, false))
            {
                world.Also(new[] { "pq" });
                foreach (var step in Steps("lose 0 N pq ; sync 1 0"))
                {
                    world.Run(step, "unknown", wrong);
                }

                Assert.That(world.RuleAt(world.Copy(0), "N").Row("pq").Lost, Is.Empty, "a tag no known name has tells of nothing");
                foreach (var step in Steps("name 0 pq ; row 0 root pq rw ; gives 0 root pq RU ; gives 0 N pq - ; gives 1 N pq - ; sync 1 0 ; gives 1 N pq - ; gives 1 root pq RU"))
                {
                    world.Run(step, "unknown", wrong);
                }

                Assert.That(world.RuleAt(world.Copy(0), "N").Row("pq").Lost, Has.Count.EqualTo(1), "the name known, the row refuses");
            }

            Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
        }

        /// <summary>What a version that lets through undergoes by hand in a copy.</summary>
        public enum Hand
        {
            /// <summary>Its entry deleted for good, with no deletion record, its summary entry kept.</summary>
            DeletedForGood,

            /// <summary>Its entry deleted for good with a deletion record, its summary entry kept.</summary>
            DeletedWithARecord,

            /// <summary>Its entry and every summary entry restating it deleted for good, with no deletion record.</summary>
            DeletedWithItsSummaryEntry,

            /// <summary>The same, with a deletion record for each.</summary>
            DeletedWithItsSummaryEntryAndRecords,

            /// <summary>Its text edited once in KeePass's entry dialog, which keeps the version written in the history.</summary>
            EditedWithinTheHistoryLimit,

            /// <summary>Edited eleven times, past the ten history items KeePass keeps.</summary>
            EditedPastTheHistoryLimit,

            /// <summary>Duplicated, as KeePass's Duplicate Entry does.</summary>
            Duplicated,

            /// <summary>Duplicated, and the duplicate deleted for good with a deletion record.</summary>
            DuplicatedAndTheDuplicateRemoved,
        }

        private static IEnumerable<TestCaseData> VectorSequences()
        {
            return RuleSequenceNames().Select(name => new TestCaseData(name).SetArgDisplayNames(Short(name)));
        }

        /// <summary>The sequences of the vectors whose last two steps meet the copies both ways.</summary>
        private static IEnumerable<TestCaseData> MetVectorSequences()
        {
            foreach (var name in RuleSequenceNames())
            {
                var steps = (JArray)Vectors()["sequences"][name]["steps"];
                var last = steps.Skip(steps.Count - 2).ToList();
                if (last.Count == 2 && last.All(step => (string)step["command"] == "Synchronize") && (int)last[0]["copy"] != (int)last[1]["copy"])
                {
                    yield return new TestCaseData(name).SetArgDisplayNames(Short(name));
                }
            }
        }

        private static IEnumerable<TestCaseData> PlayedSequences()
        {
            return Plan.Where(sequence => sequence.Ends == null).Select(sequence => new TestCaseData(sequence.Name).SetArgDisplayNames(sequence.Short));
        }

        private static IEnumerable<TestCaseData> MergedSequences()
        {
            return Plan.Where(sequence => sequence.Ends != null).Select(sequence => new TestCaseData(sequence.Name).SetArgDisplayNames(sequence.Short));
        }

        private static IEnumerable<string> RuleSequenceNames()
        {
            return ((JObject)Vectors()["sequences"]).Properties()
                .Select(sequence => sequence.Name)
                .Where(name => name.StartsWith("rules, ", StringComparison.Ordinal) || name.StartsWith("form, ", StringComparison.Ordinal));
        }

        /// <summary>A sequence's short name: the part of its name before the colon.</summary>
        private static string Short(string name)
        {
            var colon = name.IndexOf(':');
            return colon < 0 ? name : name.Substring(0, colon);
        }

        /// <summary>
        /// A sequence played step by step, its last merge the other way where
        /// <paramref name="reversed"/>, so that `*` is the other copy.
        /// </summary>
        private static void Played(Sequence sequence, bool reversed, List<string> wrong)
        {
            var last = sequence.Steps.LastOrDefault(step => step.IsMerge);
            using (var world = new World(sequence.Profiles, Tree(sequence.Levels), sequence.Copies, sequence.Reach))
            {
                foreach (var step in sequence.Steps)
                {
                    world.Run(reversed && step == last ? step.Reversed() : step, sequence.Short + (reversed ? ", the last merge the other way" : string.Empty), wrong);
                }
            }
        }

        /// <summary>A step of the vectors in the notation of this fixture, its node by its name.</summary>
        private static Step VectorStep(JObject sequence, JToken step, JToken previous)
        {
            var act = (JArray)step["act"];
            var copy = ((int)step["copy"]).ToString(CultureInfo.InvariantCulture);
            var node = (string)act[2];
            switch ((string)step["command"])
            {
                case "Set row":
                    return Step.Parse("row " + copy + " " + node + " " + (string)act[3] + " " + ModelMask((string)act[4]));
                case "Set rows":
                    return Step.Parse("rule " + copy + " " + node + " " + (string)act[3]);
                case "Remove row":
                    return Step.Parse("row " + copy + " " + node + " " + (string)act[3] + " off");
                case "Settle limit":
                    return Step.Parse("settle " + copy + " " + node + " " + (string)act[3]);
                case "Set switch":
                    return Step.Parse("switch " + copy + " " + node + " " + (string)act[3]);
                case "Remove version":
                    var objectText = StoreComposer.RowObject((string)sequence["nodes"][(string)act[2]], (string)act[3]);
                    var text = (string)previous["rules"]["standing"][objectText][(int)act[4]];
                    return Step.Parse("drop " + copy + " " + node + " " + (string)act[3] + " " + text);
                default:
                    throw new ArgumentOutOfRangeException(nameof(step), (string)step["command"], "a command this test does not play");
            }
        }

        /// <summary>The model's mask as this notation writes it: `-` for an empty rule.</summary>
        private static string ModelMask(string mask)
        {
            return string.IsNullOrEmpty(mask) ? "-" : mask;
        }

        /// <summary>The name a sequence of the vectors gives the node of this identifier.</summary>
        private static string NodeName(JObject sequence, string hex)
        {
            return ((JObject)sequence["nodes"]).Properties().Single(property => (string)property.Value == hex).Name;
        }

        /// <summary>What a copy of the model gives in this answer, keyed as <see cref="World.Gives"/> keys it.</summary>
        private static Dictionary<string, AclGrant> ModelGives(JObject sequence, JToken answer)
        {
            var gives = new Dictionary<string, AclGrant>(StringComparer.Ordinal);
            foreach (var node in (JObject)answer["rules"]["gives"])
            {
                foreach (var profile in (JObject)node.Value)
                {
                    gives[NodeName(sequence, node.Key) + " " + profile.Key] = Rules.Letters((string)profile.Value);
                }
            }

            return gives;
        }

        /// <summary>The tree of a sequence of this many levels, every group by its name under its parent's.</summary>
        private static (string Node, string Parent)[] Tree(int levels)
        {
            return levels == 3
                ? new[] { ("G", "root"), ("N", "G"), ("N2", "G"), ("W", "root"), ("V", "root") }
                : new[] { ("N", "root"), ("N2", "root"), ("W", "root"), ("V", "root") };
        }

        private static List<Step> Steps(string text)
        {
            return text.Split(';').Select(part => Step.Parse(part.Trim())).ToList();
        }

        /// <summary>
        /// Every single merge of two copies: each method, each way, and Synchronize also with the
        /// store's entries of either copy dated a day later.
        /// </summary>
        private static IEnumerable<Cell> Cells(bool dated)
        {
            foreach (var method in Methods)
            {
                foreach (var times in dated && method == PwMergeMethod.Synchronize ? new[] { Times.AsWritten, Times.IntoLater, Times.FromLater } : new[] { Times.AsWritten })
                {
                    yield return new Cell(method, times, 0, 1);
                    yield return new Cell(method, times, 1, 0);
                }
            }
        }

        /// <summary>
        /// The capabilities a grant gives: each verb alone, each verb but a removal with each
        /// switch the grant holds, and a removal with both switches, as `caps` of the measured
        /// model has them.
        /// </summary>
        private static ISet<string> Capabilities(AclGrant grant)
        {
            var letters = Letters(grant);
            var switches = letters.Where(letter => letter == 'A' || letter == 'P').ToList();
            var capabilities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var verb in letters.Where(letter => "CRUSDL".IndexOf(letter) >= 0))
            {
                capabilities.Add(verb.ToString());
                var with = verb == 'S' || verb == 'D' ? "AP".ToCharArray().ToList() : switches;
                capabilities.UnionWith(with.Select(each => verb + "+" + each));
            }

            return capabilities;
        }

        /// <summary>
        /// Every node and profile at which the merged copy gives a capability neither copy gave
        /// just before the merge, each with what it gives beyond, in ordinal order.
        /// </summary>
        private static List<string> Beyond(
            IReadOnlyDictionary<string, AclGrant> into, IReadOnlyDictionary<string, AclGrant> from, IReadOnlyDictionary<string, AclGrant> merged)
        {
            var beyond = new List<string>();
            foreach (var pair in merged.OrderBy(found => found.Key, StringComparer.Ordinal))
            {
                AclGrant first;
                AclGrant second;
                var bound = new HashSet<string>(StringComparer.Ordinal);
                if (into.TryGetValue(pair.Key, out first))
                {
                    bound.UnionWith(Capabilities(first));
                }

                if (from.TryGetValue(pair.Key, out second))
                {
                    bound.UnionWith(Capabilities(second));
                }

                var extra = Capabilities(pair.Value).Where(capability => !bound.Contains(capability)).OrderBy(capability => capability, StringComparer.Ordinal).ToList();
                if (extra.Count > 0)
                {
                    beyond.Add(pair.Key + " " + string.Join(" ", extra));
                }
            }

            return beyond;
        }

        private static void AssertState(List<string> wrong, string where, StoreState wanted, StoreState state)
        {
            if (state != wanted)
            {
                wrong.Add(where + ": the store is " + state + " where it was expected " + wanted);
            }
        }

        private static string Listed(IReadOnlyList<string> beyond)
        {
            return beyond.Count == 0 ? "no node" : string.Join("; ", beyond);
        }

        /// <summary>Each answer wanted held to the answer given, every difference named.</summary>
        private static void Compare(List<string> wrong, string where, IReadOnlyDictionary<string, AclGrant> wanted, IReadOnlyDictionary<string, AclGrant> given)
        {
            foreach (var pair in wanted.OrderBy(found => found.Key, StringComparer.Ordinal))
            {
                AclGrant answered;
                var letters = given.TryGetValue(pair.Key, out answered) ? Letters(answered) : "nothing asked";
                if (letters != Letters(pair.Value))
                {
                    wrong.Add(where + ": " + pair.Key + " is " + Shown(letters) + " where " + Shown(Letters(pair.Value)) + " was expected");
                }
            }
        }

        /// <summary>The letters of a grant in the order of `CRUSDLAP`.</summary>
        private static string Letters(AclGrant grant)
        {
            return string.Concat(Enumerable.Range(0, VerbMask.Width)
                .Where(at => grant.Holds(AclVerbs.Grantable[at]))
                .Select(at => VerbMask.Letters[at]));
        }

        private static string Shown(AclGrant grant)
        {
            return Shown(Letters(grant));
        }

        private static string Shown(string letters)
        {
            return letters.Length == 0 ? "nothing" : letters;
        }

        /// <summary>
        /// A saved database opened afresh from a copy of its file, as KeePass's import and
        /// synchronize open the other copy.
        /// </summary>
        private static TemporaryDatabase Opened(TemporaryDatabase saved)
        {
            var copy = TemporaryDatabase.New();
            File.Copy(saved.Path, copy.Path, true);
            copy.Reopen();
            return copy;
        }

        /// <summary>
        /// Every entry of the plugin's group, and every deletion record, dated to this moment.
        /// </summary>
        private static void Date(TemporaryDatabase database, DateTime when)
        {
            var group = database.Database.RootGroup.Groups.Single(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes));
            foreach (var entry in group.Entries)
            {
                entry.LastModificationTime = when;
                entry.LocationChanged = when;
            }

            foreach (var deleted in database.Database.DeletedObjects)
            {
                deleted.DeletionTime = when;
            }
        }

        private static JObject Vectors()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreWriteVectors.json");
                if (File.Exists(path))
                {
                    return JObject.Parse(File.ReadAllText(path));
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreWriteVectors.json is not above the test's directory");
        }

        private enum Times
        {
            AsWritten,
            IntoLater,
            FromLater,
        }

        /// <summary>A single merge of two copies: the method, the times, and which copy takes which.</summary>
        private sealed class Cell
        {
            public Cell(PwMergeMethod method, Times times, int into, int from)
            {
                Method = method;
                Times = times;
                Into = into;
                From = from;
            }

            public PwMergeMethod Method { get; private set; }

            public Times Times { get; private set; }

            public int Into { get; private set; }

            public int From { get; private set; }

            public override string ToString()
            {
                return Method + (Times == Times.AsWritten ? string.Empty : ", " + Times) + ", copy " + From + " into copy " + Into;
            }
        }

        /// <summary>A sequence of the measurement, as it was played, with its ends.</summary>
        private sealed class Sequence
        {
            private Sequence(string name, string[] profiles, int levels, int copies, string steps, string ends, bool bothWays, bool reach)
            {
                Name = name;
                Short = StoreRuleMergeTests.Short(name);
                Profiles = profiles;
                Levels = levels;
                Copies = copies;
                Steps = StoreRuleMergeTests.Steps(steps);
                Ends = ends == null ? null : ends.Split(',').Select(end => end.Trim().Split(' ')).ToDictionary(
                    end => end[0] + " " + end[1], end => Rules.Letters(end[2] == "-" ? string.Empty : end[2]), StringComparer.Ordinal);
                BothWays = bothWays;
                Reach = reach;
            }

            public string Name { get; private set; }

            public string Short { get; private set; }

            public string[] Profiles { get; private set; }

            /// <summary>Two for the root and N, three for the root, G and N.</summary>
            public int Levels { get; private set; }

            public int Copies { get; private set; }

            public IReadOnlyList<Step> Steps { get; private set; }

            /// <summary>What the merged copy gives once the copies met, for a sequence ending in a single merge; null otherwise.</summary>
            public IReadOnlyDictionary<string, AclGrant> Ends { get; private set; }

            /// <summary>Whether its last merge is played the other way as well.</summary>
            public bool BothWays { get; private set; }

            /// <summary>Whether what a copy gives is what a client holding the profile reaches, the list of names counted.</summary>
            public bool Reach { get; private set; }

            /// <summary>A sequence whose copies 0 and 1 meet once, by every merge, after the steps: ending at these.</summary>
            public static Sequence Merged(string name, string[] profiles, int levels, string steps, string ends, int copies = 2)
            {
                return new Sequence(name, profiles, levels, copies, steps, ends, false, false);
            }

            /// <summary>A sequence played step by step, holding its own ends.</summary>
            public static Sequence Played(string name, string[] profiles, int levels, string steps, int copies = 2, bool bothWays = false, bool reach = false)
            {
                return new Sequence(name, profiles, levels, copies, steps, null, bothWays, reach);
            }
        }

        /// <summary>A step of a sequence, in the measurement's notation.</summary>
        private sealed class Step
        {
            private Step(string text, string[] parts)
            {
                Text = text;
                Verb = parts[0].TrimEnd('!');
                Pinned = parts[0].EndsWith("!", StringComparison.Ordinal);
                Parts = parts;
            }

            public string Text { get; private set; }

            public string Verb { get; private set; }

            /// <summary>For a merge, whether it is an accepted residual: beyond both copies, pinned by what follows.</summary>
            public bool Pinned { get; private set; }

            public string[] Parts { get; private set; }

            public bool IsMerge
            {
                get { return MethodOf(Verb) != null; }
            }

            /// <summary>The copy acting, or for a merge the copy merged into; -1 for `*`.</summary>
            public int Copy
            {
                get { return Parts[1] == "*" ? -1 : int.Parse(Parts[1], CultureInfo.InvariantCulture); }
            }

            /// <summary>For a merge, the copy merged from.</summary>
            public int From
            {
                get { return int.Parse(Parts[2], CultureInfo.InvariantCulture); }
            }

            public static Step Parse(string text)
            {
                return new Step(text, text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            /// <summary>The method of a merge's verb, or null for a verb that is no merge.</summary>
            public static PwMergeMethod? MethodOf(string verb)
            {
                switch (verb)
                {
                    case "sync":
                        return PwMergeMethod.Synchronize;
                    case "newer":
                        return PwMergeMethod.OverwriteIfNewer;
                    case "over":
                        return PwMergeMethod.OverwriteExisting;
                    case "keep":
                        return PwMergeMethod.KeepExisting;
                    case "new":
                        return PwMergeMethod.CreateNewUuids;
                    default:
                        return null;
                }
            }

            /// <summary>This merge, the other way.</summary>
            public Step Reversed()
            {
                return Parse(Parts[0] + " " + Parts[2] + " " + Parts[1]);
            }

            public override string ToString()
            {
                return Text;
            }
        }

        /// <summary>What a merge left: the answers of the copy merged into, and where it gave beyond both copies.</summary>
        private sealed class Met
        {
            public Met(Dictionary<string, AclGrant> gives, List<string> beyond)
            {
                Gives = gives;
                Beyond = beyond;
            }

            public Dictionary<string, AclGrant> Gives { get; private set; }

            public List<string> Beyond { get; private set; }
        }

        /// <summary>
        /// The copies of a database a sequence is played over, with its tree of groups and its
        /// profiles, every rule written as an OK on a node's dialog writes it.
        /// </summary>
        private sealed class World : IDisposable
        {
            private readonly List<TemporaryDatabase> _copies = new List<TemporaryDatabase>();
            private readonly List<string> _order = new List<string>();
            private readonly Dictionary<string, PwUuid> _nodes = new Dictionary<string, PwUuid>(StringComparer.Ordinal);
            private readonly List<string> _profiles;
            private readonly bool _reach;

            /// <summary>When the last group was moved: each move a minute after it, so that the later move is the later.</summary>
            private DateTime _moved = DateTime.UtcNow;

            private int _written;
            private int _lost;
            private int _merged = -1;

            /// <summary>
            /// The first copy made, its authority created, its groups placed under their parents,
            /// each profile granted read at the root in a single OK, and saved; each other copy a
            /// copy of its file.
            /// </summary>
            public World(IEnumerable<string> profiles, IEnumerable<(string Node, string Parent)> tree, int copies, bool reach)
            {
                _profiles = profiles.ToList();
                _reach = reach;
                var first = TemporaryDatabase.New();
                _copies.Add(first);
                var cheapest = new AesKdf().GetDefaultParameters();
                cheapest.SetUInt64(AesKdf.ParamRounds, 1);
                first.Database.KdfParameters = cheapest;
                var created = DatabaseAuthority.Of(first.Database, NullGateLog.Instance).Create();
                Assert.That(created.Succeeded, Is.True, created.Message);

                _nodes["root"] = first.RootGroup.Uuid;
                _order.Add("root");
                foreach (var (node, parent) in tree)
                {
                    var group = new PwGroup(true, true, "Fictitious " + node, PwIcon.Folder);
                    Group(first, parent).AddGroup(group, true);
                    _nodes[node] = group.Uuid;
                    _order.Add(node);
                }

                Assert.That(
                    Press(first, "root", sheet => _profiles.All(profile => sheet.Set(profile, VerbMask.Of(Rules.Letters("R"))) == null)),
                    Is.True,
                    "the start: each profile granted read at the root");
                first.Save();
                for (var at = 1; at < copies; at++)
                {
                    _copies.Add(Opened(first));
                }
            }

            /// <summary>The world of a sequence of the write vectors: its profiles, and its tree by the names of its nodes.</summary>
            public static World Of(JObject sequence)
            {
                var profiles = ((JArray)sequence["profiles"]).Select(profile => (string)profile).ToList();
                var groups = ((JObject)sequence["tree"]["groups"]).Properties()
                    .ToDictionary(property => NodeName(sequence, property.Name), property => NodeName(sequence, (string)property.Value), StringComparer.Ordinal);
                var placed = new List<(string Node, string Parent)>();
                while (placed.Count < groups.Count)
                {
                    var next = groups.Where(pair => placed.All(done => done.Node != pair.Key) && (pair.Value == "root" || placed.Any(done => done.Node == pair.Value))).ToList();
                    Assert.That(next, Is.Not.Empty, "every group of the tree sits under the root");
                    placed.AddRange(next.Select(pair => (pair.Key, pair.Value)));
                }

                return new World(profiles, placed, 2, false);
            }

            public TemporaryDatabase Copy(int copy)
            {
                return _copies[copy];
            }

            /// <summary>These profiles asked about as well, at every node.</summary>
            public void Also(IEnumerable<string> profiles)
            {
                _profiles.AddRange(profiles.Where(profile => !_profiles.Contains(profile)));
            }

            /// <summary>
            /// A step of a sequence: a press written as an OK writes it, a move, a name, a version
            /// lost by hand, a merge held to both copies, or an answer held to what is expected.
            /// </summary>
            public void Run(Step step, string sequence, List<string> wrong)
            {
                var where = sequence + ", " + step;
                if (step.IsMerge)
                {
                    var met = Merge(step.Copy, step.From, Step.MethodOf(step.Verb).Value);
                    if (step.Pinned)
                    {
                        if (met.Beyond.Count == 0)
                        {
                            wrong.Add(where + ": the merge accepted as beyond both copies gives no more than they did");
                        }
                    }
                    else
                    {
                        wrong.AddRange(met.Beyond.Select(found => where + ": beyond both copies at " + found));
                    }

                    NextWrite(_copies[step.Copy], where, wrong);
                    return;
                }

                if (step.Verb == "gives")
                {
                    var copy = step.Copy < 0 ? _merged : step.Copy;
                    var key = step.Parts[2] + " " + step.Parts[3];
                    var wanted = step.Parts[4] == "-" ? string.Empty : step.Parts[4];
                    var given = Letters(Gives(_copies[copy])[key]);
                    if (given != wanted)
                    {
                        wrong.Add(where + ": copy " + copy + " gives " + Shown(given) + " where " + Shown(wanted) + " was expected");
                    }

                    return;
                }

                if (!Play(step))
                {
                    TestContext.Out.WriteLine(where + ": nothing written");
                }
            }

            /// <summary>A step that changes a copy, true where it wrote or moved anything.</summary>
            public bool Play(Step step)
            {
                var copy = _copies[step.Copy];
                var parts = step.Parts;
                switch (step.Verb)
                {
                    case "row":
                        return Press(copy, parts[2], sheet => parts[4] == "off" ? sheet.Remove(parts[3]) : sheet.Set(parts[3], Mask(parts[4])) == null);
                    case "rule":
                        var rows = parts[3].Contains("=")
                            ? parts[3].Split(',').Select(pair => (pair.Split('=')[0], pair.Split('=')[1]))
                            : _profiles.Select(profile => (profile, parts[3]));
                        return Press(copy, parts[2], sheet => rows.All(row => sheet.Set(row.Item1, Mask(row.Item2)) == null));
                    case "switch":
                        return Press(copy, parts[2], sheet => Switched(sheet, parts[3] == "deny"));
                    case "tickclear":
                        return Press(copy, parts[2], sheet => sheet.PressBox() && sheet.PressBox());
                    case "settle":
                        return Press(copy, parts[2], sheet => sheet.Settle(parts[3]));
                    case "drop":
                        var version = RuleAt(copy, parts[2]).Row(parts[3]).Versions.Single(held => Text(held) == parts[4]);
                        return Press(copy, parts[2], sheet => sheet.RemoveVersion(parts[3], version.Id));
                    case "move":
                        var group = Group(copy, parts[2]);
                        group.ParentGroup.Groups.Remove(group);
                        Group(copy, parts[3]).AddGroup(group, true);
                        _moved = _moved.AddMinutes(1);
                        group.LocationChanged = _moved;
                        return true;
                    case "name":
                        StoreSeeds.Name(Store(copy), parts[2]);
                        return true;
                    case "unname":
                        StoreSeeds.Unname(Store(copy), parts[2]);
                        return true;
                    case "lose":
                        Lose(copy, parts[2], parts[3]);
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(step), step.Verb, "a step this test does not play");
                }
            }

            /// <summary>
            /// Copy <paramref name="from"/> merged into copy <paramref name="into"/> by this method,
            /// the copy merged from opened afresh from its file, and the copy merged into read
            /// through its file before and after: what it gives, and where beyond both copies.
            /// </summary>
            public Met Merge(int into, int from, PwMergeMethod method)
            {
                _copies[from].Save();
                _copies[into].SaveCloseAndReopen();
                var first = Gives(_copies[into]);
                var second = Gives(_copies[from]);
                using (var source = Opened(_copies[from]))
                {
                    _copies[into].Database.MergeIn(source.Database, method, null);
                }

                _copies[into].SaveCloseAndReopen();
                _merged = into;
                var after = Gives(_copies[into]);
                return new Met(after, Beyond(first, second, after));
            }

            /// <summary>
            /// The copies 0 and 1 as they stand, met once by every single merge of
            /// <see cref="StoreRuleMergeTests.Cells(bool)"/>, each held to both copies and to the
            /// ends, a merge by new identifiers to the copy merged into as it was, and each
            /// followed by a write of the plugin's own.
            /// </summary>
            public void Cells(IReadOnlyDictionary<string, AclGrant> ends, string sequence, List<string> wrong)
            {
                _copies[0].Save();
                _copies[1].Save();
                foreach (var cell in StoreRuleMergeTests.Cells(true))
                {
                    using (var target = Opened(_copies[cell.Into]))
                    using (var source = Opened(_copies[cell.From]))
                    {
                        var named = sequence + ", " + cell;
                        var first = Gives(target);
                        var second = Gives(source);
                        var later = DateTime.UtcNow.AddDays(1);
                        if (cell.Times == Times.IntoLater)
                        {
                            Date(target, later);
                        }
                        else if (cell.Times == Times.FromLater)
                        {
                            Date(source, later);
                        }

                        target.Database.MergeIn(source.Database, cell.Method, null);
                        target.SaveCloseAndReopen();
                        var after = Gives(target);
                        if (State(target) != StoreState.Whole)
                        {
                            wrong.Add(named + ": the store is " + State(target));
                        }

                        Compare(wrong, named, cell.Method == PwMergeMethod.CreateNewUuids ? first : ends, after);
                        wrong.AddRange(Beyond(first, second, after).Select(found => named + ": beyond both copies at " + found));
                        NextWrite(target, named, wrong);
                    }
                }
            }

            /// <summary>
            /// A write of the plugin's own that touches no rule, a profile name added, made where
            /// the store is whole: every answer as it was before it.
            /// </summary>
            public void NextWrite(TemporaryDatabase copy, string where, List<string> wrong)
            {
                if (State(copy) != StoreState.Whole)
                {
                    return;
                }

                var before = Gives(copy);
                StoreSeeds.Name(Store(copy), "kept-" + (_written++).ToString(CultureInfo.InvariantCulture));
                Compare(wrong, where + ", after the plugin's next write", before, Gives(copy));
            }

            /// <summary>
            /// What a copy gives at every node of the tree for every profile, by the product's own
            /// walk over a single reading, keyed by the node's name and the profile; nothing
            /// anywhere where the store is not whole, since the gate then serves nothing; and where
            /// the world counts the list of names, nothing for a profile off it.
            /// </summary>
            public Dictionary<string, AclGrant> Gives(TemporaryDatabase copy)
            {
                var gives = new Dictionary<string, AclGrant>(StringComparer.Ordinal);
                using (var reading = Store(copy).Read())
                {
                    var whole = reading.State == StoreState.Whole;
                    var rules = whole ? StoredRules.Of(reading) : null;
                    var names = whole ? ProfileNames.Of(reading).Names : Array.Empty<string>();
                    foreach (var node in _order)
                    {
                        var at = whole ? KdbxNodes.Of(Group(copy, node), rules) : null;
                        foreach (var profile in _profiles)
                        {
                            var letters = !whole || (_reach && !names.Contains(profile)) ? AclGrant.Nothing : AclResolver.Arrives(at, profile);
                            gives[node + " " + profile] = letters;
                        }
                    }
                }

                return gives;
            }

            public StoreState State(TemporaryDatabase copy)
            {
                using (var reading = Store(copy).Read())
                {
                    return reading.State;
                }
            }

            /// <summary>The rules a copy holds at a node, from a reading let go at once.</summary>
            public NodeRule RuleAt(TemporaryDatabase copy, string node)
            {
                using (var reading = Store(copy).Read())
                {
                    return StoredRules.Of(reading).At(Id(Group(copy, node)));
                }
            }

            /// <summary>
            /// The version that lets pf through at N, in this copy, deleted, edited or duplicated
            /// by hand as KeePass does it, which no press of the tab does: the identifiers of the
            /// version and of every summary entry restating it.
            /// </summary>
            public List<string> ByHand(TemporaryDatabase copy, Hand hand)
            {
                var database = copy.Database;
                var objectText = StoreComposer.RowObject(Id(Group(copy, "N")), "pf");
                string version;
                List<string> heads;
                using (var reading = Store(copy).Read())
                {
                    version = reading.InForce.VersionsOf(objectText).Single().Id;
                    heads = reading.InForce.Heads
                        .Where(head => head.Form.Items.Any(item => item.Kind == StoreKinds.Version && item.Member(StoreKinds.Id) == version))
                        .Select(head => head.Hex)
                        .ToList();
                }

                Assert.That(heads, Is.Not.Empty, "a summary entry restates the version");
                var entry = Entry(database, version);
                switch (hand)
                {
                    case Hand.DeletedForGood:
                        Delete(database, entry, false);
                        break;
                    case Hand.DeletedWithARecord:
                        Delete(database, entry, true);
                        break;
                    case Hand.DeletedWithItsSummaryEntry:
                    case Hand.DeletedWithItsSummaryEntryAndRecords:
                        var recorded = hand == Hand.DeletedWithItsSummaryEntryAndRecords;
                        Delete(database, entry, recorded);
                        foreach (var head in heads)
                        {
                            Delete(database, Entry(database, head), recorded);
                        }

                        break;
                    case Hand.EditedWithinTheHistoryLimit:
                    case Hand.EditedPastTheHistoryLimit:
                        Assert.That(database.HistoryMaxItems, Is.EqualTo(10), "KeePass's own limit");
                        entry.CreateBackup(database);
                        entry.Strings.Set(StoreFormat.DataField, new ProtectedString(true, "{\"doc\":\"edited\"}"));
                        entry.Touch(true, false);
                        for (var edit = 2; hand == Hand.EditedPastTheHistoryLimit && edit <= 11; edit++)
                        {
                            entry.CreateBackup(database);
                            entry.Expires = !entry.Expires;
                            entry.Touch(true, false);
                        }

                        break;
                    case Hand.Duplicated:
                    case Hand.DuplicatedAndTheDuplicateRemoved:
                        var duplicate = entry.CloneDeep();
                        duplicate.SetUuid(new PwUuid(true), true);
                        entry.ParentGroup.AddEntry(duplicate, true);
                        if (hand == Hand.DuplicatedAndTheDuplicateRemoved)
                        {
                            Delete(database, duplicate, true);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(hand), hand, "a change this test does not make");
                }

                return new[] { version }.Concat(heads).ToList();
            }

            /// <summary>
            /// Two names whose tags meet under the copy's store key, found by a search, each a name
            /// the issuer could issue.
            /// </summary>
            public string[] Meeting(TemporaryDatabase copy)
            {
                using (var tags = Tags(copy))
                {
                    var seen = new Dictionary<int, string>();
                    for (var at = 0; at < 100000; at++)
                    {
                        var name = "q" + at.ToString("D5", CultureInfo.InvariantCulture);
                        var tag = Tag(tags, name);
                        string met;
                        if (seen.TryGetValue(tag, out met))
                        {
                            Assert.That(StoreComposer.ProfileTag(Key(copy), met), Is.EqualTo(StoreComposer.ProfileTag(Key(copy), name)), "the search's tag is the store's");
                            return new[] { met, name };
                        }

                        seen[tag] = name;
                    }
                }

                throw new InvalidOperationException("no two names whose tags meet among those searched");
            }

            /// <summary>A name whose tag is 0 under the copy's store key, found by a search.</summary>
            public string Zero(TemporaryDatabase copy)
            {
                using (var tags = Tags(copy))
                {
                    for (var at = 0; at < 4000000; at++)
                    {
                        var name = "z" + at.ToString("D7", CultureInfo.InvariantCulture);
                        if (Tag(tags, name) == 0)
                        {
                            Assert.That(StoreComposer.ProfileTag(Key(copy), name), Is.EqualTo(0), "the search's tag is the store's");
                            return name;
                        }
                    }
                }

                throw new InvalidOperationException("no name whose tag is 0 among those searched");
            }

            public void Dispose()
            {
                foreach (var copy in _copies)
                {
                    copy.Dispose();
                }
            }

            /// <summary>A press on the KPG tab of a node's dialog in a copy, and its OK, `NodeOks`.</summary>
            private bool Press(TemporaryDatabase copy, string node, Func<GrantSheet, bool> press)
            {
                return NodeOks.Pressed(copy.Database, Group(copy, node), press);
            }

            /// <summary>
            /// The box pressed as the operator does for what is wanted: a cleared box and a box in
            /// the mixed mark ticked once, and every other box cleared, which takes two presses
            /// from the mixed mark.
            /// </summary>
            private static bool Switched(GrantSheet sheet, bool refuses)
            {
                if (refuses)
                {
                    if (sheet.Box == GrantBox.Cleared || sheet.Box == GrantBox.Mixed)
                    {
                        sheet.PressBox();
                    }

                    return sheet.Box != GrantBox.Cleared;
                }

                for (var presses = 0; presses < 2 && sheet.Box != GrantBox.Cleared; presses++)
                {
                    sheet.PressBox();
                }

                return sheet.Box == GrantBox.Cleared;
            }

            /// <summary>
            /// A deletion record of a version of the profile's row at the node, as the file holds
            /// one of a version deleted, which nothing in force names.
            /// </summary>
            private void Lose(TemporaryDatabase copy, string node, string profile)
            {
                var key = Key(copy);
                try
                {
                    var objectName = StoreComposer.RowObject(Id(Group(copy, node)), profile);
                    var place = StoreComposer.Version(key, objectName, StoreComposer.Content(("doc", "row:R"))).Hex.Substring(0, 20);
                    var lost = StoreComposer.BytesOf(place + (_lost++).ToString("x12", CultureInfo.InvariantCulture));
                    copy.Database.DeletedObjects.Add(new PwDeletedObject(new PwUuid(lost), DateTime.UtcNow));
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            private static byte[] Key(TemporaryDatabase copy)
            {
                using (var reading = Store(copy).Read())
                {
                    return (byte[])reading.InForce.Key.Bytes.Clone();
                }
            }

            /// <summary>The keyed hash a profile's tag is taken from, `STORE.md` §609, under the copy's store key.</summary>
            private static HMACSHA256 Tags(TemporaryDatabase copy)
            {
                var key = Key(copy);
                try
                {
                    return new HMACSHA256(key);
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            private static int Tag(HMACSHA256 tags, string name)
            {
                var hash = tags.ComputeHash(Encoding.UTF8.GetBytes("name\n" + name));
                return (hash[0] << 8) | hash[1];
            }

            private static PwEntry Entry(PwDatabase database, string hex)
            {
                var entry = database.RootGroup.FindEntry(new PwUuid(StoreComposer.BytesOf(hex)), true);
                Assert.That(entry, Is.Not.Null, "an entry of the store under " + hex);
                return entry;
            }

            /// <summary>An entry deleted for good, with a deletion record where KeePass leaves one.</summary>
            private static void Delete(PwDatabase database, PwEntry entry, bool recorded)
            {
                entry.ParentGroup.Entries.Remove(entry);
                if (recorded)
                {
                    database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
                }
            }

            private static string Text(RowVersion version)
            {
                switch (version.Kind)
                {
                    case RowVersionKind.Grants:
                        return RuleText.Row(version.Letters);
                    case RowVersionKind.TakenOff:
                        return RuleText.TakenOff(version.Letters);
                    default:
                        return RuleText.TakenOffRefusing;
                }
            }

            /// <summary>The mask of the tab for a mask of the model's: `-` an empty rule, `w` update.</summary>
            private static string Mask(string model)
            {
                return VerbMask.Of(Rules.Letters(model == "-" ? string.Empty : model.ToUpperInvariant().Replace('W', 'U')));
            }

            private PwGroup Group(TemporaryDatabase copy, string node)
            {
                var group = copy.RootGroup.Uuid.Equals(_nodes[node]) ? copy.RootGroup : copy.RootGroup.FindGroup(_nodes[node], true);
                Assert.That(group, Is.Not.Null, "the group " + node);
                return group;
            }

            private static string Id(PwGroup group)
            {
                return DatabaseRules.Id(group.Uuid);
            }

            private static KdbxStore Store(TemporaryDatabase copy)
            {
                return new KdbxStore(copy.Database, GateVersion.Current, NullGateLog.Instance);
            }
        }
    }
}
