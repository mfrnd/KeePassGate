using System;

namespace KeePassGate.Store
{
    /// <summary>
    /// An identifier of the store's sixteen bytes, `STORE.md` §609, held as two numbers read
    /// big-endian: compared, hashed and ordered as its bytes are, which is as its lowercase
    /// hexadecimal is compared by ordinal, with no text made of it.
    /// </summary>
    /// <remarks>
    /// **What a reading compares identifiers as**: a deletion record and a name in `after` alike,
    /// so that a hold passing over every deletion record of a file makes no text of any of them,
    /// and only an identifier handed out, a version lost or a summary entry told of, is written as
    /// text, `StoreInForce`.
    /// </remarks>
    internal readonly struct StoreId : IEquatable<StoreId>, IComparable<StoreId>
    {
        /// <summary>The bytes of an identifier.</summary>
        public const int Length = StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength + StoreFormat.CheckLength;

        /// <summary>The characters of an identifier written as lowercase hexadecimal.</summary>
        public const int TextLength = Length * 2;

        private const string Digits = "0123456789abcdef";

        /// <summary>The bits of the second number that bytes 9 and 10 take, the rest of a place being 0.</summary>
        private const ulong NumberBits = 0xFFFF000000000000UL;

        /// <summary>The bits of the first number that bytes 4 to 8 take.</summary>
        private const ulong SubjectBits = (1UL << (StoreFormat.SubjectLength * 8)) - 1;

        /// <summary>Bytes 1 to 8.</summary>
        private readonly ulong _high;

        /// <summary>Bytes 9 to 16.</summary>
        private readonly ulong _low;

        private StoreId(ulong high, ulong low)
        {
            _high = high;
            _low = low;
        }

        /// <summary>Its first three bytes, the tag, as a number, as `StoreFormat.TagNumber` reads them.</summary>
        public int Tag
        {
            get { return (int)(_high >> 40); }
        }

        /// <summary>Bytes 4 to 8, the hash of what the record is about, as a number read big-endian.</summary>
        public long Subject
        {
            get { return (long)(_high & SubjectBits); }
        }

        /// <summary>
        /// Its first eight bytes, the tag and the subject hash, as the 16 lowercase hexadecimal
        /// characters an item with no subject holds, `STORE.md` §611.
        /// </summary>
        public string TagAndSubject
        {
            get
            {
                var text = new char[(StoreFormat.TagLength + StoreFormat.SubjectLength) * 2];
                Write(_high, text, 0);
                return new string(text);
            }
        }

        /// <summary>Bytes 9 and 10: a height, a format number or the tag of a profile's name.</summary>
        public int Number
        {
            get { return (int)(_low >> 48); }
        }

        /// <summary>
        /// Its place, `STORE.md` §609: its first ten bytes, the tag, the subject hash and the
        /// number, with the rest 0, which is the identifier the place is compared as.
        /// </summary>
        public StoreId Place
        {
            get { return new StoreId(_high, _low & NumberBits); }
        }

        /// <summary>The identifier of these sixteen bytes; false for bytes of any other length.</summary>
        public static bool TryOf(byte[] bytes, out StoreId id)
        {
            id = default(StoreId);
            if (bytes == null || bytes.Length != Length)
            {
                return false;
            }

            ulong high = 0;
            ulong low = 0;
            for (var index = 0; index < 8; index++)
            {
                high = (high << 8) | bytes[index];
                low = (low << 8) | bytes[index + 8];
            }

            id = new StoreId(high, low);
            return true;
        }

        /// <summary>
        /// The identifier a text names: exactly 32 lowercase hexadecimal characters; false for any
        /// other text, an identifier written in capitals among it.
        /// </summary>
        public static bool TryParse(string text, out StoreId id)
        {
            id = default(StoreId);
            return text != null && text.Length == TextLength && TryParse(text, 0, out id);
        }

        /// <summary>
        /// The identifier the 32 characters of a text from this place name, each a lowercase
        /// hexadecimal digit; false where any is not, or where the text ends before them.
        /// </summary>
        public static bool TryParse(string text, int start, out StoreId id)
        {
            id = default(StoreId);
            if (text == null || start < 0 || start > text.Length - TextLength)
            {
                return false;
            }

            ulong high;
            ulong low;
            if (!HexNumber(text, start, 16, out high) || !HexNumber(text, start + 16, 16, out low))
            {
                return false;
            }

            id = new StoreId(high, low);
            return true;
        }

        /// <summary>
        /// The tag and the subject hash 16 lowercase hexadecimal characters name, as the
        /// identifier they begin with the rest 0, which <see cref="Tag"/> and
        /// <see cref="Subject"/> read; false for any other text.
        /// </summary>
        public static bool TryParseTagAndSubject(string text, out StoreId id)
        {
            id = default(StoreId);
            ulong high;
            if (text == null || text.Length != (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2 || !HexNumber(text, 0, text.Length, out high))
            {
                return false;
            }

            id = new StoreId(high, 0);
            return true;
        }

        /// <summary>
        /// A place as the identifier it is compared as: these bytes of a tag, the subject hash and
        /// the number, the rest 0.
        /// </summary>
        public static StoreId PlaceOf(byte[] tag, byte[] subject, int number)
        {
            var bytes = new byte[Length];
            Buffer.BlockCopy(tag, 0, bytes, 0, StoreFormat.TagLength);
            Buffer.BlockCopy(subject, 0, bytes, StoreFormat.TagLength, StoreFormat.SubjectLength);
            bytes[StoreFormat.TagLength + StoreFormat.SubjectLength] = (byte)(number >> 8);
            bytes[StoreFormat.TagLength + StoreFormat.SubjectLength + 1] = (byte)number;
            StoreId id;
            TryOf(bytes, out id);
            return id;
        }

        /// <summary>
        /// The place the first twenty characters of a text name, as the first ten bytes of an
        /// identifier written so: null where any of them is no lowercase hexadecimal digit, or
        /// where the text is shorter, an item's `id` being any text.
        /// </summary>
        public static StoreId? PlaceOf(string text)
        {
            const int PlaceText = (StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength) * 2;
            ulong high;
            ulong number;
            if (text == null || text.Length < PlaceText || !HexNumber(text, 0, 16, out high) || !HexNumber(text, 16, PlaceText - 16, out number))
            {
                return null;
            }

            return new StoreId(high, number << 48);
        }

        /// <summary>The five bytes of a subject's hash as the number <see cref="Subject"/> compares them as.</summary>
        public static long SubjectOf(byte[] hash)
        {
            long subject = 0;
            for (var index = 0; index < StoreFormat.SubjectLength; index++)
            {
                subject = (subject << 8) | hash[index];
            }

            return subject;
        }

        public static bool operator ==(StoreId one, StoreId other)
        {
            return one.Equals(other);
        }

        public static bool operator !=(StoreId one, StoreId other)
        {
            return !one.Equals(other);
        }

        public bool Equals(StoreId other)
        {
            return _high == other._high && _low == other._low;
        }

        public override bool Equals(object obj)
        {
            return obj is StoreId && Equals((StoreId)obj);
        }

        public override int GetHashCode()
        {
            var mixed = _high ^ (_high >> 29) ^ _low ^ (_low >> 31);
            return unchecked((int)mixed ^ (int)(mixed >> 32));
        }

        /// <summary>Ordered as its bytes, which is as its lowercase hexadecimal by ordinal.</summary>
        public int CompareTo(StoreId other)
        {
            return _high != other._high ? _high.CompareTo(other._high) : _low.CompareTo(other._low);
        }

        /// <summary>The 32 lowercase hexadecimal characters it is written as, `Hex.Of` of its bytes.</summary>
        public override string ToString()
        {
            var text = new char[TextLength];
            Write(_high, text, 0);
            Write(_low, text, 16);
            return new string(text);
        }

        /// <summary>The number these lowercase hexadecimal digits make, read big-endian; false where one is not.</summary>
        private static bool HexNumber(string text, int start, int digits, out ulong number)
        {
            number = 0;
            for (var index = start; index < start + digits; index++)
            {
                var character = text[index];
                int digit;
                if (character >= '0' && character <= '9')
                {
                    digit = character - '0';
                }
                else if (character >= 'a' && character <= 'f')
                {
                    digit = character - 'a' + 10;
                }
                else
                {
                    return false;
                }

                number = (number << 4) | (uint)digit;
            }

            return true;
        }

        private static void Write(ulong number, char[] text, int start)
        {
            for (var index = 15; index >= 0; index--)
            {
                text[start + index] = Digits[(int)(number & 0xF)];
                number >>= 4;
            }
        }
    }
}
