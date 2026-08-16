# What to read JSON with, and what to validate with

**Newtonsoft.Json for the grammar, and a checker of our own for the policy.** One assembly, MIT, and
no schema library at all.

Measured on `net48`, which is the constraint that decides most of this. Versions are in
`spikes/json/counts.json` beside every number.

## What it costs to read JSON

| | version | assemblies | license |
| --- | --- | --- | --- |
| **Newtonsoft.Json** | 13.0.3 | **1** | MIT |
| System.Text.Json | 8.0.5 | **9** | MIT |

Nine against one is the whole comparison, and the ninth is the interesting one.
`System.Text.Json` brings `System.Runtime.CompilerServices.Unsafe`, which on this framework normally
needs an assembly binding redirect. **A plugin cannot supply one**: the only `app.config` in play
belongs to the host process, and editing somebody else's configuration file to load your own plugin
is not a deployment story.

The rest of the nine are the shims that make a modern package work on a framework that predates it:
`System.Buffers`, `System.Memory`, `System.Numerics.Vectors`, `System.Threading.Tasks.Extensions`,
`System.ValueTuple`, `Microsoft.Bcl.AsyncInterfaces`, `System.Text.Encodings.Web`. Each one is
loaded into the process that holds the user's open database, and each one has to be redistributed
beside the plugin.

## What it costs to validate against a schema

| | version | assemblies | license | what it actually does |
| --- | --- | --- | --- | --- |
| Newtonsoft.Json.Schema | 4.0.1 | 2 | **AGPL-3.0**, or paid | validates at run time |
| NJsonSchema | 11.6.1 | 14 | MIT | validates at run time |
| Corvus.Json.ExtendedTypes | 4.0.4 | 20 | Apache-2.0 | generates C# types at build time |

**Newtonsoft.Json.Schema is refused on its license and nothing else.** It is the cheapest of the
three by a wide margin and it is by the same author as the reader, so it would have been the obvious
pairing. AGPL-3.0 for an MIT plugin distributed beside a GPL host is not a trade to weigh. The
license was read out of the `LICENSE.md` inside the package.

**NJsonSchema was the plan, and counting it is what changed the plan.** It is the only candidate
that targets `.NETFramework4.6.2` directly, with no shims and no guesswork, which is exactly why a
design written before anybody counted named it for checking bodies.

It costs **fourteen assemblies**, and the list is the argument: `Newtonsoft.Json` **and**
`System.Text.Json`, `System.IO.Pipelines`, `Namotion.Reflection`, `NJsonSchema.Annotations`, and the
usual shims down to `System.ValueTuple`. It brings two JSON readers into the process to validate
bodies that are one flat object of strings, which is the shape this wire carries and nothing more,
so almost all of what it brings would never run.

**Corvus answers a different question.** It generates C# types from a JSON Schema at build time, so
it does not validate an incoming body against a document at run time, which is what is needed. It
also does not read the routing document, which is OpenAPI in YAML, so it would arrive beside the
YAML and OpenAPI readers rather than instead of them.

## So the policy is ours

A validator is refused, and what replaces it is a checker written against the subset the wire
actually carries: one flat object whose values are strings or arrays of strings.

**That is only safe with one rule attached.** A hand-written checker that quietly ignores a schema
construct it does not implement is worse than no checker, because a body would pass unchecked and
nobody would know. So it **refuses at startup any schema it does not implement**. A construct nobody
implemented stops the plugin loading, which cannot be mistaken for a body that was checked.

## The split worth keeping

**The grammar is Newtonsoft's and the policy is ours**, and they are different kinds of thing.

Parsing JSON correctly is a solved problem, solved better elsewhere than it would be solved here.
Deciding what this wire may carry is a decision, and it belongs where it can be read and argued
with.

The trap is to conflate them. Refusing a schema library on its size is a decision about validation,
and it is easy to let it become a decision to own a grammar too, which is a much worse trade: a JSON
tokenizer written here would be a liability with no upside, on the wire where an unauthenticated
caller reaches first.

## What comes with taking Newtonsoft's grammar

Its parser is more forgiving than a hand-written one would be, and where it is, this project accepts
what it accepts rather than fighting it:

- **A trailing comma is read as the object it obviously is.**
- **A comment reaches nothing and is discarded.**
- **A raw control character in a string is a value**, and has to be: an entry's notes hold
  paragraphs, so a line break must survive. What keeps one out of a log is escaping on the way out.

None of the three carries a member or changes what the object says, and refusing them was the whole
cost of owning a grammar.

**One default has to be turned off.** Json.NET converts anything date shaped into a `DateTime`
unless told otherwise, so a value would come back reformatted rather than as it was sent. That is
the kind of default discovered by a client reporting a timestamp it never sent.
