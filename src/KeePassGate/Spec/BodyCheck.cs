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

            return Unimplemented(schema);
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

        /// <summary>A constraint this cannot enforce, or null when the schema uses none.</summary>
        /// <remarks>
        /// **The whitelist is on keywords, not on `type`.** Asking only what a member's type is
        /// let every other JSON Schema keyword through the startup refusal and left it enforced
        /// by nothing: `pattern` on a uuid, `minItems` on a list of profiles, a whole `allOf`.
        /// The plugin loaded clean and the constraint was checked nowhere, which is the gap this
        /// class exists to make impossible rather than the subset it claims to be.
        ///
        /// Annotations are deliberately not named here: `description`, `title` and `example` say
        /// nothing about what a body may contain, so nothing has to enforce them.
        /// </remarks>
        private static string Unimplemented(OpenApiSchema schema)
        {
            if (schema.Pattern != null)
            {
                return "pattern";
            }

            if (schema.Enum != null && schema.Enum.Count > 0)
            {
                return "enum";
            }

            if (schema.Format != null)
            {
                return "format";
            }

            if (schema.MinItems.HasValue)
            {
                return "minItems";
            }

            if (schema.UniqueItems.HasValue && schema.UniqueItems.Value)
            {
                return "uniqueItems";
            }

            if (schema.MinProperties.HasValue || schema.MaxProperties.HasValue)
            {
                return "a property count";
            }

            if (schema.AllOf.Count > 0 || schema.AnyOf.Count > 0
                || schema.OneOf.Count > 0 || schema.Not != null)
            {
                return "a composition";
            }

            if (schema.MultipleOf.HasValue || schema.Minimum != null || schema.Maximum != null)
            {
                return "a numeric bound";
            }

            return null;
        }

        private static string SupportsMember(OpenApiSchema member)
        {
            if (member == null)
            {
                return "there is no schema";
            }

            if (string.Equals(member.Type, "string", StringComparison.Ordinal))
            {
                return Unimplemented(member);
            }

            if (string.Equals(member.Type, "array", StringComparison.Ordinal))
            {
                if (member.Items == null || !string.Equals(member.Items.Type, "string", StringComparison.Ordinal))
                {
                    return "an array on this wire holds strings";
                }

                return Unimplemented(member) ?? Unimplemented(member.Items);
            }

            return "this wire carries strings and arrays of strings, and this declares " + Describe(member.Type);
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
