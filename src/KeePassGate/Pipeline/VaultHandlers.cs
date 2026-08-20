using System;
using System.Globalization;
using System.Collections.Generic;
using KeePassGate.Acl;
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
    /// the node it named, intersected the admission record with the rules, and written the decision
    /// down. What is left is doing the thing and saying what happened, which is why these are short.
    ///
    /// **The one exception is a listing**, and it is not an exception to the rule so much as a
    /// consequence of it. A list is a question about a collection, so the router authorizes the
    /// group it is anchored at and the handler leaves out what the rules do not allow. An entry
    /// omitted is not a refusal and does not get its own audit line: it is the normal case, and a
    /// line per invisible entry would bury the decisions worth reading.
    ///
    /// **Absent and refused answer the same 404** for the node an operation is about, which the
    /// router decides, and these use the same words for the things underneath it.
    ///
    /// Inside an answer they are told apart on purpose. A route that is allowed and a value that is
    /// not gives `null` beside a `protected` member saying which could have been withheld, because
    /// the caller reached the node and only the value was refused.
    /// </remarks>
    public static class VaultHandlers
    {
        /// <summary>What a create answers with.</summary>
        public const int Created = 201;

        public static void ListGroups(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Groups(context, vault.Groups());
        }

        public static void ListSubgroups(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Groups(context, vault.Subgroups(Captured(context, "uuid")));
        }

        public static void ReadGroup(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var group = vault.Group(Captured(context, "uuid"));
            if (group == null)
            {
                Unreachable(context);
                return;
            }

            var json = new JsonWriter();
            Describe(json, group);
            context.Respond(200, json.Done());
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
                Captured(context, "uuid"), body.Text("Name"), body.Text("Notes")));
        }

        public static void UpdateGroup(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var body = JsonBody.Read(context.Body);
            Answer(context, vault.UpdateGroup(
                Captured(context, "uuid"), body.Text("Name"), body.Text("Notes")));
        }

        public static void DeleteGroup(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Answer(context, vault.DeleteGroup(Captured(context, "uuid")));
        }

        public static void ListEntries(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Entries(context, vault.Entries());
        }

        public static void ListGroupEntries(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Entries(context, vault.Entries(Captured(context, "uuid")));
        }

        public static void ReadEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var entry = vault.Read(Captured(context, "uuid"));
            if (entry == null)
            {
                Unreachable(context);
                return;
            }

            var json = new JsonWriter();
            json.Text("uuid", entry.Entry.Uuid);
            json.Text("group", entry.Entry.Group);
            json.Text("created", Moments.Wire(entry.Entry.Created));
            json.Text("modified", Moments.Wire(entry.Entry.Modified));

            var reaches = ReachesProtected(context, entry.Entry.Node, AclVerb.Read);
            var blanked = false;

            json.Object("fields");
            foreach (var field in entry.Fields)
            {
                // Withheld as `null` rather than left out, so a client can tell a value it may not
                // have from a field that is not there. The `protected` member below says which
                // were which, which is what makes the difference readable rather than guessed at.
                var withheld = !reaches && IsProtected(entry, field.Key);
                blanked = blanked || withheld;

                json.Text(field.Key, withheld ? null : field.Value);
            }

            json.End();

            if (blanked)
            {
                // Once, however many fields were blanked: the decision was one decision.
                Recorded(context, entry.Entry.Node, AclVerb.Read);
            }

            json.Array("protected");
            foreach (var name in entry.Protected)
            {
                json.Item(name);
            }

            json.End();

            context.Respond(200, json.Done());
        }

        public static void CreateEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var fields = Standard(context);
            var group = vault.Group(Captured(context, "uuid"));

            if (group != null && Guarded(vault, fields.Keys) && !ReachesProtected(context, group.Node, AclVerb.Create))
            {
                Withheld(context, group.Node, AclVerb.Create);
                return;
            }

            if (Compiled(fields))
            {
                Resolved(context);
                return;
            }

            Answer(context, vault.CreateEntry(Captured(context, "uuid"), fields));
        }

        public static void UpdateEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            var fields = Standard(context);
            var entry = vault.Entry(uuid);

            if (entry != null
                && (Guarded(vault, fields.Keys) || Guarded(vault, uuid, fields.Keys))
                && !ReachesProtected(context, entry.Node, AclVerb.Update))
            {
                Withheld(context, entry.Node, AclVerb.Update);
                return;
            }

            if (Compiled(fields))
            {
                Resolved(context);
                return;
            }

            Answer(context, vault.UpdateEntry(uuid, fields));
        }

        public static void DeleteEntry(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Answer(context, vault.DeleteEntry(Captured(context, "uuid")));
        }

        public static void ListFields(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            if (vault.Entry(uuid) == null)
            {
                Unreachable(context);
                return;
            }

            var json = new JsonWriter();
            json.Array("fields");

            foreach (var field in vault.Fields(uuid))
            {
                json.Item();
                json.Text("name", field.Name);
                json.Flag("protected", field.IsProtected);
                json.End();
            }

            json.End();
            context.Respond(200, json.Done());
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
            var value = vault.Field(uuid, name);

            if (value == null)
            {
                Unreachable(context);
                return;
            }

            // True until something says otherwise. The entry can go between the value being read
            // and this, taken by an operator's delete or by another connection's, and a loop that
            // finds nothing then left this false and sent a protected value to a rule that may not
            // have it. Fail closed here and the worst case is a value withheld from somebody
            // entitled to it, which is a retry rather than a disclosure.
            var guarded = true;
            foreach (var field in vault.Fields(uuid))
            {
                if (string.Equals(field.Name, name, StringComparison.Ordinal))
                {
                    guarded = field.IsProtected;
                }
            }

            var json = new JsonWriter();
            json.Text("name", name);

            // The entry, not everything in it. All this needs is the node the rules are asked
            // about, and `Read` builds a whole `VaultEntryDetail` to get at it: every string the
            // entry holds, decrypted and copied into a dictionary, to answer a question about one
            // field this handler has already read.
            //
            // The waste is the whole of the reason. It is not a claim that a value a caller is
            // refused is safer for not having been decrypted: this route reads the value before it
            // decides to withhold it, `readEntry` decrypts every string before blanking the
            // protected ones, and the two listings decrypt a protected title and address for every
            // entry, including the ones they then leave out. `CLAUDE.md` concedes local code
            // execution as the user and forbids claiming a control that only defends against it,
            // which is the only thing such a property would be.
            var entry = vault.Entry(uuid);
            if (entry == null)
            {
                // Gone since the value was read. There is no node left to ask the rules about, so
                // there is no answer to give: this lookup used to sit inside the conjunction below,
                // where a missing entry made `withheld` false and sent the value out.
                Unreachable(context);
                return;
            }

            var withheld = guarded && !ReachesProtected(context, entry.Node, AclVerb.Read);
            if (withheld)
            {
                Recorded(context, entry.Node, AclVerb.Read);
            }

            json.Text("value", withheld ? null : value);
            json.Flag("protected", guarded);

            context.Respond(200, json.Done());
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
            var guarded = body != null
                && string.Equals(body.Text("protected"), "true", StringComparison.OrdinalIgnoreCase);

            var entry = vault.Entry(uuid);
            if (entry != null && (guarded || vault.Protects(name))
                && !ReachesProtected(context, entry.Node, AclVerb.Create))
            {
                Withheld(context, entry.Node, AclVerb.Create);
                return;
            }

            var made = body == null ? null : body.Text("value");
            if (Compiled(name, made))
            {
                Resolved(context);
                return;
            }

            // What the check above asked about, not half of it. Asking on `guarded || Protects`
            // and then storing only `guarded` meant a caller could re-create a standard field
            // unprotected after stripping it, and every later client with `read` and no
            // `protected` was handed it in clear until KeePass next normalized the file.
            Answer(context, vault.CreateField(uuid, name, made, guarded || vault.Protects(name)));
        }

        public static void UpdateField(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var body = JsonBody.Read(context.Body);
            var uuid = Captured(context, "uuid");
            var name = Captured(context, "name");
            var entry = vault.Entry(uuid);

            // Both halves. `Guarded` answers the flag this string carries; `Protects` answers what
            // the database says about the name. They disagree whenever an operator turns a
            // protection on, because KeePass does not go back and re-flag what is already there.
            // While they disagreed, this route wrote a password the entry route refused.
            if (entry != null && (vault.Protects(name) || Guarded(vault, uuid, new[] { name }))
                && !ReachesProtected(context, entry.Node, AclVerb.Update))
            {
                Withheld(context, entry.Node, AclVerb.Update);
                return;
            }

            var written = body == null ? null : body.Text("value");
            if (Compiled(name, written))
            {
                Resolved(context);
                return;
            }

            Answer(context, vault.UpdateField(uuid, name, written));
        }

        public static void StripField(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Answer(context, vault.DeleteField(Captured(context, "uuid"), Captured(context, "name")));
        }

        public static void ListAttachments(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            if (vault.Entry(uuid) == null)
            {
                Unreachable(context);
                return;
            }

            var json = new JsonWriter();
            json.Array("attachments");

            foreach (var attachment in vault.Attachments(uuid))
            {
                json.Item();
                json.Text("name", attachment.Name);
                json.Number("size", attachment.Size);
                json.End();
            }

            json.End();
            context.Respond(200, json.Done());
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
        /// well.
        /// </remarks>
        public const int MaxAttachmentBytes = 512 * 1024;

        public static void ReadAttachment(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            var uuid = Captured(context, "uuid");
            var name = Captured(context, "name");

            // The ceiling goes with the question, so an attachment over it comes back measured
            // and not decrypted: its size is kept beside its encrypted bytes. Asking a listing
            // first and reading second was two reads, and an attachment replaced between them,
            // by the operator's own OK or a synchronize, was decrypted whole at whatever size it
            // had become before anything refused it.
            var attachment = vault.Attachment(uuid, name, MaxAttachmentBytes);
            if (attachment == null)
            {
                Unreachable(context);
                return;
            }

            // Measured again on what came back, because a vault handing over more than it was
            // asked for must not be the way past the ceiling either.
            var content = attachment.Content;
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

            var json = new JsonWriter();
            json.Text("name", name);
            json.Number("size", content.Length);
            json.Text("content", Convert.ToBase64String(content));

            context.Respond(200, json.Done());
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

            Answer(context, vault.CreateAttachment(Captured(context, "uuid"), body.Text("name"), content));
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
                Captured(context, "uuid"), Captured(context, "name"), content));
        }

        public static void StripAttachment(IGateContext context)
        {
            var vault = Vault(context);
            if (vault == null)
            {
                return;
            }

            Answer(context, vault.DeleteAttachment(Captured(context, "uuid"), Captured(context, "name")));
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
                if (value != null)
                {
                    fields[name] = value;
                }
            }

            return fields;
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

        private static void Groups(IGateContext context, IReadOnlyList<VaultGroup> groups)
        {
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

            context.Respond(200, json.Done());
        }

        private static void Entries(IGateContext context, IReadOnlyList<VaultEntry> entries)
        {
            var json = new JsonWriter();
            json.Array("entries");
            var shown = 0;

            // One rules document is read once per listing, however many entries lie under it,
            // rather than once for every entry and every question asked of it.
            var read = new AclDocuments();

            foreach (var entry in entries)
            {
                if (!Visible(context, entry.Node, read))
                {
                    continue;
                }

                // The same question the read route asks, for the same reason. Without it a client
                // holding `list` alone would be told a title that `read` without `protected`
                // refuses, which would make the smaller grant the more revealing one. Asked only
                // of an entry with a protected title or address, since nothing else it says
                // depends on the answer, and asking it of every entry was a second walk to the
                // root for each one.
                var withheld = (entry.TitleProtected || entry.UrlProtected)
                               && !ReachesProtectedInListing(context, entry.Node, AclVerb.List, read);

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

            context.Respond(200, json.Done());
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
        /// </remarks>
        private static bool Visible(IGateContext context, IAclNode node, AclDocuments read)
        {
            return AclResolver.Permits(node, context.Client.Profiles, AclVerb.List, AclReach.Fields, read);
        }

        /// <summary>Whether the database protects this one, by name.</summary>
        private static bool IsProtected(VaultEntryDetail entry, string field)
        {
            foreach (var name in entry.Protected)
            {
                if (string.Equals(name, field, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether any of these names is one the database would protect.</summary>
        private static bool Guarded(IVault vault, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                if (vault.Protects(name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether any of these names is protected on the entry that already exists.
        /// </summary>
        /// <remarks>
        /// Asked as well as `Protects`, because the two answer different halves. A field is
        /// protected either because of what it is called, which the database decides, or because
        /// somebody marked that one on that entry. Overwriting either is setting a secret.
        /// </remarks>
        private static bool Guarded(IVault vault, string entryUuid, IEnumerable<string> names)
        {
            foreach (var field in vault.Fields(entryUuid))
            {
                if (!field.IsProtected)
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (string.Equals(field.Name, name, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a value written under this name is one KeePass would resolve or run rather than
        /// open as it stands.
        /// </summary>
        /// <remarks>
        /// **The one thing a write is refused for what it says rather than for where it goes.**
        /// This gate stores a value verbatim and KeePass, not this gate, decides later what it
        /// means. Its Open URL action compiles the entry's URL through `SprEngine` first, and that
        /// resolves `{REF:P@I:&lt;uuid&gt;}` against the whole open database with no notion of
        /// these rules. So a client holding nothing but `update` on one entry, possibly one it
        /// made itself, could write a URL carrying a reference to the password of an entry it was
        /// never granted, and the operator's own double-click would send that password to
        /// whichever host the URL names. Nothing asks the operator first on that path. A `cmd://`
        /// value is the other half: KeePass starts it as a command line, behind a confirmation the
        /// operator is free to turn off.
        ///
        /// **Only the URL field.** Braces are ordinary in a note and refusing them everywhere
        /// would be this API deciding what a database may hold. A brace is not a character a URL
        /// may carry unencoded in the first place, so what a caller loses is a spelling that `%7B`
        /// already has.
        ///
        /// **What this does not reach** is the rest of what KeePass compiles. A reference planted
        /// in `UserName` or `Title` is resolved by a copy or an auto-type, into the operator's own
        /// clipboard or window rather than out to a stranger, and one in `Password` asks the
        /// `protected` switch already. That leaves a write verb as sensitive as a read verb, which
        /// is something an operator granting one has to know rather than something this can
        /// enumerate.
        ///
        /// The name is compared the way the database compares one, so a spelling of `URL` its own
        /// dictionary cannot tell from `URL` is the URL field here too. The name itself is the
        /// document's, which is what the wire calls it.
        /// </remarks>
        private static bool Compiled(string name, string value)
        {
            if (value == null || Comparer<string>.Default.Compare(name, "URL") != 0)
            {
                return false;
            }

            return value.IndexOf('{') >= 0
                || value.TrimStart().StartsWith("cmd://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The same question about a whole body, which the two entry routes take.</summary>
        private static bool Compiled(IReadOnlyDictionary<string, string> fields)
        {
            foreach (var field in fields)
            {
                if (Compiled(field.Key, field.Value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>What a caller is told about a value this gate will not hand KeePass.</summary>
        /// <remarks>
        /// A sentence rather than one word, and it names the rule rather than the bytes: this is a
        /// refusal a client is meant to act on, and there is one thing to change. It is asked
        /// after the rules, so a client that may not write here learns nothing from it.
        /// </remarks>
        private static void Resolved(IGateContext context)
        {
            context.Respond(400, HttpResponse.ErrorBody(
                "this gate does not store a URL that KeePass would resolve or run"));
        }

        /// <summary>
        /// What a caller is told when a write would set a value it may not set.
        /// </summary>
        /// <remarks>
        /// The same answer a rule that refuses anything else gives, which is what every refusal
        /// here looks like: a client that may reach nothing must not be able to map the database by
        /// noticing which refusals differ. It costs an honest caller a clearer message, and the
        /// message it would have wanted is on the tab of whoever wrote the rule.
        /// </remarks>
        private static void Withheld(IGateContext context, IAclNode node, AclVerb verb)
        {
            Recorded(context, node, verb);
            Unreachable(context);
        }

        /// <summary>Writes down a protected-scope refusal, which is the one the router never sees.</summary>
        /// <remarks>
        /// The router records that the verb was granted, before dispatch, and that line is true.
        /// This is the second decision on the same request: the same client asking for the values
        /// inside an answer it is otherwise allowed, and being refused them. Without it the audit
        /// said a client had been given something the gate had in fact withheld. That attempt is
        /// exactly the one the `protected` switch exists to stop, so it is exactly the line an
        /// operator opens the file to find.
        ///
        /// **Only where something was actually withheld.** Asked on every read, this would write a
        /// line for the ordinary case of a client that simply does not hold `protected`, on entries
        /// with nothing protected on them, which is the noise `A_listing_is_recorded_once` refuses.
        /// </remarks>
        private static void Recorded(IGateContext context, IAclNode node, AclVerb verb)
        {
            object found;
            if (context.Items.TryGetValue(SpecRouter.ReachItem, out found) && found is ProtectedReach)
            {
                // Records because it refuses; the router's own check decides which way.
                ((ProtectedReach)found)(node, verb);
            }
        }

        /// <summary>
        /// Whether this caller may have the values the database protects on this node.
        /// </summary>
        /// <remarks>
        /// **The one question a handler asks rather than the router**, and the asymmetry is worth
        /// naming. An attachment is a route of its own, so the router asks before dispatch and no
        /// handler thinks about scope. A protected value arrives inside the answer to a route that
        /// is allowed, so the only moment the question exists is while that answer is being
        /// written.
        ///
        /// It is asked wherever a protected value would be written, which is a handful of places,
        /// for the reason everything else is asked once: field values are written in more places
        /// than anybody can hold in their head, and a check each of them has to remember is a check
        /// one of them will not.
        /// </remarks>
        private static bool ReachesProtected(IGateContext context, IAclNode node, AclVerb verb)
        {
            return AclResolver.Permits(node, context.Client.Profiles, verb, AclReach.Protected);
        }

        /// <summary>The same question, asked per item and deliberately not recorded.</summary>
        /// <remarks>
        /// For the reason `Visible` gives just above: a listing asks this about every entry it
        /// walks, so recording it would be a line per item for the ordinary case. The decisions
        /// worth reading are the ones about a named node, and those go through `ReachesProtected`.
        /// </remarks>
        private static bool ReachesProtectedInListing(
            IGateContext context, IAclNode node, AclVerb verb, AclDocuments read)
        {
            return AclResolver.Permits(node, context.Client.Profiles, verb, AclReach.Protected, read);
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

        /// <summary>
        /// What a change answers with.
        /// </summary>
        /// <remarks>
        /// A refusal that says nothing is there is the same 404 as a node the rules refuse, so a
        /// client cannot tell a name it may not use from one that is not taken.
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
                if (write.Uuid == null)
                {
                    context.Respond(200, "{\"ok\":true}");
                    return;
                }

                var json = new JsonWriter();
                json.Text("uuid", write.Uuid);
                context.Respond(Created, json.Done());
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

            if (write.IsFailed)
            {
                // Nothing was attempted: the database closed under the request, KeePass answered
                // the attempt with an error, or the vault was handed a name the routing table
                // never lets through. Answered as this plugin's own failure for the reason the one
                // above is, and it was the same defect: a caller was told its request had nowhere
                // to go, which is what an absent node and a refused one are told, so an operator
                // was sent to look at rules and identifiers over a database that had simply gone.
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
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
