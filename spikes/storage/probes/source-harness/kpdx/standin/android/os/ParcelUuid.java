// Stand-in, written for this harness: Android's ParcelUuid, a UUID that can be parceled.
package android.os;

import java.util.UUID;

public final class ParcelUuid implements Parcelable {
    private final UUID uuid;
    public ParcelUuid(UUID uuid) { this.uuid = uuid; }
    public UUID getUuid() { return uuid; }
    public static ParcelUuid fromString(String s) { return new ParcelUuid(UUID.fromString(s)); }
    @Override public boolean equals(Object o) { return o instanceof ParcelUuid && uuid.equals(((ParcelUuid) o).uuid); }
    @Override public int hashCode() { return uuid.hashCode(); }
    @Override public String toString() { return uuid.toString(); }
    @Override public int describeContents() { return 0; }
    @Override public void writeToParcel(Parcel dest, int flags) { throw Parcel.none(); }
    public static final Parcelable.Creator<ParcelUuid> CREATOR = new Parcelable.Creator<ParcelUuid>() {
        public ParcelUuid createFromParcel(Parcel source) { throw Parcel.none(); }
        public ParcelUuid[] newArray(int size) { return new ParcelUuid[size]; }
    };
}
