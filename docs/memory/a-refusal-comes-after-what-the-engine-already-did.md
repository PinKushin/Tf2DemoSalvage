---
name: a-refusal-comes-after-what-the-engine-already-did
description: "When the engine refuses a start late, port the refusal at that point: what ran before it (the channel pick freeing the old sound) still happened. B724."
metadata:
  type: project
---

**An engine "does not play it" is a place in a function, not a filter in front of it.** B724's first port of
"a `SND_SPEAKER` start whose wave loops is ignored" returned from `SoundPresenter` before the sink was asked. In
`S_StartDynamicSound` (engine.dll build 11191711, `FUN_18002e6e0`) the test is at `0x18002ec0a`, after
`SND_PickDynamicChannel` and the free of what the picked channel held: the refused start has already cut off the
entity's previous sound on that channel. Ours left it playing. A review caught it; the tests had not, because they
asserted only "nothing started".

**Why:** an early return is the natural way to write "the engine ignores X", and it silently drops every side effect
the engine performs before it notices X.

**How to apply:** for any refusal, read what the function has already done by that line (channels picked, voices
freed, counters bumped, state written) and put ours at the same point, in the component that owns those effects
(`AudioOutput.Play`, with a `StartRefusal`). Test the side effect, not only the absence: the old voice is gone.
Related: [[a-guard-you-remove-may-be-the-mechanism]], [[a-budget-rule-must-not-gate-a-correctness-rule]].
