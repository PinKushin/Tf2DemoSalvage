---
name: valve-parity-audit
description: >
  Audit one thing this project already draws or decodes against what the Source engine
  actually does, branch by branch, and file what diverges. Use when asked to audit parity,
  check a mechanism against Valve, find out why something looks or performs wrong, or when
  about to implement an engine behaviour and the engine has not been read yet. Also use
  before believing any claim of the form "we do X like Valve" or "the engine does not do Y".
---

# Valve parity audit

This project is a parity audit-and-fix loop (D127, and the owner's own words). The job is not to
find features Valve has and we lack — that list is long, visible, and mostly uninteresting. **The job
is to find things we already do that the engine does differently**, because those are invisible: they
draw something, the suite is green, and they look finished.

## Rank by what we already draw, never by branch count

`docs/PARITY-AUDIT.md` was once ranked by how many branches an engine function has. That ranks by how
much work a full implementation would be, which is the wrong axis: **a function implemented well and
one implemented badly have the same branch count.** The top-ranked finding under that scheme was 299
undrawn ragdolls — a feature the owner plays with switched off.

Pick a subject that is on screen or on the wire right now.

## The method, in order

1. **Read the engine function in full.** Every branch, to the closing brace. Half a mechanism is not
   parity — finding the flag is the easy half, and the guard, the early-out and the clamp are where
   the divergences live.
2. **Read the overrides before concluding anything.** A base implementation is not the behaviour.
   `C_BaseAnimating::SetupBones` clears its bone cache only `if ( LastBoneChangedTime() >=
   m_flLastBoneSetupTime )`, which reads as a divergence from ours — until you read that the base
   returns `FLT_MAX` and only `C_ClientRagdoll` overrides it. That one was one edit away from being
   filed as a bug.
3. **Find every consumer on our side, then ask whether the value is HONOURED.** A decoded field is
   not an implemented behaviour: `m_bClientSideAnimation` was decoded, documented with its citation,
   and had exactly one reference in the repository — its own declaration. So were
   `m_nResetEventsParity` and `m_flPlaybackRate`. `grep` for the field, then grep for what reads it.
4. **State the divergence as a sentence about behaviour**, not about code shape. "We walk every prop
   where the engine walks a maintained list" is a finding. "This method is different" is not.
5. **Write it down where it belongs** (below), with a citation and an evidence class.

## Where the answers are — a menu, not a ladder

Pick the source that holds the answer and skip the rest.

| Source | Holds | Path |
|---|---|---|
| `source-sdk-2013` | shaders, formats, math, message lists, material flags, all client behaviour | `F:/src/source-sdk-2013` |
| **the game's shipped data** | what the game READS — `items_game.txt`, `modevents.res`, VMTs, `.res` files | the TF2 install |
| demostf/parser | demo container and entity decode, for cross-checking only | never port; **knows nothing about rendering** |
| VDC wiki | conventions the SDK does not spell out | secondary; a wiki page is not a citation of behaviour |
| a decompiler | the closed engine — material system, TF2's own shaders | Ghidra, **JDK 21**; output stays outside every git tree |

**The shipped data is the source people forget.** `$modblend` was filed as needing a decompiler and
needed none — it is declared in three VMTs and read by nothing. Game event field widths are
documented in a comment block at the top of `modevents.res`.

## Traps that have actually caught this project

- **An absence claim needs a control.** Before reporting "the engine does not do X" or "we have no
  Y", ask the same search for something that MUST be there. `--first-person` was reported missing
  from a grep over `Viewer3D` while it lived in `Presentation` — and that same claim had already been
  corrected in an earlier handoff before being made again. A grep's scope is a claim about the grep.
- **A probe or counter is an instrument, and instruments lie.** Five did in one session: a counter a
  later pass reset before it was read, a `with` expression that copied a neighbouring record's
  fields, a derived `selected − culled` that counted undrawable props as posed. **Report the value
  the code USED, carried to it — never one recomputed by a second route.**
- **`grep -E "error C"` does not match analyzer errors.** Sonar emits `error S`. A build checked that
  way reported success while failing, and a stale binary was then measured.
- **A measurement of our data cannot find a feature that was never implemented.** Three correct
  measurements in a row means the question is wrong, not the data. Read the source first.

## Evidence classes — mark every claim

Read-from-source · measured · arithmetic · differential · **interpolated**. They are not equal and
the difference has repeatedly settled arguments. **Flag interpolations every time** — an inference
that sounds like a measurement is how a wrong conclusion gets repeated confidently.

## Delegating the reading

The bulk quoting of a long engine function is mechanical and safe to fan out to a read-only
subagent — ask it to return the function verbatim with `file:line`, and every override of every
virtual it calls. **Do not delegate the comparison or the conclusion.** That is the judgement, and a
report that arrives looking authoritative is exactly how a wrong finding gets believed.

## Output

**Findings go in `docs/PARITY-AUDIT.md`**; anything that becomes a tracked defect gets a numbered
`B###` entry in `docs/RISKS.md`. A decision the owner makes goes in `docs/DECISIONS.md` as `D###`,
in his words, in the same commit as the work it governs.

Each finding needs:

- **The engine's behaviour**, quoted, with `file:line`.
- **Ours**, with `file:line`.
- **What is visible when it is wrong** — the symptom somebody would report. If there isn't one, say
  so; that is a real and useful answer.
- **Evidence class**, and what would falsify the finding.
- **What is NOT established.** The gaps are the part worth keeping.

## Before claiming a fix works

A test that has never been red proves nothing. Sabotage the code on purpose, watch the RIGHT test
fail, restore with a precise inverse edit — never `git checkout --`, which discards unrelated work.
A change that alters what a SHARED helper means needs the whole suite, not a targeted check.

**Anything about a UI that cannot be verified by looking is a QUESTION for the owner, not a
statement.** Take the capture yourself where the instrument can reach — `--measure`, `--shot`,
`TF2VIEW_CAMERA` — and say plainly when it cannot.
