// Stand-in, written for this harness: Android's log, to standard error when KPDX_LOG is set.
package android.util;

public final class Log {
    private static final boolean ON = System.getenv("KPDX_LOG") != null;
    private static int out(String level, String tag, String msg, Throwable t) {
        if (ON) {
            System.err.println("[" + level + "] " + tag + ": " + msg);
            if (t != null) t.printStackTrace();
        }
        return 0;
    }
    public static int v(String tag, String msg) { return out("V", tag, msg, null); }
    public static int v(String tag, String msg, Throwable t) { return out("V", tag, msg, t); }
    public static int d(String tag, String msg) { return out("D", tag, msg, null); }
    public static int d(String tag, String msg, Throwable t) { return out("D", tag, msg, t); }
    public static int i(String tag, String msg) { return out("I", tag, msg, null); }
    public static int i(String tag, String msg, Throwable t) { return out("I", tag, msg, t); }
    public static int w(String tag, String msg) { return out("W", tag, msg, null); }
    public static int w(String tag, String msg, Throwable t) { return out("W", tag, msg, t); }
    public static int w(String tag, Throwable t) { return out("W", tag, "", t); }
    public static int e(String tag, String msg) { return out("E", tag, msg, null); }
    public static int e(String tag, String msg, Throwable t) { return out("E", tag, msg, t); }
}
