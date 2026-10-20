using System.Globalization;
using System.Text;

namespace KeePassGate
{
    /// <summary>
    /// Bytes as lower case hexadecimal, in one place.
    /// </summary>
    /// <remarks>
    /// Two copies of this existed, one beside the certificate digest and one beside the SNI name,
    /// and they are the same four lines. They are also the two places where a difference would be
    /// invisible and expensive: a digest and a name that disagree about case or padding do not
    /// throw, they simply stop matching, and what fails is a client being admitted.
    /// </remarks>
    internal static class Hex
    {
        public static string Of(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }

            var text = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }
    }
}
