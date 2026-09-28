---
name: one-look-can-be-two-mechanisms
description: A single visible feature can be a skin AND a bodygroup; implementing one paints a texture onto a mesh nobody draws, which looks identical to doing nothing.
metadata:
  type: reference
---

A disguised spy's mask took two extra days because the SKIN was right — `GetSkin`'s formula
(`c_tf_player.cpp:7790`) was implemented and resolved correctly — but the mask still didn't appear,
because the mask MESH is a
bodygroup alternative, and every player drew at the default bodygroup. The right texture was painted
onto a mesh nobody drew.

**Why:** the two halves are set in different functions, hundreds of lines apart, each reading
complete alone.

**How to apply:** when a feature is a LOOK rather than a value, ask which of Valve's four levers
produce it before implementing any — model, skin, bodygroup, material. Get the full list, implement
all of it, then check the rendered artefact.

**Reliable tell: two branches testing the SAME condition in different functions** — a condition
duplicated across functions means one mechanism split across them; finding one arm means going to
look for the others. See [[parity-is-the-search-not-the-defence]].

**The instrument that settled it:** dump the model — parts with names and alternative counts, meshes
with their part/alternative, what each skin paints each with. One line of that output ended an
argument reading code hadn't.
