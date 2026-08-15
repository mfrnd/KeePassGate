using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KeePassGate.Transport;
using Newtonsoft.Json;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The authority's three parts as one stored value, which is what makes them indivisible.
    /// </summary>
    /// <remarks>
    /// One key rather than three, and the reason is what a kdbx merge does. `PwDatabase.CustomData`
    /// merges key by key, which is what admission records are stored in and what they want; the
    /// authority wants the opposite. A merge that took the three parts separately could take the
    /// certificate from one replica and the private key of a different authority from another, and
    /// the pair would no longer chain: the store would read that as damaged, the gate would serve
    /// nothing, and the only remedy is a recreation that revokes every client. Inside one key
    /// there is nothing to take apart, so whichever replica wins contributes all three.
    ///
    /// The shape is what `JsonBody` already reads, one flat object of strings, so no parser
    /// arrives with this and nothing about escaping is decided twice.
    ///
    /// <code>
    /// {"v":"1","ca":"&lt;base64 PKCS#12&gt;","host":"&lt;base64 PKCS#12&gt;","sniKey":"&lt;base64&gt;"}
    /// </code>
    ///
    /// **Everything here fails closed.** A document that will not read, that is missing a part, or
    /// that carries a version this plugin does not know is damage rather than absence: it is
    /// reported with a reason and never quietly repaired.
    /// </remarks>
    public sealed class AuthorityDocument
    {
        /// <summary>The version this plugin writes, and the only one it reads.</summary>
        public const string Version = "1";

        /// <summary>The member naming the format version.</summary>
        public const string VersionKey = "v";

        /// <summary>The member holding the authority certificate and its private key.</summary>
        public const string AuthorityPartKey = "ca";

        /// <summary>The member holding the normal host certificate and its key.</summary>
        public const string HostPartKey = "host";

        /// <summary>The member holding the gate key the connection names are derived from.</summary>
        public const string SniKeyPartKey = "sniKey";

        private AuthorityDocument(string authority, string host, string sniKey)
        {
            Authority = authority;
            Host = host;
            SniKey = sniKey;
        }

        /// <summary>The authority certificate and its private key, as `CertificateBlob` writes one.</summary>
        public string Authority { get; }

        /// <summary>The normal host certificate and its key, as `CertificateBlob` writes one.</summary>
        public string Host { get; }

        /// <summary>The gate key, base64 of the bytes `AuthorityKeys.NewSniKey` produced.</summary>
        public string SniKey { get; }

        /// <summary>
        /// Reads a stored document, or answers null and says in `problems` what was wrong with it.
        /// </summary>
        /// <remarks>
        /// An empty argument answers null with nothing added to `problems`, which is how the store
        /// tells absence from damage: nothing there is not the same as something unreadable.
        /// </remarks>
        public static AuthorityDocument Read(string stored, ICollection<string> problems)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return null;
            }

            var body = JsonBody.Read(stored);
            if (body == null)
            {
                Complain(problems, "The authority is stored in a form this plugin cannot read.");
                return null;
            }

            var version = body.Text(VersionKey);
            if (version != Version)
            {
                // A newer plugin's document, or something else entirely. Either way this one must
                // not guess at what it means: an authority read wrongly is a gate serving with a
                // key it does not understand.
                Complain(problems, "The authority was written in a format version this plugin does not know.");
                return null;
            }

            var authority = body.Text(AuthorityPartKey);
            var host = body.Text(HostPartKey);
            var sniKey = body.Text(SniKeyPartKey);

            if (string.IsNullOrEmpty(authority))
            {
                Complain(problems, "The authority certificate is missing.");
            }

            if (string.IsNullOrEmpty(host))
            {
                Complain(problems, "The host certificate is missing.");
            }

            if (string.IsNullOrEmpty(sniKey))
            {
                Complain(problems, "The gate key is missing.");
            }

            if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(host) || string.IsNullOrEmpty(sniKey))
            {
                return null;
            }

            return new AuthorityDocument(authority, host, sniKey);
        }

        /// <summary>What to store for these three parts.</summary>
        /// <remarks>
        /// Written by `Json.NET`, the same one that reads it back, so nothing about how a base64
        /// blob is escaped is decided in two places.
        /// </remarks>
        public static string Save(string authority, string host, string sniKey)
        {
            var text = new StringWriter(CultureInfo.InvariantCulture);
            var json = new JsonTextWriter(text) { Formatting = Formatting.None };

            json.WriteStartObject();
            json.WritePropertyName(VersionKey);
            json.WriteValue(Version);
            json.WritePropertyName(AuthorityPartKey);
            json.WriteValue(authority);
            json.WritePropertyName(HostPartKey);
            json.WriteValue(host);
            json.WritePropertyName(SniKeyPartKey);
            json.WriteValue(sniKey);
            json.WriteEndObject();
            json.Flush();

            return text.ToString();
        }

        private static void Complain(ICollection<string> problems, string what)
        {
            if (problems != null)
            {
                problems.Add(what);
            }
        }
    }
}
