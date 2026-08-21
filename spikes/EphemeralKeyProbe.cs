using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

/// <summary>
/// Can a private key stay out of the user's profile and still be used?
///
/// The design says the authority lives inside the database and nowhere else. That is a claim about
/// the disk: importing a PKCS#12 on .NET Framework writes a key file into
/// %APPDATA%\Microsoft\Crypto\Keys unless EphemeralKeySet is asked for.
///
/// **The order of the cases below is the point.** An earlier version of this probe imported the
/// same PKCS#12 with a persisted container first and an ephemeral one second, and concluded that
/// ephemeral keys serve TLS. They do not. The first import had left a key Windows then found, so
/// the probe was measuring its own side effect. Case A is therefore first and stands alone.
/// </summary>
public static class EphemeralKeyProbe
{
    private const X509KeyStorageFlags Ephemeral =
        X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;

    private const X509KeyStorageFlags Persisted = X509KeyStorageFlags.Exportable;

    public static string Run()
    {
        var report = new System.Text.StringBuilder();

        report.AppendLine("A. ephemeral only, nothing imported before it");
        report.AppendLine("     server: " + ServerHandshake(Ephemeral));
        report.AppendLine("     client: " + ClientHandshake(Ephemeral));
        report.AppendLine("     signs:  " + CanSign(Ephemeral));

        report.AppendLine("B. persisted only");
        report.AppendLine("     server: " + ServerHandshake(Persisted));
        report.AppendLine("     client: " + ClientHandshake(Persisted));

        report.AppendLine("C. persisted first, then ephemeral, same PKCS#12");
        report.AppendLine("     " + PersistedThenEphemeral());

        report.AppendLine("D. what each import costs on disk");
        report.AppendLine("     " + KeyFiles());

        return report.ToString();
    }

    /// <summary>The trap, kept because it is the reason this probe exists at all.</summary>
    private static string PersistedThenEphemeral()
    {
        var pkcs12 = NewLeaf();

        var persisted = new X509Certificate2(pkcs12, (string)null, Persisted);
        var first = Handshake(persisted);
        persisted.Reset();

        var ephemeral = new X509Certificate2(pkcs12, (string)null, Ephemeral);
        var second = Handshake(ephemeral);
        ephemeral.Reset();

        return "persisted -> " + first + ", then ephemeral -> " + second +
               "  (the second borrows what the first left behind)";
    }

    private static string KeyFiles()
    {
        var pkcs12 = NewLeaf();

        var before = KeyFileCount();
        var ephemeral = new X509Certificate2(pkcs12, (string)null, Ephemeral);
        var withEphemeral = KeyFileCount();
        ephemeral.Reset();

        var persisted = new X509Certificate2(pkcs12, (string)null, Persisted);
        var withPersisted = KeyFileCount();
        persisted.Reset();
        var afterwards = KeyFileCount();

        return "before " + before + ", ephemeral " + withEphemeral +
               ", persisted " + withPersisted + ", after dispose " + afterwards;
    }

    private static string ServerHandshake(X509KeyStorageFlags flags)
    {
        var certificate = new X509Certificate2(NewLeaf(), (string)null, flags);
        try
        {
            return Handshake(certificate);
        }
        finally
        {
            certificate.Reset();
        }
    }

    private static string CanSign(X509KeyStorageFlags flags)
    {
        var certificate = new X509Certificate2(NewLeaf(), (string)null, flags);
        try
        {
            var key = certificate.GetECDsaPrivateKey();
            if (key == null)
            {
                return "no private key";
            }

            var signature = key.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256);
            return certificate.GetECDsaPublicKey().VerifyData(new byte[] { 1, 2, 3 }, signature, HashAlgorithmName.SHA256)
                ? "yes"
                : "signature did not verify";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
        finally
        {
            certificate.Reset();
        }
    }

    /// <summary>Does a client with this kind of key get its certificate onto the wire?</summary>
    private static string ClientHandshake(X509KeyStorageFlags clientFlags)
    {
        var server = new X509Certificate2(NewLeaf(), (string)null, Persisted);
        var client = new X509Certificate2(NewLeaf(), (string)null, clientFlags);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var seen = "server never ran";

        var thread = new Thread(delegate()
        {
            try
            {
                using (var accepted = listener.AcceptTcpClient())
                using (var stream = new SslStream(accepted.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsServer(server, true, SslProtocols.Tls13, false);
                    seen = stream.RemoteCertificate == null ? "no client certificate" : "client certificate seen";
                }
            }
            catch (Exception ex)
            {
                seen = "handshake failed (" + ex.GetType().Name + ")";
            }
        });

        thread.IsBackground = true;
        thread.Start();

        try
        {
            using (var tcp = new TcpClient("127.0.0.1", port))
            using (var stream = new SslStream(
                tcp.GetStream(), false, delegate { return true; }, delegate { return client; }))
            {
                var certificates = new X509Certificate2Collection();
                certificates.Add(client);
                stream.AuthenticateAsClient("probe.kpg", certificates, SslProtocols.Tls13, false);
                stream.Write(new byte[] { 1 }, 0, 1);
                stream.Flush();
            }
        }
        catch (Exception)
        {
        }

        thread.Join(10000);
        listener.Stop();
        server.Reset();
        client.Reset();
        return seen;
    }

    private static string Handshake(X509Certificate2 serverCertificate)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var result = "not reached";

        var server = new Thread(delegate()
        {
            try
            {
                using (var accepted = listener.AcceptTcpClient())
                using (var stream = new SslStream(accepted.GetStream(), false))
                {
                    stream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls13, false);
                    result = "ok, " + stream.SslProtocol;
                }
            }
            catch (Exception ex)
            {
                result = ex.GetType().Name + ": " + ex.Message;
            }
        });

        server.IsBackground = true;
        server.Start();

        try
        {
            using (var client = new TcpClient("127.0.0.1", port))
            using (var stream = new SslStream(client.GetStream(), false, delegate { return true; }))
            {
                stream.AuthenticateAsClient("probe.kpg", null, SslProtocols.Tls13, false);
            }
        }
        catch (Exception)
        {
        }

        server.Join(10000);
        listener.Stop();
        return result;
    }

    private static int KeyFileCount()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft\\Crypto\\Keys");
            return Directory.Exists(directory) ? Directory.GetFiles(directory).Length : -1;
        }
        catch (Exception)
        {
            return -2;
        }
    }

    /// <summary>A fresh authority and one leaf under it, as PKCS#12 with no password.</summary>
    private static byte[] NewLeaf()
    {
        var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest(
            new X500DistinguishedName("CN=probe authority"), caKey, HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));

        var now = DateTimeOffset.UtcNow;
        var ca = caRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));

        var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest(
            new X500DistinguishedName("CN=probe leaf"), leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2") }, false));

        var serial = new byte[16];
        using (var random = RandomNumberGenerator.Create())
        {
            random.GetBytes(serial);
        }

        serial[0] &= 0x7F;

        var leaf = leafRequest.Create(ca, now.AddMinutes(-5), now.AddYears(1), serial);
        return leaf.CopyWithPrivateKey(leafKey).Export(X509ContentType.Pkcs12);
    }
}
