// Stand-in, written for this harness: the parts of Android's Uri the database module names, over
// java.net.URI. Used for OTP links and file names, never by a harness step.
package android.net;

import java.net.URI;
import java.net.URLDecoder;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;

public class Uri implements android.os.Parcelable {
    private final String text;
    private final URI uri;
    private Uri(String text) {
        this.text = text;
        URI u;
        try { u = new URI(text); } catch (Exception e) { u = null; }
        this.uri = u;
    }
    public static Uri parse(String s) { return new Uri(s); }
    public static Uri fromFile(java.io.File f) { return new Uri(f.toURI().toString()); }
    public static String decode(String s) { return s == null ? null : URLDecoder.decode(s.replace("+", "%2B"), StandardCharsets.UTF_8); }
    public static String encode(String s) { return s == null ? null : URLEncoder.encode(s, StandardCharsets.UTF_8).replace("+", "%20"); }
    public String getScheme() { return uri == null ? null : uri.getScheme(); }
    public String getAuthority() { return uri == null ? null : uri.getAuthority(); }
    public String getHost() { return uri == null ? null : uri.getHost(); }
    public String getPath() { return uri == null ? null : uri.getPath(); }
    public String getLastPathSegment() {
        String p = getPath();
        if (p == null) return null;
        int i = p.lastIndexOf('/');
        return i < 0 ? p : p.substring(i + 1);
    }
    public String getQueryParameter(String key) {
        if (uri == null || uri.getRawQuery() == null) return null;
        for (String kv : uri.getRawQuery().split("&")) {
            int i = kv.indexOf('=');
            String k = i < 0 ? kv : kv.substring(0, i);
            if (decode(k).equals(key)) return i < 0 ? "" : decode(kv.substring(i + 1));
        }
        return null;
    }
    @Override public int describeContents() { return 0; }
    @Override public void writeToParcel(android.os.Parcel dest, int flags) { dest.writeString(text); }
    @Override public String toString() { return text; }
    @Override public boolean equals(Object o) { return o instanceof Uri && text.equals(((Uri) o).text); }
    @Override public int hashCode() { return text.hashCode(); }
}
