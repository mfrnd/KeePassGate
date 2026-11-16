// Stand-in, written for this harness: Android's Parcel, with the methods KeePassDX's model classes
// name. No harness step parcels anything, so each method refuses rather than pretends.
package android.os;

import java.io.Serializable;
import java.util.List;

public final class Parcel {
    static UnsupportedOperationException none() {
        return new UnsupportedOperationException("stand-in: nothing is parceled in the harness");
    }

    private Parcel() { }

    public static Parcel obtain() { throw none(); }
    public void recycle() { throw none(); }

    public byte readByte() { throw none(); }
    public void writeByte(byte v) { throw none(); }
    public int readInt() { throw none(); }
    public void writeInt(int v) { throw none(); }
    public long readLong() { throw none(); }
    public void writeLong(long v) { throw none(); }
    public float readFloat() { throw none(); }
    public void writeFloat(float v) { throw none(); }
    public double readDouble() { throw none(); }
    public void writeDouble(double v) { throw none(); }
    public String readString() { throw none(); }
    public void writeString(String v) { throw none(); }
    public byte[] createByteArray() { throw none(); }
    public void readByteArray(byte[] v) { throw none(); }
    public void writeByteArray(byte[] v) { throw none(); }
    public char[] createCharArray() { throw none(); }
    public void readCharArray(char[] v) { throw none(); }
    public void writeCharArray(char[] v) { throw none(); }
    public java.util.ArrayList<String> createStringArrayList() { throw none(); }
    public void readStringList(List<String> list) { throw none(); }
    public void writeStringList(List<String> list) { throw none(); }
    public <T> void readTypedList(List<T> list, Parcelable.Creator<T> c) { throw none(); }
    public <T> java.util.ArrayList<T> createTypedArrayList(Parcelable.Creator<T> c) { throw none(); }
    public <T extends Parcelable> void writeTypedList(List<T> list) { throw none(); }
    public <T extends Parcelable> void writeTypedList(List<T> list, int flags) { throw none(); }
    public <T extends Parcelable> T readParcelable(ClassLoader loader) { throw none(); }
    public <T> T readParcelable(ClassLoader loader, Class<T> clazz) { throw none(); }
    public void writeParcelable(Parcelable p, int flags) { throw none(); }
    public Serializable readSerializable() { throw none(); }
    public <T> T readSerializable(ClassLoader loader, Class<T> clazz) { throw none(); }
    public void writeSerializable(Serializable s) { throw none(); }
    @SuppressWarnings("rawtypes")
    public void readList(List outVal, ClassLoader loader) { throw none(); }
    public <T> void readList(List<? super T> outVal, ClassLoader loader, Class<T> clazz) { throw none(); }
    @SuppressWarnings("rawtypes")
    public void writeList(List val) { throw none(); }
    public int dataSize() { throw none(); }
}
