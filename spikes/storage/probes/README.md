# Probes

Every number a paper in `papers/` quotes from running code is traced to a probe here, which anybody
can run again from the branch root. A number that does not come out the same is marked so, never
carried over on trust. These are the rules every family of probes follows.

## Where a probe lives

- **One directory per family**, `spikes\storage\probes\<family>\`, named after the paper the probes
  belong to: the option's key for an option (`store-entries`), `ground-<key>` for a ground report
  (`ground-keepass`). The CRDT models are `spikes\storage\model\` and the emulator measurement is
  `spikes\storage\android\`, under the same rules.
- **Inside an option's family, a directory per paper**: `design\`, `survival\` and
  `security-and-fit\`, because the design's probe and its critics' probes share file names. Below
  that, each probe keeps the layout its agent gave it, `probe\`, `scripts\` and the rest, because
  the papers cite those paths: `tools/Copy-Papers.ps1` turned every working directory a paper names
  into these places.

## What `RUN.md` holds

Each family has a `RUN.md` at its root, holding for each probe:

1. what it measures, and which paper and section cite it;
2. the one command that runs it from the branch root;
3. where its fresh output goes, `out\`, and where its recorded output is, `recorded\`;
4. how long it ran here;
5. a table of the numbers the papers quote from it, with columns for the number, where it is
   quoted, the recorded value, this run's value, and "reproduced": yes, differs, no or not run;
6. every change made to the probe to run it here, each with its reason.

## Output and databases

- **Recorded output lives in `recorded\`**, as the run that the papers quote left it, with nothing
  but machine paths, user names and session identifiers replaced, and `RUN.md` names which were.
  Git stores text with its line endings normalized, so a record is held to a fresh run line for
  line and not byte for byte.
  **Fresh output goes to `out\`**, which is not committed, so a re-run is compared with the record
  rather than written over it. A paper's path into a probe's `out\` names the file a run writes
  there; its recorded copy has the same name under `recorded\`.
- **No `.kdbx` is kept**, in any directory, for any reason. A probe makes its databases every time
  it runs, in the system's temporary directory or in a `work\` beside it, which is not committed
  either, and what it recorded about a database is a dump of it, never the file.

## A probe changes only to run here

**Do not improve a probe.** Its recorded numbers came from exactly that code, so the only changes
are the changes running it from this branch needs: paths, the references below, a missing `using` or
project file. A defect that changes a result is reported in `RUN.md` and left in place.

- **KeePass.** A C# probe's project references `KeePass` with `<HintPath>$(KeePassExe)</HintPath>`
  and nothing else about it: `spikes\storage\Directory.Build.props` defines that as the pinned
  `.keepass\<version>\KeePass.exe`, and stops the build with the command that provides it when it
  is absent, so a family adds no `Directory.Build.props` of its own. A PowerShell probe
  dot-sources `tools\KeePass.ps1` by its path relative to `$PSScriptRoot` and loads
  `Get-KeePassExe`, the full path of the same file once it is provided and checked.
- **KeePassXC.** A probe asks `Get-KeePassXCCli -Version <release>` from `tools\KeePassXC.ps1`,
  which provides a release `keepassxc.sha256` pins and answers the full path of
  `.keepassxc\<release>\keepassxc-cli.exe`. A probe that is not PowerShell is handed that path by
  its command, after the command has provided it.
- **The product.** A probe that compiles part of KeePassGate gets it from `tools\Product.ps1`:
  `Export-ProductFile -Path src/KeePassGate/Acl/AclResolver.cs` writes the file as it is at the
  commit that tool names into `.product\`, at its path in the product, and a project compiles
  `$(ProductDir)src\KeePassGate\Acl\AclResolver.cs`. No file of the product is committed here.
- **What runs it.** The tools run in Windows PowerShell 5.1, and so does every command in a `RUN.md`
  that does not name PowerShell 7. A script that drives `keepassxc-cli` through a process of its own
  was measured in PowerShell 7, because it sets a property .NET Framework lacks, and the family's
  command starts it with `pwsh.exe`; the family's `RUN.md` names each such script. Python runs
  through `uv run --no-project --with <packages> python -I -X utf8`. A probe that names a package
  without a version was measured with what `uv` resolved on 2026-11-15, `pykeepass` 4.2.0 among
  them, and setting `UV_EXCLUDE_NEWER` to `2026-11-16T00:00:00Z` before a run resolves the same
  again. Nothing a probe runs shows a window.
