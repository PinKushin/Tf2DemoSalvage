---
name: valve-shape-beyond-parity
description: "Structure and ownership follow Valve's source everywhere, so a Source developer understands it at once; names stay C# style but must map recognisably to the engine's (D163)"
metadata:
  type: feedback
---

**Owner, 2026-09-30, choosing the engine's ownership over a behaviour-identical design** (B438: interpolation state kept
on the timeline/tracks, like `C_BaseEntity`'s interpolation list, not moved into the viewer): *"I'd rather it be closer
to the valve source, that's why I say 100% valve parity. I want someone who knows source to be able to understand it
immediately."* **Not a new decision**: the standing direction (D163's 2026-09-30 note). **Names:** *"a source expert
should be able to tell from our c# style names what they are equivalent to"*. C# style is fine; NO rename pass to Valve
identifiers. Why: *"I definitely don't want to change naming conventions right in the middle of the project, that would
read and look horrible"*: one convention throughout beats a mixed one. The first quote was over-read as a new D200 plus
a rename; he corrected it.

**A feature beyond parity is still built the way Valve would have built it.** D163, owner, on
fetching old maps/models (D162): *"even those areas need to take vavles conventions into account and
probably follow them so any ai working on it stays looking like valve code."*

**Why:** wants the whole codebase to read as Valve's shape, so whoever works on it next (AI included)
keeps following Valve's patterns rather than inventing parallel ones.

**How to apply:** structure/ownership: where the engine keeps it. Names: C# style, but each maps visibly to its engine
concept. Beyond parity: find the nearest engine mechanism and use its shape: old content as a search path (matching
`gameinfo.txt` order), downloads following the engine's download queue (B392), checks named after the client's own
checks. Read the SDK/disassembly for the mechanism first, exactly as for parity work. Related:
[[valve-parity-is-the-first-principle]], [[parity-is-the-search-not-the-defence]].
