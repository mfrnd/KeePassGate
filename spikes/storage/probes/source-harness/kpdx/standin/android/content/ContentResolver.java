// Stand-in, written for this harness: the harness opens files by path, never through a resolver.
package android.content;

import android.net.Uri;
import java.io.FileNotFoundException;
import java.io.InputStream;
import java.io.OutputStream;

public abstract class ContentResolver {
    public abstract InputStream openInputStream(Uri uri) throws FileNotFoundException;
    public abstract OutputStream openOutputStream(Uri uri, String mode) throws FileNotFoundException;
}
