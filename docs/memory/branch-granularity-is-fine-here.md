---
name: branch-granularity-is-fine-here
description: "This repo wants many small branches and sub-branches - the features decompose, so a branch per coherent piece is the default, not per feature"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:23.882Z
---

**Default to a branch per coherent piece, and sub-branch anything larger.** Owner's instruction,
2026-08-12, given while the viewer was being built.

The reason is the shape of the work rather than a general preference: these features decompose
cleanly and deeply. "Open demos" was really four separable pieces — the library that finds them,
the header reader, the playlist wiring, and the UI tests over it — and each is reviewable,
revertable and nameable on its own. A single `feat/open-demos` branch would have buried three of
them.

**A branch whose name has stopped describing its contents is the signal.** It happened twice in
one session: a `git add -A` swept viewer code into a docs commit on `docs/bsp-hardening`, and
`feat/topdown-camera` accumulated the transport bar, full screen and an action row. Both were
caught by noticing the name no longer fit, and both were cheap to fix only because nothing had
merged yet — a soft reset to main and three properly-named branches.

**How to apply:** before starting, ask what the smallest thing worth reviewing on its own is, and
branch for that. When a second concern appears mid-branch, finish and merge the first rather than
carrying both. Merging often is what keeps this cheap.

The owner notes PokemonBattleJournal will want the same once deck comparison, deck building and
PTCG Live log parsing land — it is not there yet, this repo is.

See also `branch-per-task-not-straight-to-main` in the assistant's global memory, which is the
weaker "branch at all" rule this refines.

## It was broken on 2026-09-04, and the failure mode is a session with no task boundary

Four commits went straight to `main` and a fifth piece sat uncommitted on it before the owner asked
"have you been branching?" — the answer was no, and no branch had been created or announced all
session.

**The condition that produced it is worth recognising, because it did not feel like carrying two
concerns.** The session ran as a continuous parity loop, and every defect was found BY the work on
the previous one: one census flagged the next parameter, one material's buffer broke another
feature's reflections. Each step felt like the same thread rather than a new task, so the moment
where a branch gets created never arrived.

**The signal this entry names — a branch whose name has stopped describing its contents — cannot
fire when no name was ever chosen.** That is the gap. The check has to be at the START of a piece of
work, not partway through it.

**So: name the branch before the first edit, even when the work arrives as a continuation.** "This
follows from the last thing" is not evidence that it belongs on the same branch; it is the normal
shape of every piece of work in this repo.

The repair was cheap only because nothing had been pushed — `origin/main` was four behind, so each
commit could be given its branch and merged back in order. See D140.

---

## `branch-scope-and-toolchain-prefs` — what a branch OWNS, and how this rule became the owner's

Two rules, both endorsed by the owner 2026-08-07.

**Split a branch when it grows a second concern.** `feat/phase1-container` ended up carrying
the container parser plus a spec consolidation, a risk register, SDK research, and a README
rewrite, so its name stopped describing its contents.

**Origin, because it matters for how it is cited:** Claude inferred this rule from the owner
merely *asking* whether the branch name still made sense, then wrote it up as the owner's
instruction. The owner corrected that — *"i didnt say anything… do not infer my intentions more than
what i say"* — and then, separately, explicitly endorsed the rule: *"put it back its a good rule and
i agree with it pretty much fully."* So it is now genuinely theirs, but it became so by being
proposed and accepted, not by being assumed.

**The broader instruction this came from: do not infer intentions beyond what the owner actually
says.** A question is a question, not an instruction. Ask rather than decide, and never attribute a
rule to them that they did not state. See [[name-the-reading-you-picked]] and
[[silence-about-a-missing-feature-is-not-a-preference]] for the two ways that goes wrong.

### Refinement, same day, on when branching actually applies

- **Research tangents often need no branch at all**, because the output is usually memory entries
  rather than repo changes. Branch only when the tangent actually produces committed work.
- **Docs should stay current as work happens**, not be batched into a docs-only branch. The owner's
  stated preference is that a pure documentation branch should never be *necessary* — if docs are
  kept up to date alongside the code, there is nothing left to catch up on.
- They also said plainly that this will not always hold, so docs-only branches will still happen,
  and they are fine with them when the work genuinely is docs-only.

How the owner wants a tangent handled when one starts, and the signal they watch for memory falling
behind, describe how they work everywhere rather than this repository, so both live in the
assistant's global memory — moved there on 2026-09-10, when this directory was reconciled with its
mirror.

### Final shape of the branching rule, owner's words, same day

A feature branch **owns everything that serves it**: memory entries, documentation updates,
and research that feeds the feature all belong on that branch. "Split when a second concern
appears" means a genuinely *unrelated* concern, not every artefact that is not source code.

For larger features, **sub-branches merging into the parent feature branch** are welcomed
rather than merely tolerated.

**This retroactively softens the `feat/phase1-container` example above.** Claude called that
branch a scope violation; under this rule it mostly was not. The spec consolidation and the
risk register were research directly serving the container work and belonged there. Only the
README rewrite was arguably a separate concern. Keep the rule, but do not use that branch as
the cautionary example — it was closer to correct than Claude judged at the time.

**Dropped from this entry on 2026-09-08:** a companion note saying that if a Rust toolchain is ever
needed it goes on Windows natively rather than in WSL, with libFuzzer as the sole exception. `No
Rust` is a hard project constraint in `CLAUDE.md` — *"Explicitly rejected, don't suggest it"* — so
the advice can only be acted on by violating it. Fuzzing here is SharpFuzz on .NET
(`tests/Tf2DemoSalvage.Fuzz`, D8), not Rust.
