// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Specialized;
using System.Security.Principal;

namespace KeePassGate.Vendor.Http
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class HttpListenerException : Win32Exception
    {
        public HttpListenerException() : base(Marshal.GetLastWin32Error())
        {
            if (NetEventSource.Log.IsEnabled()) NetEventSource.Info(this, $"{NativeErrorCode}:{Message}");
        }

        public HttpListenerException(int errorCode) : base(errorCode)
        {
            if (NetEventSource.Log.IsEnabled()) NetEventSource.Info(this, $"{NativeErrorCode}:{Message}");
        }

        public HttpListenerException(int errorCode, string message) : base(errorCode, message)
        {
            if (NetEventSource.Log.IsEnabled()) NetEventSource.Info(this, $"{NativeErrorCode}:{Message}");
        }

        // PATCH: net48 ObsoleteAttribute has neither DiagnosticId nor UrlFormat. The message is kept.
        [Obsolete(Obsoletions.LegacyFormatterImplMessage)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected HttpListenerException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
            if (NetEventSource.Log.IsEnabled()) NetEventSource.Info(this, $"{NativeErrorCode}:{Message}");
        }

        // the base class returns the HResult with this property
        // we need the Win32 Error Code, hence the override.
        public override int ErrorCode => NativeErrorCode;
    }
}
