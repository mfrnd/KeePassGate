"""Every failed test in one or more `NUnitOnMono` result files, put down to one cause.

    uv run --no-project python -X utf8 -I tools/classify-failures.py results/unit.tsv results/host.tsv

The causes are tried in order and the first that matches wins, so a test that fails for two reasons
is counted under the one listed first. A failure no cause matches is listed by name under `other`,
which is how a new cause is found: the list is meant to come out empty.
"""

import collections
import re
import sys

# The fixtures whose client is the tests' own SslStream (GateClient, PairingClient), which on Mono
# offers no TLS 1.3, so the gate refuses the handshake and the test sees no answer at all.
STREAM_CLIENTS = (".GateListenerTests.", ".LockedGateTests.", ".PairingGateTests.", ".HostCertificateHandshakeTests.",
                  ".LongLivedCertificateTests.The_gate_serves", ".OpenDatabaseTargetsTests.")

PLUGIN_USER32 = ("IsWindowEnabled", "EnableWindow", "GetWindow ", "GetAncestor", "FlashWindowEx",
                 "SetWindowDisplayAffinity", "GetWindowThreadProcessId", "EnumThreadWindows", "IsWindowVisible",
                 "GetClassName")

# Step 33's queue of turns asks user32 for KeePass's thread on its first turn and walks that thread's
# windows before a write is queued, so on Mono a turn never starts and its caller hears nothing.
QUEUE_TESTS = (".UiThreadTests.Work_", ".RenewalGateTests.")

# (cause, whose it is, test) over (name, message, where)
CAUSES = [
    ("no CertificateRequest", "runtime",
     lambda n, m, w: "CertificateRequest" in m),
    ("no EC public key in X509Certificate2", "runtime",
     lambda n, m, w: "unknown OID '1.2.840.10045.2.1'" in m),
    ("user32 called by the plugin", "runtime",
     lambda n, m, w: "EntryPointNotFoundException" in m
     and (w.startswith("at KeePassGate.Ui.NativeMethods") or any(f in m for f in PLUGIN_USER32))),
    ("user32 in the queue of turns (step 33)", "runtime",
     lambda n, m, w: any(t in n for t in QUEUE_TESTS)),
    ("no Control.LogicalToDeviceUnits", "runtime",
     lambda n, m, w: "LogicalToDeviceUnits" in m),
    ("DataGridView column auto sizing", "runtime",
     lambda n, m, w: "GrantPanel.BuildGrid" in w or ".Attach(" in m),
    ("Control.Scale leaves Padding alone", "runtime",
     lambda n, m, w: "TabPageInjectorTests.A_page_is_scaled" in n or "unscaled inset" in m),
    ("libgdiplus refuses a drawing parameter", "runtime",
     lambda n, m, w: "GDI+ status: InvalidParameter" in m),
    ("KeePass compiles a placeholder differently", "runtime",
     lambda n, m, w: "KeePass and the vectors disagree" in m),
    ("a soft hyphen is not ignorable", "runtime",
     lambda n, m, w: "KdbxVaultTests.An_entry_route_refuses_a_whole_body" in n
     or "ApiTests.A_protected_field_is_not_reached_through_a_name_the_database_cannot_tell_from_it" in n),
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
    ("SslStream without TLS 1.3 in the tests", "tests",
     lambda n, m, w: "AuthenticationException" in m or "protocol_version" in m or "Say hello first" in m
     or (any(f in n for f in STREAM_CLIENTS)
         and any(x in m for x in ("But was:  0", "Answered, Is.True", "ServerProved", "Expected: IEnumerable But was: null",
                                  "NullReferenceException", "the head ended early", "slow.Calls")))),
    ("WinForms accessibility and list layout", "runtime",
     lambda n, m, w: "AccessibilityObject.Description" in m or "grew to fit rows it is meant to scroll" in m
     or (".ClientListTests." in n and not m)),
    # cspell:ignore Btls
    ("Windows loading a stored key in the tests", "tests",
     lambda n, m, w: ("NotImplementedException" in m or "MonoBtls" in m or "Unable to decode certificate" in m)
     and (".CertificateBlobTests." in n or ".CertifiedKeyTests." in n or ".HostCertificateHandshakeTests." in n)),
    ("the local time zone moved by the tests", "tests",
     lambda n, m, w: "keeps its local time zone" in m),
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
