# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It answers what a JSON reader costs this project. It is
taken from `master`, it is never merged, and nothing here ships or is built by any gate.

The plugin, its client and its design documents are on the branches that carry the product. This
branch deliberately has none of them, so the question can be read without the project around it.

## The rules that matter

**Count, do not cite.** Every number here comes from building a throwaway project and looking at the
output directory. A package page's dependency list is not the same question, because the framework
and the version decide what actually lands.

**Pin the version with the number.** An assembly count is a fact about a version, not about a
library, and a count without one rots quietly.

**Do not build a reader here.** This branch decides what to take. What is taken is used on the
branch that carries the product, where it is tested.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
