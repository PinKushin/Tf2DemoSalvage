---
name: conformance-test-before-implementation
description: "Write the conformance test citing Valve's source first, then unit/integration/UI tests, then the implementation; and escalate SDK to Rust parser to decompiler when the SDK cannot answer."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-09-09T03:42:50.564Z
---

**Order of work reproducing engine behaviour: conformance test, then ordinary tests, then code.**

**Why:** a parity test written after the implementation describes what was built, not what the engine
does — the one thing it must never do. Owner made this standing after five defects were all found by
reading the SDK late.

**Four sources, a MENU not a ladder** — pick whichever holds the answer:
1. `source-sdk-2013` — shaders, formats, math, message lists. Cite in comments (why `S125` is
   disabled repo-wide).
2. demostf/parser (Rust) — container/entity decode, cross-check only, never port. **Skip for
   rendering — it's never drawn a pixel.**
3. Valve Developer Community wiki — secondary, conventions the SDK omits.
4. A decompiler — closed material system, TF2's own shaders. **Reach for it readily.**

**"Not in the published SDK" means pick a different source, not guess** — `$modblend` is the worked
example.

**Decompiler constraint is REPOSITORY SIZE, not law** — run with project/output paths under a temp
dir outside every git tree; carry back only hand-written notes (a constant, a field order, a formula).
Never paste a decompiled function into source.

**Two instruments, not interchangeable:** `SdkCoverageTests` (generated denominator, catches MISSING
features) vs. hand-written conformance suites (catch WRONG ones — an extraction can't tell you
`$detail` uses the wrong blend mode).

Related: [[nothing-is-closed]].

---

## The reason is ENUMERATION, not ceremony — B135, 2026-08-21

Owner, after four divergences found one at a time via screenshots: *"dont just implement or fix, conf
test then implement/fix. you would have found the divergence if you went to conf tests first."*
Writing a conformance test forces the engine's behaviour to be ENUMERATED — you must read the whole
feature to assert it. Reacting to a symptom only finds the one thing that showed.

Measured: B135's four divergences (pass order, cull mode, depth writes, depth bias) were found across
an evening, one screenshot at a time. **Two more turned up in the minute it took to start writing the
conformance test**, with no prior symptom: overlay render order, overlay fade (unread lump).

**Tell that this is skipped:** a citation in a commit message with no test beside it — a citation
doesn't redden when the value changes back.

## Read the coverage report before re-deriving the gap — 2026-08-25

Asked which world lumps the engine loads that we don't (B194), `Mod_Load*` was grepped by hand out of
`engine.dll`. **`docs/SDK-COVERAGE.md` already said "27 of 66" and named `LUMP_VERTNORMALS`.** Owner:
*"yep thats why i say conformance tests first too"*. The generated half of the instrument KEEPS the
enumeration — check the generated report first when the question is "what is missing"; measurement
can only find data that's wrong, never a feature never implemented.

Related: [[nothing-is-closed]], [[the-denominator-decides-what-can-be-lost]].

## `ask-valve-before-designing-not-after` — a design defended by taste gets undone by taste

Owner, mid-refactor: *"and how does valve handle these timings?"* — design (a two-clock type)
survived unchanged; the justification didn't. Before: "I'd rather not merge these two clocks... B209
has open pacing questions" — an argument from our own code. After: **Valve keeps six distinct time
quantities, each named by what it obeys** (`realtime` follows `host_timescale`, `Plat_FloatTime`
doesn't, `frametime`/`absoluteframetime` paused vs. not — the free camera flies by `absoluteframetime`
at `view.cpp:153`, `cl_showfps` reads it too at `vgui_fpspanel.cpp:166`) — merging them would BE the
divergence.

**Why beyond this case:** a design defended by taste gets undone by the next person's taste; one
defended by citation is a fact someone must argue with. It also validated something already there —
B174 had independently arrived at the same answer with no citation. Full write-up:
`docs/findings/39-the-engines-frame-clocks.md`.

## `decide-home-and-parity-before-writing` — both answers, before the code, in the commit

Owner: *"every time we implement a couple of new things, we have to go back and fix all the
archetectural and parity issues, the going back over and over is the annoying part."* Three days of
rework against a couple of features, because structure got answered by proximity ("new code goes
where its neighbours are") while parity already had a rule.

**Answer both before writing, put them in the commit:**
1. What's the engine's arrangement for this job? One grep — take that shape and name
   (`SoundscapeSystem`, `UpdateClientSideAnimations`).
2. Which project does it belong in, and can it be tested there? "The viewer, because that's where
   the caller lives" is drift starting.

Recorded as D89. Related: [[valve-parity-is-the-first-principle]], [[output-level-assertion-or-it-is-not-done]].
