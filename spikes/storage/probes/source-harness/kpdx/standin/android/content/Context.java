// Stand-in, written for this harness: no application context exists off the device.
package android.content;

import java.io.File;

public abstract class Context {
    public abstract Context getApplicationContext();
    public abstract File getFilesDir();
    public abstract File getNoBackupFilesDir();
    public abstract File getCacheDir();
    public abstract ContentResolver getContentResolver();
}
