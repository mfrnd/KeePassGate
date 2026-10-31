using System.Globalization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Transport;

namespace KeePassGate.Testing
{
    /// <summary>What came back from one pairing call.</summary>
    public sealed class PairingReply
    {
        public bool Answered { get; set; }

        public int Status { get; set; }

        public string Body { get; set; }

        public JsonBody Json { get; set; }

        public string Failure { get; set; }

        /// <summary>
        /// Whether the server's own proof recomputed. False on `/pair/hello` means an impostor, and
        /// a real client aborts there rather than carrying on.
        /// </summary>
        public bool ServerProved { get; set; }

        public override string ToString()
        {
            return Answered ? Status + " " + Body : "no answer: " + Failure;
        }
    }

    /// <summary>
    /// The client half of pairing, written against the same proofs as the server.
    /// </summary>
    /// <remarks>
    /// It exists so that the flow is tested as a conversation rather than as two handlers. Both
    /// halves compute their proofs from <see cref="PairingProof"/>, so the test does not prove that
    /// the constructions are the ones the document describes: the unit tests over the proofs
    /// themselves do that. What this proves is that a client holding nothing but a token and a port
    /// reaches a certificate, and that everything short of that is refused.
    ///
    /// It is a client, so it does what a real one has to do: no trust anchor before pairing, the
    /// server's certificate accepted on the strength of the proof alone, and a fresh name on every
    /// connection because the transport answers one request each.
    /// </remarks>
    public sealed class PairingClient
    {
        private readonly int _port;
        private readonly byte[] _token;

        private byte[] _challenge;
        private byte[] _binding;
        private byte[] _nonce;

        public PairingClient(int port, string tokenText)
        {
            _port = port;
            _token = PairingToken.Decode(tokenText);
            if (_token == null)
            {
                throw new ArgumentException("That is not a pairing token: " + tokenText, nameof(tokenText));
            }

            Timeout = 30000;
        }

        /// <summary>How long to wait for an answer. The approval half of pairing is slow on purpose.</summary>
        public int Timeout { get; set; }

        /// <summary>Sends this instead of the real proof, which is how a refusal is tested.</summary>
        public byte[] WrongProof { get; set; }

        /// <summary>What the server issued, once a request has been approved.</summary>
        public X509Certificate2 Identity { get; private set; }

        /// <summary>The authority to pin from now on.</summary>
        public X509Certificate2 Authority { get; private set; }

        /// <summary>The gate key every connection after pairing is named from.</summary>
        public byte[] GateKey { get; private set; }

        public IReadOnlyList<string> Profiles { get; private set; }

        /// <summary>Phase 3. The client speaks first, and only then is the server's proof checked.</summary>
        public PairingReply Hello()
        {
            byte[] nonce;
            var name = NewName(out nonce);
            byte[] serverDer = null;

            var reply = Send(name, PairingHandler.HelloPath, der =>
            {
                serverDer = der;
                var binding = PairingProof.Binding(der);
                var proof = WrongProof ?? PairingProof.Client(_token, binding, nonce);
                return Bodies.Of(("proof", Convert.ToBase64String(proof)));
            });

            if (!reply.Answered || reply.Status != 200 || reply.Json == null)
            {
                return reply;
            }

            var challenge = Base64(reply.Json.Text("challenge"));
            var offered = Base64(reply.Json.Text("proof"));
            if (challenge == null || offered == null)
            {
                reply.Failure = "the hello came back without a challenge and a proof";
                return reply;
            }

            var expected = PairingProof.Server(_token, PairingProof.Binding(serverDer), nonce, challenge);
            reply.ServerProved = FixedTime.Equals(expected, offered);

            if (reply.ServerProved)
            {
                _challenge = challenge;
                _binding = PairingProof.Binding(serverDer);
                _nonce = nonce;
            }

            return reply;
        }

        /// <summary>
        /// How long the next request asks its certificate to last, as the wire writes it, or null
        /// to ask for nothing. Sent as it is and signed for as it is, so the screenshot fixture
        /// asks for exactly the lifetime its picture shows.
        /// </summary>
        public string Lifetime { get; set; }

        /// <summary>
        /// Phase 4, on a new connection, which is why the challenge carries the binding and the
        /// nonce of the hello rather than of this one.
        /// </summary>
        public PairingReply Request(string name, params string[] profiles)
        {
            if (_challenge == null)
            {
                throw new InvalidOperationException("Say hello first.");
            }

            var asked = profiles ?? Array.Empty<string>();

            byte[] unused;
            var serverName = NewName(out unused);

            var reply = Send(serverName, PairingHandler.RequestPath, der =>
            {
                var proof = WrongProof ?? PairingProof.Request(
                    _token, _binding, _nonce, _challenge, PairingProof.Asked(name, asked, Lifetime));

                var body = new StringBuilder();
                body.Append("{\"name\":\"").Append(name).Append("\",\"profiles\":[");
                for (var i = 0; i < asked.Length; i++)
                {
                    if (i > 0)
                    {
                        body.Append(',');
                    }

                    body.Append('"').Append(asked[i]).Append('"');
                }

                body.Append(']');
                if (Lifetime != null)
                {
                    body.Append(",\"lifetime\":\"").Append(Lifetime).Append('"');
                }

                body.Append(",\"proof\":\"").Append(Convert.ToBase64String(proof)).Append("\"}");
                return body.ToString();
            });

            if (!reply.Answered || reply.Status != 200 || reply.Json == null)
            {
                return reply;
            }

            var identity = reply.Json.Text("identity");
            var authority = Base64(reply.Json.Text("authority"));
            var gateKey = Base64(reply.Json.Text("gateKey"));

            if (identity == null || authority == null || gateKey == null)
            {
                reply.Failure = "the bundle was missing something";
                return reply;
            }

            // **Never `PersistKeySet`.** That flag writes the private key into a container under the
            // user's profile, and nothing takes it away again: every pairing test in every run
            // would leave one behind for good, in a repository whose whole point is that no key
            // this plugin handles reaches the disk. Nothing here needs it, because the certificate
            // is used for one client handshake and then dropped.
            using (PrivateKeyFiles.Lock())
            {
                Identity = new X509Certificate2(
                    Convert.FromBase64String(identity),
                    (string)null,
                    X509KeyStorageFlags.Exportable);
            }

            Authority = new X509Certificate2(authority);
            GateKey = gateKey;
            Profiles = reply.Json.Array("profiles") ?? Array.Empty<string>();
            return reply;
        }

        /// <summary>A name for this connection, built from the token exactly as the gate expects.</summary>
        public string NewName(out byte[] nonce)
        {
            nonce = new byte[SniName.NonceLength];
            using (var random = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            return SniName.For(_token, nonce);
        }

        private PairingReply Send(string serverName, string path, Func<byte[], string> body)
        {
            var reply = new PairingReply();

            var thrown = Thrown.By(() =>
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", _port);
                    tcp.ReceiveTimeout = Timeout;
                    tcp.SendTimeout = Timeout;

                    byte[] serverDer = null;

                    using (var tls = new SslStream(
                        tcp.GetStream(),
                        false,
                        delegate(object sender, X509Certificate presented, X509Chain chain, SslPolicyErrors errors)
                        {
                            // No anchor exists yet, so there is nothing to check here and the
                            // proof is what decides. This is exactly the branch `PAIRING.md`
                            // §304 describes for an unpaired client.
                            if (presented != null)
                            {
                                using (var certificate = new X509Certificate2(presented))
                                {
                                    serverDer = certificate.RawData;
                                }
                            }

                            return true;
                        },
                        delegate { return null; }))
                    {
                        tls.AuthenticateAsClient(
                            serverName, new X509Certificate2Collection(), SslProtocols.Tls13, false);

                        var json = body(serverDer);
                        var payload = Encoding.UTF8.GetBytes(json);

                        var head = new StringBuilder();
                        head.Append("POST ").Append(path).Append(" HTTP/1.1\r\n");
                        head.Append("Host: ").Append(serverName).Append("\r\n");
                        head.Append("Content-Type: application/json\r\n");
                        head.Append("Content-Length: ").Append(payload.Length).Append("\r\n\r\n");

                        var headBytes = Encoding.ASCII.GetBytes(head.ToString());
                        tls.Write(headBytes, 0, headBytes.Length);
                        tls.Write(payload, 0, payload.Length);
                        tls.Flush();

                        var raw = HttpAnswer.ReadToEnd(tls);
                        if (raw.Length == 0)
                        {
                            reply.Failure = "the connection was closed without an answer";
                            return;
                        }

                        // Read before it is believed. A truncated answer, from the gate dying part
                        // way through a write or a connection budget closing the socket, used to
                        // come back as an answered reply carrying a status of zero, so every
                        // assertion about it read "expected 200, was 0" over an empty body instead
                        // of naming the truncation, and one asserting that a reply arrived passed
                        // on a conversation that produced none.
                        if (raw.Length < 12 || !raw.StartsWith("HTTP/1.", StringComparison.Ordinal))
                        {
                            reply.Failure = "the answer is not an HTTP response: " + Shorten(raw);
                            return;
                        }

                        int status;
                        if (!int.TryParse(
                                raw.Substring(9, 3),
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out status))
                        {
                            reply.Failure = "the answer carries no status: " + Shorten(raw);
                            return;
                        }

                        var blank = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        reply.Body = blank < 0 ? string.Empty : raw.Substring(blank + 4);
                        reply.Json = JsonBody.Read(reply.Body);

                        // Last, so that everything an assertion reads off this reply is already on
                        // it by the time it says an answer arrived.
                        reply.Status = status;
                        reply.Answered = true;
                    }
                }
            });

            if (thrown != null)
            {
                reply.Failure = thrown.GetType().Name + ": " + thrown.Message;
            }

            return reply;
        }

        /// <summary>Enough of an unusable answer to say what arrived, without pasting a page of it.</summary>
        private static string Shorten(string raw)
        {
            var oneLine = raw.Replace('\r', ' ').Replace('\n', ' ');
            return oneLine.Length <= 60 ? oneLine : oneLine.Substring(0, 60) + "...";
        }

        private static byte[] Base64(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
