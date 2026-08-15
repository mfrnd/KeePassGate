using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using Microsoft.OpenApi.Exceptions;
using Microsoft.OpenApi.Readers.Exceptions;
using System.Threading;

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
        /// Minting a certificate from a stored authority.
        /// </summary>
        /// <remarks>
        /// Everything <see cref="OfCryptography"/> names, since the authority arrives as stored
        /// bytes and a key it may not be able to sign with, plus `InvalidOperationException`, which
        /// is what `AuthorityIssuer` refuses an authority whose own dates have passed with.
        ///
        /// **That refusal is the ordinary state of an old database, not a defect here.**
        /// `AuthorityStore.Read` ignores expiry deliberately, so an authority past its ten years is
        /// served and reaches every site that issues. A site that does not name this lets the
        /// exception travel to a catch-all, which reports it as something this plugin got wrong and
        /// counts it on the KPG tab, while the operator is the only one who can act on
        /// it: `CA.md` §219 has no reissue, and recreating the authority is what replaces one.
        /// </remarks>
        public static bool OfIssuance(Exception error)
        {
            return OfCryptography(error)
                   || error is InvalidOperationException;
        }

        /// <summary>
        /// Reading a KeePass collection while something else is changing it.
        /// </summary>
        /// <remarks>
        /// Two types, and they are not interchangeable. A list whose version moved under an
        /// enumerator throws `InvalidOperationException`. A copy whose source grew while it was
        /// being made throws `ArgumentException` out of `Array.Copy`. That copy is `GetEntries`,
        /// building a new list out of a tree that is still changing. A filter naming only the
        /// first misses every flat sweep, which is the busiest route there is.
        ///
        /// **`GateFailure` is deliberately not here.** This filter guards a retry, and the thing a
        /// spent retry throws is a `GateFailure`; catching it would turn giving up into an infinite
        /// loop. `OfCustomData` includes it because nothing it guards retries.
        /// </remarks>
        public static bool OfMovingTree(Exception error)
        {
            return error is InvalidOperationException
                   || error is ArgumentException;
        }

        /// <summary>
        /// Reading or writing custom data: the database's own, a group's or an entry's.
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
        /// Writing to a file this plugin owns, which today is the audit.
        /// </summary>
        /// <remarks>
        /// Every way a path can turn out to be unusable: gone, locked by somebody else, forbidden
        /// by policy, full, or not a path at all. The same list the log uses, named once here now
        /// that a second writer needs it.
        ///
        /// What lands here is never allowed to fail a request. A gate that stopped answering
        /// because it could not write a line would be a denial of service anybody with a full disk
        /// could arrange.
        /// </remarks>
        public static bool OfStorage(Exception error)
        {
            return error is IOException
                   || error is UnauthorizedAccessException
                   || error is SecurityException
                   || error is ArgumentException
                   || error is NotSupportedException;
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
        /// Reading the OpenAPI document that is this API's routing table.
        /// </summary>
        /// <remarks>
        /// Measured rather than assumed, and two of the three cost time. The reader **reports some
        /// problems and throws others**: malformed JSON comes back as an entry in
        /// `diagnostic.Errors`, and in that case `Paths` can be null, while a document with no
        /// recognizable version throws instead. And the exception it throws,
        /// `OpenApiUnsupportedSpecVersionException`, **derives straight from `Exception`** rather
        /// than from the library's own `OpenApiException`, so there is no one base to catch and
        /// both have to be named.
        ///
        /// This is only ever reached at startup, over a document that ships inside the assembly, so
        /// what it buys is a sentence naming the document rather than a stack trace out of a
        /// dependency.
        /// </remarks>
        public static bool OfDocument(Exception error)
        {
            // `InvalidOperationException` among them because the reader raises one from a
            // LINQ `First()` when the document is empty, which is a document this refuses
            // rather than a defect here.
            return error is OpenApiException
                   || error is OpenApiUnsupportedSpecVersionException
                   || error is FormatException
                   || error is ArgumentException
                   || error is InvalidOperationException;
        }

        /// <summary>
        /// Putting something on the clipboard.
        /// </summary>
        /// <remarks>
        /// `ExternalException` is the one that happens: the clipboard is a single system-wide lock
        /// and another program can be holding it, which is why `SetDataObject` takes a retry count
        /// and still gives up eventually. `ThreadStateException` would mean this ran off a
        /// single-threaded apartment, which is a defect here rather than bad luck, but it is a
        /// defect that must not take KeePass down with it. Note that `Win32Exception` derives from
        /// `ExternalException` and is therefore already covered.
        ///
        /// **`ArgumentException` is deliberately not here.** The only way `SetDataObject` raises one
        /// is being handed nothing to put on the clipboard, which is this plugin's own defect rather
        /// than the desktop being busy. Catching it would report a bug of ours to the operator as
        /// "another program is holding the clipboard" and tell them to do something that cannot
        /// work.
        /// See https://learn.microsoft.com/dotnet/api/system.windows.forms.clipboard.setdataobject
        /// </remarks>
        public static bool OfClipboard(Exception error)
        {
            return error is ExternalException
                   || error is ThreadStateException;
        }

        /// <summary>
        /// A socket, a stream, or the TLS over it.
        /// </summary>
        /// <remarks>
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
