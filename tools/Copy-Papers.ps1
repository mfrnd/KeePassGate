<#
.SYNOPSIS
    Copies the evaluation's reports into `papers\` as they were written, with the paths of the
    machine they were written on replaced by paths on this branch and the phrases that make a
    program's place in the evaluation a person's replaced by the evaluation's terms, and counts each
    replacement.

.DESCRIPTION
    The reports are working papers, kept as written: nothing in them is corrected, reflowed or
    brought up to date. They cannot keep two things. The first is where they were written. They name
    the agents' working directories under a session's temporary directory, which carries the user's
    name and the session's identifier, and the worktrees the evaluation read, which are directories
    of one machine. Each such path is replaced here by the place the same thing has on this branch,
    or by a neutral description where it has none, and by this script rather than by hand, so that
    the change is itself something anybody holding the originals can make again and compare. The
    second is wording that makes a program's place in the evaluation a person's, which the wording
    table replaces the same way (below).

    **The rules are generic and the machine's paths arrive as arguments.** A path under a session's
    temporary directory is recognized by its shape, which names no user and no session, and the
    worktrees are passed in, so this file carries none of them either.

    **Where an agent's working directory goes.** Every agent of the evaluation worked in a directory
    of its own named after its report: `<option>` for a design, `<option>-survival` and
    `<option>-security-and-fit` for its two critiques, and the ground report's key for a ground
    report. A design's probe and its critics' probes are one family,
    `spikes\storage\probes\<option>`, with a directory each for the paper they belong to, `design`,
    `survival` and `security-and-fit`, because their files share names: two of them are
    `probe\Program.cs`. A ground report's family is `spikes\storage\probes\ground-<key>`. The formal
    critic's model is `spikes\storage\model\critic` and the emulator measurement is
    `spikes\storage\android`. The rest of each path is left exactly as the agent wrote it. A
    directory this table does not name stops the copy rather than being guessed at.

    **What is replaced, in this order**, each counted:

    - a KeePassXC release the `keepassxc` ground report extracted among its downloads, by
      `.keepassxc\<version>\`, where `tools/KeePassXC.ps1` provides the same release from the same
      pinned archive;
    - an agent's working directory, as a full path, as `...\scratchpad\storage\<dir>`, as
      `...\storage\<dir>` and as `...\<dir>`, by its place on this branch;
    - the directory all of them were in, by `spikes\storage\probes`;
    - a session's scratch directory itself, by `<scratch>`, and the word for it in prose by
      "scratch directory";
    - the product's worktree: before `.keepass\`, by nothing, since this branch provides the same
      KeePass at the same place; before a path in the product, by that path "of the product at
      `a42e38aa`"; on its own, by "the product at `a42e38aa`". Where the report already has "at
      `a42e38aa`" after the path, that is taken into the replacement rather than written twice,
      and where it has it on the next line, the replacement leaves it to that line;
    - the worktree of `spikes/revocation-merge`, by that branch;
    - the main checkout and the directory of worktrees, by those words;
    - the reports' own directory, by where each report is in `papers\`, and its `crdt-model` by
      `spikes\storage\model`;
    - in the two later Android reports only, `measured-overwrite.md` and
      `measured-k2a-synchronize.md`, their evidence: `android-evidence\overwrite\` and
      `android-evidence\k2a-synchronize\` "beside this report" by the record under
      `spikes\storage\android\recorded\`, and the evidence's `scripts\`, `flows\` and `probe\` by
      the family's `scripts\`, `scripts\flows\` and `probe\overwrite\`. A path below the evidence
      that the record keeps, `base\`, `runs\`, `logs\` or `server\`, is left as written, since it
      holds below the record as it held below the evidence. `measured-android.md` keeps its
      `android-evidence\` as written, which the family's `RUN.md` maps, and so do the reports of
      the measurements made after the first draft: they were written with no path of the machine
      in them, each naming its family on the branch, and the `desktop-evidence\` or
      `android-evidence\` they name beside them is mapped by the `RUN.md` of that family or of
      `spikes\storage\android`.

    **Then the wording, by a table of exact replacements per report.** The reports were written
    inside a working session, and in places they name a person where the evaluation means which
    programs it covers: whose phone, programs, setup or database, whose requirement, idea, question
    or ruling, and instructions quoted as they were given. A program is compatible when measurement
    against the stated criteria shows it, and the papers are made to read that way: each such phrase
    is replaced by the programs in scope, a phone, an arrangement that is nobody's, an existing
    database, the survival requirement, the project's decision, a finding as measured, or the idea
    by its name, and a quoted instruction is taken out. "The operator" as the
    person who runs the plugin, approves a pairing, answers a dialog or edits a rule stays, and so
    does a ruling the product's own documents record. An entry names the report, the text as
    written, its replacement and how many times the report holds that text; a count that differs
    stops the copy with nothing written, so a changed original cannot pass unchanged. The table is
    applied after the paths and counted by the kind of replacement.

    Before anything is written, every copy is searched again for a user's directory, `AppData`,
    `scratchpad` and any of the paths passed in, and one that still holds any of them stops the copy
    with nothing written.

.PARAMETER Source
    The directory holding the reports as the evaluation left them.

.PARAMETER ProductTree
    The worktree of the product the reports read, at `a42e38aa`.

.PARAMETER RevocationMergeTree
    The worktree of `spikes/revocation-merge` the reports read.

.PARAMETER MainCheckout
    The repository's main checkout, which the reports name as something nothing wrote to.

.PARAMETER Worktrees
    The directory holding the worktrees, which the reports name for the same reason.

.EXAMPLE
    .\tools\Copy-Papers.ps1 -Source <reports> -ProductTree <product worktree> `
        -RevocationMergeTree <revocation-merge worktree> -MainCheckout <main checkout> `
        -Worktrees <directory of worktrees>
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $ProductTree,
    [Parameter(Mandatory)] [string] $RevocationMergeTree,
    [Parameter(Mandatory)] [string] $MainCheckout,
    [Parameter(Mandatory)] [string] $Worktrees
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$Source = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')

$options = @(
    'store-entries', 'rules-on-node', 'rules-marker-entry', 'rules-central', 'rules-linked',
    'outside-file', 'hardened-custom-data', 'versioned-fields', 'wildcard', 'single-entry', 'crdt',
    'grant-only'
)
$grounds = @('inventory', 'measured', 'keepass', 'keepassxc', 'android', 'others')
# The measurements made after the evaluation's first draft, each a report of its own. They name no
# working directory: each names its family on the branch, or its evidence beside it, which the
# family's `RUN.md` maps.
$measuredLater = @(
    'rollback-rule-models', 'desktop-windows', 'store-as-a-whole', 'kdbx-places', 'source-harness',
    'keepassdx', 'edited-refusals', 'hide-and-lock', 'k2a-background', 'decided-store',
    'two-readings', 'copies-extended', 'rules-per-profile'
)

# Each report, and where it goes. Renamed only where the directory it lands in already tells what
# the old name did.
$reports = [ordered]@{}
foreach ($key in $grounds) {
    $reports["ground-$key.md"] = "papers\ground\ground-$key.md"
}
$reports['measured-android.md'] = 'papers\ground\measured-android.md'
$reports['measured-overwrite.md'] = 'papers\ground\measured-overwrite.md'
$reports['measured-k2a-synchronize.md'] = 'papers\ground\measured-k2a-synchronize.md'
foreach ($key in $measuredLater) {
    $reports["measured-$key.md"] = "papers\ground\measured-$key.md"
}
foreach ($key in $options) {
    $reports["option-$key.md"] = "papers\options\$key\design.md"
    $reports["critique-$key-survival.md"] = "papers\options\$key\critique-survival.md"
    $reports["critique-$key-security-and-fit.md"] = "papers\options\$key\critique-security-and-fit.md"
    $reports["digest-$key.md"] = "papers\options\$key\digest.md"
}
$reports['critique-crdt-formal.md'] = 'papers\options\crdt\critique-formal.md'
$reports['EVALUATION.md'] = 'papers\draft\EVALUATION-draft.md'
$reports['completeness-of-draft.md'] = 'papers\draft\completeness-of-draft.md'

# What else in the reports' directory a report names.
$beside = @{ 'crdt-model' = 'spikes\storage\model' }

# Each agent's working directory, and where its probes are on this branch.
$probes = @{}
foreach ($key in $options) {
    $probes[$key] = "spikes\storage\probes\$key\design"
    $probes["$key-survival"] = "spikes\storage\probes\$key\survival"
    $probes["$key-security-and-fit"] = "spikes\storage\probes\$key\security-and-fit"
}
foreach ($key in $grounds) {
    $probes[$key] = "spikes\storage\probes\ground-$key"
}
$probes['crdt-formal'] = 'spikes\storage\model\critic'
$probes['android-measure'] = 'spikes\storage\android'

function Get-ProbePlace([string] $Directory) {
    if (-not $probes.ContainsKey($Directory)) {
        throw "A report names the working directory '$Directory', which has no place on this branch yet. Add it to the table in this script."
    }

    $probes[$Directory]
}

# The shapes. A session's scratch directory is a user's temporary directory, a directory of the
# assistant's, one named after a project, one named by a session's identifier, and `scratchpad`.
$guid = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
$scratch = '[A-Za-z]:\\Users\\[^\\\s`]+\\AppData\\Local\\Temp\\claude\\[^\\\s`]+\\' + $guid + '\\scratchpad'
$dirEnd = '(?=[\\`\s,;:)\]}]|$)'
$product = [regex]::Escape($ProductTree.TrimEnd('\'))
$revocation = [regex]::Escape($RevocationMergeTree.TrimEnd('\'))
$checkout = [regex]::Escape($MainCheckout.TrimEnd('\'))
$trees = [regex]::Escape($Worktrees.TrimEnd('\'))
$sourceDir = [regex]::Escape($Source)

# The two later Android reports name their evidence beside them, and where its parts are on the
# branch. Their rules apply to those two reports only: `measured-android.md` names an evidence
# directory of the same shape, which the family's `RUN.md` maps instead.
$laterAndroid = @('measured-overwrite.md', 'measured-k2a-synchronize.md')
$laterPlaces = @{
    'scripts' = 'spikes\storage\android\scripts\'
    'flows'   = 'spikes\storage\android\scripts\flows\'
    'probe'   = 'spikes\storage\android\probe\overwrite\'
}

$rules = @(
    @{ Name = 'a KeePassXC release among the keepassxc ground report''s downloads'
       Pattern = '(?:' + $scratch + '|\.\.\.\\scratchpad|\.\.\.)\\storage\\keepassxc\\dl\\(?<v>\d+\.\d+\.\d+)\\x\\KeePassXC-[^\\`\s]+\\'
       With = { param($m) '.keepassxc\' + $m.Groups['v'].Value + '\' } }
    @{ Name = 'an agent''s working directory, as a full path'
       Pattern = $scratch + '\\storage\\(?<dir>[A-Za-z0-9-]+)' + $dirEnd
       With = { param($m) Get-ProbePlace $m.Groups['dir'].Value } }
    @{ Name = 'an agent''s working directory, as `...\scratchpad\storage\<dir>`'
       Pattern = '\.\.\.\\scratchpad\\storage\\(?<dir>[A-Za-z0-9-]+)' + $dirEnd
       With = { param($m) Get-ProbePlace $m.Groups['dir'].Value } }
    @{ Name = 'an agent''s working directory, as `...\storage\<dir>`'
       Pattern = '\.\.\.\\storage\\(?<dir>[A-Za-z0-9-]+)' + $dirEnd
       With = { param($m) Get-ProbePlace $m.Groups['dir'].Value } }
    @{ Name = 'the directory of every agent, as a full path or as `...\scratchpad\storage`'
       Pattern = '(?:' + $scratch + '|\.\.\.\\scratchpad)\\storage(?=[\\`\s,;:)]|$)'
       With = { param($m) 'spikes\storage\probes' } }
    @{ Name = 'an agent''s working directory, as `...\<dir>` where `...` was that directory'
       Pattern = '\.\.\.\\(?<dir>[A-Za-z0-9-]+)(?=\\)'
       Only = { param($m) $probes.ContainsKey($m.Groups['dir'].Value) }
       With = { param($m) Get-ProbePlace $m.Groups['dir'].Value } }
    @{ Name = 'a session''s scratch directory itself'
       Pattern = $scratch + '(?=[\\`\s]|$)'
       With = { param($m) '<scratch>' } }
    @{ Name = 'the word scratchpad in prose'
       Pattern = '\bscratchpad\b'
       With = { param($m) 'scratch directory' } }
    @{ Name = 'the product''s worktree before `.keepass\`'
       Pattern = $product + '\\(?=\.keepass\\)'
       With = { param($m) '' } }
    @{ Name = 'the product''s worktree before a path in the product'
       Pattern = '`' + $product + '\\(?<rest>[^`]+)`(?: at `a42e38aa`|(?<next>(?= at\r?\n[ \t]*`a42e38aa`)))?'
       With = {
           param($m)
           if ($m.Groups['next'].Success) { return '`' + $m.Groups['rest'].Value + '` of the product' }
           '`' + $m.Groups['rest'].Value + '` of the product at `a42e38aa`'
       } }
    @{ Name = 'the product''s worktree on its own'
       Pattern = '(?-i:the )?(?-i:worktree )?`' + $product + '`(?: at `a42e38aa`|(?<next>(?= at\r?\n[ \t]*`a42e38aa`)))?'
       With = {
           param($m)
           if ($m.Groups['next'].Success) { return 'the product' }
           'the product at `a42e38aa`'
       } }
    @{ Name = 'a file in the worktree of `spikes/revocation-merge`'
       Pattern = '`' + $revocation + '\\(?<rest>[^`]+)`'
       With = { param($m) '`' + $m.Groups['rest'].Value + '` of the branch `spikes/revocation-merge`' } }
    @{ Name = 'the worktree of `spikes/revocation-merge` on its own'
       Pattern = '`' + $revocation + '`'
       With = { param($m) 'the branch `spikes/revocation-merge`' } }
    @{ Name = 'the main checkout'
       Pattern = '`' + $checkout + '`'
       With = { param($m) 'the main checkout' } }
    @{ Name = 'the directory of worktrees'
       Pattern = '`' + $trees + '`'
       With = { param($m) 'the worktrees' } }
    @{ Name = 'the reports'' own directory'
       Pattern = $sourceDir + '\\(?<name>[^\\\s`,;)]+)'
       With = {
           param($m)
           $name = $m.Groups['name'].Value
           if ($reports.Contains($name)) { return $reports[$name] }
           if ($beside.ContainsKey($name)) { return $beside[$name] }
           throw "A report names '$name' in the reports' directory, which has no place on this branch."
       } }
    @{ Name = 'the evidence of a later Android report, "beside this report"'
       Reports = $laterAndroid
       Pattern = '`android-evidence\\(?<m>overwrite|k2a-synchronize)\\(?<rest>[^`]*)`(?: beside this report)?'
       With = { param($m) '`spikes\storage\android\recorded\' + $m.Groups['m'].Value + '\' + $m.Groups['rest'].Value + '`' } }
    @{ Name = 'the scripts, flows and probe in the evidence of a later Android report'
       Reports = $laterAndroid
       Pattern = '`(?<d>scripts|flows|probe)\\(?<rest>[^`]*)`'
       With = { param($m) '`' + $laterPlaces[$m.Groups['d'].Value] + $m.Groups['rest'].Value + '`' } }
)

# The wording. Each entry is a phrase of a report, as written and as it reads on the branch, with
# the number of times the report holds it (1 unless given). Several strings are a text across that
# many lines, joined by the report's own line ending. `Row` is the kind of replacement, and the
# replacements are counted by it. Entries of a report apply in the order listed.
$inScope = 'the programs in scope'
$aPhone = 'a phone'
$survival = 'the survival requirement'
$decision = 'the project''s decision'
$measured = 'incompatible, as measured'
$quotation = 'a quotation taken out'
$idea = 'an idea by its name'
$question = 'a question without a person'
$arrangement = 'an arrangement, nobody''s'
$existing = 'an existing database'
$kinds = @($inScope, $aPhone, $survival, $decision, $measured, $quotation, $idea, $question,
    $arrangement, $existing)

$wording = @(
    # draft\EVALUATION-draft.md
    @{ Report = 'EVALUATION.md'; Row = $decision
       As = 'for the operator''s', 'decision. It rests'
       With = 'for the project''s', 'decision. It rests' }
    @{ Report = 'EVALUATION.md'; Row = $idea
       As = 'both written after an operator remark)'
       With = 'both added later)' }
    @{ Report = 'EVALUATION.md'; Row = $survival
       As = 'with the operator''s condition for this rethink'
       With = 'with the survival requirement for this rethink' }
    @{ Report = 'EVALUATION.md'; Row = $survival
       As = '**The operator''s condition.**'
       With = '**The survival requirement.**' }
    @{ Report = 'EVALUATION.md'; Row = $idea
       As = '(the operator''s idea as worded)'
       With = '(the timestamp-named-field idea as worded)' }
    @{ Report = 'EVALUATION.md'; Row = $idea
       As = '`versioned-fields`: the operator''s idea in its strongest form'
       With = '`versioned-fields`: the timestamp-named-field idea in its strongest form' }
    @{ Report = 'EVALUATION.md'; Row = $question
       As = '**What it is.** The operator''s later question:'
       With = '**What it is.** The evaluation''s later question:' }
    @{ Report = 'EVALUATION.md'; Row = $idea
       As = 'Written after the operator''s remark that references'
       With = 'Written for the idea that references' }
    @{ Report = 'EVALUATION.md'; Row = $idea
       As = 'It is the operator''s idea with its unit changed'
       With = 'It is the timestamp-named-field idea with its unit changed' }
    @{ Report = 'EVALUATION.md'; Row = $question
       As = 'it keeps both things the operator', 'asked for:'
       With = 'it keeps both things the evaluation', 'asks for:' }
    @{ Report = 'EVALUATION.md'; Row = $idea
       As = '**What the three operator ideas become.**'
       With = '**What the three ideas become.**' }
    @{ Report = 'EVALUATION.md'; Row = $idea
       As = 'which is what the operator''s single-entry question'
       With = 'which is what the `single-entry` question' }
    @{ Report = 'EVALUATION.md'; Row = $decision
       As = '**The operator ruling that a whole-file overwrite must be survived.**'
       With = '**A decision of the project that a whole-file overwrite must be survived.**' }

    # draft\completeness-of-draft.md
    @{ Report = 'completeness-of-draft.md'; Row = $idea
       As = '1. The operator''s own idea, with its preprocessing step,'
       With = '1. The timestamp-named-field idea, with its preprocessing step,' }
    @{ Report = 'completeness-of-draft.md'; Row = $idea
       As = 'With the operator''s preprocessing step,'
       With = 'With the idea''s preprocessing step,' }
    @{ Report = 'completeness-of-draft.md'; Row = $inScope
       As = 'KeePassDroid is one of the operator''s own phone programs'
       With = 'KeePassDroid is one of the Android programs in scope' }

    # ground\
    @{ Report = 'ground-android.md'; Row = $idea
       As = '### 1.6 Verdict on the operator''s proposal'
       With = '### 1.6 Verdict on the timestamp-named-field proposal' }
    @{ Report = 'ground-inventory.md'; Row = $idea
       As = 'the operator''s timestamp-named-field idea'
       With = 'the timestamp-named-field idea' }
    @{ Report = 'ground-inventory.md'; Row = $idea
       As = '## 4. The question the operator''s option turns on:'
       With = '## 4. The question the timestamp-named-field option turns on:' }
    @{ Report = 'ground-inventory.md'; Row = $survival
       As = '`CA.md:157-180`; the operator''s decision in the task'
       With = '`CA.md:157-180`; the survival requirement in the task' }
    @{ Report = 'ground-inventory.md'; Row = $survival
       As = '| the operator''s decision: other programs must leave the data intact |'
       With = '| the survival requirement: other programs must leave the data intact |' }
    @{ Report = 'ground-inventory.md'; Row = $survival
       As = '**ruled out** by the operator''s decision: other programs drop the times'
       With = '**ruled out** by the survival requirement: other programs drop the times' }
    @{ Report = 'ground-keepassxc.md'; Row = $idea
       As = 'shape the operator''s proposal would take.'
       With = 'shape the timestamp-named-field proposal would take.' }
    @{ Report = 'ground-keepassxc.md'; Row = $idea
       As = 'Measured with the operator''s shape:'
       With = 'Measured with the proposal''s shape:' }
    @{ Report = 'ground-measured.md'; Row = $idea
       As = 'the most direct question the operator''s idea raises'
       With = 'the most direct question the timestamp-named-field idea raises' }
    @{ Report = 'ground-others.md'; Row = $idea
       As = 'because the operator''s option turns on it;'
       With = 'because the timestamp-named-field option turns on it;' }
    @{ Report = 'ground-others.md'; Row = $idea
       As = '## 6. What this leaves the operator''s proposal'
       With = '## 6. What this leaves the timestamp-named-field proposal' }
    @{ Report = 'measured-android.md'; Row = $aPhone
       As = '## 7. What survives on the operator''s phone'
       With = '## 7. What survives on a phone' }
    @{ Report = 'measured-android.md'; Row = $inScope
       As = '(third, not on the operator''s phone)'
       With = '(third, not in scope)' }
    @{ Report = 'measured-k2a-synchronize.md'; Row = $quotation
       As = 'emulator, at the operator''s request ("Measure Keepass2Android', 'synchronize feature"), after the ruling that there is to be'
       With = 'emulator, after the ruling that', 'there is to be' }
    @{ Report = 'measured-k2a-synchronize.md'; Row = $quotation
       As = 'writers** ("I want one synchronized kdbx, no separate inbox. I want to modify passwords on any', 'device"). The question:'
       With = 'writers**. The question:' }
    @{ Report = 'measured-k2a-synchronize.md'; Row = $decision
       As = 'emulator, after the ruling that'
       With = 'emulator, after the project''s decision of 2026-11-15 that' }
    @{ Report = 'measured-k2a-synchronize.md'; Row = $quotation
       As = 'Not run here, by the operator''s ruling ("No more KeePassDroid, mark it as incompatible for', 'sync/merge"). The evidence is in'
       With = 'Not run here, since KeePassDroid is incompatible.', 'The evidence is in' }
    @{ Report = 'measured-overwrite.md'; Row = $decision
       As = 'Two rulings of the operator arrived while this ran'
       With = 'Two of the project''s decisions arrived while this ran' }
    @{ Report = 'measured-overwrite.md'; Row = $inScope
       As = 'is no longer one of the operator''s programs and'
       With = 'is no longer one of the Android programs in scope and' }
    @{ Report = 'measured-overwrite.md'; Row = $measured
       As = 'and is marked incompatible with a'
       With = 'and is incompatible, as measured, with a' }
    @{ Report = 'measured-overwrite.md'; Row = $quotation
       As = 'Cut to the baseline and its lock by the operator''s ruling ("No more KeePassDroid, mark it as', 'incompatible for sync/merge"); what is here is the evidence for that mark.'
       With = 'Cut to the baseline and its lock when KeePassDroid was found incompatible with a synchronized or', 'merged database; what is here is the evidence for that finding.' }
    @{ Report = 'measured-overwrite.md'; Row = $quotation
       As = 'Cases 2 to 5 were not run, by the ruling.'
       With = 'Cases 2 to 5 were not run, since KeePassDroid is incompatible.' }
    @{ Report = 'measured-overwrite.md'; Row = $quotation
       As = '- **KeePassDroid cases 2 to 5**: cut by the operator''s ruling.'
       With = '- **KeePassDroid cases 2 to 5**: cut when KeePassDroid was found incompatible (section 3).' }
    @{ Report = 'measured-overwrite.md'; Row = $aPhone
       As = 'so the operator''s phone cannot reach'
       With = 'so a phone cannot reach' }
    @{ Report = 'measured-overwrite.md'; Row = $measured
       As = 'By the operator''s', 'ruling it is marked incompatible for a synchronized or merged database.'
       With = 'It is incompatible,', 'as measured, for a synchronized or merged database.' }

    # options\crdt
    @{ Report = 'critique-crdt-formal.md'; Row = $question
       As = 'steps through programs the operator named,'
       With = 'steps through programs in scope,' }
    @{ Report = 'critique-crdt-formal.md'; Row = $decision
       As = 'which needs the operator''s ruling |'
       With = 'which needs a decision of the project |' }
    @{ Report = 'critique-crdt-formal.md'; Row = $decision
       As = 'unless the operator rules that a'
       With = 'unless the project decides that a' }
    @{ Report = 'critique-crdt-formal.md'; Row = $inScope
       As = 'The steps are the operator''s own programs:'
       With = 'The steps are the programs in scope:' }
    @{ Report = 'critique-crdt-formal.md'; Row = $inScope
       As = '**The operator''s own program does it**:'
       With = '**An Android program in scope does it**:' }
    @{ Report = 'critique-crdt-formal.md'; Row = $decision
       As = '**Change, needing the operator''s ruling.**'
       With = '**Change, needing a decision of the project.**' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $survival
       As = 'So the operator''s hard requirement can be met.'
       With = 'So the survival requirement can be met.' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $aPhone
       As = 'are about the operator''s own phone:'
       With = 'are about a phone:' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $aPhone
       As = '**Sequence A, on the operator''s phone.**'
       With = '**Sequence A, on a phone.**' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $quotation
       As = ' names ("I don''t want to lose rules just because I edited the', 'database from ... my phone"), done by'
       With = ' names,', 'done by' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $survival
       As = 'the case the operator''s requirement names,'
       With = 'the case the survival requirement names,' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $inScope
       As = 'It is the operator''s own phone program, and'
       With = 'It is an Android program in scope, and' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $decision
       As = 'so it needs the operator''s ruling against'
       With = 'so it needs a decision of the project against' }
    @{ Report = 'critique-crdt-survival.md'; Row = $question
       As = 'in every program the operator named,'
       With = 'in every program in scope,' }
    @{ Report = 'critique-crdt-survival.md'; Row = $inScope
       As = 'reaches the operator''s own programs'
       With = 'reaches the programs in scope' }
    @{ Report = 'critique-crdt-survival.md'; Row = $inScope
       As = '**R3, the operator''s own programs**'
       With = '**R3, the programs in scope**' }
    @{ Report = 'option-crdt.md'; Row = $quotation
       As = 'The operator asked', '"Could Conflict-Free Replicated Data Type (CRDT) help in any of the options?" and then "And the', 'CRDT?". This is'
       With = 'The evaluation asks', 'whether a conflict-free replicated data type (CRDT) could help in any of the options.', 'This is' }
    @{ Report = 'option-crdt.md'; Row = $inScope
       As = 'and KeePassDroid is one of the operator''s two phone programs.'
       With = 'and KeePassDroid is one of the Android programs in scope.' }
    @{ Report = 'option-crdt.md'; Row = $decision
       As = 'needs the operator''s', '  ruling.'
       With = 'needs a decision of the', '  project.' }
    @{ Report = 'option-crdt.md'; Row = $survival
       As = 'would fail the operator''s requirement under'
       With = 'would fail the survival requirement under' }
    @{ Report = 'option-crdt.md'; Row = $aPhone
       As = '**How often an operator''s phone saves over a changed file**'
       With = '**How often a phone saves over a changed file**' }

    # options\grant-only
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $decision
       As = '**[R ruling]**: an operator ruling as the session''s memory records it'
       With = '**[R ruling]**: a decision of the project as the session''s memory records it' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $survival
       As = 'keeps the', 'operator''s hard requirement, and'
       With = 'keeps the', 'survival requirement, and' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $inScope
       As = 'one thing the', '   operator''s own phone program (Keepass2Android''s Move)'
       With = 'one thing an', '   Android program in scope (Keepass2Android''s Move)' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $decision
       As = 'migration code the operator''s standing rulings'
       With = 'migration code the project''s standing decisions' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $aPhone
       As = 'and the operator''s phone, a drag and a merge all change it silently'
       With = 'and a phone, a drag and a merge all change it silently' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $inScope
       As = 'against the operator''s own programs and an ordinary database'
       With = 'against the programs in scope and an ordinary database' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $aPhone
       As = 'The steps behind E3 and E4 on the operator''s phone:'
       With = 'The steps behind E3 and E4 on a phone:' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $inScope
       As = 'so the operator''s other phone program cannot'
       With = 'so the other Android program in scope cannot' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $decision
       As = '- The operator ruled that 0.1.0 had no users (2026-11-08)'
       With = '- The project decided that 0.1.0 had no users (2026-11-08)' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $quotation
       As = 'that no code handles earlier stored data: "no migration, no programmatic cleanup, no', '  one-time warnings, no reading of old keys", with a one-off'
       With = 'that no code handles earlier stored data (no migration, no programmatic cleanup, no', '  one-time warnings, no reading of old keys), with a one-off' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $survival
       As = 'which is the operator''s hard requirement.'
       With = 'which is the survival requirement.' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $aPhone
       As = 'of the operator''s phone use.'
       With = 'of phone use.' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $decision
       As = 'by the operator''s ruling; S2 has what to do.'
       With = 'as the project decided; S2 has what to do.' }
    @{ Report = 'critique-grant-only-survival.md'; Row = $aPhone
       As = 'for the operator''s own phone.'
       With = 'for a phone.' }
    @{ Report = 'critique-grant-only-survival.md'; Row = $decision
       As = '**If the operator rules that KeePassDroid''s'
       With = '**If the project decides that KeePassDroid''s' }
    @{ Report = 'option-grant-only.md'; Row = $inScope
       As = 'are the merges the operator''s own programs make.'
       With = 'are the merges the programs in scope make.' }

    # options\hardened-custom-data
    @{ Report = 'critique-hardened-custom-data-survival.md'; Row = $idea
       As = '## 7. What this means for the operator''s idea of timestamped JSON'
       With = '## 7. What this means for the idea of timestamped JSON' }
    @{ Report = 'digest-hardened-custom-data.md'; Row = $inScope
       As = 'Against KeePassDroid, the operator''s own', 'phone program, it fails closed'
       With = 'Against KeePassDroid, one of the Android programs', 'in scope, it fails closed' }

    # options\outside-file
    @{ Report = 'critique-outside-file-security-and-fit.md'; Row = $decision
       As = 'The operator ruling records 0.1.0 as released but unused', '  (memory: release-step-plan, operator 2026-11-08)'
       With = 'The project''s decision records 0.1.0 as released but unused', '  (memory: release-step-plan, 2026-11-08)' }
    @{ Report = 'critique-outside-file-survival.md'; Row = $idea
       As = '## 8. A note on the operator''s idea'
       With = '## 8. A note on the timestamp-named-field idea' }
    @{ Report = 'critique-outside-file-survival.md'; Row = $idea
       As = 'The design presents itself as the operator''s idea (JSON'
       With = 'The design presents itself as the timestamp-named-field idea (JSON' }
    @{ Report = 'option-outside-file.md'; Row = $idea
       As = '**How this relates to the operator''s idea**'
       With = '**How this relates to the timestamp-named-field idea**' }

    # options\rules-central
    @{ Report = 'digest-rules-central.md'; Row = $inScope
       As = 'every operation on the operator''s two phones [M emulator]'
       With = 'every operation in the Android programs in scope [M emulator]' }

    # options\rules-linked
    @{ Report = 'critique-rules-linked-security-and-fit.md'; Row = $survival
       As = 'the option can meet the operator''s hard requirement, but not as written.'
       With = 'the option can meet the survival requirement, but not as written.' }
    @{ Report = 'critique-rules-linked-survival.md'; Row = $inScope
       As = 'edits on the operator''s own phone programs keep the reference'
       With = 'edits on the Android programs in scope keep the reference' }
    @{ Report = 'critique-rules-linked-survival.md'; Row = $inScope
       As = 'because KeePassDroid is one of the operator''s two phone programs and'
       With = 'because KeePassDroid is one of the Android programs in scope and' }
    @{ Report = 'option-rules-linked.md'; Row = $idea
       As = 'The operator''s words: "When rules or meta data sit outside the node, then consider the references', 'between these nodes should be bidirectional." Two designs'
       With = '`rules-linked` takes up the idea that when rules or meta data sit outside the node, the references', 'between these nodes should be bidirectional. Two designs' }
    @{ Report = 'option-rules-linked.md'; Row = $survival
       As = '**Against the operator''s requirement**'
       With = '**Against the survival requirement**' }
    @{ Report = 'digest-rules-linked.md'; Row = $inScope
       As = 'neither of the operator''s phone programs empties the'
       With = 'neither of the Android programs in scope empties the' }
    @{ Report = 'digest-rules-linked.md'; Row = $aPhone
       As = 'in the operator''s phone use.'
       With = 'in phone use.' }

    # options\rules-marker-entry
    @{ Report = 'option-rules-marker-entry.md'; Row = $idea
       As = '**The operator''s idea, with its unit changed.**'
       With = '**The timestamp-named-field idea, with its unit changed.**' }
    @{ Report = 'digest-rules-marker-entry.md'; Row = $inScope
       As = '(not the operator''s)'
       With = '(not in scope)' }

    # options\rules-on-node
    @{ Report = 'critique-rules-on-node-security-and-fit.md'; Row = $decision
       As = 'or the operator rules that no 0.1.0 database'
       With = 'or the project decides that no 0.1.0 database' }
    @{ Report = 'option-rules-on-node.md'; Row = $survival
       As = '| Ruled out by the operator''s decision |'
       With = '| Ruled out by the survival requirement |' }
    @{ Report = 'digest-rules-on-node.md'; Row = $inScope
       As = 'every phone program the operator', 'uses (M)'
       With = 'every Android program', 'in scope (M)' }

    # options\single-entry
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $inScope
       As = 'KeePassDroid, which the operator uses, shows it in clear'
       With = 'KeePassDroid, one of the Android programs in scope, shows it in clear' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $aPhone
       As = 'on the operator''s phone the recovery can need a backup'
       With = 'on a phone the recovery can need a backup' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $survival
       As = 'to meet the operator''s survival requirement:'
       With = 'to meet the survival requirement:' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $aPhone
       As = 'and the operator''s phone shows it in clear'
       With = 'and a phone shows it in clear' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $aPhone
       As = 'and on the operator''s own', 'phone it is not hidden at all:'
       With = 'and on a', 'phone it is not hidden at all:' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $inScope
       As = '**KeePassDroid 2.7, which the operator uses, ignores'
       With = '**KeePassDroid 2.7, one of the Android programs in scope, ignores' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $inScope
       As = 'the operator''s other phone program, masks it'
       With = 'the other Android program in scope, masks it' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $survival
       As = 'which is the operator''s', 'hard requirement firing.'
       With = 'which is the survival', 'requirement firing.' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $survival
       As = 'is exactly what the operator''s hard requirement forbids'
       With = 'is exactly what the survival requirement forbids' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $aPhone
       As = '- **On the operator''s phone the recovery is weaker.**'
       With = '- **On a phone the recovery is weaker.**' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $quotation
       As = ' fears ("I don''t want to lose rules just', '  because I edited from my phone"), narrowed to'
       With = ' fears,', '  narrowed to' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $survival
       As = 'the scenario the operator fears,'
       With = 'the scenario the survival requirement names,' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $question
       As = 'the one point the operator', 'asked about:'
       With = 'the one point the evaluation', 'asks about:' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $inScope
       As = 'one of the operator''s two phone programs, does that'
       With = 'one of the Android programs in scope, does that' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $inScope
       As = '**Sequence, the operator''s own programs**'
       With = '**Sequence, the programs in scope**' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $quotation
       As = 'so this is fatal to "I don''t want to lose rules just because I', 'edited the database from my phone" for every in-file option alike.'
       With = 'so this is fatal to the survival requirement', 'for every in-file option alike.' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $inScope
       As = 'KeeWeb is not one of the', 'operator''s programs.'
       With = 'KeeWeb is not one of the', 'programs in scope.' }
    @{ Report = 'option-single-entry.md'; Row = $quotation
       As = 'The operator asked: "Do', 'we have an option on the list where the whole rule and meta data handling is kept in one entry?', 'Then merges would be one or the other, all or nothing. But how would that handle cases where a', 'non-kpg program changes the tree?" No other'
       With = 'The evaluation asks', 'whether an option on the list keeps the whole rule and meta data handling in one entry, so that', 'merges would be one or the other, all or nothing, and how that would handle cases where a', 'non-kpg program changes the tree. No other' }
    @{ Report = 'option-single-entry.md'; Row = $question
       As = '## 0. The operator''s question, answered first'
       With = '## 0. The question, answered first' }
    @{ Report = 'option-single-entry.md'; Row = $idea
       As = 'which is what the operator''s option set out to avoid.'
       With = 'which is what `single-entry` set out to avoid.' }
    @{ Report = 'option-single-entry.md'; Row = $inScope
       As = 'KeePassDroid, which the operator uses, does this at'
       With = 'KeePassDroid, one of the Android programs in scope, does this at' }
    @{ Report = 'option-single-entry.md'; Row = $inScope
       As = '1. **The operator''s phone programs, on an emulator.**'
       With = '1. **The Android programs in scope, on an emulator.**' }

    # options\store-entries
    @{ Report = 'option-store-entries.md'; Row = $idea
       As = '**The operator''s idea, moved from the field to the entry.**'
       With = '**The timestamp-named-field idea, moved from the field to the entry.**' }
    @{ Report = 'digest-store-entries.md'; Row = $inScope
       As = 'both of the operator''s phone programs for every operation'
       With = 'the Android programs in scope for every operation' }

    # options\versioned-fields. The report's own spelling, inside the quotation the table takes out:
    # cspell:ignore jsons
    @{ Report = 'critique-versioned-fields-security-and-fit.md'; Row = $decision
       As = 'The operator''s decision was driven by'
       With = 'The project''s decision of 2026-11-15 was driven by' }
    @{ Report = 'critique-versioned-fields-survival.md'; Row = $survival
       As = 'and the operator''s condition that the other programs'
       With = 'and the survival requirement that the other programs' }
    @{ Report = 'option-versioned-fields.md'; Row = $idea
       As = 'This is the operator''s', 'own proposal: "store as a json'
       With = 'This is the timestamp-named-field', 'proposal: "store as a json' }
    @{ Report = 'option-versioned-fields.md'; Row = $quotation
       As = 'proposal: "store as a json inside an entry field where the name is derived from a timestamp so', 'that a merge would preserve both jsons", with "a path for groups (e.g. Meta entry inside the group', 'or a global meta entry for every kpg data) and a preprocessing step when loading to take care of', 'merge issues". One design'
       With = 'proposal: JSON stored inside an entry field whose name is derived from a timestamp, so that a', 'merge would preserve both documents, with a path for groups (a meta entry inside the group or a', 'global meta entry for all KPG data) and a preprocessing step when loading to take care of merge', 'issues. One design' }
    @{ Report = 'option-versioned-fields.md'; Row = $idea
       As = '| the operator''s idea: a person reading the entry'
       With = '| the timestamp-named-field idea: a person reading the entry' }

    # An arrangement of programs, a setup or a synchronizer is nobody's.
    @{ Report = 'EVALUATION.md'; Row = $arrangement
       As = 'occur in the operator''s own use**:'
       With = 'occur in use with one desktop and phones**:' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $arrangement
       As = 'In the operator''s arrangement, one KeePass'
       With = 'In an arrangement of one KeePass' }
    @{ Report = 'critique-crdt-security-and-fit.md'; Row = $arrangement
       As = 'How often KeePassDroid, in the operator''s own arrangement, saves'
       With = 'How often KeePassDroid, in an arrangement with a synchronizer, saves' }
    @{ Report = 'critique-crdt-survival.md'; Row = $arrangement
       As = 'if the operator''s arrangement lets KeePassDroid''s'
       With = 'if an arrangement lets KeePassDroid''s' }
    @{ Report = 'critique-crdt-survival.md'; Row = $arrangement
       As = 'If the operator''s synchronizer lets'
       With = 'If a synchronizer lets' }
    @{ Report = 'critique-crdt-survival.md'; Row = $arrangement
       As = 'through the operator''s own', '   synchronizer,'
       With = 'through a', '   synchronizer,' }
    @{ Report = 'option-crdt.md'; Row = $arrangement
       As = 'in the operator''s own', '   arrangement, which decides'
       With = 'in an arrangement', '   with a synchronizer, which decides' }
    @{ Report = 'critique-rules-linked-survival.md'; Row = $arrangement
       As = 'That is the operator''s arrangement, not something the plugin sees'
       With = 'That is a property of the arrangement, not something the plugin sees' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $arrangement
       As = 'For the operator''s own setup, one KeePass with the plugin and the', 'two phone programs, the state entry'
       With = 'For a setup of one KeePass with the plugin and the', 'Android programs in scope, the state entry' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $arrangement
       As = '2.1 in the operator''s own setup.'
       With = '2.1 in a setup of one KeePass and phones.' }

    # A database is an existing database, never a real one somebody holds.
    @{ Report = 'EVALUATION.md'; Row = $existing
       As = 'can afford at the operator''s', '   database size,'
       With = 'can afford at the size of an', '   existing database,' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $existing
       As = 'The transition of the operator''s own database is where this option is most likely'
       With = 'The transition of an existing database is where this option is most likely' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $existing
       As = 'The transition of the operator''s own database is where this option widens'
       With = 'The transition of an existing database is where this option widens' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $existing
       As = 'over a copy of the', 'operator''s database and kept'
       With = 'over a copy of an', 'existing database and kept' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $existing
       As = 'Only the operator''s own, as the project decided;'
       With = 'Only an existing database, as the project decided;' }
    @{ Report = 'option-grant-only.md'; Row = $existing
       As = 'How many narrowing rules the operator''s own database holds'
       With = 'How many narrowing rules an existing database holds' }
    @{ Report = 'option-grant-only.md'; Row = $existing
       As = 'That is the falsifier of this option and only the operator can answer it.'
       With = 'That is the falsifier of this option and only a count over such a database can answer it.' }
    @{ Report = 'option-grant-only.md'; Row = $existing
       As = 'exists beyond the operator''s own is the operator''s question'
       With = 'exists is the operator''s question' }
    @{ Report = 'option-grant-only.md'; Row = $existing
       As = 'not the operator''s database;'
       With = 'not an existing database;' }
    @{ Report = 'option-grant-only.md'; Row = $existing
       As = '1. **The operator''s own database**:'
       With = '1. **An existing database**:' }
    @{ Report = 'option-grant-only.md'; Row = $question
       As = '1. Does your database hold, or do you want, a node that'
       With = '1. Must a database be able to hold a node that' }
    @{ Report = 'digest-grant-only.md'; Row = $existing
       As = 'Measure first: the operator''s', 'narrowing count;'
       With = 'Measure first: the count of narrowing', 'rules in an existing database;' }

    # What is to be decided about the store, the scope or the design is the project's.
    @{ Report = 'EVALUATION.md'; Row = $decision
       As = 'question 1 for the operator.'
       With = 'question 1 for the project.' }
    @{ Report = 'EVALUATION.md'; Row = $decision
       As = '## 7. Questions only the operator can answer'
       With = '## 7. Questions that are the project''s to decide' }
    @{ Report = 'completeness-of-draft.md'; Row = $decision
       As = 'Question 1 asks the operator to choose'
       With = 'Question 1 asks the project to choose' }
    @{ Report = 'ground-inventory.md'; Row = $decision
       As = 'needs carrying over a question for the operator |'
       With = 'needs carrying over a question for the project |' }
    @{ Report = 'option-crdt.md'; Row = $decision
       As = 'It is a question for the operator.'
       With = 'It is a question for the project.' }
    @{ Report = 'option-crdt.md'; Row = $decision
       As = 'and it needs the operator''s ruling since it reverses'
       With = 'and it needs a decision of the project since it reverses' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $decision
       As = 'asks the operator is answered by these rulings unless the operator reopens them.'
       With = 'asks the project is answered by these rulings unless the project reopens them.' }
    @{ Report = 'critique-grant-only-security-and-fit.md'; Row = $decision
       As = 'can be put to the operator in the form that is true.'
       With = 'can be put to the project in the form that is true.' }
    @{ Report = 'option-grant-only.md'; Row = $decision
       As = 'exists is the operator''s question', '  (`EVALUATION.md` question 6)'
       With = 'exists is a question for the project', '  (`EVALUATION.md` question 6)' }
    @{ Report = 'option-grant-only.md'; Row = $decision
       As = '**How I would put it to the operator.**'
       With = '**How I would put it to the project.**' }
    @{ Report = 'option-grant-only.md'; Row = $decision
       As = 'Three questions decide it, and only the operator can answer them:'
       With = 'Three questions decide it, and only the project can answer them:' }
    @{ Report = 'critique-hardened-custom-data-security-and-fit.md'; Row = $decision
       As = 'That is a decision for the operator, as'
       With = 'That is a decision for the project, as' }
    @{ Report = 'option-hardened-custom-data.md'; Row = $decision
       As = 'the operator''s question (O7).'
       With = 'a question for the project (O7).' }
    @{ Report = 'critique-rules-central-security-and-fit.md'; Row = $decision
       As = '(serious; the residual is the operator''s call)'
       With = '(serious; the residual is the project''s call)' }
    @{ Report = 'critique-rules-central-security-and-fit.md'; Row = $decision
       As = 'is the operator''s', 'question (O7).'
       With = 'is a question for', 'the project (O7).' }
    @{ Report = 'option-rules-central.md'; Row = $decision
       As = 'is the', 'operator''s question (O7 in'
       With = 'is a', 'question for the project (O7 in' }
    @{ Report = 'critique-rules-linked-survival.md'; Row = $decision
       As = 'and the operator should be asked whether that is acceptable.'
       With = 'and the project decides whether that is acceptable.' }
    @{ Report = 'option-rules-linked.md'; Row = $decision
       As = '"the residual is the operator''s call"'
       With = '"the residual is the project''s call"' }
    @{ Report = 'option-rules-linked.md'; Row = $decision
       As = 'is in use is the operator''s question.'
       With = 'is in use is a question for the project.' }
    @{ Report = 'option-rules-on-node.md'; Row = $decision
       As = 'is the operator''s question (O7 in'
       With = 'is a question for the project (O7 in' }
    @{ Report = 'critique-single-entry-security-and-fit.md'; Row = $decision
       As = 'and so the', 'operator''s O7 question'
       With = 'and so the', 'project''s O7 question' }
    @{ Report = 'critique-single-entry-survival.md'; Row = $decision
       As = 'so it is the operator''s call.'
       With = 'so it is the project''s call.' }
    @{ Report = 'option-single-entry.md'; Row = $decision
       As = 'is a ruling for the operator;'
       With = 'is a decision for the project;' }
    @{ Report = 'option-single-entry.md'; Row = $decision
       As = 'is the operator''s', 'question (O7).'
       With = 'is a question for', 'the project (O7).' }
    @{ Report = 'option-wildcard.md'; Row = $decision
       As = '**For the operator to rule on**:'
       With = '**For the project to decide**:' }

    # The evaluation's questions ask about the requirement and the list, not about what anybody uses.
    @{ Report = 'EVALUATION.md'; Row = $question
       As = 'Is an operating rule acceptable (the phone', '   merges with Keepass2Android, or KeePassDroid only saves a copy it opened after KeePass''s last', '   save), or must'
       With = 'Is an operating rule acceptable (a program', '   merges before it saves, or saves only a copy it opened after KeePass''s last', '   save), or must' }
    @{ Report = 'EVALUATION.md'; Row = $question
       As = '10. **Which programs are in use.** KeePassXC''s merge, KeePassDX and its releases, KeeWeb: which', '    of them really meet these databases? That sizes every residue above, and KeeWeb merges may'
       With = '10. **Which programs are listed as compatible.** KeePassXC''s merge, KeePassDX and its releases,', '    KeeWeb: which of them are to be listed as compatible and measured further? That sizes every', '    residue above, and KeeWeb merges may' }
)

foreach ($entry in $wording) {
    if (-not $reports.Contains($entry.Report)) {
        throw "The wording table names '$($entry.Report)', which is not among the reports."
    }
    if ($kinds -notcontains $entry.Row) {
        throw "The wording table gives '$($entry.Row)' as a kind, which is not among the kinds."
    }
}

# What no copy may still hold once the rules have run.
$forbidden = @(
    '[A-Za-z]:\\Users\\',
    'AppData\\',
    'scratchpad',
    '\\Temp\\claude\\',
    $product,
    $revocation,
    $checkout,
    $trees,
    $sourceDir
)

$counts = [ordered]@{}
$touched = @{}
foreach ($rule in $rules) {
    $counts[$rule.Name] = 0
    $touched[$rule.Name] = New-Object System.Collections.Generic.HashSet[string]
}
foreach ($kind in $kinds) {
    $counts["wording: $kind"] = 0
    $touched["wording: $kind"] = New-Object System.Collections.Generic.HashSet[string]
}

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$copies = [ordered]@{}

foreach ($name in $reports.Keys) {
    $from = Join-Path $Source $name
    if (-not (Test-Path -LiteralPath $from)) {
        throw "The reports' directory holds no $name, which this copy expects."
    }

    $text = $utf8.GetString([System.IO.File]::ReadAllBytes($from))

    foreach ($rule in $rules) {
        if ($rule.ContainsKey('Reports') -and $rule.Reports -notcontains $name) {
            continue
        }

        $regex = New-Object System.Text.RegularExpressions.Regex($rule.Pattern, 'IgnoreCase, Multiline')
        $ruleName = $rule.Name
        $text = $regex.Replace($text, [System.Text.RegularExpressions.MatchEvaluator] {
            param($m)
            if ($rule.ContainsKey('Only') -and -not (& $rule.Only $m)) {
                return $m.Value
            }

            $counts[$ruleName] = $counts[$ruleName] + 1
            [void] $touched[$ruleName].Add($name)
            [string] (& $rule.With $m)
        })
    }

    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    foreach ($entry in $wording) {
        if ($entry.Report -ne $name) {
            continue
        }

        $as = [string]::Join($newline, [string[]] $entry.As)
        $with = [string]::Join($newline, [string[]] $entry.With)
        $times = if ($entry.ContainsKey('Times')) { $entry.Times } else { 1 }
        $found = 0
        $at = $text.IndexOf($as, [System.StringComparison]::Ordinal)
        while ($at -ge 0) {
            $found++
            $at = $text.IndexOf($as, $at + $as.Length, [System.StringComparison]::Ordinal)
        }
        if ($found -ne $times) {
            throw "$name holds '$as' $found times where the wording table expects $times. Nothing was written."
        }

        $text = $text.Replace($as, $with)
        $kind = "wording: $($entry.Row)"
        $counts[$kind] = $counts[$kind] + $times
        [void] $touched[$kind].Add($name)
    }

    foreach ($pattern in $forbidden) {
        $left = [regex]::Match($text, $pattern, 'IgnoreCase')
        if ($left.Success) {
            $at = $text.Substring([Math]::Max(0, $left.Index - 40), [Math]::Min(80, $text.Length - [Math]::Max(0, $left.Index - 40)))
            throw "$name still holds '$($left.Value)' after the rules ran, near: $at. Nothing was written."
        }
    }

    $copies[$reports[$name]] = $text
}

foreach ($target in $copies.Keys) {
    $path = Join-Path $root $target
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [System.IO.File]::WriteAllBytes($path, $utf8.GetBytes($copies[$target]))
}

$left = Get-ChildItem -LiteralPath $Source -File | Where-Object { -not $reports.Contains($_.Name) }
foreach ($file in $left) {
    Write-Host "  not copied: $($file.Name)" -ForegroundColor DarkGray
}

Write-Host "  copied $($copies.Count) reports into papers" -ForegroundColor DarkGray
foreach ($ruleName in $counts.Keys) {
    [pscustomobject]@{
        Replaced = $counts[$ruleName]
        Files    = $touched[$ruleName].Count
        Rule     = $ruleName
    }
}
