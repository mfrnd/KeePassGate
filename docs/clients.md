# Who is in, and taking one back out

The **Clients** page of the **KPG** tab lists every program this database has admitted, and takes
one away.

![The clients page with an admitted client on it](images/clients-admitted.png)

## The list

One row per admission record, newest first, because the one being looked for is usually the one
just approved.

| | |
| --- | --- |
| **Client** | the name the program asked to be called when it paired |
| **Profiles** | the profiles approved for it, which is what its rules are worked out from. One this database no longer names is dimmed and marked "(removed)": the client still holds it, and it grants nothing unless the name is added back |
| **Admitted** | when it was approved, in UTC and to the second |
| **Until** | when its certificate stops working, or why it does not work at all |
| **X** | takes this client's admission away |

A client whose certificate has expired is refused like any other, and **Until** says `expired`
instead of a date, by this computer's clock. The row goes with the next change the plugin itself
makes to the database: revoking a client, pairing one, or keeping, recreating or deleting the
authority. The plugin's log names each row that goes that way, because a clock set wrong can cost
one that setting it right does not bring back. Until then it says `expired`, because a row that has
expired and a row somebody removed look the same once the row is gone, and only one of them needs
doing anything about.

**Until** says `not this authority` for a record whose certificate this database's authority did not
issue. The gate checks that before anything else, so such a record grants nothing whatever its dates
say, and a date there would be the one thing about the row that is not true. Records reach that
state by outliving the authority that made sense of them: an authority recreated or deleted while a
program was pairing, the authorities not kept when a database held more than one, or a synchronize
or a partial restore carrying records across. Removing them is the same **X**, and there is nothing
else to do with them.

Two more phrases take the date's place, for the two things the gate asks next, in the order it asks
them. `not for client use` is a certificate this authority issued for something other than a
program to present, such as its own host certificate: only a file written by hand or merged from
another copy holds such a record, since pairing records only the client certificates it issues, and
it grants nothing. `not yet valid` is a certificate whose first moment has not come, which a clock
that is wrong on the machine that issued it, or on the one reading the file, produces: it grants
nothing until then. The expiry warnings on the Certificate Authority page leave all three out,
since the gate refuses them before their end matters.

**A record that cannot be read at all** is listed too, after the clients: damaged by a merge,
edited by hand, or written by a later version of the plugin. It admits nobody and has nothing to
show but the start of the key it is stored under, so the row holds that, `cannot be read` under
**Until**, and the **X**, which takes it away like any other, once OK is pressed. The Certificate
Authority page counts such a record and says how many cannot be read.

The list is read from the open database rather than remembered, so it never shows a client that
has gone or misses one that has arrived. A program that pairs while this dialog is open is added
to the list as it is approved, without the page having to be left and come back to, and bringing
the page to the front reads the database again anyway.

## Revoking

Press the **X** at the end of a client's row.

The row goes, and the page says so. **Nothing has happened to the database yet**: press **OK** on
the settings dialog to make it real, then **save the database** to write it to disk. Cancel discards
the revocation along with everything else done on this tab.

That is worth knowing rather than assuming, because it is the one place where waiting costs
something. A revocation that has not been accepted has not revoked anything, so if a
client's access is being taken away out of necessity, press OK and save before doing anything else.

The mark is in the row it acts on, so there is never a question of which client a press would
remove. The same mark takes a rule away in the access editor.

**Once accepted it takes effect at once, and saving is what keeps it.** Records are read from the
open database on every request, so from the next request onwards that client is told it is not
admitted. Nothing has to be reloaded and nothing has to be told.

The client keeps holding a certificate. It has simply stopped meaning anything to this database,
which is the point of the record existing separately: a certificate proves who a program is, and a
record is what grants it anything.

A revocation survives a synchronize and an import, including one from a copy of the file taken while
the client was still admitted. Revoking adds a mark of its own to the database, beside the client's
record, and the mark is never changed afterwards. KeePass merges by adding what the other copy has
and never takes a mark away, so the client stays refused whatever the other copy holds, whatever
the clocks of the computers involved say, and whether or not the file kept the time of each change.

The other copy can still bring the client's record back. It admits nobody, and it is not listed as
a client: a line under the list says how many revoked clients reappeared in a merge, and that they
stay refused. The next change the plugin itself makes to the database takes those records away and
the line with them: revoking another client, pairing one, or keeping, recreating or deleting the
authority.

The mark stays until the client's certificate expires. After that the certificate is refused on its
dates anyway, and the next revocation takes the mark away, and the emptied record with it.

**All of that holds in KeePass 2.48 or later with the plugin in it, and only there.** Other programs
can open the database, KeePassXC and KeePass before 2.48 among them, but saving or merging in one of
them can quietly bring back a revoked client, a replaced authority or a removed profile, because
their merges replace or drop what the plugin keeps. So save and merge a paired database only in
KeePass 2.48 or later with the plugin, and after any change made anywhere else, look at this list
and at the Certificate Authority page.

**Nor is anything here managed from KeePass's own Database Maintenance dialog.** Its **Plugin Data**
tab lists what the plugin keeps, each revocation's mark among it, and deletes a row on request.
Deleting a mark there takes away the one thing a merge cannot undo, so a copy of the file from
before the revocation can admit the client again. Revoke on this page, and leave that tab alone.

## Getting a client back

There is no undo. Pair it again: hand out a new invitation, let it ask, approve it. It gets a new
certificate and a new record, and the profiles are chosen again.

## Revoking everything at once

**Recreate authority** on the Certificate Authority page replaces the authority, which invalidates
every certificate it ever issued and removes every record and every revocation with it. The old
authority is retired rather than only removed, so a copy of the file from before cannot put it back
into service. Every program has to pair again.

That is the recovery from an authority no longer trusted. For one program, use the list.

**An exported database needs the same recovery if it goes astray.** KeePass's **File**,
**Export**, **KeePass XML (2.x)** writes everything the database holds in the clear, the
authority's signing key and every client's record included, and the plugin is not told. Keep such a
file as carefully as the database itself, and recreate the authority if it is lost.

## Expiry

The plugin warns before a client stops working, on the Certificate Authority page, in the last
third of its certificate's life and never more than thirty days ahead: thirty days for a
certificate of a year, ten for one of a month, a little over two for one of a week. So a short
certificate is not reported as about to expire from the day it was issued. A client that is about
to expire has to pair again; there is no renewal. Pairing again writes a new record beside
the old one rather than replacing it, so the program is listed twice under one name, told apart by
**Admitted** and **Until**, and the old certificate keeps its profiles until its own date. Revoke the
older row once the new one is approved.

**The authority and the certificate KeePass answers with are good for a hundred years**, and
nothing counts down to that. They are made together and end together, so neither runs out under the
other, and nothing on the tab warns about them or turns a pairing away ahead of them. A leaked key is
answered by recreating the authority, as above, however long it has left.
