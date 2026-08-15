using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace KeePassGate.Audit
{
    /// <summary>
    /// One decision, as it is written down.
    /// </summary>
    /// <remarks>
    /// **What is not in here is the design.** No entry title, no field name, no value, no group
    /// path, no profile a client asked about that it does not hold. A log of titles is an inventory
    /// of the database sitting beside the database, readable by anybody who can read a file, and it
    /// would be the easiest way to lose everything this plugin is for. What identifies a node is
    /// its UUID and what identifies a client is its admission key, both of which mean something to
    /// somebody holding the database and nothing to anybody else.
    ///
    /// The reason is a phrase the resolver chose from a fixed set, never text a caller sent. A log
    /// that echoed a request would be a log an attacker could write into.
    ///
    /// One JSON object per line, because that is the shape every log reader already has: a line is
    /// a record, a truncated write costs one record, and appending needs no rewriting of what came
    /// before.
    ///
    /// **Every value is a string, including the decision**, so that a line says
    /// `"decision":"refused"` rather than `false`, which is a word a person skimming a file can see
    /// rather than a flag they have to remember the sense of. The exceptions are the four members
    /// that can have nothing to say. Those are `database`, `client`, `node` and `decidedAt`, each
    /// written as a bare `null` rather than left out, because a reader that has to tell "absent"
    /// from "not applicable" is a reader that will guess.
    ///
    /// **That null is why `JsonBody` cannot read a line back**, since that reader takes strings and
    /// arrays of strings and nothing else. It is a real cost and it is paid deliberately: the lines
    /// it refuses are exactly the interesting ones, a question that never reached a node, which is
    /// every certificate this authority issued and the gate refused, and nothing in the plugin
    /// reads the audit back anyway. A line with no client is written only by a defect: a caller
    /// that proved nothing to this authority is not written down at all. The reader this file is
    /// written for is whatever the operator already greps with.
    /// </remarks>
    public sealed class AuditEntry
    {
        /// <summary>What a permitted decision says.</summary>
        public const string Granted = "permitted";

        /// <summary>What a refused one says.</summary>
        public const string Refused = "refused";

        public AuditEntry(
            DateTimeOffset at,
            string databaseId,
            string admissionKey,
            string operationId,
            string node,
            string decidedAt,
            bool permitted,
            string reason)
        {
            At = at;
            DatabaseId = databaseId;
            AdmissionKey = admissionKey;
            OperationId = operationId;
            Node = node;
            DecidedAt = decidedAt;
            Permitted = permitted;
            Reason = reason;
        }

        public DateTimeOffset At { get; private set; }

        /// <summary>
        /// Which database answered, by its authority's whole digest, of which the status route
        /// shows the first characters: never its path.
        /// </summary>
        public string DatabaseId { get; private set; }

        /// <summary>Which client asked. The admission key, so a revoked client stays identifiable.</summary>
        public string AdmissionKey { get; private set; }

        /// <summary>What it asked for, by the name the routing table gives it.</summary>
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

        public bool Permitted { get; private set; }

        /// <summary>Why, in the resolver's own words. Never anything a caller sent.</summary>
        public string Reason { get; private set; }

        /// <summary>
        /// The line this becomes. One object, flat, with every member always present.
        /// </summary>
        /// <remarks>
        /// Always present, including as `null`, because a reader that has to tell "absent" from
        /// "not applicable" is a reader that will guess. A missing member reads as a log written by
        /// a different version; an explicit null reads as a question that did not arise.
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
            Member(json, "client", AdmissionKey);
            Member(json, "operation", OperationId);
            Member(json, "node", Node);
            Member(json, "decidedAt", DecidedAt);

            Member(json, "decision", Permitted ? Granted : Refused);
            Member(json, "reason", Reason);

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
