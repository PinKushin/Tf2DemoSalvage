---
name: a-reading-that-makes-valve-absurd-is-the-reading
description: When shipped data read our way makes the engine draw nonsense, distrust the reader first; a text scan saw commented-out keys as set (B523)
metadata:
  node_type: memory
  type: feedback
  modified: 2026-10-12
---

`ScriptKeyValue.First` searched for a quoted key anywhere in a class script. Shipped `soldier.ctx` and `medic.ctx`
carry `//"DontDoNewJump" "1"` (medic also `//"DontDoAirwalk" "1"`): comments. Read as set for 10 days (B437).
Result: every soldier jump asked for `ACT_MP_JUMP_PRIMARY`, no model has it, engine fallback is sequence 0 = `ref`.
Soldiers jumped in the reference pose.

**Why:** the "measurement" beat a correct comment ("finished migration, every class false") because it had a test.
The test's guard ("some class must opt out, or the reader answers a constant") locked the wrong reading in.

**How to apply:**
- A reading of shipped data that makes TF2 itself draw something absurd is wrong in the READER until shown
  otherwise. Ask what the reading implies on screen before porting it.
- Print the raw file once (`weapon-script playerclasses/soldier` decrypts a class script) before trusting a parsed flag.
- Any text scan of KeyValues must skip `//` to end of line at a token start (`CUtlBuffer::EatCPPComment`).
- `class-activities <class> <ACT_...>` answers whether a class model has a sequence for an activity.
- A guard against "reader answers a constant" must use a key whose value is known another way (the model path),
  never an assumption about the data.
- `moment-dump` samples as PLAYBACK (interpolated, about 10 ticks behind on f12); a paused `--shot` draws last
  received. Measured: shot at 63600 = moment-dump at 63610. Use `TF2VIEW_PICK` for what a shot drew.
