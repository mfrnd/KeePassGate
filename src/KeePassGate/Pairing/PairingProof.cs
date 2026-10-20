using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// The proofs the two sides exchange before anybody is asked to approve anything.
    /// </summary>
    /// <remarks>
    /// Three values, all `HMAC-SHA256` under the invitation token, each over a different label so
    /// that no proof can ever be replayed as another.
    ///
    /// **The binding is RFC 5929 `tls-server-end-point`**: `SHA-256` over the DER of the
    /// certificate the server is actually serving with. It needs no TLS interface on either side,
    /// which matters here because the transport is managed code and offers none: the server knows
    /// the certificate it chose and the client has just been sent it. `SHA-256` is not negotiated,
    /// because this authority signs with `ecdsa-with-SHA256` and nothing else, which the issuer
    /// enforces and the tests assert.
    ///
    /// What each one is for, from `PAIRING.md` §304 and §305:
    ///
    /// - **The client speaks first.** An impostor learns nothing and does no work until the caller
    ///   has shown it holds the token, so a captured name replayed at the gate dies here rather than
    ///   in front of the operator.
    /// - **The server answers.** An unpaired client has no anchor yet and would otherwise accept
    ///   whatever certificate arrived. Because the binding hashes the certificate in use, a machine
    ///   in the middle can only produce a proof over its own certificate, which recomputes to
    ///   something else on the client.
    /// - **The request is proved too.** `tls-server-end-point` is a constant for the life of a
    ///   certificate, so the freshness comes from the nonce the client chose and the challenge the
    ///   server chose, and the request carries the name, the profiles and the lifetime being asked
    ///   for, so what the operator is shown is what was signed for.
    /// </remarks>
    public static class PairingProof
    {
        public const string ClientLabel = "kpg-client-proof";

        public const string ServerLabel = "kpg-server-proof";

        public const string RequestLabel = "kpg-request-proof";

        /// <summary>`HMAC-SHA256` output, so every proof is this long.</summary>
        public const int Length = 32;

        /// <summary>
        /// RFC 5929 `tls-server-end-point` over the certificate the connection is served with.
        /// </summary>
        public static byte[] Binding(byte[] serverCertificateDer)
        {
            if (serverCertificateDer == null)
            {
                throw new ArgumentNullException(nameof(serverCertificateDer));
            }

            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(serverCertificateDer);
            }
        }

        public static byte[] Client(byte[] token, byte[] binding, byte[] nonce)
        {
            return Compute(token, ClientLabel, new[] { binding, nonce });
        }

        public static byte[] Server(byte[] token, byte[] binding, byte[] nonce, byte[] challenge)
        {
            return Compute(token, ServerLabel, new[] { binding, nonce, challenge });
        }

        public static byte[] Request(
            byte[] token, byte[] binding, byte[] nonce, byte[] challenge, byte[] request)
        {
            return Compute(token, RequestLabel, new[] { binding, nonce, challenge, request });
        }

        /// <summary>
        /// What a pairing request commits to: the name asked for, the profiles asked for in the
        /// order they were sent, and the lifetime asked for as the request wrote it, or nothing when
        /// it asked for none. Both sides build this from the same values, so a request altered on
        /// the way no longer proves itself.
        /// </summary>
        /// <remarks>
        /// A line each. The lifetime is signed for because the operator is shown it, like the
        /// other two, and it is written exactly as it arrived, which the document holds to one
        /// spelling. No line can hold a line break: a name and a profile are refused for one
        /// before this is computed, and a lifetime's pattern allows none.
        /// </remarks>
        public static byte[] Asked(string name, IEnumerable<string> profiles, string lifetime)
        {
            var text = new StringBuilder();
            text.Append(name ?? string.Empty).Append('\n');

            if (profiles != null)
            {
                var first = true;
                foreach (var profile in profiles)
                {
                    if (!first)
                    {
                        text.Append(',');
                    }

                    text.Append(profile);
                    first = false;
                }
            }

            text.Append('\n').Append(lifetime ?? string.Empty);
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        /// <summary>
        /// Each part is length prefixed, so no two different sets of parts can produce the same
        /// bytes to sign. Without it, a name and a profile list could be shuffled across the
        /// boundary between them and still verify.
        /// </summary>
        private static byte[] Compute(byte[] token, string label, byte[][] parts)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            using (var buffer = new MemoryStream())
            {
                var labelBytes = Encoding.ASCII.GetBytes(label);
                buffer.Write(labelBytes, 0, labelBytes.Length);
                buffer.WriteByte(0);

                foreach (var part in parts)
                {
                    if (part == null)
                    {
                        throw new ArgumentException("A proof is computed over every part of it.", nameof(parts));
                    }

                    var length = new[]
                    {
                        (byte)(part.Length >> 24),
                        (byte)(part.Length >> 16),
                        (byte)(part.Length >> 8),
                        (byte)part.Length
                    };

                    buffer.Write(length, 0, length.Length);
                    buffer.Write(part, 0, part.Length);
                }

                using (var hmac = new HMACSHA256(token))
                {
                    return hmac.ComputeHash(buffer.ToArray());
                }
            }
        }
    }
}
