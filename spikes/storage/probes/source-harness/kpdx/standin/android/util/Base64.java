// Stand-in, written for this harness from the documented behavior of Android's Base64: the
// encoder wraps at 76 characters and ends the output with a line break unless NO_WRAP is given,
// writes CR LF with CRLF, leaves out '=' with NO_PADDING and uses '-' and '_' with URL_SAFE; the
// decoder skips every character outside the alphabet and takes the padding as optional.
package android.util;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;

public final class Base64 {
    public static final int DEFAULT = 0;
    public static final int NO_PADDING = 1;
    public static final int NO_WRAP = 2;
    public static final int CRLF = 4;
    public static final int URL_SAFE = 8;
    public static final int NO_CLOSE = 16;

    private Base64() { }

    public static byte[] encode(byte[] input, int flags) { return encode(input, 0, input.length, flags); }

    public static byte[] encode(byte[] input, int offset, int len, int flags) {
        byte[] slice = java.util.Arrays.copyOfRange(input, offset, offset + len);
        java.util.Base64.Encoder enc = (flags & URL_SAFE) != 0 ? java.util.Base64.getUrlEncoder() : java.util.Base64.getEncoder();
        if ((flags & NO_PADDING) != 0) enc = enc.withoutPadding();
        String s = enc.encodeToString(slice);
        if ((flags & NO_WRAP) != 0 || s.isEmpty()) return s.getBytes(StandardCharsets.US_ASCII);
        String nl = (flags & CRLF) != 0 ? "\r\n" : "\n";
        StringBuilder sb = new StringBuilder(s.length() + s.length() / 76 * 2 + 2);
        for (int i = 0; i < s.length(); i += 76) {
            sb.append(s, i, Math.min(s.length(), i + 76)).append(nl);
        }
        return sb.toString().getBytes(StandardCharsets.US_ASCII);
    }

    public static String encodeToString(byte[] input, int flags) {
        return new String(encode(input, flags), StandardCharsets.US_ASCII);
    }

    public static String encodeToString(byte[] input, int offset, int len, int flags) {
        return new String(encode(input, offset, len, flags), StandardCharsets.US_ASCII);
    }

    public static byte[] decode(String str, int flags) { return decode(str.getBytes(StandardCharsets.US_ASCII), flags); }

    public static byte[] decode(byte[] input, int flags) { return decode(input, 0, input.length, flags); }

    public static byte[] decode(byte[] input, int offset, int len, int flags) {
        boolean url = (flags & URL_SAFE) != 0;
        StringBuilder sb = new StringBuilder(len);
        for (int i = offset; i < offset + len; i++) {
            char c = (char) (input[i] & 0xFF);
            boolean alpha = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (alpha) sb.append(c);
            else if (url && c == '-') sb.append('+');
            else if (url && c == '_') sb.append('/');
            else if (!url && (c == '+' || c == '/')) sb.append(c);
            else if (c == '=') break;
        }
        int rem = sb.length() % 4;
        if (rem == 1) throw new IllegalArgumentException("bad base-64");
        if (rem == 2) sb.append("==");
        if (rem == 3) sb.append('=');
        return java.util.Base64.getDecoder().decode(sb.toString());
    }

    static byte[] readAll(java.io.InputStream in) throws java.io.IOException {
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) b.write(buf, 0, n);
        return b.toByteArray();
    }
}
