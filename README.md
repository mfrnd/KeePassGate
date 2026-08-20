# KeePassGate: can a hosted runner drive a Windows GUI?

**A measurement, kept for reference and never merged.** The end to end tier starts a real KeePass
and types into it, and whether that can run on a GitHub-hosted Windows runner decides whether this
project needs a machine of its own.

It can. Every part of it, on `windows-latest`, with no self-hosted runner and no container.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design documents.
The subject is the machine rather than the product, and depending on the product would make the
answer harder to trust and the branch harder to keep.

What it settled is in [`RESULTS.md`](RESULTS.md), with the recorded output beside it.

## Running it

Push the branch. The workflow runs on push rather than on dispatch, because `workflow_dispatch` is
not offered for a workflow file that has never run on the default branch, which is exactly the
situation a branch like this is in.

The probe starts one throwaway process, writes two temporary files it removes again, and
synthesizes input at the desktop. It is `.github/desktop-probe.ps1`, and it leaves nothing behind outside its own process.

## Why it was worth measuring

The claim being defended was that hosted runners cannot drive a desktop, and it had been argued
rather than tested. An argument that decides infrastructure is worth one workflow run.
