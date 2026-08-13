# Findings — a reverse-engineering history of TF2's demo system

This folder is the **narrative record**: how each part of the format was worked out, what was
believed first, what turned out to be wrong, and which piece of evidence settled it. It is written
to be readable end to end and to be quotable in a write-up.

It is deliberately *not* a specification. The repository already separates those jobs:

| Document | Answers |
|---|---|
| `docs/SPEC.md` | **What the format is.** The current, correct description, by layer. |
| `docs/findings/` | **How we know, and what we got wrong.** Chronological, with the evidence. |
| `docs/RISKS.md` | **What is still open.** Numbered bugs and unknowns, each with its status. |
| `docs/DECISIONS.md` | **Why the project is built this way.** Engineering choices, numbered D1…. |
| `docs/TIMELINE.md` | **The era axis.** Which build shipped which protocol, and how that was dated. |

When a finding lands, the spec gets the conclusion and this folder gets the story. Neither should
restate the other at length — where detail lives elsewhere, these files link to it rather than copy
it, because a second copy is a copy that goes stale.

## Contents

| File | Covers |
|---|---|
| [01-container.md](01-container.md) | The `.dem` envelope: header, command stream, `democmdinfo`, tick zero |
| [02-net-messages.md](02-net-messages.md) | The message layer inside `dem_packet`, and why it is a dependency chain |
| [03-string-tables.md](03-string-tables.md) | String tables, their history encoding, and the 64 KiB writer cap |
| [04-entities.md](04-entities.md) | `SendTable` flattening, delta decode, and the removal list |
| [05-user-messages.md](05-user-messages.md) | The user message layer: ids, layouts, and the era break in `Damage` |
| [06-protocol-eras.md](06-protocol-eras.md) | What changed at each protocol number, and how each boundary was measured |
| [07-writing-demos.md](07-writing-demos.md) | Re-encoding, byte-identical round trips, and generating demos the engine plays |
| [08-method.md](08-method.md) | The techniques that actually worked, and the ones that misled |
| [09-valve-implementation.md](09-valve-implementation.md) | What Valve's own engine and game code say — behaviours, not format |
| [10-maps.md](10-maps.md) | Reading a BSP: compressed lumps, and what counts as "the map" |
| [11-models.md](11-models.md) | The `.mdl`/`.vvd`/`.vtx` chain, static prop placement, and baked vertex light |
| [12-shader-parity.md](12-shader-parity.md) | Which material features the renderer still lacks, by drawn area |
| [13-settings-parity.md](13-settings-parity.md) | TF2's graphics options, and where ours stand against each |
| [14-playback-parity.md](14-playback-parity.md) | What TF2 gives you when it plays an STV demo natively |
| [15-detail-textures.md](15-detail-textures.md) | The `$detail` chain: twelve combine modes, and three traps in them |

## Which of this is original

Worth separating, because the two categories carry different weight in a write-up.

**Transcribed** — read out of published source and verified against the corpus. Most user message
layouts, the bit-level primitives, the protocol change *list*. Valuable as confirmation; not a
discovery. The contribution here is that it has been checked against real files across five eras.

**Original, as far as this project can establish** — not in `proto_version.h`, not in the SDK, not
in any public writeup found:

- **Dates for the protocol boundaries.** Valve publishes what changed, never when. Protocols 11,
  14, 15, 16 and 24 are now pinned to exact build dates by running period clients
  ([06](06-protocol-eras.md)).
- **The cadence of change** — three protocol bumps in the first five months, then two in three
  years, then eight in under two, then thirteen years frozen. This has consequences for where to
  hunt for the missing specimens.
- **The `Damage` user message layout below protocol 15** ([05](05-user-messages.md)).
- **`dem_stringtables` is absent at protocol 14 and below** — an era difference `proto_version.h`
  does not mention.
- **The writer's 64 KiB schema cap**, established by comparing a POV and a SourceTV recording of
  the same session ([03](03-string-tables.md)).
- **How `bf_write` behaves under pressure in practice**, and what that means for anyone
  re-encoding ([09](09-valve-implementation.md)).

## Conventions used throughout

**Every claim states its evidence class.** These are not equal and the difference has repeatedly
mattered:

- **Read from published source** — `ValveSoftware/source-sdk-2013`. The strongest, because it
  states intent rather than implying it. Never a decompile; see `CLAUDE.md`.
- **Measured on the corpus** — observed across real demos, with counts.
- **Arithmetic** — ruled in or out by bit widths and lengths before any byte was read.
- **Differential** — agreement or disagreement with `demostf/parser`.
- **Interpolated** — believed but not exercised, because no specimen exists. Always flagged.

**Wrong turns are kept, not tidied away.** A conclusion recorded without the reasoning that failed
is the kind that gets confidently repeated. Several sections below exist mainly to record a wrong
belief and what killed it.

**Dates are absolute.** "Recently" ages badly in a document meant to be read later.
