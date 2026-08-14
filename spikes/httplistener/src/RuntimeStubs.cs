// Stand-ins for things the runtime provides to its own sources and .NET Framework does not.
// None of this is logic: they are attributes the compiler recognizes by name, obsoletion message
// constants, and the one internal async-result base the vendored request type derives from.

using System;
using System.Threading;

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) { ReturnValue = returnValue; }
        public bool ReturnValue { get; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    internal sealed class MemberNotNullAttribute : Attribute
    {
        public MemberNotNullAttribute(string member) { Members = new[] { member }; }
        public MemberNotNullAttribute(params string[] members) { Members = members; }
        public string[] Members { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    internal sealed class StringSyntaxAttribute : Attribute
    {
        public StringSyntaxAttribute(string syntax) { Syntax = syntax; }
        public string Syntax { get; }
        public const string Uri = nameof(Uri);
        public const string CompositeFormat = nameof(CompositeFormat);
    }
}

namespace KeePassGate.Vendor.Http
{
    /// <summary>Obsoletion message constants the runtime keeps internally.</summary>
    internal static class Obsoletions
    {
        internal const string SharedUrlPrefix = "https://aka.ms/dotnet-warnings/";
        internal const string LegacyFormatterImplMessage = "This API supports obsolete formatter-based serialization.";
        internal const string LegacyFormatterImplDiagId = "SYSLIB0051";
        internal const string SystemTextEncodingUTF7Message = "The UTF-7 encoding is insecure.";
        internal const string SystemTextEncodingUTF7DiagId = "SYSLIB0001";
    }

    /// <summary>
    /// The runtime's internal lazy async result, reduced to what the vendored request type uses:
    /// construction, one completion, and a result. The path that reaches it is the client
    /// certificate fetch, which this port supplies directly, so it is present to compile rather
    /// than to run.
    /// </summary>
    internal class LazyAsyncResult : IAsyncResult
    {
        private readonly object _owner;
        private readonly AsyncCallback? _callback;
        private ManualResetEvent? _event;
        private int _completed;

        internal LazyAsyncResult(object owner, object? userState, AsyncCallback? callback)
        {
            _owner = owner;
            AsyncState = userState;
            _callback = callback;
        }

        public object? AsyncState { get; }
        public bool CompletedSynchronously { get { return false; } }
        public bool IsCompleted { get { return Volatile.Read(ref _completed) != 0; } }
        internal object? Result { get; private set; }
        internal bool EndCalled { get; set; }
        internal object AsyncObject { get { return _owner; } }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (_event == null)
                {
                    ManualResetEvent created = new ManualResetEvent(IsCompleted);
                    if (Interlocked.CompareExchange(ref _event, created, null) != null) created.Dispose();
                    if (IsCompleted) _event!.Set();
                }
                return _event!;
            }
        }

        internal void InvokeCallback(object? result)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            Result = result;
            _event?.Set();
            _callback?.Invoke(this);
        }

        internal object? InternalWaitForCompletion()
        {
            if (!IsCompleted) AsyncWaitHandle.WaitOne();
            return Result;
        }
    }
}
