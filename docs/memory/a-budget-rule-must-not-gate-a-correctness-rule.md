---
name: a-budget-rule-must-not-gate-a-correctness-rule
description: `(mustSkin || overBudget) && bones.Count > 1` let an optimisation veto a correctness flag; a one-bone worn model could never skin, so it could never bone-merge.
metadata:
  type: project
---

```csharp
bool skin = (mustSkin || wantedFrames > affordable) && bones.Count > 1;   // wrong
bool skin = bones.Count > 0 && (mustSkin || (wantedFrames > affordable && bones.Count > 1));
```

`mustSkin` means "this model is bone-merged and CANNOT be baked". `bones.Count > 1` means "skinning
a rigid model buys nothing". The first is correctness, the second is a budget heuristic, and the
conjunction let the heuristic veto the correctness flag.

**Why:** baking discards bone indices, so a baked model cannot bone-merge and is drawn at its
wearer's origin — a player's feet, or for a viewmodel **the camera**. The Original
(`c_bet_rocketlauncher.mdl`) has exactly one bone, `weapon_bone`, which its arms do supply, so it
could merge perfectly and never merged at all. It filled the screen on every demo since June 2012.

It hid because the stock launcher has four bones, clears the guard and works — one weapon wrong for
a reason unrelated to that weapon.

**How to apply:** when a predicate mixes "must" with "worth it", check the must cannot be overridden.
The tell here was a comment stating the intent — "skinned however cheap it is, and this is not an
optimisation choice" — directly above a line that contradicted it. Related:
[[bone-merge-sends-no-position]], [[a-player-has-two-viewmodels]],
[[measure-the-output-not-the-capability]].
