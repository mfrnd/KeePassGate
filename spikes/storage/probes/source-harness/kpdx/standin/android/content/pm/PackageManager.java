// Stand-in, written for this harness: no package manager exists off the device.
package android.content.pm;

import android.content.Intent;
import java.util.List;

public abstract class PackageManager {
    public static final int GET_META_DATA = 128;
    public static final int GET_SIGNING_CERTIFICATES = 134217728;

    public static final class PackageInfoFlags {
        private final long v;
        private PackageInfoFlags(long v) { this.v = v; }
        public static PackageInfoFlags of(long v) { return new PackageInfoFlags(v); }
    }

    public static final class ResolveInfoFlags {
        private final long v;
        private ResolveInfoFlags(long v) { this.v = v; }
        public static ResolveInfoFlags of(long v) { return new ResolveInfoFlags(v); }
    }

    public static class NameNotFoundException extends Exception { }

    public abstract PackageInfo getPackageInfo(String packageName, int flags) throws NameNotFoundException;
    public abstract PackageInfo getPackageInfo(String packageName, PackageInfoFlags flags) throws NameNotFoundException;
    public abstract List<ResolveInfo> queryIntentActivities(Intent intent, int flags);
    public abstract List<ResolveInfo> queryIntentActivities(Intent intent, ResolveInfoFlags flags);
}
