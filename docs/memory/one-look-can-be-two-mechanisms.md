---
name: one-look-can-be-two-mechanisms
description: A single visible feature can be a skin AND a bodygroup; implementing one paints a texture onto a mesh nobody draws, which looks identical to doing nothing.
metadata:
  type: reference
---

**A disguised spy's mask took two days longer than it should have, because the skin was right.**
`C_TFPlayer::GetSkin` adds `4 + ( ( disguiseClass - TF_FIRST_NORMAL_CLASS ) * 2 )`, we implemented
it, and it resolved correctly — skin family 9 for a BLU spy disguised as a RED soldier, measured on
the owner's own demo. The mask still did not appear, because the mask MESH is alternative 1 of the
body part named `spyMask`, and every player drew at `m_nBody = 0`. The right texture was painted
onto a mesh nobody drew.

**Why:** the two halves are set in different functions, hundreds of lines apart, and each one reads
complete on its own. `GetSkin` (`c_tf_player.cpp:7790`) says which mask; the tail of
`ValidateModelIndex` (`:9024`) says whether there is a mask. Nothing in either points at the other.

**How to apply:** when a feature is a *look* rather than a value, ask which of Valve's four levers
produce it before implementing any of them — **model, skin, bodygroup, material**. A hat is a
separate entity; a team colour is a skin; a mask, a sapper light and a broken bottle are bodygroups.
Get the list first, then implement all of it, then check the rendered artefact.

**The tell that there is a second half, and it is reliable:** two branches that test the *same
condition* in different functions. `ValidateModelIndex`'s mask branch and `GetSkin`'s mask branch
are `!IsEnemyPlayer() || disguiseClass == TF_CLASS_SPY` — character for character the same pair.
A condition duplicated across functions means one mechanism split across them, and finding one arm
means going to look for the others. See [[decoding-a-field-is-not-honouring-it]] and
[[half-a-mechanism-is-not-parity]].

**The instrument that settled it** is worth keeping: dump the model. Parts with their NAMES and
alternative counts, meshes with their part and alternative, and what each skin family paints each
one with — `dotnet run --project tools/Tf2DemoSalvage.Probe -- model models/player/spy.mdl 9`. One
line of that output (`shownAtBody0 False`) ended an argument that reading code had not.
