# Third party notices

<!-- A copyright holder SharpYaml's license names, quoted as it is written there. -->
<!-- cspell:ignore Aubry -->

KeePassGate redistributes the components below, and what each license asks of that is one thing:
that its copyright notice and its permission notice go with every copy. Each is reproduced here in
full, as the component publishes it. `build.ps1 -Task Package` zips this file beside the assemblies
in the package a release publishes, and `build.ps1 -Task Deploy` copies it beside them into
KeePass. This obligation is independent of KeePassGate's own license and would exist whatever that
license were.

**The HTTP half of the transport is not among them.** The accept loop, the ClientHello peek and the
request parser are this project's own code. **The TLS half is Bouncy Castle**, and the reason is
recorded rather than assumed: the platform's own TLS refuses to serve with a key that has not been
written to the user's profile, and `CA.md` §202 says an authority lives in the database and nowhere
else. See `DESIGN.md` §133 and `ManagedTlsProbe.cs` on `spikes/tls-and-certificates`.

**KeePass itself is not redistributed.** The plugin is built against a local copy of `KeePass.exe`
and ships neither it nor KeePassLib. What a user runs is assembled on their machine, by KeePass's
own loader, from two separately distributed halves.

## websocket-sharp, no longer taken

Kept here as a note rather than a notice, because a reader who saw earlier drafts will look for it.
The dependency was removed once selecting a host certificate by SNI meant owning the accept loop and
the `SslStream`, which left it parsing only a request line and a header block.
[`DESIGN.md`](DESIGN.md) §104 records the reversal and the seam to return to.

Nothing of it is redistributed, so no notice is owed. If it is ever reinstated, it is MIT, parts of
that tree derive from Mono and carry headers naming **Novell, Inc.** alongside sta.blockhead, and a
submodule rather than an assembled copy is what keeps those headers intact.

## Bouncy Castle for .NET

Taken for `Org.BouncyCastle.Tls`, which terminates the transport's TLS 1.3 in managed code so that
no private key this plugin holds is ever written to disk. Redistributed as
`BouncyCastle.Cryptography.dll` beside the plugin.

> MIT License (<https://opensource.org/licenses/MIT>)
>
> Copyright (c) 2000-2026 The Legion of the Bouncy Castle Inc. (<https://www.bouncycastle.org>).
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction,
> including without limitation the rights to use, copy, modify, merge, publish, distribute,
> sub license, and/or sell copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions: The above copyright notice and this
> permission notice shall be included in all copies or substantial portions of the Software.
>
> **THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
> NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
> OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.**

## Microsoft.OpenApi, Microsoft.OpenApi.Readers

> Copyright (c) Microsoft Corporation. All rights reserved.
>
> MIT License
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## SharpYaml

> Copyright (c) 2013-2022 SharpYaml - Alexandre Mutel
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in
> all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
> THE SOFTWARE.

SharpYaml is a fork of YamlDotNet, and its license carries YamlDotNet's beside its own:

> Copyright (c) 2008, 2009, 2010, 2011, 2012 Antoine Aubry
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of
> this software and associated documentation files (the "Software"), to deal in
> the Software without restriction, including without limitation the rights to
> use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
> of the Software, and to permit persons to whom the Software is furnished to do
> so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

It arrives with `Microsoft.OpenApi.Readers`, whose package depends on it whether or not any YAML is
parsed. Since it ships either way, the routing table is written in YAML rather than JSON: shipping a
YAML reader and then not using it would have cost the readability for nothing.

## Newtonsoft.Json

> The MIT License (MIT)
>
> Copyright (c) 2007 James Newton-King
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of
> this software and associated documentation files (the "Software"), to deal in
> the Software without restriction, including without limitation the rights to
> use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
> the Software, and to permit persons to whom the Software is furnished to do so,
> subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
> FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
> IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
> CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

It reads request bodies, and it is one assembly with no dependencies of its own. The policy over
what it parses is not its: `JsonBody` decides what this wire may carry, which is one flat object
of strings and arrays of strings. The `spikes/json-reading` branch has what the alternatives cost.

## The proofs are not here, and neither are their licenses

The three HTTP implementations this project measured are vendored on the `spikes/http-lib-measurements`
branch, taken from `master` and never merged. **Nothing from it is redistributed by the plugin.** No
mainline branch has that source in its tree or in its history, and each proof there vendors exactly
one candidate and links none of the others, so nothing on this side inherits anything from it.

That is what makes the LGPL-2.1 candidate a decision rather than a problem: measuring a library is
not taking it. The branch carries its own `THIRD-PARTY-NOTICES.md` for what it redistributes.

## What is actually redistributed

Five assemblies, and all of them are MIT: `BouncyCastle.Cryptography`, `Microsoft.OpenApi`,
`Microsoft.OpenApi.Readers`, `SharpYaml` and `Newtonsoft.Json`. `build.ps1 -Task Deploy` and
`build.ps1 -Task Package` both ship every DLL from the build output, so what ships is exactly that
list, and both put this file beside them, taking it from the repository, since the build output
holds no copy of it.

This file is maintained by hand and is to be re-checked against the resolved package set as each
step lands. The package holds it to that: `build.ps1 -Task Package` refuses a DLL beside the plugin
that the list above does not name.
