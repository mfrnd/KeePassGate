using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// What each kind of call is allowed to fail with, in one place, so that a handler catches the
    /// failures it thought about and lets every other one travel.
    /// </summary>
    /// <remarks>
    /// Used as an exception filter: `catch (Exception error) when (Expected.OfCryptography(error))`.
    /// A filter is not a catch-all. It is evaluated before any handler runs, and an exception it
    /// does not match carries on unwound, which is the whole point: a `NullReferenceException` from
    /// a defect here, an `OutOfMemoryException`, or a `TypeLoadException` from a KeePass upgrade
    /// must reach somebody rather than become a log line saying something else failed.
    ///
    /// The lists live here rather than at each site because they are worth reviewing together, and
    /// because a list repeated at every call site is a list that drifts.
    /// </remarks>
    internal static class Expected
    {
        /// <summary>
        /// Reading, writing or building certificates and keys.
        /// </summary>
        /// <remarks>
        /// `CryptographicException` covers a malformed DER or PKCS#12, an unusable key and a chain
        /// that will not build. `FormatException` is base64 that is not base64.
        /// `PlatformNotSupportedException` is a curve or an algorithm this Windows does not have.
        /// See https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates
        /// </remarks>
        public static bool OfCryptography(Exception error)
        {
            return error is CryptographicException
                   || error is FormatException
                   || error is ArgumentException
                   || error is PlatformNotSupportedException
                   || error is GateFailure;
        }

        /// <summary>
        /// Reading or writing custom data, the database's own.
        /// </summary>
        /// <remarks>
        /// `InvalidOperationException` is the one that matters and it is not hypothetical: the gate
        /// reads custom data on a connection thread while the user interface thread can be writing
        /// to the same dictionary, and that is what an invalidated enumeration throws.
        /// </remarks>
        public static bool OfCustomData(Exception error)
        {
            return error is InvalidOperationException
                   || error is ArgumentException
                   || error is GateFailure;
        }

        /// <summary>
        /// Reaching into KeePass by reflection, which is how everything the plugin does not compile
        /// against is reached.
        /// </summary>
        /// <remarks>
        /// A KeePass upgrade that renames or removes a member lands here, which is why every one of
        /// these sites degrades to "the plugin can see less than it wants" rather than failing to
        /// load. The host tests pin the same names so the degradation is never a surprise.
        /// </remarks>
        public static bool OfReflection(Exception error)
        {
            // `CustomAttributeFormatException` and `FileLoadException` are named because
            // `GateVersion` reads its attributes from a static initializer, where anything not
            // caught becomes a `TypeInitializationException` the runtime then caches for the
            // life of the process: the version would stop the plugin loading instead of
            // reading "unknown".
            return error is CustomAttributeFormatException
                   || error is FileLoadException
                   || error is TargetInvocationException
                   || error is MemberAccessException
                   || error is MissingMemberException
                   || error is AmbiguousMatchException
                   || error is TypeLoadException
                   || error is InvalidCastException
                   || error is SecurityException;
        }

        /// <summary>
        /// Touching a window or a control.
        /// </summary>
        /// <remarks>
        /// A form can be disposed between being announced and being decorated, a handle can fail to
        /// be created, and a marshaled call can arrive after its target has gone.
        /// </remarks>
        public static bool OfUserInterface(Exception error)
        {
            return error is InvalidOperationException
                   || error is ObjectDisposedException
                   || error is ArgumentException
                   // `ExternalException` rather than `Win32Exception`, which derives from it:
                   // GDI+ reports a picture it cannot decode as the base type, so naming only
                   // the derived one let every drawing failure past this filter.
                   //
                   // **`OutOfMemoryException` is not handled, and that is a known limitation.**
                   // GDI+ raises it for a picture it cannot decode as well as for real exhaustion,
                   // and this filter is shared by every user interface call site, so naming it here
                   // would swallow the real one too. The attach path tolerates it structurally
                   // instead, by recording the tab before anything draws. What would close it is a
                   // filter narrow enough to cover the drawing calls alone.
                   || error is ExternalException
                   || error is GateFailure;
        }

        /// <summary>
        /// A socket, a stream, or the TLS over it.
        /// </summary>
        /// <remarks>
        /// TODO(step 6): the transport takes Bouncy Castle there; until then this filter is written
        /// for the stack that is coming rather than the one in the tree.
        /// Bouncy Castle's `TlsFatalAlert` derives from `IOException`, so a failed handshake and a
        /// client that hung up arrive as the same kind of thing, which is what they are to a server
        /// that is about to close the connection either way.
        ///
        /// **`InvalidOperationException` is deliberately not here.** On a connection it is only
        /// ever this plugin calling something out of order. Bouncy Castle raises whatever goes
        /// wrong inside a record as an alert, and keeps this one for a caller that uses it out of
        /// order, such as reading before its handshake has finished; `TcpClient.GetStream` raises
        /// it for a socket that is not connected, and an accepted one is. Named here, it turned a
        /// defect on the connection thread, a collection that moved while a name was resolved,
        /// into a line saying a connection ended early, and nothing was counted.
        ///
        /// **Nor are `FileNotFoundException` and `FileLoadException`, although both are
        /// `IOException`s.** Nothing a connection does opens a file, so on a connection either one
        /// is the runtime failing to load an assembly this plugin needs, missing or not the version
        /// it was built against. That is a broken installation and a defect to count: taken for a
        /// client that hung up, it would fail every connection with a line nobody reads as a
        /// failure.
        /// </remarks>
        public static bool OfConnection(Exception error)
        {
            return (error is IOException && !(error is FileNotFoundException || error is FileLoadException))
                   || error is SocketException
                   || error is ObjectDisposedException
                   || error is GateFailure;
        }
    }
}
