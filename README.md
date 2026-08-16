# KeePassGate: reading and writing JSON

**One question, kept for reference and never merged.** KeePassGate's wire is JSON, and its routing
table is an OpenAPI document. Something has to read both. What does each candidate cost, and what
does taking it oblige the project to?

This branch is taken from `master` and holds nothing else: no plugin, no client, no design
documents. Those are on the branches that carry the product, which keep the answer and not the
working out.

## Why cost is the question

The plugin runs inside the KeePass process, and every assembly it brings is loaded beside the user's
open database and has to be redistributed with it. A library that reads JSON well but arrives with
eight friends is a different proposition from one that arrives alone, and on .NET Framework the
difference is larger than it looks, because the framework predates most of what a modern package
assumes.

License matters for the same reason. The plugin is MIT beside a GPL host, so a dependency that
imposes anything stronger is not a trade to weigh, it is a refusal.

## How it is answered here

By building, not by reading package pages. [`spikes/json/count.ps1`](spikes/json/count.ps1) creates a
throwaway `net48` library, adds one package, builds it, and counts what lands in the output
directory beside the project's own assembly. That is exactly the set that would ship next to the
plugin.

## The answer

**Newtonsoft.Json for the grammar, and a checker of our own for the policy.** One assembly against
nine for the alternative, no schema library at all, and a startup refusal of any schema construct
the checker does not implement so that a subset cannot become a gap. [`RESULTS.md`](RESULTS.md) has
the numbers, the licenses, and the trap worth naming: refusing a schema library is a decision about
validation, and letting it become a decision to own a JSON grammar is a much worse trade.
