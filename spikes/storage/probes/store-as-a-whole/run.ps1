<#
.SYNOPSIS
    Runs the family store-as-a-whole: the recommended store in the design's model, and over real
    files through KeePassLib 2.62 and keepassxc-cli 2.4.2, 2.6.6, 2.7.12 and 2.8.0.

.DESCRIPTION
    Windows PowerShell 5.1, from the branch root or anywhere. Provides and checks KeePass 2.62 and
    the four KeePassXC releases through the branch's tools, empties `out\` and `work\`, then runs
    each part as a process of its own, at most -Parallel at a time, each writing what it prints
    into `out\<part>.txt`. The databases go into `work\`, which is not committed; nothing shows a
    window. Each part's time goes to `out\times.txt`. Then `compare.py` holds `out\` to `recorded\`.

      design-full          model\crdt_model.py full: the copy with every new switch at its default
      store-search         model\store_runs.py search: the design's search on the store
      store-search-noauth  the same, with no hand deletion or edit of the authority's entry
      store-ablations      the cost of each change taken by decision
      store-ext            the extended model of the survival critique, on the store
      store-scenarios      the design's 20 replays on the store
      store-section6       the sequences of EVALUATION.md section 6
      store-repair-closes  histories the repair of change 7 closes, and those it leaves
      b-growth             probe\growth.py: B.7
      b-merges             probe\cases.py merges: B.1, B.3, B.4
      b-slow               probe\cases.py slow: the KeePassXC cells under the slow derivation
      b-dedupe             probe\cases.py dedupe: B.2
      b-deletions          probe\cases.py deletions: B.5
      b-clear              probe\cases.py clear: every deletion record removed (desktop measurement)
      b-dupes              probe\cases.py dupes: maintenance commands (desktop measurement)
      b-repair             probe\cases.py repair: B.6, with the import cases (desktop measurement)
      b-random             probe\cases.py random 300 7: the bridge over random histories (C)

    With -Decided, the store as decided on 2026-11-16 instead: no repair after a Synchronize, and
    every file read under the history rule. Its parts write into `out\decided\` and are compared
    with `recorded-decided\`; the databases go into `work\d-<part>`:

      store-search         store_runs.py --decided search: the search with no repair
      store-search-noauth  the same, with no hand deletion or edit of the authority's entry
      store-ext            the extended model with no repair
      b-growth             growth.py --decided: B.7, read under the history rule
      b-merges, b-dedupe, b-deletions, b-clear, b-dupes, b-repair, b-random
                           cases.py --decided: the same parts with no repair and the history rule
      b-history            cases.py --decided history: each kind of store entry edited, the history
                           rule beside the plain stop, then merged both ways

    With -Rewrite (ADDED, two readings), the store as decided and an edited record written again at
    the plugin's next own write, in two forms: `backup` (the edited version put into the history, as
    an edit through KeePass's own path does) and `cleared` (the entry's history emptied). Output in
    `out\rewrite\`, compared with `recorded-rewrite\`; databases in `work\wb-*`, `work\wc-*` and
    `work\wn-*`:

      one, alternate, two, prune
                           readings.py --rewrite backup: the parts of reading 1
      b-random             cases.py --rewrite backup random 300 7
      one-cleared, alternate-cleared, two-cleared, prune-cleared, b-random-cleared
                           the same in the form `cleared`
      two-none, prune-none the control: the same two-gate sequences with nothing written again

    With -Copies (ADDED, two readings), the store as decided read with a record that can only take
    access away counted from a copy under another identifier, held to the completeness check like
    any record (`held`) and, in the parts ending in `-exempt`, not (`exempt`). Output in
    `out\copies\`, compared with `recorded-copies\`; databases in `work\ch-*` and `work\ce-*`:

      b-merges, b-dedupe, b-deletions, b-clear, b-dupes, b-repair, b-random
                           cases.py --copies held: the parts of the store as decided
      copies               readings.py --copies held copies: copies that are not an import
      the same names ending in -exempt
                           the same with --copies exempt

    With -Reading <level> (ADDED, copies extended), the store as decided read with a wider reading
    of a copy, rules per profile switched on: `nothing`, `profile`, `verb` or `cap`, which is how
    far a copied rule version limits the object it names (read.py's `ext`), or `cap-items`, which
    is `cap` with the items of a copied summary entry counted as well. Each is held to the
    completeness check and, in the parts ending in `-exempt`, exempt from it. Output in
    `out\reading-<level>\`, compared with `recorded-reading-<level>\`; databases in
    `work\x<first letter of the level>h-*` and `work\x<first letter of the level>e-*`, the
    letter `i` for `cap-items`:

      b-merges, b-dedupe, b-deletions, b-clear, b-dupes, b-repair, b-random
                           cases.py --reading <level> held: the parts of the store as decided,
                           `b-merges` with nine further histories, eight of them with rules per
                           profile
      copies               readings.py --reading <level> held copies
      kinds                readings.py --reading <level> held kinds: a retirement that settles, a
                           summary entry's items and a store key, each read from a copy or not
      the same names ending in -exempt
                           the same with exempt

    With -Rows (ADDED, rules per profile), the store as decided with a rule stored as a record for
    each node and profile and the node's switch "Refuse any profile not named here" as a record of
    its own. A removed row is written as decided (`empty`: an empty rule where the switch refuses)
    unless a part names `off` (the control: the rule taken off). Output in `out\rows\`, compared
    with `recorded-rows\`; databases in `work\pe-*` and `work\po-*`:

      b-merges, b-deletions, b-clear, b-history
                           cases.py --rows empty: the parts of the store as decided, per profile
      b-random             cases.py --rows empty random 300 7: the histories of the store as
                           decided, each file also played in the whole-rule form
      b-random-rows, b-random-rows-off
                           cases.py --rows empty|off random-rows 300 11: histories of rows and
                           switches, each merge held to what both copies gave before it
      store-search, store-search-acts
                           store_runs.py --rows empty [acts] search: the model's search, with the
                           acts on a row and on the switch in the alphabet in the second
      store-search-noauth  the same with those acts, and no hand deletion or edit of the
                           authority's entry
      beyond, beyond-again, beyond-off, beyond-whole
                           store_runs.py --rows empty|again|off|whole beyond 200000: how often a
                           merge in the model gives a letter neither copy gave just before it
      apart, same, states, switch, dels, growth
                           rows.py --rows empty: items 2 to 7 of measured-rules-per-profile.md

    With -Owed (ADDED, owed forms), rules per profile with a removed row written as decided, in the
    forms STORE.md 644 item 24 left owed. Output in `out\owed\`, compared with `recorded-owed\`;
    databases in `work\qo-*`:

      combine              store_runs.py --owed combine: versions of a row made apart, the full mask
      beyond-2-rw, beyond-4-rw, beyond-2-mask, beyond-4-mask, beyond-4-mask-3
                           store_runs.py --owed P L V beyond 200000 34: `beyond` with two or four
                           profiles, the letters read and write or the full mask, and two or three
                           levels, each merge held to both copies by letters and by capabilities
      afters, merges4, random4, levels, random3, offlist, tags
                           owed.py: items 1 to 5 of measured-rules-owed-forms.md on real files

    With -Answers (ADDED, renewal answers), the standing answers to a renewal of STORE.md 618 and
    S7: the store as decided with crdt_model.ANSWERS on, the reading of lost versions of
    measured-lost-settled, every certificate and client key in hexadecimal on files. Output in
    `out\answers\`, compared with `recorded-answers\`; databases in `work\an-*`; then the three files
    of vectors that `vectors.py --answers` prints, into `out\answers\vectors\`, held to
    `recorded-lost\vectors\` in `out\answers\vectors-compare.txt`:

      cells, losses, random
                           answers.py: A1 to A14 and the lost versions on real files, every merge
                           both ways, and 300 random histories of answers and merges on files
      model-hand, model-losses
                           answer_runs.py hand and losses: the same sequences in the model, under
                           both oracles, with the two orders of the writes made apart
      laws, exhaustive-4, exhaustive-5
                           answer_runs.py: the meet over the chain; every sequence of 4 steps of the
                           answers' alphabet with the deletions by hand, and of 5 without
      answers-two, answers-three, answers-hand, answers-long
                           answer_runs.py answers: 200,000 random histories of the answers' acts each
      search-2, search-3   answer_runs.py search: the design's twelve configurations over 2 and 3
                           files, 20,000 histories each, its seeds, the answers' acts in the alphabet
      bridge-two, bridge-hand
                           answer_runs.py bridge: every file of 2,000 histories read by read.py

    With -Seal (ADDED, seal again), Seal again as PLAN 3.1 of the pull request that builds it
    designs it: the configuration of -Answers with crdt_model.SEAL and read.py's listing of what a
    stopped store lost on. Output in `out\seal\`, compared with `recorded-seal\`; databases in
    `work\se-*`; then the three files of vectors that `vectors.py --seal` prints, into
    `out\seal\vectors\`, held to `recorded-answers\vectors\` in `out\seal\vectors-compare.txt`:

      cells, edits         seal.py: R1 to R21 on real files, every merge both ways; each kind of
                           record edited past the history limit, sealed, then merged both ways
      hand                 seal_runs.py hand: R1 to R21 in the model, Create on a stopped store with
                           the switch off, the seal beside an act, the seal composed as shown
      seal-two, seal-three, seal-long
                           seal_runs.py seal: random histories of the seal's alphabet
      damage               seal_runs.py damage: configurations F, I and K, a damaged version sealed
      exhaustive-4         seal_runs.py exhaustive 4: every sequence of 4 steps of a narrow alphabet
      search-2, search-3   seal_runs.py search: the design's twelve configurations over 2 and 3
                           files, 20,000 histories each, its seeds, the seal's acts in the alphabet
      bridge               seal_runs.py bridge: every file of 2,000 histories read by read.py, and
                           the seal read.py plans held to the seal the model writes

    With -Starts (ADDED, started again), the store started again of STORE.md 632 and the keys of
    649: the configuration of -Seal with crdt_model.STARTS on, the keep record and the version
    record on files and the room of 643 read there. Output in `out\starts\`; databases in
    `work\sa-*`:

      cells, random, room, dialog
                           restart.py: T1 to T16 on real files, every merge both ways; 300 random
                           histories of a store started again on files; the two stores at the
                           ceilings, started again; what Seal again's reading lists of a store a
                           start stopped, and the seal pressed over it
      model-hand, laws     restart_runs.py hand and laws: T1 to T16 in the model; the marking of
                           649 step 4 held to its laws and to read.py's
      exhaustive-4, exhaustive-4-noauth, exhaustive-5
                           restart_runs.py: every sequence of 4 steps of a small alphabet with the
                           deletions by hand, of 4 with a keep record's alone, and of 5 without
      starts-two, starts-three, starts-hand, starts-hand-noauth, starts-long
                           restart_runs.py starts: 200,000 random histories of the acts of a store
                           started again each
      search-2, search-3, search-noauth-2, search-noauth-3
                           restart_runs.py search: the design's twelve configurations over 2 and 3
                           files, 20,000 histories each, restart in the alphabet; again with no
                           entry that can carry a store key deleted or edited by hand
      bridge-two, bridge-hand
                           restart_runs.py bridge: every file of 2,000 histories read by read.py

.PARAMETER Parts
    Which parts to run; every part by default.

.PARAMETER Parallel
    How many parts run at once.

.PARAMETER Decided
    Runs the store as decided rather than the recorded configuration with the repair.

.PARAMETER Rewrite
    Runs reading 1 of the store as decided: an edited record written again at the next own write.

.PARAMETER Copies
    Runs reading 2 of the store as decided: a record that takes access away read from a copy.

.PARAMETER Reading
    Runs a wider reading of a copy on the store as decided: nothing, profile, verb, cap or
    cap-items.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Decided
#>
param([string[]] $Parts = @(), [int] $Parallel = 6, [switch] $Decided, [switch] $Rewrite, [switch] $Copies,
    [ValidateSet('', 'nothing', 'profile', 'verb', 'cap', 'cap-items')] [string] $Reading = '',
    [switch] $Rows,     # ADDED (rules per profile)
    [switch] $Owed,     # ADDED (owed forms)
    [switch] $S34,      # ADDED (S34 forms)
    [switch] $Answers,  # ADDED (renewal answers)
    [switch] $Seal,     # ADDED (seal again)
    [switch] $Starts)   # ADDED (started again)
if (@($Decided.IsPresent, $Rewrite.IsPresent, $Copies.IsPresent, ($Reading -ne ''), $Rows.IsPresent, $Owed.IsPresent, $S34.IsPresent, $Answers.IsPresent, $Seal.IsPresent, $Starts.IsPresent | Where-Object { $_ }).Count -gt 1) {
    throw 'One of -Decided, -Rewrite, -Copies, -Reading, -Rows, -Owed, -S34, -Answers, -Seal, -Starts.'     # ADDED (two readings); -Reading (copies extended); -Rows (rules per profile); -Owed (owed forms); -S34 (S34 forms); -Answers (renewal answers); -Seal (seal again); -Starts (started again)
}

$ErrorActionPreference = 'Stop'
$family = $PSScriptRoot
$root = (Resolve-Path (Join-Path $family '..\..\..\..')).Path
. (Join-Path $root 'tools\KeePass.ps1')
. (Join-Path $root 'tools\KeePassXC.ps1')
[void](Get-KeePassExe)
foreach ($version in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { [void](Get-KeePassXCCli -Version $version) }

$model = Join-Path $family 'model'
$probe = Join-Path $family 'probe'
$all = [ordered]@{
    'design-full'         = @("$model\crdt_model.py", 'full')
    'store-search'        = @("$model\store_runs.py", 'search')
    'store-search-noauth' = @("$model\store_runs.py", 'search-noauth')
    'store-ablations'     = @("$model\store_runs.py", 'ablations')
    'store-ext'           = @("$model\store_runs.py", 'ext', '8000')
    'store-scenarios'     = @("$model\store_runs.py", 'scenarios')
    'store-section6'      = @("$model\store_runs.py", 'section6')
    'store-repair-closes' = @("$model\repair_closes.py", '20000')
    'b-growth'            = @("$probe\growth.py")
    'b-merges'            = @("$probe\cases.py", 'merges')
    'b-slow'              = @("$probe\cases.py", 'slow')
    'b-dedupe'            = @("$probe\cases.py", 'dedupe')
    'b-deletions'         = @("$probe\cases.py", 'deletions')
    'b-clear'             = @("$probe\cases.py", 'clear')
    'b-dupes'             = @("$probe\cases.py", 'dupes')
    'b-repair'            = @("$probe\cases.py", 'repair')
    'b-random'            = @("$probe\cases.py", 'random', '300', '7')
}
if ($Decided) {
    $all = [ordered]@{
        'store-search'        = @("$model\store_runs.py", '--decided', 'search')
        'store-search-noauth' = @("$model\store_runs.py", '--decided', 'search-noauth')
        'store-ext'           = @("$model\store_runs.py", '--decided', 'ext', '8000')
        'b-growth'            = @("$probe\growth.py", '--decided')
        'b-merges'            = @("$probe\cases.py", '--decided', 'merges')
        'b-dedupe'            = @("$probe\cases.py", '--decided', 'dedupe')
        'b-deletions'         = @("$probe\cases.py", '--decided', 'deletions')
        'b-clear'             = @("$probe\cases.py", '--decided', 'clear')
        'b-dupes'             = @("$probe\cases.py", '--decided', 'dupes')
        'b-repair'            = @("$probe\cases.py", '--decided', 'repair')
        'b-random'            = @("$probe\cases.py", '--decided', 'random', '300', '7')
        'b-history'           = @("$probe\cases.py", '--decided', 'history')
    }
}
# ADDED (two readings): the two readings, each a configuration of its own
$config = $null
if ($Rewrite) {
    $config = 'rewrite'
    $all = [ordered]@{}
    foreach ($form in 'backup', 'cleared') {
        $suffix = if ($form -eq 'backup') { '' } else { '-cleared' }
        foreach ($p in 'one', 'alternate', 'two', 'prune') {
            $all["$p$suffix"] = @("$probe\readings.py", '--rewrite', $form, $p)
        }
        $all["b-random$suffix"] = @("$probe\cases.py", '--rewrite', $form, 'random', '300', '7')
    }
    foreach ($p in 'two', 'prune') { $all["$p-none"] = @("$probe\readings.py", '--rewrite', 'none', $p) }
}
if ($Copies) {
    $config = 'copies'
    $all = [ordered]@{}
    foreach ($variant in 'held', 'exempt') {
        $suffix = if ($variant -eq 'held') { '' } else { '-exempt' }
        foreach ($p in 'merges', 'dedupe', 'deletions', 'clear', 'dupes', 'repair') {
            $all["b-$p$suffix"] = @("$probe\cases.py", '--copies', $variant, $p)
        }
        $all["b-random$suffix"] = @("$probe\cases.py", '--copies', $variant, 'random', '300', '7')
        $all["copies$suffix"] = @("$probe\readings.py", '--copies', $variant, 'copies')
    }
}
# ADDED (copies extended): a wider reading of a copy, a configuration of its own for each level
if ($Reading) {
    $config = "reading-$Reading"
    $all = [ordered]@{}
    foreach ($variant in 'held', 'exempt') {
        $suffix = if ($variant -eq 'held') { '' } else { '-exempt' }
        foreach ($p in 'merges', 'dedupe', 'deletions', 'clear', 'dupes', 'repair') {
            $all["b-$p$suffix"] = @("$probe\cases.py", '--reading', $Reading, $variant, $p)
        }
        $all["b-random$suffix"] = @("$probe\cases.py", '--reading', $Reading, $variant, 'random', '300', '7')
        $all["copies$suffix"] = @("$probe\readings.py", '--reading', $Reading, $variant, 'copies')
        $all["kinds$suffix"] = @("$probe\readings.py", '--reading', $Reading, $variant, 'kinds')
    }
}
# ADDED (rules per profile): a rule as a record for each node and profile
if ($Rows) {
    $config = 'rows'
    $all = [ordered]@{
        'store-search'       = @("$model\store_runs.py", '--rows', 'empty', 'search')
        'store-search-acts'  = @("$model\store_runs.py", '--rows', 'empty', 'acts', 'search')
        'store-search-noauth' = @("$model\store_runs.py", '--rows', 'empty', 'acts', 'search-noauth')
        'beyond'            = @("$model\store_runs.py", '--rows', 'empty', 'beyond', '200000')
        'beyond-again'       = @("$model\store_runs.py", '--rows', 'again', 'beyond', '200000')
        'beyond-off'         = @("$model\store_runs.py", '--rows', 'off', 'beyond', '200000')
        'beyond-whole'       = @("$model\store_runs.py", '--rows', 'whole', 'beyond', '200000')
        'b-merges'           = @("$probe\cases.py", '--rows', 'empty', 'merges')
        'b-random'           = @("$probe\cases.py", '--rows', 'empty', 'random', '300', '7')
        'b-clear'            = @("$probe\cases.py", '--rows', 'empty', 'clear')
        'b-deletions'        = @("$probe\cases.py", '--rows', 'empty', 'deletions')
        'b-history'          = @("$probe\cases.py", '--rows', 'empty', 'history')
        'b-random-rows'      = @("$probe\cases.py", '--rows', 'empty', 'random-rows', '300', '11')
        'b-random-rows-off'  = @("$probe\cases.py", '--rows', 'off', 'random-rows', '300', '11')
        'apart'              = @("$probe\rows.py", '--rows', 'empty', 'apart')
        'same'               = @("$probe\rows.py", '--rows', 'empty', 'same')
        'states'             = @("$probe\rows.py", '--rows', 'empty', 'states')
        'switch'             = @("$probe\rows.py", '--rows', 'empty', 'switch')
        'dels'               = @("$probe\rows.py", '--rows', 'empty', 'dels')
        'growth'             = @("$probe\rows.py", '--rows', 'empty', 'growth')
    }
}
# ADDED (owed forms): the forms STORE.md 644 item 24 left owed, the longest parts first
if ($Owed) {
    $config = 'owed'
    $all = [ordered]@{
        'afters'          = @("$probe\owed.py", 'afters')
        'beyond-4-mask-3' = @("$model\store_runs.py", '--owed', '4', 'crusdlap', '3', 'beyond', '200000', '34')
        'beyond-4-mask'   = @("$model\store_runs.py", '--owed', '4', 'crusdlap', '2', 'beyond', '200000', '34')
        'beyond-4-rw'     = @("$model\store_runs.py", '--owed', '4', 'rw', '2', 'beyond', '200000', '34')
        'beyond-2-mask'   = @("$model\store_runs.py", '--owed', '2', 'crusdlap', '2', 'beyond', '200000', '34')
        'beyond-2-rw'     = @("$model\store_runs.py", '--owed', '2', 'rw', '2', 'beyond', '200000', '34')
        'random4'         = @("$probe\owed.py", 'random4', '300', '13')
        'random3'         = @("$probe\owed.py", 'random3', '300', '17')
        'levels'          = @("$probe\owed.py", 'levels')
        'merges4'         = @("$probe\owed.py", 'merges4')
        'offlist'         = @("$probe\owed.py", 'offlist')
        'tags'            = @("$probe\owed.py", 'tags')
        'combine'         = @("$model\store_runs.py", '--owed', 'combine', '200000')
    }
}
# ADDED (S34 forms): the forms of S34-DESIGN.md, each beside the store as decided, the longest first
if ($S34) {
    $config = 's34'
    $all = [ordered]@{}
    foreach ($form in 'seen', 'above') {
        $all["search-acts-$form"] = @("$model\store_runs.py", '--depform', $form, '--rows', 'off', 'acts', 'search')
        $all["beyond-4-mask-3-$form"] = @("$model\store_runs.py", '--dep', $form, 'off', '4', 'crusdlap', '3', '200000', '34')
        $all["long-2-$form"] = @("$model\store_runs.py", '--dep', $form, 'off', '2', 'rw', '2', '200000', '34', '8-14')
        $all["moves-3-$form"] = @("$model\store_runs.py", '--dep', $form, 'off', '2', 'rw', '3', '200000', '34', 'moves')
        $all["beyond-4-mask-$form"] = @("$model\store_runs.py", '--dep', $form, 'off', '4', 'crusdlap', '2', '200000', '34')
        $all["beyond-2-mask-$form"] = @("$model\store_runs.py", '--dep', $form, 'off', '2', 'crusdlap', '2', '200000', '34')
        $all["beyond-4-rw-$form"] = @("$model\store_runs.py", '--dep', $form, 'off', '4', 'rw', '2', '200000', '34')
        foreach ($variant in 'off', 'again', 'empty') {
            $all["beyond-2-rw-$variant-$form"] = @("$model\store_runs.py", '--dep', $form, $variant, '2', 'rw', '2', '200000', '34')
        }
    }
    foreach ($variant in 'again', 'off') {
        $all["beyond-2-rw-$variant-none"] = @("$model\store_runs.py", '--dep', 'none', $variant, '2', 'rw', '2', '200000', '34')
    }
    $all['combine'] = @("$model\store_runs.py", '--dep', 'combine', '200000')
    foreach ($form in 'seen', 'above') {
        $all["subtree-$form"] = @("$model\store_runs.py", '--dep', 'subtree', $form)
        foreach ($p in 'switch', 'cells3', 'cells4', 'afters', 'moves', 'hand') {
            $all["$p-$form"] = @("$probe\s34.py", $p, $form)
        }
    }
}
# ADDED (renewal answers): the standing answers to a renewal, the longest parts first; each model part
# draws on four processes of its own
if ($Answers) {
    $config = 'answers'
    $all = [ordered]@{
        'exhaustive-5'  = @("$model\answer_runs.py", 'exhaustive', '5', 'none', '4')
        'cells'         = @("$probe\answers.py", 'cells')
        'random'        = @("$probe\answers.py", 'random', '300', '51')
        'losses'        = @("$probe\answers.py", 'losses')
        'exhaustive-4'  = @("$model\answer_runs.py", 'exhaustive', '4', 'faults', '4')
        'search-3'      = @("$model\answer_runs.py", 'search', '3', '20000', '4')
        'search-2'      = @("$model\answer_runs.py", 'search', '2', '20000', '4')
        'answers-long'  = @("$model\answer_runs.py", 'answers', 'two', '200000', '34', '8', '16', '4')
        'answers-three' = @("$model\answer_runs.py", 'answers', 'three', '200000', '32', '4', '12', '4')
        'answers-hand'  = @("$model\answer_runs.py", 'answers', 'hand', '200000', '33', '4', '12', '4')
        'answers-two'   = @("$model\answer_runs.py", 'answers', 'two', '200000', '31', '4', '12', '4')
        'bridge-two'    = @("$model\answer_runs.py", 'bridge', 'two', '2000', '41', '4')
        'bridge-hand'   = @("$model\answer_runs.py", 'bridge', 'hand', '2000', '42', '4')
        'model-hand'    = @("$model\answer_runs.py", 'hand')
        'model-losses'  = @("$model\answer_runs.py", 'losses')
        'laws'          = @("$model\answer_runs.py", 'laws', '20000', '7')
    }
}
# ADDED (seal again): Seal again, the longest parts first; each model part draws on four processes of
# its own
if ($Seal) {
    $config = 'seal'
    $all = [ordered]@{
        'exhaustive-4' = @("$model\seal_runs.py", 'exhaustive', '4', '4')
        'cells'        = @("$probe\seal.py", 'cells')
        'edits'        = @("$probe\seal.py", 'edits')
        'search-3'     = @("$model\seal_runs.py", 'search', '3', '20000', '4')
        'search-2'     = @("$model\seal_runs.py", 'search', '2', '20000', '4')
        'seal-two'     = @("$model\seal_runs.py", 'seal', 'two', '200000', '61', '4', '12', '4')
        'seal-three'   = @("$model\seal_runs.py", 'seal', 'three', '100000', '62', '4', '12', '4')
        'seal-long'    = @("$model\seal_runs.py", 'seal', 'two', '100000', '63', '8', '16', '4')
        'damage'       = @("$model\seal_runs.py", 'damage', '20000', '4')
        'bridge'       = @("$model\seal_runs.py", 'bridge', 'two', '2000', '71', '4')
        'hand'         = @("$model\seal_runs.py", 'hand')
    }
}
# ADDED (started again): the store started again, the longest parts first; each model part draws on
# four processes of its own
if ($Starts) {
    $config = 'starts'
    $all = [ordered]@{
        'exhaustive-5' = @("$model\restart_runs.py", 'exhaustive', '5', 'none', '4')
        'cells'        = @("$probe\restart.py", 'cells')
        'random'       = @("$probe\restart.py", 'random', '300', '81')
        'room'         = @("$probe\restart.py", 'room')
        'dialog'       = @("$probe\restart.py", 'dialog')
        'exhaustive-4' = @("$model\restart_runs.py", 'exhaustive', '4', 'faults', '4')
        'exhaustive-4-noauth' = @("$model\restart_runs.py", 'exhaustive', '4', 'noauth', '4')
        'search-3'     = @("$model\restart_runs.py", 'search', '3', '20000', '4')
        'search-2'     = @("$model\restart_runs.py", 'search', '2', '20000', '4')
        'search-noauth-3' = @("$model\restart_runs.py", 'search-noauth', '3', '20000', '4')
        'search-noauth-2' = @("$model\restart_runs.py", 'search-noauth', '2', '20000', '4')
        'starts-long'  = @("$model\restart_runs.py", 'starts', 'two', '200000', '64', '8', '16', '4')
        'starts-three' = @("$model\restart_runs.py", 'starts', 'three', '200000', '62', '4', '12', '4')
        'starts-hand'  = @("$model\restart_runs.py", 'starts', 'hand', '200000', '63', '4', '12', '4')
        'starts-hand-noauth' = @("$model\restart_runs.py", 'starts', 'hand-noauth', '200000', '65', '4', '12', '4')
        'starts-two'   = @("$model\restart_runs.py", 'starts', 'two', '200000', '61', '4', '12', '4')
        'bridge-two'   = @("$model\restart_runs.py", 'bridge', 'two', '2000', '71', '4')
        'bridge-hand'  = @("$model\restart_runs.py", 'bridge', 'hand', '2000', '72', '4')
        'model-hand'   = @("$model\restart_runs.py", 'hand')
        'laws'         = @("$model\restart_runs.py", 'laws', '20000', '7')
    }
}
# Through -File a list arrives as one string, so commas are split here.
$Parts = @($Parts | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Parts.Count -eq 0) { $Parts = @($all.Keys) }
foreach ($p in $Parts) { if (-not $all.Contains($p)) { throw "No part named $p." } }

$out = Join-Path $family 'out'
$work = Join-Path $family 'work'
if ($Decided) { $out = Join-Path $out 'decided' }
if ($config) { $out = Join-Path $out $config }      # ADDED (two readings)
if ($Parts.Count -eq $all.Count) {
    if ($config) {
        # ADDED (two readings): the configuration's own output and databases only
        if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
        $prefixes = if ($Rewrite) { 'wb-*', 'wc-*', 'wn-*' } elseif ($Copies) { 'ch-*', 'ce-*' } elseif ($Rows) { 'pe-*', 'po-*' } elseif ($Owed) { 'qo-*' } elseif ($S34) { 'qss-*', 'qsa-*' } elseif ($Answers) { 'an-*' } elseif ($Seal) { 'se-*' } elseif ($Starts) { 'sa-*' } else {     # ADDED (owed forms): qo-*; (S34 forms): qss-* and qsa-*; (renewal answers): an-*; (seal again): se-*; (started again): sa-*
            $letter = if ($Reading -eq 'cap-items') { 'i' } else { $Reading[0] }     # ADDED (copies extended)
            "x${letter}h-*", "x${letter}e-*"
        }
        if (Test-Path -LiteralPath $work) {
            foreach ($f in $prefixes) { Get-ChildItem -LiteralPath $work -Directory -Filter $f | Remove-Item -Recurse -Force }
        }
    } elseif ($Decided) {
        # the decided parts' own output and databases only
        if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
        if (Test-Path -LiteralPath $work) {
            Get-ChildItem -LiteralPath $work -Directory -Filter 'd-*' | Remove-Item -Recurse -Force
        }
    } else {
        foreach ($dir in $out, $work) {
            if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        }
    }
}
foreach ($dir in $out, $work) {
    if (-not (Test-Path -LiteralPath $dir)) { [void](New-Item -ItemType Directory -Path $dir -Force) }
}
# A part named alone replaces only its own output; each part's databases are in a directory of
# its own under work\, which the part empties itself.
foreach ($p in $Parts) {
    foreach ($ext in '.txt', '.err') {
        $f = Join-Path $out "$p$ext"
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    }
}

$running = @{}
$times = New-Object System.Collections.Generic.List[string]
$queue = New-Object System.Collections.Queue
foreach ($p in $Parts) { $queue.Enqueue($p) }
while ($queue.Count -gt 0 -or $running.Count -gt 0) {
    while ($queue.Count -gt 0 -and $running.Count -lt $Parallel) {
        $p = $queue.Dequeue()
        $argv = @('run', '--no-project', 'python', '-I', '-X', 'utf8') + $all[$p]
        $proc = Start-Process -FilePath 'uv' -ArgumentList $argv -NoNewWindow -PassThru `
            -RedirectStandardOutput (Join-Path $out "$p.txt") -RedirectStandardError (Join-Path $out "$p.err")
        [void]$proc.Handle     # so that ExitCode is kept once the process has gone
        $running[$p] = @{ Process = $proc; Started = Get-Date }
        Write-Host "started $p"
    }
    Start-Sleep -Seconds 5
    foreach ($p in @($running.Keys)) {
        $r = $running[$p]
        if ($r.Process.HasExited) {
            $line = '{0}: {1:N0} s, exit {2}' -f $p, ((Get-Date) - $r.Started).TotalSeconds, $r.Process.ExitCode
            $times.Add($line)
            Write-Host $line
            $running.Remove($p)
        }
    }
}
$times | Add-Content -Encoding UTF8 (Join-Path $out 'times.txt')
if ($Rows) {     # ADDED (rules per profile): each cell of three parts against the store as decided's record
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare.py') --rows-against-decided |
        Set-Content -Encoding UTF8 (Join-Path $out 'against-decided.txt')
}
if ($Answers -and $Parts.Count -eq $all.Count) {     # ADDED (renewal answers): the vectors, and how they hold
    $vectors = Join-Path $out 'vectors'
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare_answers.py') print $vectors |
        Set-Content -Encoding UTF8 (Join-Path $out 'vectors-print.txt')
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare_answers.py') vectors $vectors (Join-Path $family 'recorded-lost\vectors') |
        Set-Content -Encoding UTF8 (Join-Path $out 'vectors-compare.txt')
}
if ($Seal -and $Parts.Count -eq $all.Count) {     # ADDED (seal again): the vectors, and how they hold
    $vectors = Join-Path $out 'vectors'
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare_seal.py') print $vectors |
        Set-Content -Encoding UTF8 (Join-Path $out 'vectors-print.txt')
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare_seal.py') vectors $vectors (Join-Path $family 'recorded-answers\vectors') |
        Set-Content -Encoding UTF8 (Join-Path $out 'vectors-compare.txt')
}
if ($Starts -and $Parts.Count -eq $all.Count) {     # ADDED (started again): the vectors, and how they hold
    $vectors = Join-Path $out 'vectors'
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare_starts.py') print $vectors |
        Set-Content -Encoding UTF8 (Join-Path $out 'vectors-print.txt')
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare_starts.py') vectors $vectors (Join-Path $family 'recorded-seal\vectors') |
        Set-Content -Encoding UTF8 (Join-Path $out 'vectors-compare.txt')
}
if ($config) {     # ADDED (two readings)
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare.py') "--$config"
} elseif ($Decided) {
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare.py') --decided
} else {
    & uv run --no-project python -I -X utf8 (Join-Path $family 'compare.py')
}
