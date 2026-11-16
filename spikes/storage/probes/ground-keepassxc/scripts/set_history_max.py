"""Copies a database with Meta/HistoryMaxItems set to a given number, leaving every history as it is."""
import sys

from pykeepass import PyKeePass

kp = PyKeePass(sys.argv[1], password="probe-only")
kp.tree.getroot().find("Meta").find("HistoryMaxItems").text = sys.argv[3]
kp.save(sys.argv[2])
counts = {}
for e in kp.tree.getroot().iter("Entry"):
    h = e.find("History")
    if h is not None and len(h):
        title = [s.findtext("Value") for s in e.findall("String") if s.findtext("Key") == "Title"][0]
        counts[title] = len(h)
print(counts)
