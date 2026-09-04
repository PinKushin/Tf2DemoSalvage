---
name: a-shared-helper-may-hold-another-functions-rule
description: Two engine functions can implement the same-looking rule and differ only at the edges, so reusing the existing helper imports the wrong one — the DRY move is the defect.
metadata:
  type: feedback
---

**Before reusing this project's helper for a rule the engine states twice, check that both engine
functions agree — including their `default` branch.** Two functions can compute the same thing for
every value anyone has looked at and diverge outside it, and the reuse then silently applies one
subject's rule to another.

Measured while implementing the corpse appearance (B315). Team-to-skin exists twice in TF2:

```cpp
// C_TFPlayer::GetSkin, c_tf_player.cpp:7807-7817 — what PlayerSkin.ForTeam implements
case TF_TEAM_RED:  nSkin = 0; break;
case TF_TEAM_BLUE: nSkin = 1; break;
default:           nSkin = 0; break;

// C_TFRagdoll::CreateTFRagdoll, c_tf_player.cpp:712-719
if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
```

Identical for RED and BLU. A **player** with no team falls to RED; a **corpse** with no team falls
to BLU. Calling `PlayerSkin.ForTeam` from the ragdoll looked like DRY and was a divergence.

**Why this one hides so well.** Every symptom is at an edge nobody photographs, the helper already
carries a citation so it reads as settled ([[ask-which-engine-mechanism-you-are-copying]]), and the
suite stays green because no existing test supplies the odd value. The reviewer's eye is drawn to
whether the rule is right, not to whether it is the right subject's rule.

**The tell is a bare `else` against an explicit `default:`.** Valve wrote a switch in one place and
an if/else in the other — different authors, different days, and the difference is real rather than
stylistic. Whenever the engine spells a rule out twice, that is a fact about the engine, not
duplication to be cleaned up.

**Do not merge them afterwards either.** The comment on `RagdollAppearance` says why they are apart,
because a future reader will otherwise see two identical-looking expressions and unify them. That
comment is doing the same job as the test.

Caught by `Skin_ForNoTeamAtAll_IsBlu`, which was written before the code and failed against the
reuse — [[conformance-test-before-implementation]] earning its keep. Related:
[[half-a-mechanism-is-not-parity]], [[a-property-name-needs-its-declaring-table]] (the same shape one
layer down: a name is not enough, you need the table it was declared in),
[[parity-is-the-search-not-the-defence]].
