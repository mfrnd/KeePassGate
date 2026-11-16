// Stand-in, written for this harness, for KeePassDX's JNI class of the same name, which hands the
// parameters to the reference argon2 C library (argon2_ctx, a 32-byte hash). This computes the
// same function with Bouncy Castle's Argon2BytesGenerator. Only key derivation depends on it.
package com.kunzisoft.encrypt.argon2;

import java.io.IOException;
import org.bouncycastle.crypto.generators.Argon2BytesGenerator;
import org.bouncycastle.crypto.params.Argon2Parameters;

public class NativeArgon2KeyTransformer {
    enum CType {
        ARGON2_D(0),
        ARGON2_I(1),
        ARGON2_ID(2);
        int cValue = 0;
        CType(int i) { cValue = i; }
    }

    public static byte[] nTransformKey(int type, byte[] password, byte[] salt, int parallelism, int memory,
                                       int iterations, byte[] secretKey, byte[] associatedData, int version)
            throws IOException {
        int bcType = type == 0 ? Argon2Parameters.ARGON2_d : (type == 1 ? Argon2Parameters.ARGON2_i : Argon2Parameters.ARGON2_id);
        Argon2Parameters.Builder b = new Argon2Parameters.Builder(bcType)
                .withVersion(version)
                .withIterations(iterations)
                .withMemoryAsKB(memory)
                .withParallelism(parallelism)
                .withSalt(salt);
        if (secretKey != null && secretKey.length > 0) b.withSecret(secretKey);
        if (associatedData != null && associatedData.length > 0) b.withAdditional(associatedData);
        Argon2BytesGenerator g = new Argon2BytesGenerator();
        g.init(b.build());
        byte[] out = new byte[32];
        g.generateBytes(password, out);
        return out;
    }
}
