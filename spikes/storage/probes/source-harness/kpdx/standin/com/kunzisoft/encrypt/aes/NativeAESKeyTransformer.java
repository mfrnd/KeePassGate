// Stand-in, written for this harness, for KeePassDX's JNI class of the same name: the native AES
// key transformation is not loaded on the desktop, so the call fails with an exception and
// AESTransformer takes its own fallback, transformKeyInJVM, which computes the same key.
package com.kunzisoft.encrypt.aes;

public class NativeAESKeyTransformer {
    public static byte[] nTransformKey(byte[] seed, byte[] key, long rounds) {
        throw new IllegalStateException("stand-in: no native AES key transformation");
    }
}
