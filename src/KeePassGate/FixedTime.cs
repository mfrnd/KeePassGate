using System;

namespace KeePassGate
{
    /// <summary>
    /// Comparisons that take the same time whatever the answer.
    /// </summary>
    /// <remarks>
    /// Used wherever an attacker chooses one side of the comparison: the gate tag they send, the
    /// certificate digest they present. .NET Framework has no `CryptographicOperations.FixedTimeEquals`,
    /// so it is written here, once, rather than approximated at each call site.
    ///
    /// The length is not secret and leaks by construction, which is fine for everything here: a
    /// digest and a tag are both of a known size.
    ///
    /// **At the root rather than beside the gate, because the authority compares digests too.**
    /// It began under `KeePassGate.Gate`, where the first caller was, and the admission record
    /// then had to reach into the gate's namespace to compare one certificate digest with
    /// another, while the gate reaches into the authority's for the records it admits by.
    /// That is a cycle between two namespaces over four lines of arithmetic that belong to
    /// neither. Here,
    /// like `Hex`, it is reached from both without either naming the other.
    ///
    /// .NET added `CryptographicOperations.FixedTimeEquals` in .NET Core 2.1 and it has never been
    /// available on .NET Framework, which is what this plugin targets.
    /// https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals
    /// </remarks>
    public static class FixedTime
    {
        public static bool Equals(byte[] left, byte[] right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }

        /// <summary>
        /// The same for text that is compared byte by byte, such as a lowercase hex digest. Case
        /// sensitive and culture free on purpose: these are identifiers, not words.
        /// </summary>
        public static bool Equals(string left, string right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }
    }
}
