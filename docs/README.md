# KeePassGate, for the person running it

KeePassGate lets a program reach one KeePass database over the network on this machine, and only as
much of it as the operator decides. This is the part of the documentation written for whoever decides.

Nothing here assumes the design has been read. To find out why any of it works the way it
does, [`../DESIGN.md`](../DESIGN.md) is the argument and [`architecture.md`](architecture.md) is the
one page summary.

## Read in this order

| | |
| --- | --- |
| [`getting-started.md`](getting-started.md) | install the plugin, check it loaded, and give the database an authority |
| [`pairing.md`](pairing.md) | let a program in, and decide what it may reach |
| [`access-control.md`](access-control.md) | name profiles, and write the rules that say what "may reach" means |
| [`clients.md`](clients.md) | see who is in, and take one back out |
| [`audit.md`](audit.md) | where the record of every decision is, and what a line in it says |
| [`architecture.md`](architecture.md) | what the pieces are, on one page |
| [`pykeepassgate.md`](pykeepassgate.md) | the client, from the other side |

## What it is, in four sentences

Every database is its own certificate authority. A program that wants in is handed a certificate by
that authority, and the database keeps a record saying which profiles that certificate may use. A
profile is a name the database keeps, and the rules on its groups and entries say what each name
reaches.
Take the record away and the program is refused from its next request onwards.

## What it is not

**It is not a way to share a database.** One KeePass, running on one machine, with the database
open. Nothing is served while KeePass is closed, and a locked KeePass answers only that it is
locked.

**It is not a defense against a program running as the same account.** Anything on the machine
running as that account can read what this plugin reads. The design concedes that throughout, and so
does this. What it defends against is a program deliberately let in asking for more than was meant.

## The pictures

Every screenshot here is taken by driving a real KeePass with the plugin in it, and retaken by
one command when the tab changes, so what appears is what the version being read about looks like. The database
in them is invented and thrown away afterwards: no name in any picture belongs to anything real.
