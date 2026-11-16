// Stand-in, written for this harness: Android 11's Xml.newSerializer() answers libcore's
// KXmlSerializer, a modified kXML 2; this answers kXML 2.3.0's. They differ in what they do with a
// character XML 1.0 cannot hold (libcore refuses it, kXML 2.3.0 writes a character reference) and
// in nothing a value of the harness's bases holds.
package android.util;

import org.kxml2.io.KXmlSerializer;
import org.xmlpull.v1.XmlSerializer;

public final class Xml {
    public static XmlSerializer newSerializer() { return new KXmlSerializer(); }
}
