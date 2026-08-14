// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Principal;
using System;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Specialized;

namespace KeePassGate.Vendor.Http
{
    public class HttpListenerBasicIdentity : GenericIdentity
    {
        public HttpListenerBasicIdentity(string username, string password) :
            base(username, "Basic")
        {
            Password = password;
        }

        public virtual string Password { get; }
    }
}
