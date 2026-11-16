// Stand-in, written for this harness: encodes everything written to it with Base64.encode and the
// same flags when it is closed, then closes the stream under it unless NO_CLOSE is given.
package android.util;

import java.io.ByteArrayOutputStream;
import java.io.FilterOutputStream;
import java.io.IOException;
import java.io.OutputStream;

public class Base64OutputStream extends FilterOutputStream {
    private final int flags;
    private final ByteArrayOutputStream buffer = new ByteArrayOutputStream();
    private boolean closed;

    public Base64OutputStream(OutputStream out, int flags) {
        super(out);
        this.flags = flags;
    }

    @Override public void write(int b) { buffer.write(b); }
    @Override public void write(byte[] b, int off, int len) { buffer.write(b, off, len); }
    @Override public void flush() { }

    @Override public void close() throws IOException {
        if (closed) return;
        closed = true;
        out.write(Base64.encode(buffer.toByteArray(), flags));
        out.flush();
        if ((flags & Base64.NO_CLOSE) == 0) out.close();
    }
}
