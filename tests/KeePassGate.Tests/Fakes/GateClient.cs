using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>What came back, or why nothing did.</summary>
    internal sealed class GateReply
    {
        public bool Connected { get; set; }

        /// <summary>False when the handshake failed or the connection was dropped without a word.</summary>
        public bool Answered { get; set; }

        public int Status { get; set; }

        public string Body { get; set; }

        public string Raw { get; set; }

        /// <summary>Why it went wrong, for a failure message worth reading.</summary>
        public string Failure { get; set; }

        /// <summary>The certificate the server presented, which is how a test checks SNI selection.</summary>
        public X509Certificate2 ServerCertificate { get; set; }

        public override string ToString()
        {
            return Answered ? Status + " " + Body : "no answer: " + Failure;
        }
    }

    /// <summary>
    /// A client that speaks the transport the way a real one will: TLS 1.3, a name the gate can
    /// resolve, and a certificate its authority issued.
    /// </summary>
    internal static class GateClient
    {
        public static GateReply Get(int port, string serverName, X509Certificate2 certificate, string path)
        {
            return Send(port, serverName, certificate, "GET " + path + " HTTP/1.1\r\nHost: " + serverName + "\r\n\r\n");
        }

        public static GateReply Send(int port, string serverName, X509Certificate2 certificate, string request)
        {
            return Send(port, serverName, certificate, Encoding.ASCII.GetBytes(request));
        }

        public static GateReply Send(int port, string serverName, X509Certificate2 certificate, byte[] request)
        {
            var reply = new GateReply();

            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", port);
                    tcp.ReceiveTimeout = 10000;
                    tcp.SendTimeout = 10000;
                    reply.Connected = true;

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
                            // A client with no anchor in any machine store always sees errors here.
                            // What it does about that is the client library's business, and this
                            // one only records what it was shown.
                            if (presented != null)
                            {
                                reply.ServerCertificate = new X509Certificate2(presented);
                            }

                            return true;
                        },
                        delegate { return certificate; }))
                    {
                        tls.AuthenticateAsClient(serverName, certificates, SslProtocols.Tls13, false);

                        tls.Write(request, 0, request.Length);
                        tls.Flush();

                        reply.Raw = ReadToEnd(tls);
                    }
                }
            }
            catch (Exception error)
            {
                reply.Failure = error.GetType().Name + ": " + error.Message;
                return reply;
            }

            if (string.IsNullOrEmpty(reply.Raw))
            {
                reply.Failure = "the connection was closed without an answer";
                return reply;
            }

            reply.Answered = true;
            reply.Status = StatusOf(reply.Raw);
            var blank = reply.Raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            reply.Body = blank < 0 ? string.Empty : reply.Raw.Substring(blank + 4);
            return reply;
        }

        /// <summary>Sends bytes that are not TLS at all, to see what the gate does with them.</summary>
        public static GateReply SendRaw(int port, byte[] bytes)
        {
            var reply = new GateReply();

            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", port);
                    tcp.ReceiveTimeout = 5000;
                    reply.Connected = true;

                    var stream = tcp.GetStream();
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();

                    reply.Raw = ReadToEnd(stream);
                    reply.Answered = !string.IsNullOrEmpty(reply.Raw);
                }
            }
            catch (Exception error)
            {
                reply.Failure = error.GetType().Name + ": " + error.Message;
            }

            return reply;
        }

        private static string ReadToEnd(Stream stream)
        {
            // Gathered whole and decoded once. Decoding each read on its own split any character
            // whose bytes straddled a 4096 byte boundary into two wrong ones, which is the same
            // fault the body reader was fixed for and just as invisible until a name has an
            // accent in it.
            var buffer = new byte[4096];
            var body = new MemoryStream();

            while (true)
            {
                int read;
                try
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                body.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(body.ToArray());
        }

        private static int StatusOf(string response)
        {
            if (!response.StartsWith("HTTP/1.1 ", StringComparison.Ordinal) || response.Length < 12)
            {
                return 0;
            }

            int status;
            return int.TryParse(response.Substring(9, 3), out status) ? status : 0;
        }
    }
}
