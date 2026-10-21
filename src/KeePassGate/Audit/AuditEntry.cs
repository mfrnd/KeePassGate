using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace KeePassGate.Audit
{
    /// <summary>
    /// One decision, as it is written down: the rules', or the operator's about an admission.
    /// </summary>
    /// <remarks>
    /// **What is not in here is the design.** No entry title, no field name, no value, no group
    /// path, no profile a client asked about that it does not hold. A log of titles is an inventory
    /// of the database sitting beside the database, readable by anybody who can read a file, and it
    /// would be the easiest way to lose everything this plugin is for. What identifies a node is
    /// its UUID, what identifies a certificate is its admission key and what identifies a client
    /// is its client key, all of which mean something to somebody holding the database and nothing
    /// to anybody else.
    ///
    /// **Both keys, under the names `/gate/whoami` answers them by**, so that one word means one
    /// thing wherever it is read. `admissionKey` is the certificate on the connection, or the one a
    /// revocation took away, which is what the Clients page revokes and a renewal changes.
    /// `clientKey` is the client it belongs to, the admission key of the certificate it paired
    /// with, which every one of its records carries and a renewal keeps, so every line a client
    /// ever caused is found under one value however often it renewed. Taken from the admission
    /// record the request already read, never read again for the line; the operator's revocation,
    /// which no request asked for, takes it from the record the revocation empties, read once as
    /// the dialog's OK applies it. Null wherever no record stands behind the line: a pairing
    /// nobody approved, and a certificate refused before or without one.
    ///
    /// The reason is a phrase this plugin chose from a fixed set, never text a caller sent. A log
    /// that echoed a request would be a log an attacker could write into.
    ///
    /// One JSON object per line, because that is the shape every log reader already has: a line is
    /// a record, a truncated write costs one record, and appending needs no rewriting of what came
    /// before.
    ///
    /// **Every value is a string, including the decision**, so that a line says
    /// `"decision":"refused"` rather than `false`, which is a word a person skimming a file can see
    /// rather than a flag they have to remember the sense of. The exceptions are the six members
    /// that can have nothing to say. Those are `database`, `admissionKey`, `clientKey`,
    /// `operation`, `node` and `decidedAt`, each written as a bare `null` rather than left out,
    /// because a reader that has to tell "absent" from "not applicable" is a reader that will
    /// guess.
    ///
    /// **Two kinds of line, in one form.** Most say what the rules decided about a request, and
    /// their decision is `permitted` or `refused`. The rest say what happened to an admission, which
    /// no rule decides: a pairing or a renewal as the operator answered it, `approved`, `refused`
    /// or `unanswered`, and a revocation, `revoked`, the operator's or a client's own. Their own
    /// words keep `permitted` meaning one thing, that the rules allowed something.
    ///
    /// **Four members are written only where they apply.** `removedProfiles` is a list of names on
    /// a refusal that profiles the operator removed would have permitted; `invitation` names the
    /// invitation a pairing arrived on, and `issued` the admission key an approval admitted, on a
    /// pairing or a renewal; `requested` names the identifier a request asked for when it reached
    /// no node. None is a question every line answers, and written as `null` on every
    /// other line each would change every line to explain a few.
    ///
    /// **That null is why `JsonBody` cannot read a line back**, since that reader takes strings and
    /// arrays of strings and nothing else. It is a real cost and it is paid deliberately: the lines
    /// it refuses are exactly the interesting ones, a question that never reached a node, which is
    /// every certificate this authority issued and the gate refused, and nothing in the plugin
    /// parses a line back: the one reader, `JsonLinesAudit.LastSeen`, finds `at` and
    /// `admissionKey` by position for the authority chooser. A line with no admission key is a
    /// pairing, asked for by a program holding no certificate yet and named by its invitation
    /// instead; any other is written only by a defect, since a caller that proved nothing to this
    /// authority is not written down at all. The reader this file is written for is whatever the
    /// operator already greps with.
    /// </remarks>
    public sealed class AuditEntry
    {
        /// <summary>What a permitted decision says.</summary>
        public const string Granted = "permitted";

        /// <summary>What a refused one says, the rules' or the operator's.</summary>
        public const string Refused = "refused";

        /// <summary>What a pairing or a renewal the operator approved says.</summary>
        public const string Approved = "approved";

        /// <summary>What a pairing or a renewal nobody answered says.</summary>
        public const string Unanswered = "unanswered";

        /// <summary>What a revocation says, the operator's or a client's own.</summary>
        public const string Revoked = "revoked";

        public AuditEntry(
            DateTimeOffset at,
            string databaseId,
            string admissionKey,
            string clientKey,
            string operationId,
            string node,
            string decidedAt,
            bool permitted,
            string reason,
            IReadOnlyList<string> removedProfiles = null,
            string requested = null)
            : this(
                at,
                databaseId,
                admissionKey,
                clientKey,
                operationId,
                node,
                decidedAt,
                permitted ? Granted : Refused,
                reason,
                removedProfiles,
                null,
                null)
        {
            Requested = node == null ? requested : null;
        }

        private AuditEntry(
            DateTimeOffset at,
            string databaseId,
            string admissionKey,
            string clientKey,
            string operationId,
            string node,
            string decidedAt,
            string decision,
            string reason,
            IReadOnlyList<string> removedProfiles,
            string invitation,
            string issued)
        {
            At = at;
            DatabaseId = databaseId;
            AdmissionKey = admissionKey;
            ClientKey = clientKey;
            OperationId = operationId;
            Node = node;
            DecidedAt = decidedAt;
            Decision = decision;
            Reason = reason;
            RemovedProfiles = removedProfiles;
            Invitation = invitation;
            Issued = issued;
        }

        /// <summary>
        /// A line about an admission rather than a rule: a pairing or a renewal as the operator
        /// answered it, or a revocation.
        /// </summary>
        /// <remarks>
        /// About no node, so `node` and `decidedAt` are null: what decided is a person or a client
        /// giving its own admission up, and neither is a rule anywhere in the tree.
        /// </remarks>
        /// <param name="admissionKey">
        /// The certificate the line is about: the one that asked, or the one revoked. Null on a
        /// pairing, whose asker holds no certificate yet.
        /// </param>
        /// <param name="clientKey">
        /// The client that certificate belongs to, from its admission record; on an approved
        /// pairing the key it issued, and null on a pairing nobody approved, which admitted nobody.
        /// </param>
        /// <param name="operationId">What was asked for, or null for the operator's revocation, which no route asked for.</param>
        /// <param name="invitation">The invitation a pairing arrived on, or null on anything else.</param>
        /// <param name="issued">The admission key an approval admitted, or null on anything else.</param>
        internal static AuditEntry Admission(
            DateTimeOffset at,
            string databaseId,
            string admissionKey,
            string clientKey,
            string operationId,
            string decision,
            string reason,
            string invitation,
            string issued)
        {
            return new AuditEntry(
                at,
                databaseId,
                admissionKey,
                clientKey,
                operationId,
                null,
                null,
                decision,
                reason,
                null,
                invitation,
                issued);
        }

        public DateTimeOffset At { get; private set; }

        /// <summary>
        /// Which database answered, by its authority's whole digest, of which the status route
        /// shows the first characters: never its path.
        /// </summary>
        public string DatabaseId { get; private set; }

        /// <summary>
        /// Which certificate the line is about, by admission key, so a revoked one stays
        /// identifiable: the one that asked, or the one revoked. Null on a pairing. Written as
        /// `admissionKey`.
        /// </summary>
        public string AdmissionKey { get; private set; }

        /// <summary>
        /// Which client the line is about, by client key: the admission key of the certificate it
        /// paired with, which its admission record carries and a renewal keeps. Written as
        /// `clientKey`.
        /// </summary>
        /// <remarks>
        /// The issued key on an approved pairing, which is that client's first certificate. Null
        /// where no admission record stands behind the line: a pairing refused or unanswered,
        /// which admitted nobody, and a certificate refused for having no record, for not being
        /// for client authentication or for being outside its dates. The last two are refused
        /// before the record is read, and a revoked one's record is emptied, so naming a client
        /// there would cost each such request a read of its own for a certificate that reaches
        /// nothing. Its admission key is the one the Clients page revoked, which is the name the
        /// operator has for it.
        /// </remarks>
        public string ClientKey { get; private set; }

        /// <summary>
        /// What it asked for, by the name the routing table gives it, or null for the operator's
        /// revocation, which no route asked for.
        /// </summary>
        public string OperationId { get; private set; }

        /// <summary>
        /// Which group or entry it was about, by UUID, or null when the request never reached one.
        /// </summary>
        public string Node { get; private set; }

        /// <summary>
        /// Which node's rule answered, by UUID, or null when no rule did.
        /// </summary>
        /// <remarks>
        /// Not the same as <see cref="Node"/>, and the difference is the whole reason this is
        /// written down. Nearest wins, so a refusal is usually decided somewhere above the node
        /// asked about: without this, an operator reading "a rule here refuses anything it does not
        /// name" against an entry opens that entry, finds no rule on it, and concludes the audit is
        /// wrong. A removal is worse. It is decided on two nodes, the container it leaves and the
        /// node itself, and the line could not say which of them said no.
        /// </remarks>
        public string DecidedAt { get; private set; }

        /// <summary>
        /// The word the line is written with: `permitted` or `refused` for the rules, and
        /// `approved`, `refused`, `unanswered` or `revoked` for an admission.
        /// </summary>
        public string Decision { get; private set; }

        /// <summary>Whether the rules permitted it, which no line about an admission says.</summary>
        public bool Permitted
        {
            get { return string.Equals(Decision, Granted, StringComparison.Ordinal); }
        }

        /// <summary>Why, in this plugin's own words. Never anything a caller sent.</summary>
        public string Reason { get; private set; }

        /// <summary>
        /// The profiles the operator removed that would have permitted this, or null on every
        /// line but a refusal they caused, where the line then leaves the member out.
        /// </summary>
        /// <remarks>
        /// **The one place a profile name reaches this file, and only a name that grants nothing.**
        /// A client's profiles are its record's less every name the database no longer lists, so a
        /// refusal because the operator took a name away read exactly like a rule saying no. When
        /// the record's whole list would have been permitted, the reason is
        /// `AccessCheck.ProfileRemoved` and this names the removed profiles that would have done
        /// it. What the client still holds is never written, which is the rule the line keeps.
        /// </remarks>
        public IReadOnlyList<string> RemovedProfiles { get; private set; }

        /// <summary>
        /// The invitation a pairing arrived on, as the KPG tab and the approval dialog show it, or
        /// null on every other line, which then leaves the member out.
        /// </summary>
        public string Invitation { get; private set; }

        /// <summary>
        /// The admission key an approval admitted, on a pairing or a renewal, or null on every
        /// other line, which then leaves the member out.
        /// </summary>
        /// <remarks>
        /// Written only once the record it names is in the database, so a key here is always one
        /// that was admitted: an approval whose record never landed admitted nobody and gets no
        /// line at all. On a pairing it is the client key as well, which every later line from that
        /// client names under `clientKey`, and its lines name it under `admissionKey` until it
        /// renews.
        /// </remarks>
        public string Issued { get; private set; }

        /// <summary>
        /// The identifier a request named for a node it never reached, or null on every other
        /// line, which then leaves the member out.
        /// </summary>
        /// <remarks>
        /// A request for an entry that is not there, or is in the recycle bin, reaches no node, so
        /// `node` is null and the line said nothing about which one was asked for, which is the
        /// question an operator following a failed reference opens the file with. Only an
        /// identifier as the routes write one, 32 hexadecimal characters or `root`, so the member
        /// holds nothing a caller could spell freely; anything else is left out. Telling the
        /// operator the identifier tells the caller nothing, since the caller sent it.
        /// </remarks>
        public string Requested { get; private set; }

        /// <summary>
        /// The line this becomes. One object, flat, with every member always present but the notes
        /// that apply only to some lines.
        /// </summary>
        /// <remarks>
        /// Always present, including as `null`, because a reader that has to tell "absent" from
        /// "not applicable" is a reader that will guess. A missing member reads as a log written by
        /// a different version; an explicit null reads as a question that did not arise. The
        /// exceptions are <see cref="RemovedProfiles"/>, <see cref="Invitation"/>,
        /// <see cref="Issued"/> and <see cref="Requested"/>, for the reason the class gives: each
        /// belongs to one kind of line, whose reason or decision already says it is there.
        /// </remarks>
        public string Line()
        {
            // Written by `Json.NET`, which is here for the request bodies. A node's name comes
            // out of a database this plugin did not write, so what it looks like escaped is a
            // question with one answer in this repository and not two.
            var text = new StringWriter(CultureInfo.InvariantCulture);
            var json = new JsonTextWriter(text) { Formatting = Formatting.None };

            json.WriteStartObject();

            json.WritePropertyName("at");
            json.WriteValue(Moments.Wire(At));

            Member(json, "database", DatabaseId);
            Member(json, "admissionKey", AdmissionKey);
            Member(json, "clientKey", ClientKey);
            Member(json, "operation", OperationId);
            Member(json, "node", Node);
            Member(json, "decidedAt", DecidedAt);

            Member(json, "decision", Decision);
            Member(json, "reason", Reason);

            if (RemovedProfiles != null)
            {
                json.WritePropertyName("removedProfiles");
                json.WriteStartArray();
                foreach (var profile in RemovedProfiles)
                {
                    json.WriteValue(profile);
                }

                json.WriteEndArray();
            }

            if (Invitation != null)
            {
                Member(json, "invitation", Invitation);
            }

            if (Issued != null)
            {
                Member(json, "issued", Issued);
            }

            if (Requested != null)
            {
                Member(json, "requested", Requested);
            }

            json.WriteEndObject();
            json.Flush();

            return text.ToString();
        }

        public override string ToString()
        {
            return Line();
        }

        private static void Member(JsonTextWriter json, string name, string value)
        {
            json.WritePropertyName(name);
            json.WriteValue(value);
        }
    }
}
