// The runtime's ETW tracing, reduced to nothing.
//
// This is the one dependency deliberately stubbed rather than vendored. Upstream it is an
// EventSource with generated plumbing, and it exists to emit diagnostics that this project has no
// consumer for. Every call site is a trace, so a no-op is behavior-preserving for everything
// except diagnostics, and the alternative is carrying an EventSource into a password manager's
// process for no benefit.

using System;
using System.Runtime.CompilerServices;

namespace KeePassGate.Vendor.Http
{
    internal static class NetEventSource
    {
        public static readonly NetEventSourceLog Log = new NetEventSourceLog();

        public static bool IsEnabled
        {
            get { return false; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object? thisOrContextObject, object? formattableString = null,
                                [CallerMemberName] string? memberName = null)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object? thisOrContextObject, object? formattableString = null,
                                 [CallerMemberName] string? memberName = null)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Verbose(object? thisOrContextObject, object? formattableString = null,
                                   [CallerMemberName] string? memberName = null)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Enter(object? thisOrContextObject, object? arg0 = null, object? arg1 = null,
                                 object? arg2 = null, [CallerMemberName] string? memberName = null)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Exit(object? thisOrContextObject, object? arg0 = null,
                                [CallerMemberName] string? memberName = null)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Associate(object? first, object? second,
                                     [CallerMemberName] string? memberName = null)
        {
        }

        internal sealed class NetEventSourceLog
        {
            public bool IsEnabled() { return false; }
        }
    }
}
