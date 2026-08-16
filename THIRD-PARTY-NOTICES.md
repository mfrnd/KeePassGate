# Third party notices

This branch redistributes the sources below, so their notices travel with it. Each is vendored under
the `vendor/` directory of the one proof that measures it, with its own license headers intact.

## Why nothing here is mixed

**Each proof is a separate program that vendors exactly one candidate.** Three directories, three
`csproj` files, three executables. None of them references another, none of them is built into a
common assembly, and no build here produces anything that combines two of these libraries. A
candidate's license therefore reaches its own directory and stops there.

**Nothing here is a product.** These programs exist to be run once and read: they mint a certificate
authority in memory, serve one request over loopback, print what they saw, and exit. No binary from
this branch is published, packaged or installed.

**And this branch is never merged.** It is taken from `master` and stays beside the mainline
branches rather than under them, so no branch that carries KeePassGate itself has any of this
source in its tree or its history. That is the point of the arrangement rather than a side effect:
a spike that lands on a mainline branch has to be deleted again, and the deletion does not remove it
from the history of everything downstream.

**KeePassGate takes none of the three.** The plugin is MIT and depends on none of these libraries.
The measurements decided the question and then dissolved it, because selecting a host certificate by
SNI means owning the accept loop, and once the accept loop and the TLS are ours a candidate is
reduced to a parser small enough to write. `RESULTS.md` has that in full.

The one that would need care if it were ever taken is uHttpSharp, which is LGPL-2.1. That is
recorded on the mainline as a reason it is not taken, alongside its liveness, and measuring it here
is not taking it: reading a library and running it in a program nobody ships is not distributing a
work based on it.

## `spikes/httplistener/vendor/`

`dotnet/runtime`'s managed `HttpListener`, at commit `7cb858d4c2cc6c0d0eccc93d17644c92ea7bb1a3`.
27 source files beside a `VENDOR.md` recording their provenance. 116 substantive lines changed,
each one catalogued there.

> Licensed to the .NET Foundation under one or more agreements.
> The .NET Foundation licenses this file to you under the MIT license.

That header is retained verbatim in all 27 files, which is how it arrived.

## `spikes/uhttpsharp/vendor/`

uHttpSharp, at commit `685a33dce200978e62aaaa24687641bfb8ba9568`, recorded in
`spikes/uhttpsharp/vendor/COMMIT.txt`. 62 source files, **zero lines changed**, which is what the
measurement was about.

> GNU Lesser General Public License, version 2.1 or later
>
> Copyright (C) 2011 uhttpsharp project, <http://github.com/raistlinthewiz/uhttpsharp>

Unmodified, so what is redistributed here is the library as its authors published it, with every
header intact.

It bundles one further component, which arrives with it and is redistributed with it:

> LibLog 3.1, the MIT License
>
> Copyright (c) 2011-2015 Damian Hickey

## `spikes/wssharp/vendor/`

websocket-sharp, at commit `7aed0002451cf70ed74bc2e1ca6504dab5b50a10`, recorded in
`spikes/wssharp/vendor/COMMIT.txt`. 76 source files, 10 lines changed in one of them, five of those a
comment.

> The MIT License
>
> Copyright (c) sta.blockhead

Per file rather than for the tree: the headers carry their own year ranges, from 2010 onwards, and
they are retained as they arrived.

**28 files derive from Mono** and carry a second holder, **Novell, Inc.**, with year ranges of their
own from 2003 onwards, alongside sta.blockhead. That is why the library is vendored whole rather
than as an assembled copy of the parts a proof happens to use: assembling it by hand is how a
header goes missing.

## The harnesses

Everything under each proof's `src/` is KeePassGate's own code and is MIT, the same as the plugin.
Nothing in a harness is derived from the library it drives.
