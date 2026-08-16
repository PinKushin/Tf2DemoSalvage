---
name: shipped-data-is-a-source
description: VMTs, .res files and VPK contents answered two questions filed as needing a decompiler; the game's data ships with prose explaining itself.
metadata:
  type: project
---

**The source menu lists the SDK, the Rust parser, the wiki and a decompiler. It is missing the
game's own shipped data**, and that fifth source settled two questions this project had filed as
closed. Both on 2026-08-16, within an hour of each other.

- **`$modblend`** was the standing worked example for "the SDK cannot answer this, decompile it". No
  decompiler needed. It is declared in three shipped VMTs and read by **nothing** — the only
  consumer is an `Equals` proxy **commented out four lines below it** in the same file. No published
  shader declares it and no shipped binary contains the string, so the material system ignores it.
  Dead parameter; correct implementation is nothing.
- **Game event field widths and signedness** were "outside the SDK, `GameEventManager` is closed".
  They are in the comment block atop `game/mod_hl2mp/resource/modevents.res`: `short` is 16-bit
  **signed**, `long` 32-bit signed, `bool` 1 bit unsigned. Signedness had been assumed, and getting
  it backwards yields a plausible number rather than an error.

**Why it gets skipped: data does not feel like a source.** The habit is to look for code, and a
`.res` or `.vmt` reads as content rather than as documentation — but Valve's data files carry prose
explaining their own format, written for the people who author them.

**The rule: when the question is about a format the GAME reads, read what the game ships.** When it
is about engine behaviour, the code sources still apply.

Practical notes for doing it:

- VMTs live inside VPKs. `grep -a` works directly on the `.vpk`, and `dd` around the byte offset from
  `grep -abo` gives readable context without unpacking anything.
- Extracting `$`-prefixed strings from `bin/stdshader_dx9.dll` yields **515** parameter names — a
  usable denominator for what the shipped shaders actually accept, no decompiler involved.
- An absence measured this way needs a control in the same sweep. A grep returning zero has been a
  fact about the grep three times in this project; see
  [[an-uncoverable-gap-is-usually-your-reader]].

Related: [[tf2-game-code-is-in-the-sdk]], [[binaries-answer-what-the-sdk-cannot]],
[[read-the-spec-before-measuring-our-data]].
