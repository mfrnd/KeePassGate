// Stand-ins for the Android and Java types Keepass2Android's copy of KeePassLib touches, so that
// the copy compiles for desktop .NET unchanged. Written for this harness; none of it is the app's.
//
// What each one does in place of the original, and whether that can change what is written:
//   keepass2android.Kp2aLog          the app's logger: writes nothing (log lines only)
//   Java.Lang.JavaSystem.LoadLibrary  loads nothing: the DLL import resolver in Driver.cs maps
//                                     "argon2" to the reference libargon2 (key derivation only)
//   Com.Keepassdroid...NativeFinalKey the app's native AES-KDF: throws, so the copy takes its own
//                                     managed fallback, the same transform (key derivation only)
//   Android.Graphics.Bitmap and       a custom icon's decoded picture: never decoded here; the
//   BitmapFactory                     icon's bytes are kept and written as read
//   Java.IO.FileNotFoundException     an exception type
//   Org.BouncyCastle.*, Java.Text,    namespaces the copy names in `using` lines under code
//   Android.Webkit                    that is compiled out, or uses for nothing
using System;
using System.IO;

namespace keepass2android
{
    public static class Kp2aLog
    {
        public static bool Verbose;
        public static void Log(string message) { if (Verbose) Console.Error.WriteLine("[Kp2aLog] " + message); }
        public static void LogUnexpectedError(Exception exception) { Log(exception.ToString()); }
    }
}

namespace Java.Lang
{
    public static class JavaSystem
    {
        public static void LoadLibrary(string name) { }
    }
}

namespace Java.IO
{
    public class FileNotFoundException : System.IO.FileNotFoundException
    {
        public FileNotFoundException() { }
        public FileNotFoundException(string message) : base(message) { }
    }
}

namespace Java.Text { internal static class StandInNamespace { } }
namespace Android.Webkit { internal static class StandInNamespace { } }
namespace Org.BouncyCastle.Crypto { internal static class StandInNamespace { } }
namespace Org.BouncyCastle.Crypto.Engines { internal static class StandInNamespace { } }
namespace Org.BouncyCastle.Crypto.Parameters { internal static class StandInNamespace { } }

namespace Com.Keepassdroid.Crypto.Finalkey
{
    public class NativeFinalKey
    {
        public byte[] TransformMasterKey(byte[] seed, byte[] key, int rounds)
        {
            throw new NotSupportedException("stand-in: no native AES-KDF, the managed fallback runs");
        }
    }
}

namespace Android.Graphics
{
    public class Bitmap : IDisposable
    {
        public void Dispose() { }
        public void Recycle() { }
    }

    public static class BitmapFactory
    {
        public static Bitmap DecodeStream(Stream s) { return new Bitmap(); }
        public static Bitmap DecodeByteArray(byte[] data, int offset, int length) { return new Bitmap(); }
    }
}
