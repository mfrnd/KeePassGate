# What the completeness check found in the draft EVALUATION.md

The draft knew ten options (not crdt, not grant-only) and only the interim Android results.

## Gaps

1. The timestamp-named-field idea, with its preprocessing step, was measured and the evaluation does not report it. ground-keepass.md 1e (X13) ran timestamp-named fields in one shared entry through KeePassLib 2.62 Synchronize. With a step on load and after the merge that copies fields back out of history, 24 of 24 survived when the writes were at least 5 s apart [M]. 12 of 24 survived at the same second, 4 of 8 with HistoryMaxItems 0 [M], and the merge's own history trim runs before any plugin hook [R; M]. The evaluation turns 'preprocessing' into a fold that writes nothing (5.1) and says the idea 'does not hold in any program' (3.2). It never gives these numbers, and it never gives the reasons the idea still fails outside KeePass: ties lose a side in KeePassXC, Keepass2Android and kdbxweb [M]; KeePassXC drops the loser behind 10 newer items [M meta]; Keepass2Android trims history at every load and Strongbox at every save [R]; other programs merge without the step; and a step that writes at open reverses O1.

   Why it matters: This is the user's own proposal. The recommendation's central move, from a field to an entry, should be argued from the measurement of the proposal as given, preprocessing included. The user should see where it works (KeePass, writes a second apart) and why that is not enough.

2. A narrowing made by moving a node can be undone by a merge, and the evaluation never mentions it. Two critics rate it serious and say it applies to every option: single-entry-survival 2.5 and hardened-custom-data-survival S3. KeePassDX puts an entry back into the arriving copy's group at equal times [R ground-android 3.1, 5.4]. KeePassXC regresses a move's location time when the other copy edited the entry (move-A-edit-B) [M ground-keepassxc 4], and a later KeePass Synchronize carries that through [M ground-keepassxc 9]. The matrix row 'Where a node sits' leaves out KeePassXC's move loss. The recommendation keeps nearest-wins inheritance, and 'What it does not solve' does not list this.

   Why it matters: It is an E5 widening that touches no KPG datum, so no storage choice catches it. The critics' changes are: pin the parents of the nodes a sealing group holds, or document that a narrowing is never made by a move. The operator needs this before choosing.

3. Writes that land after a dialog has closed are not covered. In the recommendation, rule versions are written at FormClosed through the single writer, and the writer can refuse: the lock is held by a connection's write, the progress window is open, or Edit Entries is open. wildcard-security 2.4 (serious) shows the gate then keeps serving the older, wider rule while the operator believes the narrowing is in place. rules-linked-security S4 (serious, measured midsave-first) shows that a rule change applied during KeePass's save leaves the file holding the old, wider rule and memory holding the new one, with Modified cleared [M]. The evaluation reduces S4 to 'a rule write can land inside a save' and raises 2.4 only for wildcard. The recommendation claims 'OK keeps a rule' and has no rule that routes rule writes through the writer's idle check, or that keeps an overlay in force from OK.

   Why it matters: O3 and E5. A narrowing that is not durable reaches a second gate, or comes back after a crash, as the wider rule, and nothing tells the operator.

4. The second witness is weaker than the recommendation says. store-entries-survival P1 proposes the custom data key and, in the same change, says the residue 'has to be stated'. With a fast KDF a KeePassXC merge delivers no key [M spike]. With a slow KDF, merging a stale copy into the current one replaces the dictionary and removes KPG.Revoked and KPG.RetiredAuthority [M spike, 96 runs]. So one ordinary KeePassXC merge plus one hand deletion of the revocation entry, or of the KeePassGate group, admits a revoked client. The recommendation also drops the writer's refusal while Database Maintenance is open, whose write-back on a changed key count removes a key the plugin has just written [R ground-keepass 4], and nothing re-asserts a missing key.

   Why it matters: E3 and E4 are absolute. The decision to keep the keys at all rests on the second witness, and the residual case is one ordinary act plus one ordinary merge, not 'several witnesses removed by hand'.

5. A revocation pruned by a machine whose clock runs ahead is not addressed. outside-file-survival P2 (serious, modeled), versioned-fields-survival P7 and hardened-custom-data-survival M2 all show it. A machine whose clock is ahead lets go of a revocation at 'expiry' while the certificate is still valid on other machines. KeePassXC never applies deletions [M ground-keepassxc 2], so the admission record the plugin deleted comes back, and the client is admitted until the real expiry. The recommendation keeps a revocation 'until the certificate has expired', with no margin.

   Why it matters: E3 says 'whatever the clocks', and CA.md allows certificates that last a day. The fix is cheap (keep revocations some margin past notAfter), but the evaluation never names the problem.

6. A 0.1.0 plugin still running elsewhere is not covered. rules-central-security S5 (serious): after conversion, a second machine still on 0.1.0 finds no rule in node custom data and inherits, which widens. wildcard-security 3.9: removing the KPG.Authority key travels nowhere, since KeePass never removes a key and KeePassXC restores the stale dictionary [M spike], so a 0.1.0 plugin serves the old authority and records without any revocation made since. The change: write KPG.RetiredAuthority for every old authority at migration. option-single-entry 13 says the opposite ('finds no authority and serves nothing'), and the evaluation does not settle which is right. Question 6 asks only about carrying a database over and about the old rule key refusing.

   Why it matters: 0.1.0 is released and O7 is open. A widening across two machines is the case the rethink exists for, and the fix costs one key.

7. The recommended design fails C0 for KeeWeb, and question 5 offers a fix that was measured not to work. Keeping timed KPG keys means a KeeWeb save or merge leaves a file KeePass 2.62 cannot open [M ground-others, kdbxweb library]. That is a C0 failure of the recommendation itself, but sections 4 and 5 list it only as a cost. Question 5's middle option, keys written without a time, is defeated by hardened-custom-data-survival M1 [M]: KeePassXC 2.7 and later stamps time-less items with the load time in a 4.1 file. Every KeePassDX and KeePassDroid save writes 4.1 [M spike; M measured-android]. KeePassXC 2.7.12 and later adds timed keys of its own to every file it saves [M outside-file-survival].

   Why it matters: The operator is offered a mitigation that was measured not to work. The KeeWeb defect may come back whatever the plugin writes, which changes the answers to questions 5 and 10.

8. The changes rules-linked-survival proposes for P1 and P2 are not carried, and the section 4 credits survive them. P1, measured (C1b, C1c, C2): Delete Duplicate Entries, or one deletion of the copy's link or anchor, leaves a copy wider than its original and wider than today's store, and the critic says E6 fails for the copy. The evaluation calls this 'not worse than 3.7', yet section 4 still gives rules-linked 'E6: yes, node as third witness'. The critic's forward pin is left to question 7. P2, measured (C3b, C3c): the 'two targeted losses' are one tidy-up that deletes both plugin groups, and KeePass's own way back (Remove deleted objects information, then Synchronize) rolls the rules back too. In the recommendation the store group also holds the authority and the revocations, so the same tidy-up plus a KeePassXC merge of an older copy, which also removes the keys, rolls back revocations as well. The critic's change, letting the node witness its newest version, is not considered.

   Why it matters: These are the E5 and E6 claims for the design being recommended.

9. The amendment in 5.1 leans on a feature the matrix classes as breaking. KPG.Anchor is a string written into a user's entry that other copies edit. In the rules-linked probe a first rule's anchor was gone in 6 of 8 KeePass cells, and in every KeePassXC cell where the other copy edited the entry later [M option 5.2 row 01]. In rules-linked that loss is harmless: the rule only limits. Under the amendment the rule is in force, but the node no longer marks its copies or witnesses a lost rule. So for exactly those entries the design falls back to rules-central's weaknesses: a copy escapes its narrowing, and a lost version goes unnoticed. Section 2.3 lists 'a field added to an entry both copies change' among the features that cannot carry the store.

   Why it matters: The amendment is presented as removing only the grants that wait. It also removes the fail-closed fallback for a lost anchor, and section 6 item 2 would need to probe this case.

10. Section 3.9 omits outside-file's serious problems, and the alternatives offered for question 1 omit their critics. Missing: survival P1 (one writer name on two computers; a synchronizer's conflict copy loses a revocation), P2 (clock), P3 (rules removed for absent nodes), P4 (Create and Recreate carry an older rule set); security #3 (rollback detection is host-local and lost on a restore or a migration), #4 (the writer invariant breaks when KeePass's configuration is copied), #6 (metadata disclosure) and #8 (a persistent ability to forge). The security critic suggests a narrower hybrid: rules stay in the file and only add-only refusal data moves out, which has no gap 2a. The evaluation never weighs it. The host-local memory proposed in 5.1 and question 1 inherits #3 and #4: it is lost at a restore or a migration, copied with the configuration, and absent on a second gate.

   Why it matters: Question 1 asks the project to choose between the file, a sidecar and a host memory without the known failures of the last two.

11. A model change was never considered: make the ACL such that a lost rule can only narrow ('grant low', no narrowing or seal below a wider grant, enforced by the editor). option-rules-central 13 says such a database is closed against copies, and rules-central-security S4 recommends it in documentation, but no option evaluates it as a variant. It would make copies harmless, make a lost rule fail closed (E6 by construction) and remove the reason to put anything in a user's groups, so rules-central (medium cost, nothing on user nodes) would be enough. It leaves the rollback of a removed grant and the moves, and it reverses the settled seal semantics.

   Why it matters: Questions 2 and 3 (copies, visible plugin data in every ruled group) could be answered by this model change rather than by a link on every ruled node.

12. The fallback in 5.2 (rules-central) is stated without its critic's partial change. rules-central-security S4 proposes that the plugin follow KeePass's own copy commands (Duplicate Entry, Duplicate Group, the drag copy, paste, templates) and write a version for each copy. That covers KeePass's own copies, where every measured copy case was, with nothing in a user's group; only other programs' copies remain. The evaluation says copies 'then inherit from where they land'.

   Why it matters: The question 2 trade-off between links in user groups and copies that escape looks starker than the sources show.

13. Records with public, derived identifiers can be displaced. The recommendation derives the authority's and the admission records' identifiers from certificate digests, which are public, and does not say how a revocation entry's identifier is formed. wildcard-security 3.3 measured that a node carrying a witness's identifier in a merged file takes the witness away under KeePass Synchronize, because the newer object wins whole [M]. The proposed change is an HMAC of the digest under a store key. The store identifier in rule 3 marks a record's content but does not protect its identity.

   Why it matters: E9 says a merged file vouches only for itself. A prompt-injected client knows its own certificate, so a file it crafts could displace its own revocation entry, or the authority entry, under a newer time.

14. The store identifier and a retirement in another store. versioned-fields-survival P10: when one file holds two stores (two copies each made an authority before they met), 'records of another store count for nothing' means a retirement written in one store stops counting once home moves to the other. The critic's change: a retirement counts for the authority it names, in whatever store it is found. The recommendation's rule 3 applies the store identifier to every record and never states this case.

   Why it matters: E4 and E12, and the 503 undecided case the recommendation keeps 'as today'.

15. Bounded growth (A5) and the profile ceiling are not assessed for the recommendation. KeePassXC never applies deletions, so every admission record the plugin deletes, every replaced rule version, every profile act and every renewal answer comes back and stays in a file KeePassXC merged. That is the mechanism measured for versioned-fields (3 entries to 19 in 8 renewals), and store-entries-security S4 calls the refusing records clutter that only grows. A union of two healthy profile lists can pass 32 names and then reads as no list, so every client reaches nothing. Four critics raise this: store-entries-security M3, store-entries-survival P9, wildcard-survival 2.5 and hardened-custom-data M4/P8.

   Why it matters: The evaluation counts A5 against versioned-fields only. The visible clutter is also what invites the tidy-ups that remove witnesses.

16. The matrix leaves out whole classes of programs and operations. (a) The programs' own commands, which produced most of the measured widenings (C1b, C3c, C6, D1): KeePass's Delete Duplicate Entries, Delete Empty Groups, Empty Recycle Bin, Remove deleted objects information, Duplicate, drag copy, paste, templates, Edit Entries, XML Replace and print or HTML export; KeePassXC's Clone, rm moving times from 2.7.12, Discard on a changed file, KeeShare and reload-merge; KeePassDX's copy (' (~)' appended), recycling that touches the entry's time, a 'Backup' group adopted as the bin and the nested-group delete loop [R]; Keepass2Android's cache sync and merge-without-asking. (b) Two-step chains, another program's save or merge followed by KeePass Synchronize: hardened F1, the location time regression, a raised group time. Requirement A10 is also missing from section 1. (c) A second KeePass with the plugin (two gates are supported, DESIGN.md 113) and a KeePass running 0.1.0. (d) KeeWeb itself: only the kdbxweb library was run.

   Why it matters: C0 covers 'opens, saves, edits or merges', but the matrix tabulates only single saves and single merges, so the edit and maintenance acts that actually widened access in the critics' probes have no row.

17. Serious problems of the options not recommended are missing or softened. rules-on-node-survival 2.4 (a seal identity derived from content drops a later narrowing, 8 of 8 [M]) and 2.5; rules-on-node-security P6 (0.1.0 rules dropped silently), and that critic's verdict is 'viable with changes, and narrowly'. rules-central-security S3 (version entries that look identical invite deletions that end in silent reverts after Database Maintenance, pykeepass or KeePassDX). This one also applies to the recommended rules group, which keeps one entry per version for as long as the node lives. wildcard-security 2.6 (the 'gone' state refuses a readable snapshot, so every client pairs again).

   Why it matters: Not decisive for the ranking, except S3, which is a property of the recommended rules group and has a named change: a title that carries the version's place, and replaced versions moved to a second group.

18. Two points about the authority key are missing. versioned-fields-security S2 says 'the key alone admits nobody' understates the risk: the document carries the host PKCS#12 and the gate key, with which a holder can impersonate the gate to a client on the same machine. And a retired authority's private key in an entry cannot be reliably removed, since KeePassXC brings back every entry the plugin deletes [M ground-keepassxc 2], whereas today the prune removes the key once its retirement exists [R inventory D1].

   Why it matters: These belong in question 4's weighing of exposure against availability.

19. The evaluation is now behind its Android source. measured-android.md (papers\ground\measured-android.md, written 19:13, two minutes after the evaluation) confirms the interim results for Keepass2Android and KeePassDroid. It adds: Keepass2Android writes KDBX 3.1 for an AES-KDF file with no node custom data or group tags and keeps every carrier; KeePassDroid has no move, no group edit and no merge; KeePassDroid's Save makes a new version (time moved, a history item added) even when nothing changed; Keepass2Android has no Empty Recycle Bin. Its KeePassDX section is still unwritten.

   Why it matters: The [M, interim] labels can be firmed up, and parts of section 6 item 1 are already done. KeePassDX merges of the recommended shapes remain the open condition for C0.

## Statements that are wrong

1. 3.2: 'The idea as worded (fields in one shared entry) does not hold in any program (section 2.2, third row)'. Overstated. That row shows the other copy's field kept in one history item in KeePass, KeePassXC and Keepass2Android [M], and in Strongbox, KeePassKit and keepass-rs [R]. With the idea's preprocessing step, ground-keepass X13 kept 24 of 24 in KeePass when writes were 5 s apart [M]. It fails at ties, at a history limit of 0, under Keep existing and to other programs' trimming, which is not the same as 'any program'.

2. 5.1: 'Small records fail one datum at a time, and each failure is closed: a client pairs again, a node refuses.' The parent designs have measured widenings that the recommendation does not fix: rules-linked C1b, C1c and C2 (a copy open) and C3, C3b and C3c (rules rolled back); a KeePassXC merge plus one hand deletion admitting a revoked client; moves undone by merges; the clock-ahead prune.

3. 5.1 datum 4: 'It answers store-entries P1 (one hand deletion) in the way its critic proposed.' The critic's same change says the KeePassXC residue 'has to be stated': a fast KDF delivers no key, and a slow-KDF merge of a stale copy into the current one removes KPG.Revoked [M spike, 96 runs], which leaves one witness again.

4. 5.1 'What it does not solve': 'Several witnesses removed by hand, then a merge that carries no deletions with an older copy: for a revocation, the entry and the key.' The key needs no hand: a KeePassXC merge that loads the stale copy later removes it [M spike], so one hand deletion plus that merge is enough.

5. 5.1 costs: 'Without the keys the file is 4.0.' Every KeePassDX and KeePassDroid save writes 4.1 [M spike; M measured-android]. KeePassXC 2.7 and later keeps the version it read and stamps time-less items [M critic hardened-custom-data-survival M1]. KeePassXC 2.7.12 and later adds timed keys of its own to every file it saves [M critic outside-file-survival].

6. Question 5 offers 'write them without a time through KeePassLib's internal setter' as a way to avoid the KeeWeb defect. hardened-custom-data-survival M1 measured that both time-less KPG keys came back stamped after a KeePassXC save of a 4.1 file.

7. Section 4 marks C0 'yes' for six in-file options. That contradicts section 2.3: 'No option that keeps the data in the .kdbx meets C0 against [a whole-file overwrite]'. Per option-single-entry 12 and three critics, KeePassDroid is one of the Android programs in scope, and it overwrites at every save over a changed file [R ground-android 4.2].

8. Section 4 gives rules-linked 'E6: yes, node as third witness'. Its survival critic measured E6 failing for copies, the protection lost without a trace (C1b, C1c, C2), and the option's own probe lost a first rule's anchor in 6 of 8 KeePass cells when another copy edited the entry later.

9. 5.1 datum 1: 'In database custom data it is removed by a KeePassXC merge of any copy from before Create or Recreate.' Only when that copy is the one loaded later and the two loads fall in different seconds: with a fast KDF nothing changes [M spike]. And 2.4.2 and 2.6.6 kept the keys when the stale copy's dictionary was empty [M outside-file-survival]. The evaluation also leaves out that a KeePass Synchronize with any copy that holds the key restores it [M outside-file-survival, 'held, Keep existing excepted'].

10. 5.1 rule 3: the store identifier is a value that 'no file made elsewhere knows'. Every copy of the database knows it, and option-versioned-fields 12 says so: 'A file made by somebody who once held the database knows the store identifier.'

11. 5.1 costs: 'What bounds it: any channel that leaks it leaks every password in the file as well, and the key alone admits nobody.' A clipboard copy, or a single entry shown by KeePassDroid, which displays protected strings in clear [R critic], leaks the authority without the other passwords. versioned-fields-security S2 notes the host PKCS#12 and gate key let a holder impersonate the gate to a client.

12. 3.9: 'It is the only option in which a whole-file overwrite by another program does not take revocations and rules with it.' The folder has its own whole-file losses: a synchronizer's conflict copy when two computers share a writer name (outside-file-survival P1, serious) and one file rolled back (P8).

13. 5.3 item 2: if a whole-file overwrite must be survived, 'only outside-file qualifies, with E9 reversed and its uncloseable gap accepted'. The outside-file security critic's own narrower hybrid (rules stay in the file, only add-only data moves out) has no gap 2a, and 5.1 itself proposes a host memory as another candidate.

14. 5.2: wildcard 'with its critics' changes' loses because 'a copy of a narrowed node carries no rule'. wildcard-security 2.3's change, a copy hint KPG.Of in the node's custom data, makes a KeePass duplicate refuse. Only a phone save that deletes the hint brings the claim back.

15. 5.1 'Why this family': 'store-entries, versioned-fields and rules-linked are built on it [the entry added once and never changed]'. versioned-fields writes closing state pairs that carry the whole state, and the recommendation itself also rests on a string in user entries (KPG.Anchor) and on database custom data keys.

16. 3.8: 'KeePass deletes whichever of two equal entries comes first in tree order [R PwDatabase.cs:1995-2029]'. It deletes the older of the two and prefers one in the recycle bin; tree order decides only at equal times [R ground-keepass 9; R rules-linked-survival P1].

17. 2.1, Keepass2Android entry history: 'three items under a limit of ten kept [M, interim]'. The base's HistoryMaxItems was 8 [M measured-android 1.3].

18. 2.1 and 2.2, the column 'kdbxweb 2.1.1 (KeeWeb)' is marked [M]. Only the library was run. That KeeWeb's sync does the same is [A, KeeWeb itself not run] in ground-others.

19. 3.8: 'a first grant ... waits for a Confirm press in 6 of 8 cells [M option]'. The option says 6 of 8 KeePass cells and every KeePassXC cell where the other edit was later.

20. 3.1: the authority key is 'one copy, print or export away in every program'. For Keepass2Android and KeePassDX the critics mark the entry view's reveal and copy controls [A], not read or measured.
