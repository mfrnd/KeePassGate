// Stand-in, written for this harness: no resources exist off the device.
package android.content.res;

public class Resources {
    public String getString(int id) { throw new UnsupportedOperationException("stand-in: no resources"); }
    public String getString(int id, Object... args) { throw new UnsupportedOperationException("stand-in: no resources"); }
    public static class NotFoundException extends RuntimeException { }
}
