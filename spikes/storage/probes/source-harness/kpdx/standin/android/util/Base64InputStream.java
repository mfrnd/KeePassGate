// Stand-in, written for this harness: decodes the whole stream under it with Base64.decode and the
// same flags at the first read.
package android.util;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;

public class Base64InputStream extends InputStream {
    private final InputStream in;
    private final int flags;
    private ByteArrayInputStream decoded;

    public Base64InputStream(InputStream in, int flags) {
        this.in = in;
        this.flags = flags;
    }

    private ByteArrayInputStream d() throws IOException {
        if (decoded == null) decoded = new ByteArrayInputStream(Base64.decode(Base64.readAll(in), flags));
        return decoded;
    }

    @Override public int read() throws IOException { return d().read(); }
    @Override public int read(byte[] b, int off, int len) throws IOException { return d().read(b, off, len); }
    @Override public int available() throws IOException { return d().available(); }
    @Override public void close() throws IOException { in.close(); }
}
