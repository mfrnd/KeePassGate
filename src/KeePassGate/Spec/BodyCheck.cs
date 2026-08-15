using System;
using KeePassGate.Transport;
using Microsoft.OpenApi.Models;

namespace KeePassGate.Spec
{
    /// <summary>
    /// Checks a request body against the schema the document declares for it, before a handler runs.
    /// </summary>
    /// <remarks>
    /// **A subset, and the subset is enforced rather than assumed.** `Supports` says whether a
    /// schema uses only what this understands, and the router asks it about every schema in the
    /// document at startup. A construct nobody implemented is therefore a plugin that does not
    /// load, never a body that goes unchecked because the checker did not recognize its schema.
    /// That is the difference between a subset and a gap.
    ///
    /// What it understands is exactly what `JsonBody` can read and what this wire carries: one flat
    /// object whose values are strings or arrays of strings, `required`, `additionalProperties:
    /// false`, lengths, and a cap on how many items an array may hold. No schema library does this
    /// job. The cheapest brings two assemblies and a license an MIT plugin cannot take; the cheapest
    /// one it can take brings fourteen into the process that holds somebody's password database,
    /// against three for reading the document, and would validate constructs this document is not
    /// allowed to contain anyway. The counts are on the `spikes/json-reading` branch.
    ///
    /// The refusals here are one word, because a message that named the member would echo a
    /// caller's own bytes back at it. Not every refusal this API makes is one word: the ones a
    /// client is meant to act on are, and the rest are told apart by their status.
    /// </remarks>
    internal static class BodyCheck
    {
        /// <summary>What every refusal in here answers with.</summary>
        public const string Refusal = "malformed";

        /// <summary>
        /// Why this schema cannot be checked, or null when it can be.
        /// </summary>
        /// <remarks>
        /// Called for every schema in the document at startup, so the answer is a sentence for
        /// whoever is editing the document rather than something a client sees.
        /// </remarks>
        public static string Supports(OpenApiSchema schema)
        {
            if (schema == null)
            {
                return "there is no schema";
            }

            if (!string.Equals(schema.Type, "object", StringComparison.Ordinal))
            {
                return "a request body is an object, and this declares " + Describe(schema.Type);
            }

            if (schema.AdditionalPropertiesAllowed)
            {
                // Without this a member nobody declared is a member nobody checked, and the
                // handler would be reading whatever arrived.
                return "a request body declares additionalProperties: false";
            }

            if (schema.Properties == null || schema.Properties.Count == 0)
            {
                return "a request body declares its properties";
            }

            foreach (var property in schema.Properties)
            {
                var reason = SupportsMember(property.Value);
                if (reason != null)
                {
                    return "property " + property.Key + ": " + reason;
                }
            }

            foreach (var required in schema.Required)
            {
                if (!schema.Properties.ContainsKey(required))
                {
                    return "required names " + required + ", which is not declared";
                }
            }

            return Unimplemented(schema, "properties", "required", "additionalProperties");
        }

        /// <summary>
        /// Whether this body is what the document declared, and one word saying no if it is not.
        /// </summary>
        public static string Validate(OpenApiSchema schema, JsonBody body)
        {
            if (body == null)
            {
                return Refusal;
            }

            foreach (var required in schema.Required)
            {
                if (!Present(body, required))
                {
                    return Refusal;
                }
            }

            foreach (var name in body.Names)
            {
                OpenApiSchema declared;
                if (!schema.Properties.TryGetValue(name, out declared))
                {
                    // `additionalProperties: false`, which `Supports` has already insisted on.
                    return Refusal;
                }

                if (!Matches(declared, body, name))
                {
                    return Refusal;
                }
            }

            return null;
        }

        /// <summary>
        /// The first keyword this schema writes that its type may not, or null when there is none.
        /// </summary>
        /// <remarks>
        /// **An allow list, per type, on keywords.** A string may say how long it is, an array how
        /// many strings it holds, and the body itself which members it has, which of them it
        /// requires and that it has no others. Every other keyword the reader models that says
        /// anything about a value is refused wherever it is written, so a constraint nothing
        /// enforces is a plugin that does not load: `pattern` on a uuid, `minItems` on a list of
        /// profiles, a whole `allOf`, and a keyword on a type it does not apply to, such as
        /// `maxLength` on an array, which reads as a bound on the array and bounds nothing.
        ///
        /// **It was a list of refusals, and it was called a whitelist.** What it did not name went
        /// through, and a plugin that loads with a constraint checked nowhere is the gap this class
        /// exists to make impossible rather than the subset it claims to be.
        ///
        /// Annotations are deliberately not in <see cref="Keywords"/>: `title`, `description`,
        /// `example`, `deprecated`, `externalDocs`, `xml` and extensions say nothing about what a
        /// body may contain, so nothing has to enforce them. `BodyCheckTests` holds the reader's
        /// schema object to that account, so a reader that learns a new keyword stops the build
        /// rather than bringing one nothing refuses.
        ///
        /// **Four keywords that look like annotations and are not.** `readOnly` on a member of a
        /// request body says that member must not be sent, and letting it through accepted one
        /// the document forbids, which is the fail-open direction. `default` says what an absent
        /// member means, and nothing here applies one, so a handler saw absent where the document
        /// promised a value. `nullable` says a member may be null, which `JsonBody` cannot carry
        /// at all, so the document would be permitting a body the reader refuses. `writeOnly` is
        /// the mirror of `readOnly` and says nothing about a request on its own, and it is refused
        /// anyway: a document using it was written by somebody expecting the distinction to be
        /// enforced, and this is not the place to decide it does not matter.
        /// </remarks>
        private static string Unimplemented(OpenApiSchema schema, params string[] allowed)
        {
            foreach (var keyword in Keywords)
            {
                if (keyword.Written(schema) && Array.IndexOf(allowed, keyword.Name) < 0)
                {
                    return keyword.Name;
                }
            }

            return null;
        }

        /// <summary>
        /// Every keyword of the schema object the reader models that says something about a value,
        /// and how to tell it was written.
        /// </summary>
        private static readonly Keyword[] Keywords =
        {
            new Keyword("pattern", s => s.Pattern != null),
            new Keyword("format", s => s.Format != null),
            new Keyword("enum", s => s.Enum != null && s.Enum.Count > 0),
            new Keyword("default", s => s.Default != null),
            new Keyword("nullable", s => s.Nullable),
            new Keyword("readOnly", s => s.ReadOnly),
            new Keyword("writeOnly", s => s.WriteOnly),
            new Keyword("minLength", s => s.MinLength.HasValue),
            new Keyword("maxLength", s => s.MaxLength.HasValue),
            new Keyword("items", s => s.Items != null),
            new Keyword("minItems", s => s.MinItems.HasValue),
            new Keyword("maxItems", s => s.MaxItems.HasValue),
            new Keyword("uniqueItems", s => s.UniqueItems.HasValue && s.UniqueItems.Value),
            new Keyword("properties", s => s.Properties != null && s.Properties.Count > 0),
            new Keyword("required", s => s.Required != null && s.Required.Count > 0),
            new Keyword("additionalProperties", s => !s.AdditionalPropertiesAllowed || s.AdditionalProperties != null),
            new Keyword("minProperties", s => s.MinProperties.HasValue),
            new Keyword("maxProperties", s => s.MaxProperties.HasValue),
            new Keyword("allOf", s => s.AllOf != null && s.AllOf.Count > 0),
            new Keyword("anyOf", s => s.AnyOf != null && s.AnyOf.Count > 0),
            new Keyword("oneOf", s => s.OneOf != null && s.OneOf.Count > 0),
            new Keyword("not", s => s.Not != null),
            new Keyword("discriminator", s => s.Discriminator != null),
            new Keyword("multipleOf", s => s.MultipleOf.HasValue),
            new Keyword("minimum", s => s.Minimum.HasValue),
            new Keyword("exclusiveMinimum", s => s.ExclusiveMinimum.HasValue),
            new Keyword("maximum", s => s.Maximum.HasValue),
            new Keyword("exclusiveMaximum", s => s.ExclusiveMaximum.HasValue),
        };

        private static string SupportsMember(OpenApiSchema member)
        {
            if (member == null)
            {
                return "there is no schema";
            }

            if (string.Equals(member.Type, "string", StringComparison.Ordinal))
            {
                return Unimplemented(member, "minLength", "maxLength");
            }

            if (string.Equals(member.Type, "array", StringComparison.Ordinal))
            {
                if (member.Items == null || !string.Equals(member.Items.Type, "string", StringComparison.Ordinal))
                {
                    return "an array on this wire holds strings";
                }

                return Unimplemented(member, "items", "maxItems")
                       ?? Unimplemented(member.Items, "minLength", "maxLength");
            }

            return "this wire carries strings and arrays of strings, and this declares " + Describe(member.Type);
        }

        /// <summary>One keyword, by the name the document writes it under.</summary>
        private sealed class Keyword
        {
            public Keyword(string name, Func<OpenApiSchema, bool> written)
            {
                Name = name;
                Written = written;
            }

            public string Name { get; private set; }

            public Func<OpenApiSchema, bool> Written { get; private set; }
        }

        private static bool Present(JsonBody body, string name)
        {
            return body.Text(name) != null || body.Array(name) != null;
        }

        private static bool Matches(OpenApiSchema declared, JsonBody body, string name)
        {
            if (string.Equals(declared.Type, "array", StringComparison.Ordinal))
            {
                var items = body.Array(name);
                if (items == null)
                {
                    return false;
                }

                if (declared.MaxItems.HasValue && items.Count > declared.MaxItems.Value)
                {
                    return false;
                }

                foreach (var item in items)
                {
                    if (!Fits(declared.Items, item))
                    {
                        return false;
                    }
                }

                return true;
            }

            var text = body.Text(name);
            return text != null && Fits(declared, text);
        }

        /// <summary>Whether a string is as long as the document allows, counted as it counts.</summary>
        /// <remarks>
        /// **In characters, not in the UTF-16 units `string.Length` counts.** JSON Schema measures
        /// `minLength` and `maxLength` in characters, and a character outside the Basic
        /// Multilingual Plane is one of those and two of these, so a name of 512 emoji was refused
        /// under the `maxLength` of 512 that allows it, and a client validating against the
        /// document sent a body the gate turned away.
        /// </remarks>
        private static bool Fits(OpenApiSchema declared, string value)
        {
            var length = Characters(value);

            if (declared.MinLength.HasValue && length < declared.MinLength.Value)
            {
                return false;
            }

            return !declared.MaxLength.HasValue || length <= declared.MaxLength.Value;
        }

        /// <summary>How many characters a string holds, with a surrogate pair counted once.</summary>
        /// <remarks>
        /// A surrogate without its pair counts as one on its own, which is the most it can be to
        /// anything counting characters.
        /// </remarks>
        private static int Characters(string value)
        {
            var count = 0;

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                }

                count++;
            }

            return count;
        }

        private static string Describe(string type)
        {
            return string.IsNullOrEmpty(type) ? "nothing" : type;
        }
    }
}
