// Stand-in, written for this harness: Android's Intent, as a map of extras.
package android.content;

import android.os.Parcelable;
import java.io.Serializable;
import java.util.HashMap;

public class Intent {
    private final HashMap<String, Object> extras = new HashMap<>();
    public Parcelable getParcelableExtra(String key) { return (Parcelable) extras.get(key); }
    public <T> T getParcelableExtra(String key, Class<T> clazz) { return clazz.cast(extras.get(key)); }
    public Serializable getSerializableExtra(String key) { return (Serializable) extras.get(key); }
    public <T extends Serializable> T getSerializableExtra(String key, Class<T> clazz) { return clazz.cast(extras.get(key)); }
    public Parcelable[] getParcelableArrayExtra(String key) { return (Parcelable[]) extras.get(key); }
    @SuppressWarnings("unchecked")
    public <T> T[] getParcelableArrayExtra(String key, Class<T> clazz) { return (T[]) extras.get(key); }
    public String getStringExtra(String key) { return (String) extras.get(key); }
    public Intent putExtra(String key, Parcelable[] value) { extras.put(key, value); return this; }
    public Intent putExtra(String key, Parcelable value) { extras.put(key, value); return this; }
    public Intent putExtra(String key, String value) { extras.put(key, value); return this; }
}
