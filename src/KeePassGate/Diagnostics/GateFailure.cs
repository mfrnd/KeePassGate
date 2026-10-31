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
    /// everything else travel. Only a handful of places catch everything, because in each of them
    /// an exception that got past would do more harm than the defect it carries, and every one of
    /// them reports what it caught as an incident rather than as a routine error. See
    /// <see cref="GateIncidents"/>, which names them and what an escape from each would do.
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

    /// <summary>
    /// A read gave up because the database would not hold still long enough to be read.
    /// </summary>
    /// <remarks>
    /// Its own type so that a handler can answer it as busy. Left as the `InvalidOperationException`
    /// it started as, it reaches the catch-all that treats anything escaping a handler as a defect
    /// in this plugin. That put a 500 and a false incident on the operator's window for the
    /// ordinary case of an agent reading while somebody edits.
    /// </remarks>
    [Serializable]
    public sealed class VaultUnsettled : GateFailure
    {
        public VaultUnsettled(string message)
            : base(message)
        {
        }

        public VaultUnsettled(string message, Exception cause)
            : base(message, cause)
        {
        }

        private VaultUnsettled(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// The database a connection was given closed underneath it, which is a lock nearly always.
    /// </summary>
    /// <remarks>
    /// Its own type so that the caller is told the database is locked rather than that it has no
    /// record. KeePass empties a database before it says why, so between the two a connection
    /// resolved a moment earlier reads a target whose records are gone: answered as no record,
    /// an admitted and unrevoked client was told `401 unpaired`, which tells it to pair again,
    /// and the reference client's `revoke` then threw its identity away.
    /// </remarks>
    [Serializable]
    public sealed class DatabaseShut : GateFailure
    {
        public DatabaseShut(string message)
            : base(message)
        {
        }

        private DatabaseShut(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
