using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Spec;
using KeePassGate.Transport;
using KeePassGate.Vault;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// What every route that touches the database does, once the router has authorized it.
    /// </summary>
    /// <remarks>
    /// **A handler never asks whether it may.** By the time one of these runs, the router has taken
    /// the node it named, intersected the admission record with the rules, and kept the decision
    /// to write down. What is left is doing the thing and reporting what happened, which is why
    /// these are short.
    ///
    /// **A read runs inside the hold it was decided in.** The router holds KeePass's thread
    /// around the decision and the handler both, so what a handler here looks up is the database
    /// the router asked the rules about: the node is the node the router found, and a tag and
    /// the content it is served with are of one moment whichever is taken first. A read's
    /// handler therefore looks each thing up once and takes no hold of its own. What it can
    /// still find missing is a name inside the node, a field or an attachment the entry does
    /// not hold. Anything else missing is a hold that did not last, and the router answers that
    /// `503 busy` whatever was made of it here.
    ///
    /// **A read of an entry leaves the hold with the caller's view and makes its answer after
    /// it**, <see cref="Later"/>. The view keeps the objects the entry's values were held in
    /// when it was taken, so the values, their lengths, the tag and the answer made from them
    /// afterwards are still of that one moment, and none of that work, which grows with the
    /// entry, is done on KeePass's thread. Everything that reads the database stays inside:
    /// finding the entry, the rules, and the line for a value withheld. A name in the entry is
    /// looked for after the hold, in the view, since the view's strings are made of a copy of
    /// the entry's own list when they are first asked for.
    ///
    /// **A change is decided again where it is made**, and still not by its handler. The router
    /// answered before the change waited for its turn on KeePass's thread, so the handler of a
    /// change looks nothing up and decides nothing: it hands the vault the router's question and
    /// its own, `Asking`, and the vault asks them of the node it finds inside that turn.
    ///
    /// **The one exception is a listing**, and it is not an exception to the rule so much as a
    /// consequence of it. A list is a question about a collection, so the router authorizes the
    /// group it is anchored at and the handler leaves out what the rules do not allow. An entry
    /// omitted is not a refusal and does not get its own audit line: it is the normal case, and a
    /// line per invisible entry would bury the decisions worth reading.
    ///
    /// **Absent and refused answer the same 404** for the node an operation is about, which the
    /// router decides, and these use the same words for the things underneath it. A node that has
    /// gone by the time a change's turn comes is absent too, and a change the rules refuse inside
    /// its turn answers as that node does. A database that locked while the change waited is not
    /// absent, and `Answer` is where the two part.
    ///
    /// Inside an answer they are told apart on purpose. A route that is allowed and a value that is
    /// not gives `null` beside a `protected` member saying which could have been withheld, because
    /// the caller reached the node and only the value was refused.
    /// </remarks>
    public static class VaultHandlers
    {
        /// <summary>What a create answers with.</summary>
        public const int Created = 201;

        /// <summary>
        /// The member an entry's expiry travels under: answered by a read, and taken by a create
        /// and an update beside KeePass's five strings.
        /// </summary>
        private const string ExpiresMember = "expires";

        /// <summary>What a create's `expires` says to ask for KeePass's own expiry for new entries.</summary>
        private const string AutoExpiry = "auto";

        /// <summary>The query parameter that leaves expired entries out of a listing when it is `false`.</summary>
        private const string IncludeExpiredParameter = "includeExpired";

        /// <summary>
        /// The query parameter naming where a part of an entry's field listing starts: the
        /// position of its first field, counted from nothing.
        /// </summary>
        private const string FromParameter = "from";

        /// <summary>
        /// The member a part of a field listing names the position of the part after it under,
        /// present only while the entry holds fields past the part.
        /// </summary>
        private const string NextMember = "next";

        /// <summary>
        /// What a field costs a listing beyond its name, in characters: the members and the
        /// punctuation its item is written with.
        /// </summary>
        /// <remarks>
        /// Counted so that a part is held to what it writes and not to its names alone. An entry
        /// of very many short names would otherwise come to several times the ceiling in one
        /// answer, since the item around a name of three characters is ten times the name.
        /// </remarks>
        private const int FieldItemCharacters = 32;

        public static void ListGroups(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Listed(context, Groups(context, vault.Groups()));
        }

        public static void ListSubgroups(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            Listed(context, Groups(context, vault.Subgroups(uuid)));
        }

        public static void ReadGroup(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            var group = vault.Group(uuid);
            if (group == null)
            {
                Unreachable(context);
                return;
            }

            var tag = MayTag(context, group) ? vault.GroupTag(uuid) : null;
            if (PreconditionFails(context, tag))
            {
                return;
            }

            var json = new JsonWriter();
            Describe(json, group);
            context.Respond(200, json.Done(), Tagged(tag));
        }

        public static void CreateGroup(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var body = JsonBody.Read(context.Body);
            Answer(context, vault.CreateGroup(
                Captured(context, "uuid"), body.Text("Name"), body.Text("Notes"), Asking(context, false)));
        }

        public static void UpdateGroup(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            var body = JsonBody.Read(context.Body);

            Answer(context, vault.UpdateGroup(uuid, body.Text("Name"), body.Text("Notes"), Asking(context, true)));
        }

        public static void DeleteGroup(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Answer(context, vault.DeleteGroup(Captured(context, "uuid"), Asking(context, false)));
        }

        public static void ListEntries(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            bool withExpired;
            if (!IncludesExpired(context, out withExpired))
            {
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            Listed(context, Entries(context, vault.Entries(), withExpired));
        }

        public static void ListGroupEntries(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            bool withExpired;
            if (!IncludesExpired(context, out withExpired))
            {
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            var uuid = Captured(context, "uuid");
            Listed(context, Entries(context, vault.Entries(uuid), withExpired));
        }

        /// <summary>
        /// The most characters of names and values a read of an entry hands its caller: the
        /// whole entry as that caller's view holds it, a single field with its name, and what a
        /// part of a listing of the entry's fields is written with.
        /// </summary>
        /// <remarks>
        /// **A ceiling on what an answer costs, for the reason <see cref="MaxAttachmentBytes"/>
        /// is a ceiling.** An entry's answer is every string it serves, decrypted, written into a
        /// document, copied out of the writer and encoded again, which measured at about four
        /// times the strings in managed memory, all live at once: 94.5 MB for one read of two
        /// hundred fields of 64K characters. Nothing bounds how far writes can grow an entry,
        /// so without it a client holding `create` and `read` on an entry could make every read
        /// of it cost that, on all `GateOptions.MaxConnections` at once, inside the password
        /// manager's own process.
        ///
        /// **Measured over what the caller is handed, and held on reads alone.** What counts is
        /// the name of every string a route serves and the value of every string in the
        /// caller's sight; a withheld value counts nothing, and its length is not in the view,
        /// <see cref="VaultEntryView.Characters"/>. So two callers can be answered differently
        /// about one entry, each by what it would have been handed, and a refusal's count tells
        /// its caller nothing a read of the fields in its sight would not.
        ///
        /// **This reverses one number for reading and for writing**, the operator's ruling of
        /// 2026-11-10. A write that would take an entry over the ceiling was refused under the
        /// writer's lock, so that what the gate let a client write it also handed back whole,
        /// and the refusal named what the entry's strings came to, protected values included. A
        /// caller that may write and not read padded an entry up to the ceiling and learned
        /// from the count, or from where the refusals began, how long each value it was
        /// withheld was. No write is refused for an entry's size now. What that gives up: a
        /// client can write an entry past what a read hands back whole, such an entry is read
        /// a field at a time, and nothing bounds what a client holding `update` or `create` can
        /// add to an entry beyond what one request's body may carry.
        ///
        /// It is <see cref="VaultEntryView.MostCharacters"/>, and it is the attachments' half a
        /// megabyte, counted in characters.
        /// </remarks>
        public const int MaxEntryCharacters = VaultEntryView.MostCharacters;

        public static void ReadEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");

            // The view is taken inside the hold with nothing read or measured, and everything
            // made of its values is left for after the hold, `Later`.
            Reading asked;
            var view = View(context, vault, uuid, out asked);
            if (view == null)
            {
                Unreachable(context);
                return;
            }

            // The view decides what is served: a value is in the answer exactly when the view
            // holds it, and the view holds what any route shows this caller, a protected title
            // a listing shows it among them. Where the view withholds a value the refusal is
            // composed here, once, however many it withholds, from the walk the view's sight was
            // decided by, with nothing asked of the rules again, and written down where the
            // entry is served.
            var withholding = Withholding(context, view, asked);

            Later(context, () =>
            {
                // Measured before anything is read, so a view over the ceiling is refused with
                // no value read. What is measured is what this caller would be handed: every
                // name, and the values in its sight, never a withheld value. Measuring is not
                // always free: KeePass decrypts a protected value it holds as bytes to count its
                // characters, once and inside its own type, `VaultViewField`.
                if (view.Characters > MaxEntryCharacters)
                {
                    EntryTooLarge(context, view.Characters);
                    return;
                }

                // Measured again on what came back, because a view handing over more than it
                // measured must not be the way past the ceiling either. Before the condition,
                // for the reason `ReadAttachment` gives. A value out of the caller's sight is
                // not in the view, and comes back as null.
                var values = new List<string>(view.Fields.Count);
                var handed = 0;
                foreach (var field in view.Fields)
                {
                    var value = field.Value;
                    values.Add(value);
                    handed += field.Name.Length + (value == null ? 0 : value.Length);
                }

                if (handed > MaxEntryCharacters)
                {
                    EntryTooLarge(context, handed);
                    return;
                }

                // After the ceiling, so an entry too large to hand out is not digested either.
                // The view's own tag, so it is of exactly what this caller may read, and null
                // from a view whose sight does not read.
                var tag = view.Tag;
                if (PreconditionFails(context, tag))
                {
                    return;
                }

                var json = new JsonWriter();
                json.Text("uuid", view.Entry.Uuid);
                json.Text("group", view.Entry.Group);
                json.Text("created", Moments.Wire(view.Entry.Created));
                json.Text("modified", Moments.Wire(view.Entry.Modified));

                // `null` for an entry that does not expire, as `parent` is for the group with
                // nothing above it. `read` is all it needs, since when an entry expires is not a
                // value the database protects, and a listing leaves it out, because `list` gives
                // names.
                json.Text(
                    ExpiresMember, view.Entry.Expires.HasValue ? Moments.Wire(view.Entry.Expires.Value) : null);

                // Served, so the refusal composed inside the hold is written down, where the
                // view withholds a value.
                withholding(view.Fields)();

                json.Object("fields");
                for (var index = 0; index < view.Fields.Count; index++)
                {
                    // Withheld as `null` rather than left out, so a client can tell a value it
                    // may not have from a field that is not there. The `protected` member below
                    // names which could have been, which is what makes the difference readable
                    // rather than guessed at. A withheld field has no value behind it, so what
                    // is written for it is the null the view answered.
                    json.Text(view.Fields[index].Name, values[index]);
                }

                json.End();

                json.Array("protected");
                foreach (var name in view.Protected)
                {
                    json.Item(name);
                }

                json.End();

                context.Respond(200, json.Done(), Tagged(tag));
            });
        }

        public static void CreateEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            VaultExpiry expiry;
            if (!Expiring(context, true, out expiry))
            {
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            // Whether a protected value may be set is asked by the write, of the group it finds
            // and of what the database protects at that moment: `Asking` has the reason.
            Answer(context, vault.CreateEntry(
                Captured(context, "uuid"), Standard(context), Asking(context, false, Setting(AclVerb.Create)), expiry));
        }

        public static void UpdateEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            VaultExpiry expiry;
            if (!Expiring(context, false, out expiry))
            {
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            Answer(context, vault.UpdateEntry(
                Captured(context, "uuid"), Standard(context), Asking(context, true, Setting(AclVerb.Update)), expiry));
        }

        public static void DeleteEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Answer(context, vault.DeleteEntry(Captured(context, "uuid"), Asking(context, false)));
        }

        public static void ListFields(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            int from;
            if (!Starting(context, out from))
            {
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            // One read, which answers for the entry as well as for its strings: null is an entry
            // that is not there, and never an entry holding no strings. The listing itself is
            // names and flags, so nothing is read or measured for a caller that only lists, whose
            // view holds no value and has no tag. A caller holding `read` beside `list` is handed
            // the entry's tag, and taking the tag reads every value in that caller's sight, which
            // is why the tag and the answer it travels with are left for after the hold.
            var uuid = Captured(context, "uuid");
            var view = View(context, vault, uuid);
            if (view == null)
            {
                Unreachable(context);
                return;
            }

            // **Served in parts, and never refused for its size.** No write is refused for how
            // many strings an entry holds, so the names can come to more than an answer hands
            // out, and a listing refused for that left such an entry with no route naming its
            // strings: nothing to read a field at a time by, and nothing to strip to bring it
            // back under. So a part holds the fields from where the request starts for as long
            // as they fit what a read hands out, each counted by its name and what its item is
            // written with, and names where the part after it starts while there is any. Always
            // a field where any is left, however long its name, so that asking again moves on.
            //
            // Parted once the hold has ended, with the rest: walking the strings makes them,
            // and that grows with how many the entry holds.
            Later(context, () =>
            {
                var part = new List<VaultViewField>();
                var written = 0;
                var at = Math.Min(from, view.Fields.Count);
                for (; at < view.Fields.Count; at++)
                {
                    var cost = view.Fields[at].Name.Length + FieldItemCharacters;
                    if (part.Count > 0 && cost > MaxEntryCharacters - written)
                    {
                        break;
                    }

                    part.Add(view.Fields[at]);
                    written += Math.Min(cost, MaxEntryCharacters);
                }

                var next = at < view.Fields.Count ? at : (int?)null;

                var tag = view.Tag;
                if (PreconditionFails(context, tag))
                {
                    return;
                }

                var json = new JsonWriter();
                json.Array("fields");

                foreach (var field in part)
                {
                    json.Item();
                    json.Text("name", field.Name);
                    json.Flag("protected", field.IsProtected);
                    json.End();
                }

                json.End();

                if (next.HasValue)
                {
                    json.Number(NextMember, next.Value);
                }

                context.Respond(200, json.Done(), Tagged(tag));
            });
        }

        public static void ReadField(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            var name = Captured(context, "name");

            // The entry's view, and the string in it. The view carries the node the rules are
            // asked about, the string's name and its flag, measures a value in sight when asked
            // its size and reads a value only when asked for that value, so a request refused
            // before the tag is taken, for a name the entry does not hold or a field over the
            // ceiling, reads no value, and measures at most the one string. An answer that
            // is served carries the tag, which every caller reaching this route is handed, and
            // taking the tag reads every value in the caller's sight; a value out of that sight
            // is never read. Null is an entry that is not there or a name the entry does not
            // hold. The view is taken inside the hold, and the name is looked for in it, the
            // value measured and read, and the tag taken, after it, `Later`: finding a name
            // makes the view's strings, which grows with how many the entry holds.
            //
            // The saving on a refusal is the whole of the reason for reading late. It is not a
            // claim that a value a caller is refused is safer for not having been decrypted:
            // `CLAUDE.md` concedes local code execution as the user and forbids claiming a
            // control that only defends against it, which is the only thing such a property
            // would be.
            Reading asked;
            var view = View(context, vault, uuid, out asked);
            if (view == null)
            {
                Unreachable(context);
                return;
            }

            // The view decides: the value is served exactly when the view holds it, and a refusal
            // is composed here where it does not and written down where the field is served, as
            // `readEntry` does. The flag and the value are of one string of one view, so neither
            // is ever answered about a string the other was not read from.
            var withholding = Withholding(context, view, asked);

            Later(context, () =>
            {
                var field = view.Field(name);
                if (field == null)
                {
                    Unreachable(context);
                    return;
                }

                // Whether the value is withheld is decided first, by the view, and a withheld
                // value has no size here: the view holds nothing to measure it with, so it is
                // answered as the `null` every withheld value is, however long it is, and never
                // as too large. A value in sight is measured before it is read, and again on
                // what came back, as `readEntry` measures. Before the condition, for the reason
                // `ReadAttachment` gives.
                string value = null;
                if (!field.IsWithheld)
                {
                    value = field.Characters > MaxEntryCharacters ? null : field.Value;
                    var characters = value == null ? field.Characters : name.Length + value.Length;
                    if (characters > MaxEntryCharacters)
                    {
                        context.Respond(413, HttpResponse.ErrorBody(string.Format(
                            CultureInfo.InvariantCulture,
                            "this field comes to {0} characters with its name, and this gate hands out at most {1}",
                            characters,
                            MaxEntryCharacters)));
                        return;
                    }
                }

                // After the ceiling, as `readEntry` takes it.
                var tag = view.Tag;
                if (PreconditionFails(context, tag))
                {
                    return;
                }

                // Served, so the refusal composed inside the hold is written down, where the
                // view withholds this value.
                withholding(new[] { field })();

                var json = new JsonWriter();
                json.Text("name", name);
                json.Text("value", value);
                json.Flag("protected", field.IsProtected);

                context.Respond(200, json.Done(), Tagged(tag));
            });
        }

        public static void CreateField(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var body = JsonBody.Read(context.Body);
            var uuid = Captured(context, "uuid");
            var name = body == null ? null : body.Text("name");

            bool marked;
            if (!Marked(body == null ? null : body.Text("protected"), out marked))
            {
                // A marker this cannot read is refused rather than read as `false`. Everything
                // else in this plugin denies on a marker it does not recognize, and this one used
                // to be the exception: anything that was not the word `true` meant not protected,
                // so a caller that meant to store a secret stored it in clear and was told 201.
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            if (Unnameable(context, name, FieldRouteCharacters))
            {
                return;
            }

            // There is a body here: without one there is no name, and a missing name was refused
            // above. Only the caller's own marker goes down with it. Whether the database protects
            // the name is the vault's to decide, where the value is stored, so that what the rules
            // are asked about and what is stored are one answer: `IVault.CreateField`.
            Answer(context, vault.CreateField(
                uuid, name, body.Text("value"), marked, Asking(context, true, Setting(AclVerb.Create))));
        }

        public static void UpdateField(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var body = JsonBody.Read(context.Body);
            var written = body == null ? null : body.Text("value");

            // Both halves of protected, which the vault finds: what the database decides about
            // the name and the flag this string carries. While only the flag was asked, this
            // route wrote a password the entry route refused.
            Answer(context, vault.UpdateField(
                Captured(context, "uuid"),
                Captured(context, "name"),
                written,
                Asking(context, true, Setting(AclVerb.Update))));
        }

        public static void StripField(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            // A removal asks no switch, and this is not the removal being asked about: it is the
            // pair. Taking a protected string off an entry destroys the only mark that says the
            // name was a secret, so a caller that may then put a name back in its place writes an
            // unprotected value where the credential was, and every rule holding `read` without
            // `protected` is handed it from then on. That pair is an overwrite, which is what
            // `updateField` refuses one line up, and nothing stopped it here because each half is
            // allowed on its own.
            //
            // Asked of `create` rather than of `strip`, which is what keeps `...S....` exactly as
            // `DESIGN.md` §114 spells it out: a rule that may strip and may not create takes a
            // protected field off as before, because it has nowhere to put anything back. What is
            // refused is a rule that could create in its place and could not have created the
            // protected value it is taking away: any rule the client holds, each asked on its own.
            Answer(context, vault.DeleteField(
                Captured(context, "uuid"),
                Captured(context, "name"),
                Asking(context, true, (inside, found) =>
                    !found.Guarded || !CouldPutBackUnprotected(inside, found.Node))));
        }

        /// <summary>
        /// Whether one of the client's profiles could put a value back where a protected field was,
        /// and could not put back a protected value.
        /// </summary>
        /// <remarks>
        /// **Each profile asked on its own**, the operator's ruling R10. Asked as two questions of
        /// every profile at once, the guard let a strip through whenever any profile could create a
        /// protected value, though another could still put an unprotected value back. A profile
        /// that may create and reach protected values could write the protected value anyway, and
        /// a profile that may not create has nowhere to put anything back.
        ///
        /// **The refusal is written down for the profile that could put a value back**, through
        /// <see cref="Reaches(IGateContext, IAclNode, AclVerb, IReadOnlyList{string})"/> asked of
        /// that profile alone, so the strip the guard stops is on record as the attempt it is.
        /// Recorded by a walk pooling every profile, it was written down only when no profile at
        /// all could create a protected value here, and the case R10 made this guard refuse wrote
        /// nothing. Whether a profile could put anything back at all refuses nothing on its own,
        /// so that question is put to the resolver and not written down.
        /// </remarks>
        private static bool CouldPutBackUnprotected(IGateContext context, IAclNode node)
        {
            // `Any` stops at the first profile that could, so one refusal is written down.
            return context.Client.Profiles
                .Select(profile => new[] { profile })
                .Any(alone => AclResolver.Permits(node, alone, AclVerb.Create, AclReach.Fields)
                              && !Reaches(context, node, AclVerb.Create, alone));
        }

        public static void ListAttachments(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            // One read, as `ListFields` makes it. The view holds no attachments for a caller whose
            // rules open none, which the router refused before this ran, so finding none here is
            // answered as the entry not being there is. Names and sizes are in the view for a
            // caller that may list attachments and for a caller that may read them alike, and
            // are all this route serves.
            var uuid = Captured(context, "uuid");
            var view = View(context, vault, uuid);
            if (view == null || view.Sight.Attachments == VaultAttachmentSight.None)
            {
                Unreachable(context);
                return;
            }

            // The tag reads every value in the caller's sight, so it and the answer it travels
            // with are left for after the hold, as `ListFields` leaves them.
            Later(context, () =>
            {
                var tag = view.Tag;
                if (PreconditionFails(context, tag))
                {
                    return;
                }

                var json = new JsonWriter();
                json.Array("attachments");

                foreach (var attachment in view.Attachments)
                {
                    json.Item();
                    json.Text("name", attachment.Name);
                    json.Number("size", attachment.Size);
                    json.End();
                }

                json.End();
                context.Respond(200, json.Done(), Tagged(tag));
            });
        }

        /// <summary>
        /// The largest attachment this gate will hand out, which is also the largest it will take.
        /// </summary>
        /// <remarks>
        /// **A ceiling on what an answer costs, and the reason is where this runs.** Building one
        /// of these takes about ten times the attachment in managed memory, all live at once: the
        /// plaintext KeePass decrypts, the base64 string, the document that string is written
        /// into, the copy `JsonWriter.Done` takes of it, and the bytes the response is encoded to.
        /// The transport serves `GateOptions.MaxConnections` of those at a time, inside the
        /// password manager's own process, so with nothing here an admitted client could ask for
        /// as much memory as KeePass can get and take the user interface down with it.
        ///
        /// The number is the one a write already accepts, so that what the gate hands out it can
        /// also be given back.
        /// `EmbeddedDocumentTests.What_the_gate_hands_out_is_what_it_will_take_back` pins the two
        /// together, because a document and a constant that drift apart make an attachment that can
        /// be read and never restored. The document alone cannot say it exactly, since a base64
        /// length names two sizes at this one, so `Decoded` asks the constant of what it decoded as
        /// well. It is <see cref="VaultViewAttachment.MostServed"/>, so that the largest
        /// attachment a node's tag hashes is the largest a route serves.
        /// </remarks>
        public const int MaxAttachmentBytes = VaultViewAttachment.MostServed;

        public static void ReadAttachment(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            var name = Captured(context, "name");

            // The view hands over no bytes of an attachment over the ceiling, which comes back
            // measured and not decrypted: its size is kept beside its encrypted bytes. Null is an
            // entry that is not there, a name the entry does not hold, or attachments out of the
            // caller's sight. An attachment the caller may list and not read is in the view with
            // no bytes behind it, and is answered as the same absence: the router refused that
            // caller before this ran.
            var view = View(context, vault, uuid);
            if (view == null || view.Sight.Attachments != VaultAttachmentSight.Read)
            {
                Unreachable(context);
                return;
            }

            // The name is looked for, the bytes are read, and the tag taken, after the hold: the
            // view keeps the object each attachment was held in when it was taken, `Later`.
            Later(context, () =>
            {
                var attachment = view.Attachment(name);
                if (attachment == null)
                {
                    Unreachable(context);
                    return;
                }

                // Measured again on what came back, because a view handing over more than it
                // measured must not be the way past the ceiling either. Before the condition,
                // which RFC 9110 section 13.2.1 orders after every refusal that does not depend
                // on the tag: asked first, an attachment over the ceiling answered a stale tag
                // `412`, and a client never handed a tag was told `412` every time and never
                // that it was too large.
                var content = attachment.Content;
                try
                {
                    var size = content == null ? attachment.Size : content.Length;
                    if (content == null || size > MaxAttachmentBytes)
                    {
                        context.Respond(413, HttpResponse.ErrorBody(string.Format(
                            CultureInfo.InvariantCulture,
                            "this attachment is {0} bytes, and this gate hands out at most {1}",
                            size,
                            MaxAttachmentBytes)));
                        return;
                    }

                    // After the ceiling, as `readEntry` takes it.
                    var tag = view.Tag;
                    if (PreconditionFails(context, tag))
                    {
                        return;
                    }

                    var json = new JsonWriter();
                    json.Text("name", name);
                    json.Number("size", content.Length);
                    json.Text("content", Convert.ToBase64String(content));

                    context.Respond(200, json.Done(), Tagged(tag));
                }
                finally
                {
                    // The copy read here is overwritten whatever the answer, a refusal that
                    // served none of it included.
                    attachment.Discard();
                }
            });
        }

        public static void CreateAttachment(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var body = JsonBody.Read(context.Body);

            byte[] content;
            if (!Decoded(body.Text("content"), out content))
            {
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            var name = body.Text("name");
            if (Unnameable(context, name, AttachmentRouteCharacters))
            {
                return;
            }

            Answer(context, vault.CreateAttachment(
                Captured(context, "uuid"), name, content, Asking(context, true)));
        }

        public static void UpdateAttachment(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var body = JsonBody.Read(context.Body);

            byte[] content;
            if (!Decoded(body.Text("content"), out content))
            {
                context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
                return;
            }

            Answer(context, vault.UpdateAttachment(
                Captured(context, "uuid"),
                Captured(context, "name"),
                content,
                Asking(context, true)));
        }

        public static void StripAttachment(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Answer(context, vault.DeleteAttachment(
                Captured(context, "uuid"), Captured(context, "name"), Asking(context, true)));
        }

        /// <summary>
        /// Which node an operation about a group is about, for the router to ask the rules about.
        /// </summary>
        public static IAclNode GroupNode(IGateContext context)
        {
            var vault = ServedDatabase.Of(context);
            if (vault == null)
            {
                return null;
            }

            var group = vault.Group(Captured(context, "uuid"));
            return group == null ? null : group.Node;
        }

        /// <summary>Which node an operation about an entry is about.</summary>
        public static IAclNode EntryNode(IGateContext context)
        {
            var vault = ServedDatabase.Of(context);
            if (vault == null)
            {
                return null;
            }

            var entry = vault.Entry(Captured(context, "uuid"));
            return entry == null ? null : entry.Node;
        }

        /// <summary>
        /// The root group, for the two routes that are anchored there rather than at a named node.
        /// </summary>
        public static IAclNode RootNode(IGateContext context)
        {
            var vault = ServedDatabase.Of(context);
            if (vault == null)
            {
                return null;
            }

            var root = vault.Group(vault.RootAlias);
            return root == null ? null : root.Node;
        }

        private static IVault Vault(IGateContext context)
        {
            var vault = ServedDatabase.Of(context);
            if (vault == null)
            {
                // A connection with no database behind it. The router refuses a locked one long
                // before this, so arriving here would be a defect, and what it answers is what
                // every other absence answers.
                Unreachable(context);
                return null;
            }

            return vault;
        }

        private static void Unreachable(IGateContext context)
        {
            context.Respond(404, HttpResponse.ErrorBody(SpecRouter.UnreachableError));
        }

        private static string Captured(IGateContext context, string name)
        {
            object found;
            if (!context.Items.TryGetValue(SpecRouter.ParametersItem, out found))
            {
                return null;
            }

            var captured = found as IReadOnlyDictionary<string, string>;
            if (captured == null)
            {
                return null;
            }

            string value;
            return captured.TryGetValue(name, out value) ? value : null;
        }

        /// <summary>The standard fields a body named, and only those it named.</summary>
        /// <remarks>
        /// `expires` is the one member of those bodies that is not a string the entry holds, and
        /// <see cref="Expiring"/> reads it instead: written here, it would be a custom field.
        /// </remarks>
        private static IReadOnlyDictionary<string, string> Standard(IGateContext context)
        {
            var body = JsonBody.Read(context.Body);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            if (body == null)
            {
                return fields;
            }

            foreach (var name in body.Names)
            {
                var value = body.Text(name);
                if (value != null && !string.Equals(name, ExpiresMember, StringComparison.Ordinal))
                {
                    fields[name] = value;
                }
            }

            return fields;
        }

        /// <summary>
        /// What a create or an update body asks of the entry's expiry, and false when it asks in a
        /// way this gate does not read.
        /// </summary>
        /// <remarks>
        /// **One member, and four things it can say.** Absent asks nothing: a create then makes an
        /// entry that does not expire, and an update leaves the expiry as it is. Empty asks for no
        /// expiry, which takes one off. A moment asks for that moment, written the one way
        /// `Moments.WireFormat` writes every moment this gate answers with, so the `expires` a read
        /// answers goes back as it came. And `auto` asks a create for whatever KeePass's options
        /// give a new entry, and is refused on an update, whose entry has the expiry somebody
        /// already decided on. Anything else is refused rather than guessed at, as an unreadable
        /// `protected` marker is.
        ///
        /// Checked here because the document cannot say it: the member is a string with a length,
        /// and `BodyCheck` implements neither `format` nor `enum`, so it refuses at startup a
        /// schema declaring either.
        /// </remarks>
        private static bool Expiring(IGateContext context, bool creating, out VaultExpiry expiry)
        {
            expiry = null;

            var body = JsonBody.Read(context.Body);
            var asked = body == null ? null : body.Text(ExpiresMember);

            if (asked == null)
            {
                return true;
            }

            if (asked.Length == 0)
            {
                expiry = VaultExpiry.Never;
                return true;
            }

            if (string.Equals(asked, AutoExpiry, StringComparison.Ordinal))
            {
                expiry = creating ? VaultExpiry.Auto : null;
                return creating;
            }

            DateTimeOffset when;
            if (!DateTimeOffset.TryParseExact(
                    asked,
                    Moments.WireFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out when))
            {
                return false;
            }

            expiry = VaultExpiry.At(when);
            return true;
        }

        /// <summary>
        /// Whether a listing keeps the entries that have expired, and false when the query says so
        /// in a way this gate does not read.
        /// </summary>
        /// <remarks>
        /// **Kept unless the query says `false`**, because a listing is of what is there and KeePass
        /// lists an expired entry too, struck through. The two words are compared exactly and
        /// anything else is refused rather than read as either, for the reason
        /// <see cref="Marked"/> gives: a caller that meant to leave expired entries out and spelled
        /// it some other way would be handed them and told nothing. The document declares the name
        /// and cannot enumerate the words, because `BodyCheck` holds a query parameter to a bare
        /// string.
        /// </remarks>
        private static bool IncludesExpired(IGateContext context, out bool included)
        {
            included = true;

            string flag;
            if (context.Query == null || !context.Query.TryGetValue(IncludeExpiredParameter, out flag))
            {
                return true;
            }

            if (string.Equals(flag, "true", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(flag, "false", StringComparison.Ordinal))
            {
                included = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the position a field listing is asked from is a position this gate reads,
        /// and which position that is: the entry's first field where the query names none.
        /// </summary>
        /// <remarks>
        /// Digits alone, and few enough to be a position: anything else is refused and never
        /// read as the start, since a caller that asked for a later part and spelled it badly
        /// would be handed the first part again and take it for the part it asked for. The
        /// document declares the name and cannot hold it to digits, for the reason
        /// <see cref="IncludesExpired"/> gives. A position past the entry's last field is no
        /// refusal: it is a part holding nothing, which is what is there.
        /// </remarks>
        private static bool Starting(IGateContext context, out int from)
        {
            from = 0;

            string asked;
            if (context.Query == null || !context.Query.TryGetValue(FromParameter, out asked))
            {
                return true;
            }

            const int MostDigits = 9;
            var counts = asked.Length > 0 && asked.Length <= MostDigits;
            if (!counts || !asked.All(digit => digit >= '0' && digit <= '9'))
            {
                return false;
            }

            from = int.Parse(asked, NumberStyles.None, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Whether a `protected` marker is one this gate reads, and which of the two it is.
        /// </summary>
        /// <remarks>
        /// Absent is a marker too, and means the database decides: `IVault.Protects` answers for
        /// the names it protects by setting, and nothing else is a secret until somebody says so.
        /// The two words are compared exactly. The document says which two and bounds their
        /// length, and it cannot enumerate them, because `BodyCheck` implements no `enum` and
        /// refuses at startup a schema declaring one, so the last word on the pair is here.
        /// `openapi.yaml` §505 says why a boolean is spelled out rather than sent as one.
        /// </remarks>
        private static bool Marked(string marker, out bool guarded)
        {
            guarded = string.Equals(marker, "true", StringComparison.Ordinal);

            return marker == null || guarded || string.Equals(marker, "false", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a name a body is about to make is one its own routes could name again, and the
        /// refusal when it is not.
        /// </summary>
        /// <remarks>
        /// **A stricter question than the body's**, which lets a line break through because a
        /// value has to carry one. A name does not, and one holding a control character, or
        /// decoding into a climb, is refused by every route that would address it, so it was
        /// created with `201` and could never be read, changed or stripped by this API again.
        ///
        /// **A carriage return is the one that costs more than that.** KeePass writes a name into
        /// the file as XML, and the XML reader turns a carriage return into a line feed on the
        /// way back in, so `api\rkey` and `api\nkey` are two names in memory and one after the
        /// next unlock, with the later element winning. Every check a create makes, whether the
        /// name is taken and whether it is protected, runs on the spelling in memory: a client
        /// holding only `create` could write beside an existing protected field, be answered
        /// `201`, and find its own value there in clear once the file was reopened.
        ///
        /// **And a name holding a format character**, for the reasons <see cref="Formats"/> gives.
        ///
        /// **And a name too long to travel in a path.** A name goes into the routes naming it
        /// percent-encoded, three characters for each byte of its UTF-8 beyond ASCII, so nine for
        /// a Chinese character and twelve for a character beyond the first plane, and the
        /// transport refuses a target over
        /// <see cref="HttpRequestReader.MaxTargetCharacters"/> before a route is chosen. So a name
        /// of a few hundred such characters, inside the 512 the body allows, was made with `201`
        /// and every route naming it was answered `414`. Measured against the route the name
        /// would be named in, <see cref="FieldRouteCharacters"/> or
        /// <see cref="AttachmentRouteCharacters"/> before it.
        /// </remarks>
        /// <param name="before">The characters of the route naming it that come before the name.</param>
        private static bool Unnameable(IGateContext context, string name, int before)
        {
            if (PathTemplate.IsCapture(name) && !Formats(name)
                && before + InPath(name) <= HttpRequestReader.MaxTargetCharacters)
            {
                return false;
            }

            context.Respond(400, HttpResponse.ErrorBody(BodyCheck.Refusal));
            return true;
        }

        /// <summary>
        /// The characters before the name in the routes naming a field: `/entries/`, the entry's
        /// identifier and `/fields/`.
        /// </summary>
        internal static readonly int FieldRouteCharacters = "/entries/".Length + 32 + "/fields/".Length;

        /// <summary>The same, for the routes naming an attachment.</summary>
        internal static readonly int AttachmentRouteCharacters = "/entries/".Length + 32 + "/attachments/".Length;

        /// <summary>
        /// How many characters a name takes in a path, percent-encoded as the reference client
        /// writes it: every byte of its UTF-8 but an unreserved character as three.
        /// </summary>
        /// <remarks>
        /// The unreserved characters of RFC 3986 section 2.3 are the ASCII letters and digits and
        /// `-`, `.`, `_` and `~`. A client leaving more of a name unencoded takes fewer, so a
        /// name measured here travels however it is written.
        /// </remarks>
        private static int InPath(string name)
        {
            var length = 0;
            foreach (var octet in System.Text.Encoding.UTF8.GetBytes(name))
            {
                var c = (char)octet;
                length += octet < 0x80 && (Ascii.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_' || c == '~')
                    ? 1
                    : 3;
            }

            return length;
        }

        /// <summary>
        /// Whether a name holds a Unicode format character, a character of general category Cf.
        /// </summary>
        /// <remarks>
        /// **Refused in a name, because KeePass acts on what a name says.** It lists an attachment
        /// under its name and opens one it cannot show itself with whatever Windows starts for its
        /// real suffix, a program included, on the operator's double click. So a name ending in the
        /// right to left override and then `fdp.exe` is listed as ending in `.pdf` and started as a
        /// program, and a client allowed to create attachments could offer the operator a program
        /// dressed as a document. A field name is refused the same way, since the same dialog
        /// shows those. Every bidirectional control Unicode has, the Arabic letter mark, the two
        /// directional marks, the five embeddings and overrides and the four isolates, is a format
        /// character.
        ///
        /// **The rest of the category is drawn as nothing at all**: the zero-width space, the
        /// joiners, the soft hyphen, the byte order mark, the tag characters. A name holding any of
        /// them is drawn exactly as the name without it, so a client allowed to create fields could
        /// put a second `Password` on an entry, unprotected and holding what the client chose,
        /// beside the real `Password`, for the operator to type a credential into or copy from. The
        /// database cannot be left to refuse it: it tells names apart by the culture's collation,
        /// which weighs some of these and not others, so `Pass` and `word` around a zero-width
        /// space were a second name to KeePass and the same name to the eye.
        ///
        /// Asked of each character as Unicode classes it, a pair of surrogates as the character it
        /// encodes, so that a format character beyond the first plane is refused as well. A value
        /// is not a name and keeps them, because right to left text is ordinary in a note. A name
        /// that looks like another in any other way, a letter of another script or a space at its
        /// end, is not asked about here.
        /// </remarks>
        private static bool Formats(string name)
        {
            for (var at = 0; at < name.Length; at++)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(name, at) == UnicodeCategory.Format)
                {
                    return true;
                }

                if (char.IsHighSurrogate(name[at]) && at + 1 < name.Length && char.IsLowSurrogate(name[at + 1]))
                {
                    at++;
                }
            }

            return false;
        }

        private static bool Decoded(string base64, out byte[] content)
        {
            content = null;

            if (base64 == null)
            {
                return false;
            }

            try
            {
                content = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                // The schema says a string and this says which string. A body that declares base64
                // and is not is malformed in the same way a missing member is.
                return false;
            }

            // The document's `maxLength` counts characters, and base64 spells the ceiling and one
            // byte more in the same number of them: 524288 bytes pad to 699052, 524289 need no
            // padding and are 699052 too. So the ceiling is asked again of the bytes, or one size
            // is stored that every read then refuses. Refused the way the next size up already
            // is, by the document, so a client meets one answer for too large however it got there.
            if (content.Length > MaxAttachmentBytes)
            {
                content = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Answers a listing, or that the node it was taken from is not there.
        /// </summary>
        /// <remarks>
        /// **A listing is judged against the tree it was taken from.** The sweep and the rules
        /// asked of everything in it run inside the hold the router decided the request in, which
        /// is one turn on KeePass's thread rather than one an entry. What it writes is a string,
        /// kept until that hold is let go, so the answer goes out with the thread free.
        ///
        /// Null is a listing of something that is not there, which `IVault` never answers as an
        /// empty list, and is never an empty list here either.
        /// </remarks>
        private static void Listed(IGateContext context, string listing)
        {
            if (listing == null)
            {
                Unreachable(context);
                return;
            }

            context.Respond(200, listing);
        }

        /// <summary>The listing of these groups the client may see, or null when there are none to list.</summary>
        private static string Groups(IGateContext context, IReadOnlyList<VaultGroup> groups)
        {
            if (groups == null)
            {
                return null;
            }

            var json = new JsonWriter();
            json.Array("groups");
            var shown = 0;
            var read = new AclDocuments();

            foreach (var group in groups)
            {
                if (!Visible(context, group.Node, read))
                {
                    continue;
                }

                json.Item();
                Summarize(json, group);
                json.End();
                shown++;
            }

            json.End();
            json.Number("count", shown);

            return json.Done();
        }

        /// <summary>The listing of these entries the client may see, or null when there are none to list.</summary>
        private static string Entries(IGateContext context, IReadOnlyList<VaultEntry> entries, bool withExpired)
        {
            if (entries == null)
            {
                return null;
            }

            var json = new JsonWriter();
            json.Array("entries");
            var shown = 0;

            // One rules document is read once per listing, however many entries lie under it,
            // rather than once for every entry and every question asked of it.
            var read = new AclDocuments();

            // Taken once, so that one listing judges every entry against the same moment.
            var now = DateTimeOffset.UtcNow;

            foreach (var entry in entries)
            {
                // Left out as the rules leave an entry out: not a refusal, and not recorded.
                if (!withExpired && Expired(entry, now))
                {
                    continue;
                }

                if (!Visible(context, entry.Node, read))
                {
                    continue;
                }

                // The question a read of the entry asks about the title and the URL, asked in the
                // same place, `ShowsListedProtected`, so a listing and a read show a caller the
                // same of them. Without it a client holding `list` alone would be told a title
                // that `read` without `protected` refuses, which would make the smaller grant the
                // more revealing one. Asked only of an entry with a protected title or address,
                // since nothing else in its line of the listing depends on the answer, and
                // asking it of every entry was a second walk to the root for each entry.
                var withheld = (entry.TitleProtected || entry.UrlProtected)
                               && !ShowsListedProtected(context, entry.Node, read);

                json.Item();
                json.Text("uuid", entry.Uuid);
                json.Text("group", entry.Group);
                json.Text("title", withheld && entry.TitleProtected ? null : entry.Title);
                json.Text("url", withheld && entry.UrlProtected ? null : entry.Url);
                json.Text("created", Moments.Wire(entry.Created));
                json.Text("modified", Moments.Wire(entry.Modified));
                json.End();
                shown++;
            }

            json.End();
            json.Number("count", shown);

            return json.Done();
        }

        /// <summary>
        /// Whether one thing in a listing may be seen.
        /// </summary>
        /// <remarks>
        /// Asked of the resolver directly rather than through the check the router uses, because
        /// this one is not recorded. A rule lives on a node and a list crosses many of them, so an
        /// audit line per invisible entry would be a line for the normal case, and the decision
        /// worth reading, the one about the collection itself, is already written down.
        ///
        /// The profiles are the client's own, out of its admission record, so the intersection is
        /// the same one every other question makes.
        ///
        /// **Every profile the client holds, item by item**, the operator's ruling R10 as amended on
        /// 2026-11-03. The router let the listing be taken because some profile may list from where
        /// it is taken, and an item is shown through whichever profile may list it, which can be
        /// another. A listing is a read: an item appears only to a client holding a profile entitled
        /// to it, and what the profiles pool is the way to it. A change is allowed by one profile.
        /// </remarks>
        private static bool Visible(IGateContext context, IAclNode node, AclDocuments read)
        {
            return AclResolver.Permits(node, context.Client.Profiles, AclVerb.List, AclReach.Fields, read);
        }

        /// <summary>Whether an entry has expired, judged the way KeePass judges one.</summary>
        /// <remarks>
        /// It expires, and its moment is now or earlier. That one comparison is what strikes an
        /// entry through in KeePass's list, leaves it out of a search asked to exclude expired
        /// entries, and keeps auto-type from it. On the gate's clock, which is where the answer is
        /// made.
        /// </remarks>
        private static bool Expired(VaultEntry entry, DateTimeOffset now)
        {
            return entry.Expires.HasValue && entry.Expires.Value <= now;
        }

        /// <summary>
        /// The whole of an entry as this caller may read it, which a read's answer and its tag are
        /// both made from, or null when the entry is not there.
        /// </summary>
        /// <remarks>
        /// **One view for a request**, so the string a read answers with, the flag beside it, the
        /// node the rules are asked about and the tag are all of one entry at one moment, and the
        /// tag covers exactly what the view holds.
        ///
        /// **The same view whichever route asks**, since the sight it is taken with is about the
        /// caller and the entry and nothing else. So every route answering about an entry hands
        /// its caller the same tag, and a change is asked against that tag and answers with it.
        ///
        /// **What the caller may read is asked of the resolver without a line**, of the entry the
        /// vault found. A view leaving a value out is the decision, and the routes that serve
        /// values write its refusal down from the walk that made it,
        /// <see cref="Withholding"/>.
        /// </remarks>
        private static VaultEntryView View(IGateContext context, IVault vault, string uuid)
        {
            return vault.View(uuid, found => Sight(context, found));
        }

        /// <summary>
        /// The same view, with how its sight was decided, for a route that serves values and
        /// writes down the value it withholds.
        /// </summary>
        /// <param name="asked">
        /// The sight the view was taken with and the walk that decided its reach into protected
        /// values, or null when the entry is not there.
        /// </param>
        private static VaultEntryView View(IGateContext context, IVault vault, string uuid, out Reading asked)
        {
            Reading decided = null;
            var view = vault.View(uuid, found =>
            {
                decided = Read(context, found);
                return decided.Sight;
            });

            asked = decided;
            return view;
        }

        /// <summary>
        /// How far a caller may read into what was found, with the walk that decided whether
        /// protected values are in sight.
        /// </summary>
        /// <remarks>
        /// The walk is kept so that a value a view withholds is written down as the decision that
        /// withheld it, and not by asking the rules a second time: <see cref="Withholding"/>.
        /// </remarks>
        private sealed class Reading
        {
            public Reading(VaultSight sight, AclDecision protectedValues)
            {
                Sight = sight;
                ProtectedValues = protectedValues;
            }

            public VaultSight Sight { get; private set; }

            /// <summary>
            /// What the rules answered about `read` reaching protected values, or null where
            /// that was not asked: a group, and a caller with no client behind it.
            /// </summary>
            public AclDecision ProtectedValues { get; private set; }
        }

        /// <summary>
        /// How far into what a read or a change found this caller may read, through any profile
        /// it holds.
        /// </summary>
        /// <remarks>
        /// **About the caller and the node, never about the route**, so a caller has the same
        /// sight of an entry, and with it the same tag, on every route and in every change.
        ///
        /// An entry is read by a rule holding `read`. Its protected values are in sight for a
        /// rule holding `read` and `protected` together, asked whatever strings the entry
        /// holds. The protected strings a listing of entries shows, its title and its URL, are
        /// in sight wherever a listing shows them, which is `protected` beside `list` as well as
        /// beside `read`, asked through the question the listing itself asks,
        /// `ShowsListedProtected`, so that
        /// what a listing shows a caller and what a read serves it cannot come apart. Its
        /// attachments are read by a rule holding `read` and `attachments` together,
        /// and listed, names and sizes, by a rule holding `list` and `attachments` together,
        /// which is what the routing table opens the listing of attachments to. Each switch
        /// counts in the rule it stands in, and profiles are combined with or, as a read
        /// combines them.
        ///
        /// A group has no switches to read by, so whether the caller holds `read` on it is the
        /// whole of its sight, and a group's tag is handed out on that.
        ///
        /// **Asked of the resolver without a line**, each question once, each rules document
        /// read once for all of them. A caller with no client behind it sees the least, which
        /// has no tag.
        /// </remarks>
        private static VaultSight Sight(IGateContext context, VaultFound found)
        {
            return Read(context, found).Sight;
        }

        /// <summary>
        /// The same answer, with the walk that decided whether protected values are in sight.
        /// </summary>
        private static Reading Read(IGateContext context, VaultFound found)
        {
            if (found == null || context.Client == null)
            {
                return new Reading(VaultSight.Least, null);
            }

            var profiles = context.Client.Profiles;
            var node = found.Node;
            var read = new AclDocuments();
            var reads = AclResolver.Permits(node, profiles, AclVerb.Read, AclReach.Fields, read);

            if (found.Entry == null)
            {
                return new Reading(new VaultSight(reads, false, VaultAttachmentSight.None), null);
            }

            var attachments = VaultAttachmentSight.None;
            if (reads && AclResolver.Permits(node, profiles, AclVerb.Read, AclReach.Attachments, read))
            {
                attachments = VaultAttachmentSight.Read;
            }
            else if (AclResolver.Permits(node, profiles, AclVerb.List, AclReach.Attachments, read))
            {
                attachments = VaultAttachmentSight.Listed;
            }

            var protectedValues = AclResolver.Decide(node, profiles, AclVerb.Read, AclReach.Protected, read);
            var sight = new VaultSight(
                reads,
                reads && protectedValues.Permitted,
                attachments,
                reads && ShowsListedProtected(context, node, read, protectedValues.Permitted));

            return new Reading(sight, protectedValues);
        }

        /// <summary>
        /// Answers that what a read of an entry would hand its caller comes to more than a read
        /// hands out, with that caller's own count.
        /// </summary>
        private static void EntryTooLarge(IGateContext context, int characters)
        {
            context.Respond(413, HttpResponse.ErrorBody(string.Format(
                CultureInfo.InvariantCulture,
                "a read of this entry would hand over {0} characters of names and values, and this gate hands out at most {1}",
                characters,
                MaxEntryCharacters)));
        }

        /// <summary>
        /// The questions a change asks about its caller from inside its own turn, once it has
        /// found its node.
        /// </summary>
        /// <remarks>
        /// **Nothing about a change is decided here, on the connection's thread.** These used to
        /// look the node up, ask whether a protected value may be set and whether the caller may
        /// be handed the tag, and hand the vault the answers; the write then waited for its turn
        /// on KeePass's thread and acted on them, whatever the operator had done to the node, its
        /// rules or its strings in that wait. A string marked protected in the wait was
        /// overwritten for a rule without `protected`. So the vault is handed the questions and
        /// asks them of the node it finds: <see cref="VaultAsking"/> has the argument.
        ///
        /// **The router's question first, and every other question of the request it answers
        /// with.** The <see cref="DecidedInside"/> the router left reads the client's record
        /// again and asks the rules about the node the change found. What a handler asks beside
        /// it, and how far the caller may read, are asked of that request and not of the
        /// request that queued, so every answer is about one client and one node at one moment.
        ///
        /// **The caller's sight is the same question a read asks**, <see cref="Sight"/>, so the
        /// tag a change is asked against and the tag it answers with are the tag every read
        /// route hands that caller, and whether an update that would change nothing is skipped,
        /// <see cref="VaultEntryView.Reads"/>, is read off the view taken with the same answer.
        ///
        /// **A request the router left no question for is refused**, which is the direction a
        /// defect has to fail in.
        /// </remarks>
        /// <param name="answers">Whether the answer carries the tag the change leaves.</param>
        /// <param name="also">
        /// What this change asks beside the router's question, of the request as it is decided
        /// inside the turn, or null when the router's question is all there is.
        /// </param>
        private static VaultAsking Asking(
            IGateContext context,
            bool answers,
            Func<IGateContext, VaultFound, bool> also = null)
        {
            object left;
            var again = context.Items.TryGetValue(SpecRouter.InsideItem, out left) ? left as DecidedInside : null;

            // The request as it was decided inside the turn, kept for the questions that follow
            // the first in that same turn.
            IGateContext inside = null;

            return new VaultAsking(
                found =>
                {
                    inside = again == null ? null : again(found.Node);
                    return inside != null && (also == null || also(inside, found));
                },
                found => inside == null ? null : Sight(inside, found),
                Precondition(context),
                answers);
        }

        /// <summary>
        /// What a change that sets a string asks beside the router's question: whether this
        /// caller may set a protected value, where the change would set one.
        /// </summary>
        /// <remarks>
        /// **Whether it would is the vault's to find**, <see cref="VaultFound.Guarded"/>, and both
        /// halves of it: a field is protected because of what it is called, which the database
        /// decides, or because somebody marked that string on that entry, and the two disagree
        /// whenever an operator turns a protection on, since KeePass does not go back and
        /// re-flag what is already there. Overwriting either is setting a secret.
        ///
        /// **Refused as the rules refusing the change is**, the same `404`, for the reason
        /// `Answer` gives.
        /// </remarks>
        private static Func<IGateContext, VaultFound, bool> Setting(AclVerb verb)
        {
            return (inside, found) => !found.Guarded || Reaches(inside, found.Node, verb);
        }

        /// <summary>
        /// Runs the part of a read's answer that is made of an entry's values once the hold the
        /// read was decided in has ended, or where it stands for a request nothing holds.
        /// </summary>
        /// <remarks>
        /// <see cref="ServedAfter"/> has the reason and what may be left: the view, and what
        /// the hold already decided, and nothing that reads the database. Every read of an
        /// entry leaves its measuring, its reading, its tag, its condition and its answer here,
        /// and asks its view for its strings and attachments only here, so no such read does
        /// work on KeePass's thread that grows with how long the entry's values are or with how
        /// many it holds, beyond the copy of its lists the view is taken with.
        /// </remarks>
        private static void Later(IGateContext context, Action serve)
        {
            object left;
            var after = context.Items.TryGetValue(SpecRouter.AfterItem, out left) ? left as ServedAfter : null;
            if (after == null)
            {
                serve();
                return;
            }

            after(serve);
        }

        /// <summary>
        /// Composes the line for a value withheld from an answer, and hands back what answers,
        /// for the strings a read goes on to serve, with what writes the line down where the
        /// view withholds the value of any of them.
        /// </summary>
        /// <remarks>
        /// **The view decides what a read serves, and this records what it left out.** A value
        /// is in an answer exactly when the caller's view holds it, and the line written is the
        /// walk the view's sight was decided by, `read` reaching protected values, kept from
        /// that asking and handed to the router's <see cref="ProtectedWithheld"/>. So the
        /// refusal on record is the decision that withheld the value, and no second walk is
        /// asked that could answer otherwise: asked again, a refusal went unwritten whenever
        /// the two disagreed.
        ///
        /// **A value withheld from an answer is always written down.** A view that withholds
        /// with no refusal behind it, which a sight nobody decided comes to, is recorded as
        /// that, and a request the router left no recorder for writes nothing and still serves
        /// nothing withheld.
        ///
        /// **Composed here, inside the hold, and written by what comes back**, which the read
        /// runs where it serves the answer, after the hold: the line names the node and asks
        /// the rules what a removed profile would have allowed, which reads the database, and
        /// a read refused for its size or by its condition serves nothing and writes no line,
        /// <see cref="ProtectedWithheld"/>. **Composed before it is known that anything is
        /// withheld**, for any sight that could withhold a value: which strings a view
        /// withholds is known once its strings are made, and those are made after the hold,
        /// `VaultEntryView`. What comes back is never null, and neither is what that answers.
        ///
        /// Not written where the view serves every value, so a caller shown a protected title
        /// through the listing's reach, reading an entry with nothing else protected on it,
        /// writes no second line.
        ///
        /// **The recorder is told when every value withheld is a string the listings show**, a
        /// title or a URL. Those are in sight on `list` with `protected` as well, so a profile
        /// taken off the database's list can be the reason on that reach too, and the line
        /// names it: `AccessCheck.Withheld`.
        /// </remarks>
        private static Func<IEnumerable<VaultViewField>, Action> Withholding(
            IGateContext context, VaultEntryView view, Reading asked)
        {
            // A sight that reads and reaches protected values holds every value, so there is
            // nothing a view taken with it could withhold, and nothing to compose.
            if (view.Sight.Reads && view.Sight.Protected)
            {
                return served => Nothing;
            }

            object found;
            var record = context.Items.TryGetValue(SpecRouter.WithheldItem, out found) ? found as ProtectedWithheld : null;
            if (record == null)
            {
                return served => Nothing;
            }

            var decided = asked == null ? null : asked.ProtectedValues;
            var refusal = decided != null && !decided.Permitted
                ? decided
                : AclDecisions.Refused("a value was withheld by a view with no refusal behind it");

            // Both forms of the line, since which of them is written depends on which strings
            // turn out withheld, and that is known once the view's strings are made, after the
            // hold. The line itself reads the database, so both are composed here.
            var listed = record(view.Entry.Node, AclVerb.Read, refusal, true) ?? Nothing;
            var other = record(view.Entry.Node, AclVerb.Read, refusal, false) ?? Nothing;

            return served =>
            {
                var withheld = served.Where(field => field.IsWithheld).ToList();
                if (withheld.Count == 0)
                {
                    return Nothing;
                }

                return withheld.All(field => field.IsListed) ? listed : other;
            };
        }

        /// <summary>What writes nothing down, for a read that withheld nothing.</summary>
        private static void Nothing()
        {
        }

        /// <summary>
        /// Whether this caller may have the values the database protects on this node, a refusal
        /// written down by the walk that decides it.
        /// </summary>
        /// <remarks>
        /// **The one question a handler of a change asks and records rather than the router**,
        /// and the asymmetry is worth naming. An attachment is a route of its own, so the router
        /// asks, and records, before dispatch. A protected value arrives inside the body of a
        /// change the router has allowed, so the only moment the question exists is while that
        /// change is being made. So each handler that takes a field value from a body asks it,
        /// which is a check every such handler has to remember: the arrangement every other
        /// question is kept out of, and the arrangement this question needs, since only the
        /// handler holds the values, `DESIGN.md` §137. A change added later that takes field
        /// values has to ask it as well.
        ///
        /// **A read asks nothing here.** How far a caller may read is asked silently by
        /// <see cref="Sight"/>, for every view, the vault leaves out of the view what that
        /// answer does not open, and a route serving values serves what the view holds and
        /// writes a withheld value down from the walk the sight was decided by,
        /// <see cref="Withholding"/>. A read route added later takes its values from the view
        /// and records through that, and never asks this.
        ///
        /// **Asked through the router's <see cref="ProtectedReach"/>, which decides and records in
        /// one walk.** The router keeps a line for the verb it granted, and that
        /// line is true; a value refused is a second decision on the same request, and without
        /// its line the audit held that a client had set what the gate refused. That
        /// attempt is exactly what the `protected` switch exists to stop, so its line is exactly
        /// the line an operator opens the file to find. Decided here and written down by a second
        /// walk, as it was, the two could disagree, and a refusal went unwritten whenever they did.
        ///
        /// **Only where a protected value would be set.** Each caller asks once it holds a
        /// protected value to write, so a change naming nothing protected writes no second
        /// line. A handler the router did not hand the question to refuses, which is the
        /// direction a defect has to fail in.
        /// </remarks>
        private static bool Reaches(IGateContext context, IAclNode node, AclVerb verb)
        {
            return Reaches(context, node, verb, null);
        }

        /// <summary>The same question, asked of some of the caller's profiles and not all of them.</summary>
        private static bool Reaches(
            IGateContext context, IAclNode node, AclVerb verb, IReadOnlyList<string> profiles)
        {
            object found;
            var reach = context.Items.TryGetValue(SpecRouter.ReachItem, out found) ? found as ProtectedReach : null;
            return reach != null && reach(node, verb, profiles);
        }

        /// <summary>
        /// Whether this caller is shown a protected title or URL of this entry: `protected`
        /// beside `list` or beside `read` in one rule it holds there. Asked per item and
        /// deliberately not recorded.
        /// </summary>
        /// <remarks>
        /// **The one place the condition is, for a listing and for a read alike.** A listing
        /// asks it of each entry whose title or URL is protected, and <see cref="Read"/> asks it
        /// for the view every read of an entry is served from, so what a listing shows a caller
        /// of the title and the URL is what that caller's view holds of them, in both
        /// directions: a title a listing shows is served by a read, and a title a read serves is
        /// shown by a listing. Which route a caller may ask at all stays the router's decision,
        /// on that route's own verb.
        ///
        /// **Each question once.** The view has already asked `read` with `protected` to decide
        /// its sight, so it hands that answer in and `list` with `protected` is asked only where
        /// that answer did not settle it.
        ///
        /// Not recorded, for the reason `Visible` gives just above: a listing asks this about
        /// every entry it walks, so recording it would be a line per item for the ordinary case.
        /// Asked of every profile the client holds, as `Visible` is, so a protected title is
        /// shown through any profile holding `protected` beside `list` or `read` on the entry,
        /// which can be another profile than the profile the listing was taken through.
        /// </remarks>
        private static bool ShowsListedProtected(IGateContext context, IAclNode node, AclDocuments read)
        {
            var readsProtected = AclResolver.Permits(
                node, context.Client.Profiles, AclVerb.Read, AclReach.Protected, read);

            return ShowsListedProtected(context, node, read, readsProtected);
        }

        /// <summary>
        /// The same condition, for a caller whose `read` with `protected` on this entry has
        /// been answered already.
        /// </summary>
        private static bool ShowsListedProtected(
            IGateContext context, IAclNode node, AclDocuments read, bool readsProtected)
        {
            return readsProtected
                   || AclResolver.Permits(node, context.Client.Profiles, AclVerb.List, AclReach.Protected, read);
        }

        /// <summary>What `read` says about a group: what a listing says, and its notes.</summary>
        private static void Describe(JsonWriter json, VaultGroup group)
        {
            Summarize(json, group);
            json.Text("notes", group.Notes);
        }

        /// <summary>What `list` says about a group, which is where it is and what it is called.</summary>
        /// <remarks>
        /// Never the notes. They are free text an operator wrote, which is what `read` is for, and a
        /// listing that carried them would hand a client holding `list` alone what the same client
        /// is refused by reading the group.
        /// </remarks>
        private static void Summarize(JsonWriter json, VaultGroup group)
        {
            json.Text("uuid", group.Uuid);
            json.Text("name", group.Name);
            json.Text("parent", group.Parent);
            json.Text("created", Moments.Wire(group.Created));
            json.Text("modified", Moments.Wire(group.Modified));
        }

        /// <summary>What the request's `If-Match` asks, or null when it carried none.</summary>
        /// <remarks>
        /// Left by the router, and only for an operation the document declares `If-Match` on, so a
        /// handler cannot ask a condition of a route the document says takes none.
        /// </remarks>
        private static VaultPrecondition Precondition(IGateContext context)
        {
            object found;
            return context.Items.TryGetValue(SpecRouter.PreconditionItem, out found)
                ? found as VaultPrecondition
                : null;
        }

        /// <summary>Whether this caller may be handed a group's tag and have its `If-Match` asked.</summary>
        /// <remarks>
        /// **A caller holding `read` on the group**, `API.md` §410. A tag is a digest of what its
        /// holder is served, and a tag covering a part its holder may not read lets that holder
        /// test guesses at the part: it writes a guess where it may write, and the tag that
        /// comes back, or the `412` that does not, tells it whether the node now holds what it
        /// held when the tag was taken. A group's tag covers what `read` serves and a group has
        /// no switches to scope it by, so `read` is the whole question, the same question a
        /// change to the group asks through <see cref="Sight"/>. An entry's tag needs no such
        /// question here: its view has no tag for a sight that does not read.
        /// </remarks>
        private static bool MayTag(IGateContext context, VaultGroup group)
        {
            return group != null && Sight(context, new VaultFound(group)).Reads;
        }

        /// <summary>
        /// Whether a read is refused because the node no longer holds what `If-Match` said,
        /// answered here when it is.
        /// </summary>
        /// <remarks>
        /// **A read on a route declaring `If-Match` asks too**, and a client naming a tag on a read
        /// is asking to be told that what it holds is current. Asked of the tag the read was taken
        /// with, which is the tag it would be answered with, and null, which a caller that may not
        /// be handed one has, matches nothing. The routes that declare no `If-Match` never read
        /// the header, `API.md` §410.
        ///
        /// **Logged as a change's `412` is**, at `Warn` and with the reason the vault gives a
        /// change's, a caller handed no tag named as such: a client refused on its reads over and
        /// over showed nowhere, while the same refusals on its writes were each a line.
        /// </remarks>
        private static bool PreconditionFails(IGateContext context, string tag)
        {
            var asked = Precondition(context);
            if (tag == null && asked != null)
            {
                // A caller handed no tag, whose condition nothing meets, named as
                // `VaultAsking.Tagging` names a change's.
                asked = VaultPrecondition.Withheld();
            }

            if (!VaultPrecondition.Fails(asked, tag))
            {
                return false;
            }

            // Logged as a change's is, at the level and with the reason the vault gives a
            // change's, since a client refused over and over on its reads is as worth finding.
            ServedDatabase.LogOf(context).Warn(
                "a read was refused, its precondition failing: " + asked.Refusal);
            PreconditionFailed(context);
            return true;
        }

        /// <summary>What a request whose condition does not hold is answered with.</summary>
        private static void PreconditionFailed(IGateContext context)
        {
            context.Respond(412, HttpResponse.ErrorBody(PreconditionFailedError));
        }

        /// <summary>The word a `412` carries.</summary>
        public const string PreconditionFailedError = "precondition failed";

        /// <summary>A tag as `ETag` writes it: strong, and in quotes, as RFC 9110 section 8.8.3 spells one.</summary>
        /// <remarks>
        /// Strong because the tag is a digest of everything its caller may read of the node
        /// rather than a guess at whether two versions are close enough, and because `If-Match`
        /// compares strongly, so a weak tag could never be handed back and matched. A caller has
        /// the same tag on every route the node answers for, since the tag is of what that
        /// caller may read of the node and not of what a route serves: an entry's fields and
        /// attachments are the entry's.
        /// </remarks>
        private static string Quoted(string tag)
        {
            return "\"" + tag + "\"";
        }

        /// <summary>The `ETag` a read about a node carries, or null when it carries none.</summary>
        private static WebHeaderCollection Tagged(string tag)
        {
            if (tag == null)
            {
                return null;
            }

            return new WebHeaderCollection { { "ETag", Quoted(tag) } };
        }

        /// <summary>
        /// What a change answers with.
        /// </summary>
        /// <remarks>
        /// A refusal that says nothing is there is the same 404 as a node the rules refuse, so a
        /// client cannot tell a name it may not use from one that is not taken. **A change the
        /// rules refuse inside its own turn is that 404 too**, a protected value its caller may
        /// not set among them: a client that may reach nothing must not be able to map the
        /// database by noticing which refusals differ. It costs an honest caller a clearer
        /// message, and the message it would have wanted is on the tab of whoever wrote the rule.
        ///
        /// **A change whose caller the database stopped admitting while it waited is answered
        /// before this**, inside the change's turn and as that caller's next request is, which
        /// <see cref="DecidedInside"/> describes. The refusal still arrives here, and its `404`
        /// is a second answer on a request whose first stands.
        ///
        /// **The tag is the vault's answer and nothing here asks about it again.** Whether this
        /// caller may be handed the tag the change left was asked inside the change's turn, of
        /// the node as the change left it, <see cref="VaultAsking.Tagging"/>.
        ///
        /// **The tag goes in the body, as `etag`, and not in an `ETag` header.** RFC 9110 section
        /// 9.3.4 makes an `ETag` on the answer to a `PUT` mean the representation the request sent
        /// was stored as it was, and on a `POST` or a `DELETE` it names no representation the
        /// request had, while every tag here is the tag of the caller's view of the whole node
        /// the change left. Written
        /// as `ETag` would write it, quotes and all, so that it goes back as `If-Match` unchanged.
        /// </remarks>
        private static void Answer(IGateContext context, VaultWrite write)
        {
            if (write == null)
            {
                // A broken `IVault` rather than a route outcome: nothing here returns null from a
                // write, and a client cannot provoke it. It shares the status a write route
                // declares and not its meaning, which is why the body says `internal error`
                // rather than `not saved`. Answering rather than throwing keeps the connection
                // thread's own catch-all for what it is for, which is an exception nobody
                // predicted.
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
                return;
            }

            if (write.Ok)
            {
                // The node's tag as the change left it, where there is one: a client that writes
                // again can name it without reading in between.
                var tag = write.Tag;
                var json = new JsonWriter();

                if (write.Uuid == null)
                {
                    json.Flag("ok", true);
                    if (tag != null)
                    {
                        json.Text("etag", Quoted(tag));
                    }

                    context.Respond(200, json.Done());
                    return;
                }

                json.Text("uuid", write.Uuid);
                if (tag != null)
                {
                    json.Text("etag", Quoted(tag));
                }

                context.Respond(Created, json.Done());
                return;
            }

            if (write.IsPreconditionFailed)
            {
                // Not retried and not a conflict: the node is there and holds something other
                // than what the request was written against, and only reading it again says what.
                PreconditionFailed(context);
                return;
            }

            if (write.IsBusy)
            {
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return;
            }

            if (write.IsUnsaved)
            {
                // The change was made and is not on the disk. Answered as this plugin's own
                // failure, because it is one: every other refusal here is the rules or the caller,
                // and reporting it as `404` told a client its request had nowhere to go while the
                // database held the change. A retry then made a second one.
                context.Respond(500, HttpResponse.ErrorBody("not saved"));
                return;
            }

            if (write.IsUnverified)
            {
                // The change was made and saved, and the file KeePass hashed back afterwards is
                // not the one it wrote. Told apart from `not saved` because the two say opposite
                // things about what is left behind: that one has been taken back out, and this
                // one has not, because nothing knows whether the file holds it. A client that
                // treated them alike would retry over a write that may already be there.
                context.Respond(500, HttpResponse.ErrorBody("not verified"));
                return;
            }

            if (write.IsFailed)
            {
                // Nothing was attempted: the database closed under the request, KeePass answered
                // the attempt with an error, or the vault was handed a name the routing table
                // never lets through. Answered as this plugin's own failure for the reason the one
                // above is, and it was the same defect: a caller was told its request had nowhere
                // to go, which is what an absent node and a refused one are told, so an operator
                // was sent to look at rules and identifiers over a database that had simply gone.
                //
                // **A database that locked or closed while the request waited is `503 locked`**,
                // as the router answers a read that finds it locked. The handler reads nothing
                // before the write does, so the write is where a lock landing behind the router
                // is found, and a client told `500` there would report a defect where the
                // request only has to be sent again once the database is open.
                if (!ServedDatabase.TryAnswerLocked(context))
                {
                    context.Respond(500, HttpResponse.ErrorBody("internal error"));
                }

                return;
            }

            if (write.IsConflict)
            {
                context.Respond(409, HttpResponse.ErrorBody("conflict"));
                return;
            }

            Unreachable(context);
        }
    }
}
