using System.Globalization;
using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeePassGate.Testing;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>What came back from the gate of a real KeePass, or why nothing did.</summary>
    internal sealed class LiveReply
    {
        public bool Answered { get; set; }

        public int Status { get; set; }

        public string Body { get; set; }

        /// <summary>The `ETag` the answer carried, exactly as sent, or null when it carried none.</summary>
        public string Tag { get; set; }

        /// <summary>The tag a change answered with in its body, as `etag`, or null when it gave none.</summary>
        public string Written
        {
            get
            {
                if (Body == null || !Body.StartsWith("{", StringComparison.Ordinal))
                {
                    return null;
                }

                var written = Newtonsoft.Json.Linq.JObject.Parse(Body)["etag"];
                return written == null ? null : (string)written;
            }
        }

        public string ServerSubject { get; set; }

        public string Failure { get; set; }
    }

    /// <summary>
    /// An ordinary TLS 1.3 client against a KeePass in another process.
    /// </summary>
    /// <remarks>
    /// It accepts whatever certificate arrives, because what is being tested is the server. A real
    /// client pins the authority it was paired with, and the pairing client here is the one that
    /// shows what that looks like before an anchor exists.
    /// </remarks>
    internal static class LiveGateClient
    {
        public static LiveReply Get(int port, string serverName, X509Certificate2 certificate, string path)
        {
            return Send(port, serverName, certificate, "GET", path, null);
        }

        /// <summary>
        /// One request, with a body when the route takes one.
        /// </summary>
        /// <remarks>
        /// Written by hand rather than through a client library, deliberately: the gate's own
        /// parser is what this exercises, and a library that quietly corrected a request would hide
        /// exactly the thing worth knowing.
        /// </remarks>
        public static LiveReply Send(
            int port,
            string serverName,
            X509Certificate2 certificate,
            string method,
            string path,
            string body)
        {
            return Send(port, serverName, certificate, method, path, body, Answered);
        }

        /// <summary>How long a request the gate answers at once is given to be answered.</summary>
        public static readonly TimeSpan Answered = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How long a request that waits on the operator is given: longer than every wait a
        /// fixture makes for the dialog to appear and for its answer to come back, so the client is
        /// never the one that gives up first. A renewal answered after fifteen seconds was read as
        /// a connection closed without an answer while the gate issued and saved a record for a
        /// certificate nobody received.
        /// </summary>
        public static readonly TimeSpan OnTheOperator = TimeSpan.FromSeconds(150);

        /// <summary>The same, given <paramref name="wait"/> for each read and write.</summary>
        public static LiveReply Send(
            int port,
            string serverName,
            X509Certificate2 certificate,
            string method,
            string path,
            string body,
            TimeSpan wait)
        {
            return Send(port, serverName, certificate, method, path, body, null, wait);
        }

        /// <summary>The same, carrying <paramref name="ifMatch"/> as `If-Match` unless it is null.</summary>
        public static LiveReply Send(
            int port,
            string serverName,
            X509Certificate2 certificate,
            string method,
            string path,
            string body,
            string ifMatch,
            TimeSpan wait)
        {
            var reply = new LiveReply();
            var payload = body == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);

            var head = new StringBuilder();
            head.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
            head.Append("Host: ").Append(serverName).Append("\r\n");

            if (ifMatch != null)
            {
                head.Append("If-Match: ").Append(ifMatch).Append("\r\n");
            }

            if (body != null)
            {
                head.Append("Content-Type: application/json\r\n");
                head.Append("Content-Length: ")
                    .Append(payload.Length.ToString(CultureInfo.InvariantCulture))
                    .Append("\r\n");
            }

            head.Append("\r\n");

            var prefix = Encoding.ASCII.GetBytes(head.ToString());
            var request = new byte[prefix.Length + payload.Length];
            Buffer.BlockCopy(prefix, 0, request, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, request, prefix.Length, payload.Length);

            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", port);
                    tcp.ReceiveTimeout = (int)wait.TotalMilliseconds;
                    tcp.SendTimeout = (int)wait.TotalMilliseconds;

                    var certificates = new X509Certificate2Collection();
                    if (certificate != null)
                    {
                        certificates.Add(certificate);
                    }

                    using (var tls = new SslStream(
                        tcp.GetStream(),
                        false,
                        delegate(object sender, X509Certificate presented, X509Chain chain, SslPolicyErrors errors)
                        {
                            if (presented != null)
                            {
                                reply.ServerSubject = presented.Subject;
                            }

                            return true;
                        },
                        delegate { return certificate; }))
                    {
                        tls.AuthenticateAsClient(serverName, certificates, SslProtocols.Tls13, false);
                        tls.Write(request, 0, request.Length);
                        tls.Flush();

                        var raw = HttpAnswer.ReadToEnd(tls);
                        if (raw.Length == 0)
                        {
                            reply.Failure = "the connection was closed without an answer";
                            return reply;
                        }

                        reply.Answered = true;
                        reply.Status = int.Parse(
                            raw.Substring(9, 3), CultureInfo.InvariantCulture);
                        var blank = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        reply.Body = blank < 0 ? string.Empty : raw.Substring(blank + 4);
                        reply.Tag = Field(blank < 0 ? raw : raw.Substring(0, blank), "ETag");
                    }
                }
            }
            catch (Exception error)
            {
                reply.Failure = error.GetType().Name + ": " + error.Message;
            }

            return reply;
        }

        /// <summary>One header field's value out of an answer's head, or null when it has none.</summary>
        private static string Field(string head, string name)
        {
            foreach (var line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                var colon = line.IndexOf(':');
                if (colon > 0
                    && string.Equals(line.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(colon + 1).Trim();
                }
            }

            return null;
        }
    }
}
