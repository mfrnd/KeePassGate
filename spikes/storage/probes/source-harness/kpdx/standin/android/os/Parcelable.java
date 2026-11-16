// Stand-in, written for this harness: the Android interface KeePassDX's model classes implement so
// that a screen can hand them to another. The parcelize compiler plugin, run by the build as the
// project's Gradle build runs it, writes the members of the classes annotated @Parcelize. Nothing
// is parceled by a harness step.
package android.os;

public interface Parcelable {
    int CONTENTS_FILE_DESCRIPTOR = 1;
    int PARCELABLE_WRITE_RETURN_VALUE = 1;

    int describeContents();

    void writeToParcel(Parcel dest, int flags);

    interface Creator<T> {
        T createFromParcel(Parcel source);
        T[] newArray(int size);
    }

    interface ClassLoaderCreator<T> extends Creator<T> {
        T createFromParcel(Parcel source, ClassLoader loader);
    }
}
