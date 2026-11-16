// Stand-in, written for this harness: Android's Color, for the parts the database module names,
// with the same packing of an ARGB integer.
package android.graphics;

public final class Color {
    public static final int BLACK = 0xFF000000;
    public static final int WHITE = 0xFFFFFFFF;
    public static final int TRANSPARENT = 0;
    public static int alpha(int c) { return c >>> 24; }
    public static int red(int c) { return (c >> 16) & 0xFF; }
    public static int green(int c) { return (c >> 8) & 0xFF; }
    public static int blue(int c) { return c & 0xFF; }
    public static int rgb(int r, int g, int b) { return 0xFF000000 | (r << 16) | (g << 8) | b; }
    public static int argb(int a, int r, int g, int b) { return (a << 24) | (r << 16) | (g << 8) | b; }
    public static int parseColor(String s) {
        if (s.charAt(0) == '#') {
            long color = Long.parseLong(s.substring(1), 16);
            if (s.length() == 7) color |= 0x00000000ff000000L;
            else if (s.length() != 9) throw new IllegalArgumentException("Unknown color");
            return (int) color;
        }
        switch (s.toLowerCase(java.util.Locale.ROOT)) {
            case "black": return BLACK;
            case "white": return WHITE;
            case "red": return 0xFFFF0000;
            case "green": return 0xFF00FF00;
            case "blue": return 0xFF0000FF;
            case "transparent": return TRANSPARENT;
        }
        throw new IllegalArgumentException("Unknown color");
    }
}
