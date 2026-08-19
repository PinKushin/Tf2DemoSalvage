---
name: author-the-specimen-the-corpus-lacks
description: The writer is a test instrument, not just a product feature — a case no demo contains can be authored rather than hunted for
metadata:
  type: project
---

**When the corpus does not contain a case, write a demo that does.** This project can emit `.dem`
files the engine accepts ([[engine-accepts-authored-demos]]), and that makes the writer a *testing*
capability as much as a product one. A case no recording happens to contain is not automatically a
case that cannot be tested.

**This is the inference that gets forgotten**, including by the assistant, on 2026-08-19: a test
skipped because cp_process_final's own materials run no time-driven proxy, and the response was to
hunt for a map that had one rather than to consider authoring the input. The owner had to point it
out, and said explicitly that they will not always think of it at the right moment. So it is written
down here rather than left to be re-derived.

Where it applies, in rough order of value:

- **The era gaps.** Protocols 12–13 and 17–23 have no specimen and community demos are genuinely
  rare (`docs/DECISIONS.md` D5). Anything currently flagged **interpolated** in `docs/findings/`
  is a candidate.
- **Messages the corpus never carries.** A decoder branch real demos never take is most of a
  decoder ([[most-of-a-decoder-is-untested]]); an authored file can take it deliberately.
- **Edge values and malformed input.** Adversarial bytes with a known intended meaning, which is
  stronger than fuzzing alone because the expected result is known.
- **Anything whose absence would otherwise be an eternal skip.** A test that can only ever skip is
  a test that can never be wrong.

**Two distinctions the owner has drawn, and they are not the same.** *Cutting up* an existing demo
was called "a little cheaty" as a test, and the truncation code written for it was deleted the same
day. *Authoring* a specimen to exercise a specific case was endorsed outright. The difference is
between trimming someone else's recording and constructing an input whose contents you chose and can
therefore predict.

**And check whether a demo is needed at all first.** The 2026-08-19 case did not need one:
`MapAssets.Load` takes the entity model list as a parameter, so naming the capture point models
exercised the path directly. Reach for the writer when the thing under test is the *demo stream*,
not when it is something a demo merely happens to supply.

Related: [[round-trip-needs-the-encoding-shape]] for what an authored file has to record beyond the
values, [[put-the-real-file-in-the-fixture]] for why an authored input is still weaker evidence than
a real one where a real one exists.
