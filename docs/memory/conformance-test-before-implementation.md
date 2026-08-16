---
name: conformance-test-before-implementation
description: "Write the conformance test citing Valve's source first, then unit/integration/UI tests, then the implementation; and escalate SDK to Rust parser to decompiler when the SDK cannot answer."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-16T14:37:00.903Z
---

**Order of work on anything that reproduces engine behaviour: conformance test, then the ordinary
tests, then the code.**

**Why:** a parity test written after the implementation describes what was built rather than what
the engine does — the one thing it must never do. Written first, with its citation, it is the place
"what does Valve actually do" gets recorded before any code exists to bias the answer. The owner
made this a standing rule after a session where five defects were all found by reading the SDK late.

**Escalation when the SDK does not answer, in order:**

1. `source-sdk-2013` at `F:/src/source-sdk-2013` — shaders, formats, math, message lists. Cite it in
   comments; that convention is why `S125` is disabled repository-wide.
2. **demostf/parser** (Rust) — the demo container and entity decode. Read to cross-check, never
   port. It knows nothing about rendering.
3. **Valve Developer Community wiki** — conventions the SDK does not spell out. Secondary.
4. **A decompiler** — only for the closed material system and TF2's own shaders. Never into a
   repository; temp paths outside every git tree; carry back a hand-written note, never a pasted
   function.

**"Not in the published SDK" is not the end of the line** — it is a signal to pick a different
source, not to guess. `$modblend` is the worked example: TF2 ships it in real VMTs and no published
shader declares it.

**Two instruments, not interchangeable.** `SdkCoverageTests` generates the denominator from the SDK
by extraction and can never go stale; the hand-written conformance suites carry the semantics and the
cost of each gap. Only the generated half catches a MISSING feature; only the hand-written half
catches a WRONG one — an extraction cannot tell you `$detail` uses the wrong blend mode.

Related: [[read-the-spec-before-measuring-our-data]], [[measure-every-hop-before-blaming-one]].
