// Stand-in, written for this harness.
package android.content.pm;

public class Signature {
    private final byte[] bytes;
    public Signature(byte[] bytes) { this.bytes = bytes; }
    public byte[] toByteArray() { return bytes.clone(); }
}
