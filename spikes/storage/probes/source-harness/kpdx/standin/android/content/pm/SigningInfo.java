// Stand-in, written for this harness: an app's signing certificates, which no harness step asks for.
package android.content.pm;

public class SigningInfo {
    public Signature[] getApkContentsSigners() { return new Signature[0]; }
    public Signature[] getSigningCertificateHistory() { return new Signature[0]; }
    public boolean hasMultipleSigners() { return false; }
}
