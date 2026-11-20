# Deciding what a profile reaches

A profile is a name the database keeps. On its own it grants nothing. The rules that give it
meaning live on its groups and entries, and are written on the **KPG** tab of the group and
entry dialogs.

Profiles are managed on the **Profiles** page of the database settings, and approving a pairing can
create one. See [Naming profiles](#naming-profiles).

## Naming profiles

Open **File**, **Database Settings**, pick the **KPG** tab, then the **Profiles** page.

![The profiles page](images/profiles.png)

| | |
| --- | --- |
| **Profile** | the name, which is what a rule and a certificate both carry |
| **Rules** | how many groups and entries have a rule for it, whatever the rule grants, which is how many rules removing it leaves inert; nothing in the recycle bin counts, since no rule there decides anything |
| **Clients** | how many admitted clients hold it, counting only those the gate lets in: the clients page marks the rest |
| **X** | removes it |

**The database needs its certificate authority before a profile is named.** On a database with no
authority the page refuses a name, and the line under the list reads "This database has no
certificate authority yet. Create it on the Certificate Authority page first." Once
[**Create authority**](getting-started.md#create-the-authority) has been pressed, a name can be
added on the same visit to the dialog, and **OK** writes the authority and the name.

Type a name and press **Add**. A name is ASCII letters, digits, hyphens and underscores, up to 64
of them, so the letters are A to Z without accents, and it is not `default` in any mix of capitals,
which the stored rules keep for a meaning of their own. It grants nothing until a rule names it.
A database holds as many names as are added, and past the rows the page has room for the list
scrolls down.

The other way one arrives is pairing: a program asks for profiles by name, and any this database does
not name now and no client holds is marked "(new)" on the approval dialog. Approving adds it, and
a rule still naming it from before it was removed applies at once.

A pairing writes that name straight to the database, where every other change on this tab waits for
**OK**, because the name is written by the same operation as the record it belongs to. A name
created that way while this page is holding an edit is kept. What **OK** writes is the last press
made here for each name, held against the list as it stands when **OK** is pressed: a name added
here goes on the list, a name removed here goes off it, and a name a pairing added in the meantime
stays. A name added here and removed again before **OK**, or removed and added again, writes
nothing. The invite button is dead for as long as an edit is waiting all the same, because a
program approved for a profile that is part way through being removed would be admitted to a name
**OK** takes off the list, where it grants nothing.

**A name removed in another copy of the database is not added over that removal.** Copies can be
synchronized while this dialog is open, by a trigger on a timer for instance, and a synchronize
can bring in a removal of a name that was added on this page a moment before. A removal counts
against every addition made without knowing of it, so **OK** does not write that name. Once the
page shows the name gone, the line under the list reads "The profile reader was removed in another
copy of this database while this dialog was open, so OK will not add it." Everything else on the
tab is written at **OK** all the same, and the plugin's log names the name that was left out. To
have the name after all, add it on this page again: that is adding a profile back, below, where
the page asks first.

A name added here that will not be added for any other reason gets a line of its own, "The profile
reader cannot be added to the store as it stands now, so OK will not add it."

**Where the plugin cannot write into its own group in this session, the page shows neither the row
that adds a name nor the X**, and neither while the gate answers `503 split` or `503 outdated` for
the database: the plugin's group then holds the records of more than a single store, or records
that only a later release of the plugin reads. The Certificate Authority page states why.

**Removing a profile takes it off this list and touches nothing else**, and from then on it grants
nothing. A program is given only the profiles its record holds that this list still names, so a
rule naming a removed profile stays on its group or entry, inert, and the access editor shows it
struck through. The page states how many rules still name it and how many clients still hold it.

That holds wherever the rule comes from: a group or entry dialog that was open while the profile was
removed and is then accepted, an old version of an entry restored from its history, or a copy of the
database merged in. None of them can bring a removed profile back into force through its rules, so
none of them has to be closed or checked first.

**Copies of the database that each changed their profiles keep every change once they are
synchronized.** Each name added and each name removed is an entry of its own in the plugin's
group, and a synchronize in KeePass brings each copy the entries it lacks. A name added in a copy
is on the list in every copy it is synchronized with. A name removed in a copy is off the list in
each, including where another copy still named it, or had removed it and added it again without
knowing of this removal: a removal counts against every addition made without knowing of it.
Adding the name again after the synchronize puts it back, and the clients page shows it as removed
on every client holding it until then. After synchronizing copies that each changed their
profiles, check this page.

**The names are entries of the plugin's own group**, `KeePassGate`, and this page is where they
are added and removed: [The plugin's group](getting-started.md#the-plugins-group) covers what
editing or deleting an entry there by hand does.

**Adding a profile back is a full undo.** Every rule still naming it applies again, and every client
still holding it regains it, so the page asks first: it states how many rules come back into force
and names every client that regains the profile, each once by its client key, the fingerprint of the
certificate it paired with: a program that renewed and has not given the older certificate up is one
client, and two programs that chose the same name are two, each named with the start of its key,
"agent (3f2a9c1e)". This replaces naming such a name once with how many certificates it held, which
could not tell the two apart. A client whose certificate has expired is not named, since it regains
nothing: the gate refuses it before its record is read. The one exception is a client that
[renewed](clients.md#expiry) its certificate while the profile was off the list: the certificate it
renewed to was recorded without the profile and does not regain it, while the older one, until the
client gives it up, still holds it and does, which is why the warning still names that client.

As everywhere else on this tab, nothing is changed until **OK** is pressed on the settings dialog,
and **saving the database** is what puts it on disk. Cancel discards it.

## Where the rules go

Open a group with **Group**, **Edit Group**, or an entry with **Entry**, **Edit Entry**, and pick
the **KPG** tab.

![The KPG tab on a group, with a rule on it](images/group-access.png)

The line at the top states the same thing on both dialogs: rules are inherited from parent groups
and can be overwritten. On a group that reaches every entry in it and in the groups inside it; on an
entry it reaches that entry, with whatever the groups above grant shown among its own rules.

## Writing a rule

A rule is a profile and an eight character mask. **Neither is typed.** Click the empty line at the
bottom of the table and pick a profile from the list, then pick what it may do from the list the
**Permissions** column opens.

The profiles offered are the ones this database names, less any that already have a rule here. A
profile a group above grants is already on the table in gray, and the way to change that one is to
pick a rule on the row that is already there. A list of profiles too long to show whole scrolls
down.

Nothing here can be typed because both halves are exact. A mask spelled by hand is a rule nobody
meant, and a profile spelled by hand is a rule for a name that may not exist, which grants nobody
anything and reads exactly like a rule that works.

The keyboard opens both lists with **F2** or **Alt+Down** on the cell. Inside a list, **Enter**
accepts and **Escape** leaves things as they were.

**Permissions is unavailable on the empty line**, because a rule is a profile first. It opens by
itself once a profile is chosen.

**Which profile a row is for is settled once the row exists.** To use a different one, remove the
rule with the **X** and choose again. Changing it in place would be two edits wearing one gesture:
the old profile silently loses its rule and the new one silently gains whatever the row held.

The eight characters are always in the same order, and a dot means "not this one":

| | | |
| --- | --- | --- |
| `C` | create | make a new group or entry here, or add a field or an attachment to an entry |
| `R` | read | read a group's notes, or what an entry holds |
| `U` | update | rename a group or change its notes, or change what an entry holds |
| `S` | strip | take fields and attachments off an entry, though not the five every entry has, which are emptied instead |
| `D` | delete | **removals are allowed here**: see below, because this one is asked twice |
| `L` | list | see that something exists, and whether an entry has expired, without reading it |
| `A` | attachments | **and the attachments too**, for whichever of the above this rule grants |
| `P` | protected | **and the protected values too**, for whichever of the above this rule grants |

So `.R...L..` is read and list, and nothing else. `.R...LA.` is the same over entries and over the
attachments inside them. `C.......` is create and nothing else, which is what an append-only program
wants. It still learns one thing about what is there: adding a field or an attachment under a name
the entry already holds is refused as taken, so such a program can find out, one guess at a time,
which names an entry holds. It never learns a value that way.

**A program that may change or strip a part of an entry it is not shown learns the same and
changes nothing doing so.** A tag is the token a program names to change an entry only if nothing
changed since it read it, and a tag that does not match is refused. But a field or an attachment
the entry does not hold is refused as missing before that, so naming a tag that cannot match tells
the program whether a name is there: one guess at a time again, and never a value. That is open to
a program without `R` on the entry, which is handed no tag at all, and to a program with `R` and
`S` and no `A`, which is shown nothing of the attachments and may still strip them.

**A program holding `R` is handed a tag of what its rules let it read, and of nothing else.**
With `P` in the same rule the protected values are in its tag, with `A` in the same rule the
attachments are, and with `A` beside `L` alone their names and sizes are, since that is what
listing them shows. A value a program may not read is in its tag as a mark that something is
there, the same mark whatever the value is, so its tag holds nothing of the value: when such a
program writes a guess at a value it may not read, its tag moves the same whether the guess was
right, with the entry's modification time and with nothing else. A protected title or address
that a listing shows the program, under `L` with `P`, is shown to it by a read as well once it
holds `R`, and is in its tag: what a program is shown of an entry does not depend on which way
it asks.
Two programs that see different
things of an entry hold different tags for it, so a tag passed from one program to another works
only between programs that see the same. A rule widened or narrowed so that a program sees more
of an entry or less makes the tag it holds stale, so its next write under that tag is refused
once and it reads again; a rule widened where nothing new comes into sight, `A` granted over an
entry with no attachments, leaves the tag as it was. And a program that writes a value it may
not read, a password it rotates without `P` beside its `R`, is told that somebody else changed
that value in between only when the change moved the entry's modification time, to the second,
which every edit made in KeePass does. **This replaces an earlier rule**, under which a tag
covered the whole entry and went only to a program whose one rule read all of it, attachments and
protected values included: every entry KeePass makes holds a password, so a program without `P`
was handed no tag at all and could not write against what it had read.

**Nor does writing a value tell such a program what was there.** An update naming only what an
entry, an attachment or a group already holds writes nothing for a program that may read those
values. For a program that may not, it is written like any change, so sending a guess at a value
costs the same and looks the same whether the guess was right. What a program may read here is
what reading the entry would show it, a protected title or address under `L` with `P` included.

**Nor does anything tell a program how long a value it may not read is.** No read and no write
is refused for an entry's size, so no refusal moves with a value: an entry is read whole
whatever it holds, with a value the program is withheld left out, and reading that value's
field alone answers that it is withheld, however long it is. **This replaces an earlier
rule**, under which a write that would take an entry past a size was refused with the entry's
whole size in the refusal: a program that may write and not read could fill an entry up to the
limit and learn from the refusal how long its password was.

**`L` sits below `R` on purpose.** Knowing that an entry exists is not knowing what is in it.

**`S` and `D` are two sizes of removal.** `S` takes parts off a node: an entry's fields and its
attachments. `D` states removals are allowed here, and removing a group or an entry asks for it
**twice**, from the group the thing is leaving and from the thing itself, and one profile has to
grant both.

That is what allows the grant most people want. Put `D` on one group and a program may
create and remove entries and subgroups inside it, including ones it makes later, while **the group
itself stays**, because nothing above it allows removals. Put a rule without `D` on a single entry
and that entry cannot be removed while its siblings still can.

So put `D` on the group whose contents may come and go, rather than at the top of the database. A
`D` at the top is what makes everything below it removable, and then holding one thing back means
writing a rule for it.

**A group is removed only once it is empty**, and a group that still holds something is refused as
such. So a program holding `D` without `L` can find out whether a group it knows of holds anything,
though never what.

**Where a removed entry goes is out of the program's reach.** KeePass moves it to the recycle bin
when the database keeps one, and this API cannot see the bin at all: it is not listed, not
readable, and not addressable. So a program cannot read something by removing it first, unless a
merge later leaves that bin an ordinary group, and getting a removed entry back is done in KeePass,
where it is visible.

**The bin is the group the database names as its recycle bin when a request is served, and no
other group.** A synchronize or an import can change which group that is. When two copies of the
database each made a recycle bin, the merge keeps the choice of one copy, and the group the other
copy used, or this copy's own bin when the other copy's choice is taken, stays in the tree as an
ordinary group. An import can bring another database's bin in as an ordinary group as well. Nothing
remembers that such a group was a bin, so a program reaches it, and everything deleted into it,
the operator's own deletions included, under the rules that reach it where it sits: for a bin
KeePass made under the root, the root's. After a synchronize or an import, check which group
**File**, **Database Settings**, **Recycle Bin** names as the recycle bin, and empty or delete every
other group that was a bin.

**A field whose name begins `KPG.` is the plugin's own**, on whichever entry it is, and is out of a
program's reach as the bin is: no read and no listing shows it, and a program can neither add such
a field nor change or remove it. A field meant for programs needs another name.

**`A` and `P` are switches, not verbs.** Neither means "may read attachments" or "may read
passwords". Each means "and those too", for whatever else the same rule grants, so a rule holding
switches and no verb grants nothing at all.

**`P` is the difference between recognizing an entry and signing in as it.** Without it, a rule that
grants `R` still answers with the title, the address, the names of every field and the values of the
ones nothing protects; where a protected value would be, the answer is `null`. `.R...L.P` is the same
rule with the passwords in it.

**`P` beside `L` shows a protected title and address, and nothing else that is protected.** A
database can protect titles and addresses as it protects passwords. A program is then shown them
when a rule it holds on the entry carries `P` beside `L` or beside `R`, and it is shown them the
same way by a listing and by reading the entry, whichever of the two its rules let it do: what a
program is shown of an entry does not depend on which way it asks. So a program that lists under
`L` with `P` and reads under `R` reads the title it was listed, and a program that reads under `R`
with `P` and lists under `L` is listed the title it reads. The password and every other protected
field still need `R` and `P` in one rule. Without `R` a rule holding `L` with `P` shows the title
in listings and nothing by reading, since reading is not something it grants.

That is the grant to give a program that has to work out which entry it is looking at. `L` alone
does not tell it enough to tell two entries apart, and `R` used to tell it everything.

**It governs changing them too.** A rule with `U` and without `P` can correct an address and cannot
overwrite a password; one with `C` and without `P` can add a field and not a protected one. This is
worth more than it looks: a program that quietly replaces a password with one somebody else chose
has taken the account, and nothing in a log of "an entry was updated" states which happened.

**A write verb reaches further than the entry it is written on.** KeePass resolves what some
fields hold rather than using them as they stand: a URL it is told to open, a value being copied, an
auto-type sequence. A reference to another entry is resolved against the whole open database, which
knows nothing about these rules. So a program that may change one entry can put a reference to
another entry's password where the operator's own click resolves it. So give `U` and `C` as
carefully as `R`, and treat a program holding a write verb on one entry as a program whose values
are going to be read and acted on.

**The gate stores a URL as it is written, whatever KeePass will make of it.** Opening an entry's
URL is where KeePass does the most with what a program wrote. It resolves a reference in it, so the
URL can carry another entry's password to the host it names. It replaces `%NAME%` with the value of
an environment variable. It hands a path, a network share or any scheme Windows has a program for
to that program. And with a URL override switched on, under **Tools**, **Options**,
**Integration**, it puts the URL into a command line, with the entry's user name or password where
the override names them. KeePass asks first only before it runs a command line, and that question
can be switched off. So don't open a URL a program may have written.

**A link in an entry's notes is opened the same way, and by a single click.** The entry view, the
pane under the entry list, makes a link of a web address in the notes, the title, the user name or
a field of the entry's own, percent signs and all, and clicking it hands it to the browser as
opening the URL does: `%NAME%` becomes the value of the environment variable of that name first,
and nothing asks. A brace ends such a link, so a reference does not travel this way. So a program
that may write an entry can put an address of its choosing there with a variable's name in it, and
the operator's click sends the variable's value to that address. A percent sign is as ordinary in a
note as a brace is, so the gate stores it as written, and a link in an entry a program may have
written is to be left alone as its URL is.

**Global auto-type chooses an entry by its title.** When the operator presses KeePass's global
auto-type hot key, an entry matches if its title is contained in the title of the window in front,
and when only one entry matches, KeePass types its user name and password into that window without
asking. So a program with `U` on an entry can rename it after a window whose title the program
chooses, such as a web page it serves, and the operator's next press of the hot key there types a
password the rules never let it read. KeePass resolves a reference in what it types, so a reference
in the user name types another entry's password as well. Where KeePass is also set to match on an
entry's URL, the URL can do the same.
To see which entry is about to be typed, turn on **Always show global auto-type entry selection
dialog** under **Tools**, **Options**, **Advanced**.

**A write verb can also push an entry's older versions out of its history.** Every change the gate
makes to an entry first keeps the entry as it was, as an edit in KeePass does, and KeePass keeps
only as many of those versions as **Database Settings**, **Advanced**, allows, so a program that
changes an entry more times than that has pushed out every version from before it started, and the
audit names each of those writes. A program that may change a value and not read it does so even
by writing the value that is already there, since for that program every update is a change.

**A write verb can also make an entry as large as its program likes.** A request carries at most
65536 characters in a value and 1 MiB in all, and nothing limits how many requests add to an
entry, so a program holding `U` or `C` on an entry can grow it, and the database file and the
time every save takes with it, a request at a time; the audit names each of those writes.
Reading such an entry is not refused: the gate writes the answer as it is sent, so a large
entry costs KeePass little memory, and costs time on the connection that reads it. A program
holding `S` makes the entry smaller by stripping fields. The gate used to refuse the write
that would take an entry past a size, and stopped because the refusal told a program how long
the values it may not read were.

**An attachment is opened by its name.** A program holding `C` with `A` chooses what an attachment
is called, and KeePass hands an attachment it cannot show itself to whatever Windows starts for its
name's ending, a program included, when the operator double-clicks it. The gate refuses a name
holding the characters that reorder how text is displayed, so a program cannot show the operator a
document's name on something that ends in `.exe`, but a name that states `.exe` plainly is still one
it may choose. Whatever a program may create, the operator's KeePass may open.

**A field is filled in by its name too.** The gate refuses a field or attachment name holding a
character drawn as nothing at all, a zero-width space, a joiner, a soft hyphen, a byte order mark,
a variation selector or a Hangul filler among them, so a program holding `C` cannot add a second
field drawn exactly as `Password`, beside the real `Password` and holding what the program chose,
for the operator to type a credential into or copy from. A name is not checked for looking like
another in any other way: a letter from another script, or a space at the end, still makes a name
of its own, which the entry's **Advanced** tab shows beside the others.

**What counts as protected is KeePass's own answer**, not this plugin's. KeePass keeps some fields
encrypted in memory, the password by default, and an operator can mark any field or unmark one. A
field nobody protected is not a secret here however secret it reads, so a program given `R` and not
`P` will still be told what is in the notes if nothing protects them.

**A removal can answer "busy" for a moment.** If the database has never had a recycle bin, the
first removal makes one, and while the Database Settings dialog is open it holds its own copy of that
choice: making the bin underneath it would be undone by its OK. So the removal is refused until that
dialog is closed, which is worth retrying rather than an error.

**A change waits while KeePass is busy with something of its own.** A change and the save that keeps
it happen together on KeePass's own thread, and the things KeePass takes a moment over, saving,
importing, synchronizing, exporting, and anything it shows its progress window for, such as **XML
Replace**, all use that thread while they run. A change made in the middle of one of them would be
made against a database that is being written, which ends with half of somebody's import on the disk
or a change a program was told had been saved not being there. So a change waits for KeePass to
finish, for up to half a minute, and answers "busy" when KeePass is still at it after that. While
KeePass is waiting for an answer to a message box, a change answers "busy" at once, since nothing
happens on that thread until the box is closed. While KeePass's TAN wizard is open on the database,
every change answers "busy" too, since the wizard adds its entries from inside its own OK, which can
land part way through the save the change ends in. Every refusal of this kind tells the program to
come back in two seconds.

**Neither switch applies to `S` or `D`.** Removing something shows nobody anything, and a rule
that may remove an entry already takes everything in it. So `...S....` removes an attachment on its
own, and asking for a switch first would forbid the smaller act while allowing the larger.

**With one exception, and it is about `C` rather than `S`.** A rule holding both `S` and `C` could
take a protected field away and put another back under the same name, which ends with a protected
field holding a value the program chose. That is writing a secret, which is what `P` governs, so a
program is refused the strip of a protected field while any of its profiles holds `C` there without
`P`, whichever profile holds the `S`. A program whose profiles hold no `C` there, or hold it only
with `P`, is unaffected: it has nowhere to put an unprotected value back.

## Inherited rules

Rules from the groups above are shown among the local ones, in gray, with nothing in the last
column:

![The same tab on an entry, showing what the group above grants](images/entry-access.png)

Gray means this node does not carry that rule; a group above does. There is nothing to remove here,
which is why the last column is empty on those rows.

This is deliberate. A table listing only what this node grants would report nothing for an entry
that a group above already opens wide, and somebody reading that would be invited to grant more.

**A line through the mask means it no longer arrives.** That is what an inherited rule looks like
once **Refuse any profile not named here** is checked on this node: the rule is still up there and
still states what it states, and this node has stopped letting it through. The row stays on the
table because seeing what the check box refuses is the reason for checking it. To let one of them
back in, click its mask, choose what it may do here and press **Enter**, which puts a rule of this
node's own on the row. **Enter** keeps the check marks even when they are the ones the rule above
already had; clicking away from them does not, so looking at a refused rule never lets it through.

**A line through the mask also marks a profile this database no longer names**, on a rule of this
node's own or one from above. It grants nothing until the profile is added back on the **Profiles**
page, and its tooltip states as much. A rule from above on a node whose seal refuses it, or whose
own rules will not read, stays refused there once the name is back, and its tooltip states that too.
Hovering over the profile shows its whole name, which a long name's column cuts.

## Nearest wins

For any one profile, the nearest rule wins.

- A rule on the entry beats every group above it.
- A rule on a group beats the groups above that one.
- A profile that no rule names is answered by whatever the groups above state.

Different profiles do not interfere: they are worked out separately and combined, so a program
holding two profiles may reach what either of them reaches. A change has to be allowed by one
profile, though: removing an entry needs `D` from one profile in both places, and two profiles
holding one `D` each do not add up to it. Listings are where profiles work together: a listing can
be taken from a group one profile may list, and shows every item in it that any profile the program
holds may list, with a protected title where a profile it holds carries `P` beside `L` or `R` there.

An empty mask is a rule too. `........` on a group grants that profile nothing from there down,
until a nearer rule below names the profile again, which is how a subtree is closed off for one
profile while leaving the rest alone. A rule further down that names the profile still grants what
it states, so look below before relying on the closing. A profile just
picked arrives with exactly that, so a new rule starts by granting nothing and is opened
deliberately.

## Refusing everything not named

The check box at the bottom, **Refuse any profile not named here**, is stronger than any rule and
worth understanding before using.

Without it, a profile with no rule here keeps whatever the groups above grant it. With it, this node
refuses every profile it does not name, and a grant from a group above stops reaching here or
anything under it.

It closes the way in from above, not the way in from below: a group or entry under this one that
names a profile itself still grants what its own rule states, because the nearest rule about a
profile is the one that answers.

Use it on a group holding things no program should reach.

## When the tab changes nothing

**The tab shows the rules and will not change them** in three cases, and the line over the table
gives the reason:

- **Several entries edited together.** What KeePass's dialog holds for them is no single entry's
  rules, so open one entry at a time to read or change its rules.
- **An earlier version of an entry**, opened from its **History** tab. The entry's own rules shown
  are the ones that version had, not the ones in force, while the rules from the groups above are
  what those groups grant now, and KeePass keeps nothing changed on that dialog.
- **A KeePass this plugin cannot reach into**: a KeePass that keeps a dialog's settings somewhere
  the plugin does not find them, or that does not tell it which database, group or entry the dialog
  is about. The rules can be read and not changed, and
  [the plugin's log](getting-started.md#the-plugins-log) names what was missing.

**Rules that cannot be read are refused rather than skipped.** A version of a rule deleted in a
copy of the database that another copy kept, edited by hand or written by a later version of the
plugin is shown as **Lost**, and nobody can tell what it was taking away, so the profile is refused
at that group or entry and everything under it unless a nearer rule admits it. A rule chosen on
that row replaces the version when OK is pressed. A **Refuse any profile not named here** check mark
that cannot be read refuses every profile not named there, until the box is set again.

**A rule above that cannot be read** refuses here as well: nothing beyond it arrives, so a profile
that neither this node nor a group in between names is refused. It is replaced on the group that
holds it, not here.

## Press OK

**A rule is written by the dialog's OK and discarded by its Cancel or Escape**, like everything
else on those dialogs. Then save the database as usual. Nothing on the tab is written while the
dialog is open, and a rule KeePassGate could not write at OK is named in a message once the dialog
has closed.

The **KPG** tab in the database settings dialog works the same way: both tabs hold every change
until OK, and OK and Cancel do what they promise.

## Where a rule is stored

In the plugin's own store in the database, beside the certificate authority and the clients,
under the identifier of the group or entry it is about. It travels with the database, it is covered
by the database's encryption, and no other plugin's data is touched.

**A rule stays with its node and nothing else.** A group or an entry moved elsewhere keeps its rule.
A copy is a node of its own, with an identifier of its own, so **Duplicate Entry** and **Duplicate
Group**, copying entries or a group into another group, and making an entry from a template give the
copy no rule: it grants what reaches it from the groups above, so look at the **KPG** tab of the
group it lands in before moving it somewhere a program should not reach. An earlier version of an
entry, restored from its history, changes no rule.

## What a program actually gets

Two answers, and both have to allow it:

1. **The admission record**, which lists the profiles approved for that certificate. Only the ones
   this database still names count.
2. **The rules**, which state what those profiles reach.

The record is read from the open database on every request, so removing it takes effect at once.
A node a program may not reach and a node that does not exist answer the same way, so a refused
program cannot map the database by asking.

**What a program may see still names the group it is in**, by identifier, even where the rules
hide that group. So a program shown an entry inside a group it may not reach learns that the group
is there and which of what it is shown share it, though never the group's name or anything else in
it.

**A program can ask what it may do at a group or an entry** before it tries. It is told one mask for
each profile it holds, written as this tab writes them, and nothing about a profile it does not
hold. Each mask is what the rules grant at that node, as this tab shows it there, so a `D` on an
entry can still be refused by the group it is in, which removing it asks as well. A switch beside
nothing but `S` or `D` is left out, since it reaches nothing removing does not already take. A group
or an entry where none of its profiles may do anything is answered as one that does not exist, and
the audit records the question as it records any other.

Nothing else is asked. An entry KeePass shows as expired, by its date or after **Expire Now**, is
still served, because expiry is KeePass's advice to whoever uses the entry rather than a control.
A program holding `R` is told when an entry expires, one holding `L` may ask a listing to leave the
expired ones out, and one holding `U` may move the date or take it off, as it would after rotating
the password. To take a retired credential out of a program's reach, take it out of the rules.

**What a program reads is what the entry stores.** A field reference such as `{REF:P@I:...}` or a
placeholder such as `{USERNAME}` reaches it as that text, not as the value KeePass would show or
type, because resolving one reads another entry, or the machine, without asking the rules. An entry
that borrows its password from another by reference gives a program the reference, and the program
needs the rules on the other entry to read what it points at. The reference client follows such a
reference when it is asked to by reading the other entry through the gate, so the rules on that
entry decide, and the audit has a line for the read.

**A program's new entry expires only when the program asks it to.** KeePass can give every new
entry an expiry, under **Tools**, **Options**, **Security**. An entry a program creates gets that
expiry when the program asks for it, and none when it does not, so a rotation policy set there
reaches what a program makes only where the program asked for it.
