---
name: a-default-valued-field-is-untestable-on-the-corpus
description: A field that is inert at its default can never be observed on ordinary demos — author one.
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T09:17:37.671Z
---

**A field whose default makes it a no-op cannot be tested on a corpus of ordinary play, ever.**
Not "we have not found a demo yet" — the observation is impossible in principle, because the correct
implementation and the missing one produce identical output at that value.

`m_flHeadScale`, `m_flTorsoScale`, `m_flHandScale` (B312): all three multiply a scale and default to
**1**. Every recording in the corpus reports 1 — 440 of 440 on `z1800` — so every rendering
comparison agreed and every count matched while nothing read the fields at all. The same shape as
`m_flPlaybackRate`, decoded and retained and unit-tested while every animation played at rate 1.

**The tell: a default that is the IDENTITY of whatever operation the field feeds.** 1 for a
multiplier, 0 for an offset, empty for a list that is concatenated. Grep for the field, then ask what
the engine's own initialiser sets it to (`c_tf_player.cpp:577` here) — if that value is the identity,
no measurement of ordinary content can ever find the gap.

**So author the specimen** ([[author-the-specimen-the-corpus-lacks]]). `SyntheticPlayer.Demo` takes a
property dictionary and writes a real demo through the real container and schema; the value comes
back out of `DemoTimeline` having been through production's decode. That converts "correct by
construction and citation" into "observed".

**Use values that are distinct from each other AND from the default.** Equal values let a carry into
the wrong field pass; the default is what every lost hop falls back to. Three fields, three numbers,
none of them 1.

**And test the DEFAULT's own claim separately.** A control asserting "nothing sent leaves it at 1"
stays green through every sabotage of the carry, because its input is null on both sides — only a
sabotage of the coalesce itself (`?? 0f`) reddens it. Two claims, two inputs.
