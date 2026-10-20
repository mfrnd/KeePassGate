# Third party notices

KeePassGate redistributes the components below, and what each license asks of that is one thing:
that its copyright notice and its permission notice go with every copy. Each is reproduced here in
full, as the component publishes it. Nothing packages a release yet, so this file is where they are
kept, and whatever packages one has to carry it beside the assemblies. This obligation is
independent of KeePassGate's own license and would exist whatever that license were.

**The transport is not among them.** There is no third-party HTTP or TLS dependency: the accept
loop, the ClientHello peek, the `SslStream` and the request parser are all this project's own code,
on the platform's own cryptography.

**KeePass itself is not redistributed.** The plugin is built against a local copy of `KeePass.exe`
and ships neither it nor KeePassLib. What a user runs is assembled on their machine, by KeePass's
own loader, from two separately distributed halves.

**Nothing third-party is redistributed yet.** Each component's notice is added here by the commit
that first makes the plugin depend on it, so what follows is only what is not taken, and why.

## websocket-sharp, no longer taken

Kept here as a note rather than a notice, because a reader who saw earlier drafts will look for it.
The dependency was removed once selecting a host certificate by SNI meant owning the accept loop and
the `SslStream`, which left it parsing only a request line and a header block.
[`DESIGN.md`](DESIGN.md) §104 records the reversal and the seam to return to.

Nothing of it is redistributed, so no notice is owed. If it is ever reinstated, it is MIT, parts of
that tree derive from Mono and carry headers naming **Novell, Inc.** alongside sta.blockhead, and a
submodule rather than an assembled copy is what keeps those headers intact.

## The proofs are not here, and neither are their licenses

The three HTTP implementations this project measured are vendored on the `spikes/http-lib-measurements`
branch, taken from `master` and never merged. **Nothing from it is redistributed by the plugin.** No
mainline branch has that source in its tree or in its history, and each proof there vendors exactly
one candidate and links none of the others, so nothing on this side inherits anything from it.

That is what makes the LGPL-2.1 candidate a decision rather than a problem: measuring a library is
not taking it. The branch carries its own `THIRD-PARTY-NOTICES.md` for what it redistributes.
