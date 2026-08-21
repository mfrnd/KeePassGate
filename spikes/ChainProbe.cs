using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Does the natural .NET Framework validation actually pin the anchor?
// The red team says no: ExtraStore supplies candidates, AllowUnknownCertificateAuthority tolerates
// an untrusted root, and neither restricts WHICH root is acceptable. Test it with two authorities.
public static class ChainProbe
{
    private static X509Certificate2 Reload(X509Certificate2 c)
    {
        byte[] pfx = c.Export(X509ContentType.Pkcs12, "p");
        return new X509Certificate2(pfx, "p",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private static X509Certificate2 Authority(string cn)
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var r = new CertificateRequest("CN=" + cn, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            return Reload(r.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10),
                                             DateTimeOffset.UtcNow.AddDays(1)));
        }
    }

    private static X509Certificate2 Client(X509Certificate2 ca, string cn)
    {
        using (ECDsa k = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var r = new CertificateRequest("CN=" + cn, k, HashAlgorithmName.SHA256);
            r.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            var oids = new OidCollection();
            oids.Add(new Oid("1.3.6.1.5.5.7.3.2"));
            r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
            byte[] serial = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);
            serial[0] &= 0x7F;
            using (var issued = r.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-5),
                                         DateTimeOffset.UtcNow.AddHours(1), serial))
                return Reload(issued.CopyWithPrivateKey(k));
        }
    }

    /// <summary>The pattern a careful person would write first, and which the finding says is wrong.</summary>
    private static bool NaiveValidate(X509Certificate2 presented, X509Certificate2 ourAuthority)
    {
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(ourAuthority);
            return chain.Build(presented);
        }
    }

    /// <summary>With an explicit anchor pin: compare the root's DER against ours.</summary>
    private static bool PinnedValidate(X509Certificate2 presented, X509Certificate2 ourAuthority)
    {
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(ourAuthority);
            if (!chain.Build(presented)) return false;
            if (chain.ChainElements.Count != 2) return false;
            byte[] root = chain.ChainElements[chain.ChainElements.Count - 1].Certificate.RawData;
            byte[] ours = ourAuthority.RawData;
            if (root.Length != ours.Length) return false;
            int diff = 0;
            for (int i = 0; i < root.Length; i++) diff |= root[i] ^ ours[i];
            return diff == 0;
        }
    }

    public static string Run()
    {
        X509Certificate2 ours = Authority("KPG Pairing Authority");
        X509Certificate2 rogue = Authority("Somebody Else Entirely");

        X509Certificate2 legit = Client(ours, "agent-fictitious");
        X509Certificate2 foreign = Client(rogue, "agent-impostor");

        string r = "";
        r += "  naive  + our client     : " + NaiveValidate(legit, ours) + "   (must be True)\n";
        r += "  naive  + FOREIGN client : " + NaiveValidate(foreign, ours) + "   (must be False)\n";
        r += "  pinned + our client     : " + PinnedValidate(legit, ours) + "   (must be True)\n";
        r += "  pinned + FOREIGN client : " + PinnedValidate(foreign, ours) + "   (must be False)\n";
        r += "\n";
        r += "  X509Certificate2.Thumbprint length: " + legit.Thumbprint.Length +
             " hex chars => " + (legit.Thumbprint.Length * 4) + " bit (SHA-1 is 160)\n";
        return r;
    }
}
