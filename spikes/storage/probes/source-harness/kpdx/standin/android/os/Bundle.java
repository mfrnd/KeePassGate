// Stand-in, written for this harness: Android's Bundle, a map of named values. Kept as a map.
package android.os;

import java.io.Serializable;
import java.util.HashMap;

public final class Bundle {
    private final HashMap<String, Object> map = new HashMap<>();
    public Parcelable getParcelable(String key) { return (Parcelable) map.get(key); }
    public <T> T getParcelable(String key, Class<T> clazz) { return clazz.cast(map.get(key)); }
    public Serializable getSerializable(String key) { return (Serializable) map.get(key); }
    public <T extends Serializable> T getSerializable(String key, Class<T> clazz) { return clazz.cast(map.get(key)); }
    public Parcelable[] getParcelableArray(String key) { return (Parcelable[]) map.get(key); }
    @SuppressWarnings("unchecked")
    public <T> T[] getParcelableArray(String key, Class<T> clazz) { return (T[]) map.get(key); }
    public void putParcelableArray(String key, Parcelable[] value) { map.put(key, value); }
    public void putParcelable(String key, Parcelable value) { map.put(key, value); }
    public void putString(String key, String value) { map.put(key, value); }
    public String getString(String key) { return (String) map.get(key); }
    public void clear() { map.clear(); }
}
