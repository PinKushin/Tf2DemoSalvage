---
name: the-cited-line-may-be-the-wrong-branch
description: A quoted engine line with a real citation can still be the wrong branch — check which branch a DEMO actually takes before implementing it, because a citation makes a wrong answer look settled.
metadata:
  type: feedback
---

**Before implementing a line quoted from the engine, ask which branch this project's own input takes.**
A citation makes an answer look finished, and the branch that is easiest to find is often the one a
demo never reaches.

Twice in one session on `CreateTFRagdoll`:

- **`RagdollSpawn`.** It is the memorable name and it appears when anyone asks how a corpse is
  posed. It sits in the `else` of `if ( !pPlayer->IsLocalPlayer() && ... )` — the LOCAL player. A
  **SourceTV recording has no local player at all**, so every corpse in one takes the other branch and
  copies `pPlayer->GetSequence()`. A comment citing `RagdollSpawn` as the rule was written, with its
  `file:line`, and it was the minority case.
- **The skin.** `PlayerSkin.ForTeam` already existed with a citation, and it implements
  `C_TFPlayer::GetSkin` — whose `default:` is 0 where the ragdoll's bare `else` is 1. See
  [[a-shared-helper-may-hold-another-functions-rule]].

**The tell is a branch keyed on something a demo settles globally.** `IsLocalPlayer`, `IsDormant`,
`GetLocalPlayer() != NULL` — a recording answers these once for the whole file rather than per case,
so one branch is taken always and the other never. Work out which before reading further, or the
reading is about code that will not run.

**And the failure is invisible.** Implementing the wrong branch produces something that draws, cites
the engine, and passes every test written against it — the defect is only that it is the answer to a
question the demo never asks. Both of these were caught by looking at output, not by tests.

Related: [[ask-which-engine-mechanism-you-are-copying]] (a citation to the wrong mechanism),
[[half-a-mechanism-is-not-parity]], [[parity-is-the-search-not-the-defence]],
[[read-the-sdk-for-the-whole-mechanism]].
