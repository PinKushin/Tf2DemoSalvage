---
name: branch-scope-and-toolchain-prefs
description: "Split branches when a second concern appears; Rust goes on Windows natively, WSL only for libFuzzer"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-08T00:24:02.157Z
---

Two rules, both endorsed by the owner 2026-08-07.

**Split a branch when it grows a second concern.** `feat/phase1-container` ended up carrying
the container parser plus a spec consolidation, a risk register, SDK research, and a README
rewrite, so its name stopped describing its contents.

Origin, because it matters for how it is cited: Claude inferred this rule from the owner
merely *asking* whether the branch name still made sense, then wrote it up as the owner's
instruction. The owner corrected that — "i didnt say anything… do not infer my intentions
more than what i say" — and then, separately, explicitly endorsed the rule: "put it back its
a good rule and i agree with it pretty much fully." So it is now genuinely theirs, but it
became so by being proposed and accepted, not by being assumed.

**How the owner wants it applied.** They said they tangent a lot, so expect frequent
branching, and asked for one of two responses when a tangent starts: gently steer back on
track, or branch for it. With two stated exemptions where steering is *not* wanted —
research tangents, and this early phase while they are still checking that the design is
right. In those cases, follow the tangent.

**The broader instruction this came from: do not infer intentions beyond what the owner
actually says.** A question is a question, not an instruction. Ask rather than decide, and
never attribute a rule to them that they did not state.

---

**If a Rust toolchain is ever needed, install rustup natively on Windows, not in WSL.**
WSL's filesystem translation on `/mnt/c` makes builds against this repo noticeably slower
and buys nothing; rustup is native on Windows and produces the same binary.

The exception, and it is the only one: **libFuzzer for D8's coverage-guided fuzzing is
Linux-only**, so that genuinely needs WSL. Owner has a working WSL setup for it — see
[[fuzzing-belongs-here]]. Do not let that drag a Rust build into WSL alongside it.

**Done 2026-08-08.** rustup 1.29.0 installed via winget, giving rustc/cargo 1.97.1 on
`x86_64-pc-windows-msvc`. Native Windows, not WSL. MSVC BuildTools 2022 was already present so
the default toolchain links without extra setup. Cargo lives at `%USERPROFILE%\.cargoin`,
which is **not on PATH in a fresh non-login shell** — prepend it explicitly.

Superseded, kept for the reasoning: the cross-parser oracle might have been
[UntitledParser](https://github.com/UncraftedName/UntitledParser), which is C# and MIT, so
no Rust is needed at all. Recorded so the question is not re-litigated if `tf-demo-parser`
turns out to be the better oracle for TF2 specifically.

---

**Refinement from the owner, same day, on when branching actually applies.**

- **Research tangents often need no branch at all**, because the output is usually memory
  entries rather than repo changes. Branch only when the tangent actually produces committed
  work.
- **Docs should stay current as work happens**, not be batched into a docs-only branch. The
  owner's stated preference is that a pure documentation branch should never be *necessary* —
  if docs are kept up to date alongside the code, there is nothing left to catch up on.
- They also said plainly that this will not always hold, so docs-only branches will still
  happen, and they are fine with them when the work genuinely is docs-only.
- **Their signal that docs/memory are falling behind: not seeing many memories being made.**
  If that happens they will say so and ask for a catch-up pass. Treat a long stretch with no
  memory entries as a warning sign rather than waiting to be told.

---

**Final shape of the branching rule, owner's words, same day.**

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
