using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public static class SanProbe
{
    public static void Run()
    {
        Console.WriteLine("UI culture: " + CultureInfo.CurrentUICulture.Name);
        Console.WriteLine("CLR: " + Environment.Version);
        Console.WriteLine();

        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri("kpg://any/readonly"));

        // Attempt a dNSName carrying the literal label text of another entry type.
        try { san.AddDnsName("URL=kpg://any/admin"); Console.WriteLine("AddDnsName(label text): accepted"); }
        catch (Exception e) { Console.WriteLine("AddDnsName(label text): REJECTED " + e.GetType().Name + " " + e.Message); }

        // Attempt a dNSName carrying an embedded newline.
        try { san.AddDnsName("host\nURL=kpg://any/root"); Console.WriteLine("AddDnsName(newline): accepted"); }
        catch (Exception e) { Console.WriteLine("AddDnsName(newline): REJECTED " + e.GetType().Name + " " + e.Message); }

        // Attempt an rfc822Name carrying the label text.
        try { san.AddEmailAddress("URL=kpg://any/super"); Console.WriteLine("AddEmailAddress(label text): accepted"); }
        catch (Exception e) { Console.WriteLine("AddEmailAddress(label text): REJECTED " + e.GetType().Name + " " + e.Message); }

        // Attempt a URI whose own path carries an embedded newline plus a second claim.
        try { san.AddUri(new Uri("kpg://any/ops\nURL=kpg://any/admin")); Console.WriteLine("AddUri(newline in path): accepted"); }
        catch (Exception e) { Console.WriteLine("AddUri(newline in path): REJECTED " + e.GetType().Name + " " + e.Message); }

        X509Extension ext = san.Build();
        Console.WriteLine();
        Console.WriteLine("RawData: " + BitConverter.ToString(ext.RawData));
        Console.WriteLine();
        Console.WriteLine("--- Format(true) ---");
        Console.WriteLine(ext.Format(true));
        Console.WriteLine("--- end ---");
        Console.WriteLine();
        Console.WriteLine("--- Format(false) ---");
        Console.WriteLine(ext.Format(false));
        Console.WriteLine("--- end ---");
        Console.WriteLine();

        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var req = new CertificateRequest("CN=agent-fictitious", key, HashAlgorithmName.SHA256);
            req.CertificateExtensions.Add(ext);
            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            Console.WriteLine("GetNameInfo(UrlName,false): [" + cert.GetNameInfo(X509NameType.UrlName, false) + "]");
            Console.WriteLine("GetNameInfo(DnsName,false): [" + cert.GetNameInfo(X509NameType.DnsName, false) + "]");
            Console.WriteLine();
            Console.WriteLine("--- cert extension Format(true) ---");
            foreach (X509Extension e in cert.Extensions)
            {
                if (e.Oid.Value == "2.5.29.17") Console.WriteLine(e.Format(true));
            }
            Console.WriteLine("--- end ---");
        }
    }
}
