using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MonoProbe
{
    /// <summary>
    /// What `issuance` would have measured before it reached `CertificateRequest`, asked without
    /// naming that type, so that a runtime missing it still answers the rest. Whether the types
    /// the plugin's issuance names exist at all is asked by name through reflection, which answers
    /// absent rather than failing to compile the method that asks.
    /// </summary>
    internal static class EcdsaProbe
    {
        internal static void Run()
        {
            // Fully qualified, because .NET Framework binds nothing from the GAC by a partial name.
            Report.Measure("ecdsa.type-certificate-request", () => Present(
                "System.Security.Cryptography.X509Certificates.CertificateRequest, " + Framework("System.Core")));
            Report.Measure("ecdsa.type-certificate-request-in-system", () => Present(
                "System.Security.Cryptography.X509Certificates.CertificateRequest, " + Framework("System")));
            Report.Measure("ecdsa.method-copy-with-private-key", () =>
                typeof(ECDsaCertificateExtensions).GetMethod("CopyWithPrivateKey") != null);

            var key = Report.Measure("ecdsa.create-p256", () => ECDsa.Create(ECCurve.NamedCurves.nistP256),
                created => created.GetType().FullName);
            Report.Measure("ecdsa.create-default", () => ECDsa.Create(), created => created.GetType().FullName);
            if (key == null)
            {
                return;
            }

            Report.Measure("ecdsa.key-size", () => key.KeySize);
            Report.Measure("ecdsa.export-parameters", () => key.ExportParameters(false),
                parameters => "curve " + parameters.Curve.Oid.FriendlyName + ", x " + parameters.Q.X.Length + " bytes");
            Report.Measure("ecdsa.sign-verify", () =>
            {
                var data = Encoding.UTF8.GetBytes("probe");
                var signature = key.SignData(data, HashAlgorithmName.SHA256);
                return "signature " + signature.Length + " bytes, verifies " + key.VerifyData(data, signature, HashAlgorithmName.SHA256);
            });
        }

        private static string Present(string name)
        {
            return Type.GetType(name, false) == null ? "absent" : "present";
        }

        private static string Framework(string assembly)
        {
            return assembly + ", Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        }
    }
}
