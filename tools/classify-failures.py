"""Every failed test in one or more `NUnitOnMono` result files, put down to one cause.

    uv run --no-project python -X utf8 -I tools/classify-failures.py results/unit.tsv results/host.tsv

The causes are tried in order and the first that matches wins, so a test that fails for two reasons
is counted under the one listed first. A failure no cause matches is listed by name under `other`,
which is how a new cause is found: the list is meant to come out empty.
"""

import collections
import re
import sys

PLUGIN_USER32 = ("IsWindowEnabled", "EnableWindow", "GetWindow ", "GetAncestor", "FlashWindowEx",
                 "SetWindowDisplayAffinity")

# (cause, whose it is, test) over (name, message, where)
CAUSES = [
    ("no CertificateRequest", "runtime",
     lambda n, m, w: "CertificateRequest" in m),
    ("no EC public key in X509Certificate2", "runtime",
     lambda n, m, w: "unknown OID '1.2.840.10045.2.1'" in m),
    ("user32 called by the plugin", "runtime",
     lambda n, m, w: "EntryPointNotFoundException" in m
     and (w.startswith("at KeePassGate.Ui.NativeMethods") or any(f in m for f in PLUGIN_USER32))),
    ("no Control.LogicalToDeviceUnits", "runtime",
     lambda n, m, w: "LogicalToDeviceUnits" in m),
    ("DataGridView column auto sizing", "runtime",
     lambda n, m, w: "GrantPanel.BuildGrid" in w or ".Attach(" in m),
    ("Control.Scale leaves Padding alone", "runtime",
     lambda n, m, w: "TabPageInjectorTests.A_page_is_scaled" in n),
    ("KeePass compiles a placeholder differently", "runtime",
     lambda n, m, w: "KeePass and the vectors disagree" in m),
    ("a soft hyphen is not ignorable", "runtime",
     lambda n, m, w: "KdbxVaultTests.An_entry_route_refuses_a_whole_body" in n),
    ("ToolTip ignores right to left", "runtime",
     lambda n, m, w: ".TooltipsTests." in n),
    ("an open file can be moved", "platform",
     lambda n, m, w: ".TraceGateLogTests.A_file_that_cannot_be_set_aside" in n),
    ("libgdiplus GIF encoding", "runtime",
     lambda n, m, w: ".AnimatedGifTests." in n),
    ("static field read across assemblies", "runtime",
     lambda n, m, w: "MissingFieldException" in m),
    ("user32 called by the tests", "tests",
     lambda n, m, w: "EntryPointNotFoundException" in m),
    ("WindowsIdentity in the tests", "tests",
     lambda n, m, w: "JsonLinesAuditTests" in m and "TypeInitializationException" in m),
    ("Windows paths in the tests", "tests",
     lambda n, m, w: "Paths.Join" in m or "C:\\" in m),
    ("UI Automation in the tests", "tests",
     lambda n, m, w: "UIAutomation" in m),
    ("run outside a checkout", "tests",
     lambda n, m, w: "No KeePassGate.sln above" in m),
]


def read(path):
    with open(path, encoding="utf-8") as results:
        lines = results.read().splitlines()
    for line in lines[1:]:
        parts = line.split("\t") + [""] * 5
        yield parts[0], parts[1], parts[3], parts[4]


def main(paths):
    for path in paths:
        outcomes = collections.Counter()
        causes = collections.Counter()
        whose = {}
        other = []
        for name, outcome, message, where in read(path):
            outcomes[outcome.split(":")[0]] += 1
            if not outcome.startswith("Failed"):
                continue
            for cause, owner, matches in CAUSES:
                if matches(name, message, where):
                    causes[cause] += 1
                    whose[cause] = owner
                    break
            else:
                other.append(name)

        print("##", path)
        print(", ".join(f"{outcome} {count}" for outcome, count in sorted(outcomes.items())))
        print()
        print("| cause | whose | failed |")
        print("| --- | --- | ---: |")
        for cause, count in causes.most_common():
            print(f"| {cause} | {whose[cause]} | {count} |")
        print(f"| other | | {len(other)} |")
        for name in other:
            print("    other:", re.sub(r"\s+", " ", name))
        print()


if __name__ == "__main__":
    main(sys.argv[1:])
