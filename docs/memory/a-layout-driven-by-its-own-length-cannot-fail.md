---
name: a-layout-driven-by-its-own-length-cannot-fail
description: A parser whose reading is bounded by the body's own length has disabled its own exact-consumption check; TextMsg accepted 511 empty strings for two years.
metadata:
  type: project
---

`UserMessageBody` makes every layout safe with one rule: a correct layout consumes the body's
stated length **exactly**. `TextMsg` opted out of that rule without anyone deciding to — it read
NUL-terminated strings `while (offset < length)`, and reading to the end consumes the body exactly
**by construction**. The guard was still there, still evaluated, and could never fail. Measured
2026-08-19: a 512-byte body of zeros decoded as 511 empty strings and came back with fields and the
name `TextMsg` attached.

**Why:** the check is only a check when the layout's width is decided **independently of the body**.
Anything whose reading is driven by the body itself — a loop to the end, a count read out of the
body, a trailing variable-length blob — has made itself unfalsifiable, and it will accept garbage
while every other layout in the same file is held to a real standard.

**How to apply:** when adding or reviewing a layout in `UserMessageBody`, ask what decides where it
stops. If the answer is "the body", the exact-consumption guard below it is decorative. Get the
width from the source instead — `UTIL_ClientPrintFilter` in `src/game/server/util.cpp` and
`CBaseHudChat::MsgFunc_TextMsg` in `src/game/client/hud_basechat.cpp` both say five strings, always,
with empty parameters sent as empty strings. Tightening cost nothing: 19 of 19 `TextMsg` bodies
across the nine era specimens still decode, protocol 11 through 24.

Two follow-ons worth keeping:

- **Four existing fixtures asserted one, two or three strings and all four passed**, because the
  same belief wrote the fixture and the code. No server has ever sent those bodies. See
  [[put-the-real-file-in-the-fixture]].
- **It was found by a test written over ALL registered names at once** — feed every name a 4096-bit
  body, assert none decodes. Per-message tests would have given `TextMsg` the same
  too-short/too-long pair as everything else and it would have passed both, because its real defect
  was that its length was whatever it was handed. See [[most-of-a-decoder-is-untested]].
