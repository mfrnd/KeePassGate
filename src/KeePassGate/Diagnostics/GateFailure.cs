using System;
using System.Runtime.Serialization;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// Everything this plugin throws on purpose.
    /// </summary>
    /// <remarks>
    /// It exists so that handlers can catch what they meant to catch. A `catch (Exception)` around
    /// a call that can fail in one known way also swallows the ones nobody predicted: a
    /// `NullReferenceException` from a defect here, an `OutOfMemoryException`, a
    /// `TypeLoadException` from a KeePass upgrade that moved something. Those are exactly the
    /// failures worth seeing, and a log line saying "a connection failed" is where they go to die.
    ///
    /// So the rule through the whole plugin is: **catch the type that names the failure**, and let
    /// everything else travel. Only a handful of places catch everything, because in each of them a
    /// surviving exception ends the password manager, and every one of them reports what it caught
    /// as an incident rather than as a routine error. See <see cref="GateIncidents"/>.
    ///
    /// Deriving from <see cref="Exception"/> rather than from `ApplicationException`, which
    /// Microsoft's own guidance has advised against since .NET 2.0:
    /// https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types
    /// </remarks>
    [Serializable]
    public abstract class GateFailure : Exception
    {
        protected GateFailure(string message)
            : base(message)
        {
        }

        protected GateFailure(string message, Exception cause)
            : base(message, cause)
        {
        }

        protected GateFailure(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The authority, or something it issued, could not be built, read or written.
    /// </summary>
    [Serializable]
    public sealed class AuthorityFailure : GateFailure
    {
        public AuthorityFailure(string message)
            : base(message)
        {
        }

        public AuthorityFailure(string message, Exception cause)
            : base(message, cause)
        {
        }

        private AuthorityFailure(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
