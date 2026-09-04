---
name: parity-is-the-search-not-the-defence
description: The owner's "check for parity" means SEARCH Valve's code for the mechanism, not justify what we built; a rule written in our own comments is not an enforced rule; and reading the SDK for how a feature is declared rather than how it works, citing the wrong branch or the wrong sibling mechanism, decoding a field without honouring every consumer, and stopping a divergence search too early are the ways that search keeps failing.
metadata:
  type: feedback
---

**The owner, repeatedly, through an entire evening of me measuring the wrong things:** *"remember to
be checking for divergence from valve"* … *"you should be able to check our code for places we are
not following valve and find the problem"* … and when it landed: *"SEEEEEEE PARITY FIXES
EVERYTHING!!!"*

**Why:** every bug that night was a divergence with a citation sitting in the SDK, and in two cases
the citation was already sitting in this repository's own comments:

- `WorldRenderer.DrawModel` said *"a SKINNED model is put there by its bones and its matrix stays at
  identity"*. Nothing enforced it. It held by accident — a player's pose is (0,0,0), a merged item's
  is (0,0,0) by construction — until a skinned prop with a real networked origin arrived and was
  placed twice, ten thousand units off the map.
- `EntityModels.Absolute` implemented `CalcAbsolutePosition`'s bone-merge branch and applied it to
  everything, so a parented prop lost its own angles. That is the 90° gate.

**How to apply:** when a symptom is visual and the code "looks right", go and read the engine
function that owns the whole mechanism, then check each of its branches against ours by name.
`CalcAbsolutePosition` has THREE and we had two. `ShouldDraw` has a render-mode test we did not.
`GetSkin` has a bodygroup twin in `ValidateModelIndex`. Every one of those was found by reading
Valve rather than by measuring our output — see [[nothing-is-closed]].

**And a rule in a comment is not a rule.** If a comment states an invariant — "X stays at identity",
"only Y sets this" — either an assertion enforces it or it is a description of today's accident.
Both of the above were true when written and false a month later, with nothing red.

**The corollary that cost the most hours: check the instrument against Valve too.** A log line
reading `door_grate003_top at (0, 0, 0)` was the ILLUMINATION point, not the position, and five
conclusions were drawn from it. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[a-picture-is-assertable]].

**Ten memories were folded into this one on 2026-09-04** — all about the search itself going wrong:
reading the SDK for a declaration and stopping there, citing the wrong engine mechanism or the wrong
branch of the right one, decoding a field without honouring every place that consults it, following
an override forward instead of finding it through its callers, ranking by what is unimplemented
instead of auditing what already draws, treating a divergence as a decision to document rather than
a question to ask, and stopping a divergence search the moment one citation is found. Their names are
kept as headings below.

---

## `half-a-mechanism-is-not-parity`

When Valve splits a behaviour across two systems, port BOTH or neither. Implementing one and
open-coding the other's symptom is not parity, however good the citation on the half that landed.

The worked example (B222, D116): a dead spectated player. `C_HLTVCamera::CalcInEyeCamView`
(`hltvcamera.cpp:307`) switches the CAMERA to third person. This project instead emptied the
viewmodel's hands and left the first-person camera in the dead player's skull — a state the engine
cannot produce. It took the viewmodel off screen for the whole of every death and was reported for
days as "the viewmodel is missing".

**The tell was a silence in the SDK read as an omission.** `C_BaseViewModel::ShouldDraw`
(`c_baseviewmodel.cpp:277`) asks only "is the camera in-eye" and "is this the target's viewmodel" —
no liveness term. That is not an oversight: the camera guarantees in-eye is never held on a dead
target, so the draw test can afford not to ask. **One system's invariant is another system's
unstated precondition**, and a check that looks missing is often a check something upstream already
made impossible to need.

The owner's framing, which is what identified it: *"i dont think you can force tf2 to spectate a
dead player in 1st person like we can force this viewer to do by fucking up and not having
everything implemented"*. Ask whether the state you are guarding against is reachable in the engine
at all. If it is not, the guard is the bug — you have broken an invariant and are patching its
symptom.

**Why:** he had already said death was not the cause and it was kept anyway, because it carried a
citation. A citation on half a mechanism proves the half, not the port.

**How to apply:** before adding a guard that Valve does not have, find which system maintains the
condition that makes Valve's version safe, and implement that instead. State any deliberate
narrowing so it can be falsified — here liveness was applied to the spectated path only, since
`C_HLTVCamera` never runs on a POV demo.

Related: [[valve-parity-is-the-first-principle]], [[name-the-trade-before-fixing-valve]].

---

## `read-the-sdk-for-the-whole-mechanism`

**Read the SDK routine that IMPLEMENTS the behaviour, not just the one that declares it.** Finding
the flag, the send-prop or the constant is the easy half and feels like having done the research.

**Why:** the owner called this out after two avoidable defects in one session, both in bone merging.
`FollowEntity` and `EF_BONEMERGE` were read from the SDK, correctly — and then the merge itself was
written from scratch:

- Unmatched bones were given the worn model's REST pose in its own model space. Valve's
  `CBoneMergeCache::MergeMatchingBones` copies only the matches, because the worn model has already
  run its own full `SetupBones` — so an unmatched bone holds a place walked down its OWN hierarchy
  from its parent, which may itself have been merged. The invented fallback tore items across the
  map: a `ghostly_gibus` matched 1 bone of 8, seven stayed at the origin, and the triangles between
  stretched from the scout's head to his feet as a flat sheet.
- Worn models were left to the ordinary bake-vs-skin budget, so every cheap cosmetic was baked and
  had no bones at all to merge onto. Hats drew at ankle height.

The file was `src/game/client/bone_merge_cache.cpp`, about forty lines, and it answers both.

**How to apply:** after finding the flag, grep for what consumes it and read that. "I read the SDK"
is only true of the specific routine that was opened. Related:
[[read-the-encoder-not-the-decoder]] and [[research-before-code]] — same failure, one level deeper:
the hypothesis was verified for the declaration and assumed for the mechanism.

**It happened three times in one session on 2026-08-31, and the shape was READ-side versus
WRITE-side.** A demo's per-entity baselines were being investigated. `CL_CopyNewEntity` — how an
entering entity is DECODED against a baseline — was read out of a decompilation of `engine.dll`,
carefully and correctly. How a baseline is STORED was then reasoned about rather than read, and
produced three confident wrong answers in a row:

- "rebuilding from the baseline on every Enter fixes it" — ran it, changed nothing;
- "our missing baselines are in the unparsed `dem_stringtables`" — they are not in it;
- "the engine checkpoints every entity it decoded" — it does not, and a conformance test citing the
  reference parser said so before the experiment did.

The store side was **twelve lines further down the same function already open**, and it settled all
three in five minutes once looked at: the store lives in the entering path only, and it saves
`RecvTable_MergeDeltas( table, fromBuf, update, newBuf )` — the merge against whichever baseline was
used, class baseline included. That last clause was the actual defect, and no amount of reasoning
from the read side would have produced it.

**How to apply, sharpened:** when a mechanism has two sides — read/write, encode/decode, send/receive
— reading one of them is half the research, and the half you skipped is where the surprise is. If an
experiment falsifies a hypothesis about a mechanism, that is the signal to go and read the other
side, not to form a second hypothesis.

### The line you came for is usually below the one that changes its meaning

B276, and it is the sharpest instance so far. `AddBaseAnimatingInterpolatedVars` was printed to the
terminal TWICE in one session while answering "which variables are animation-latched":

```c
int flags = LATCH_ANIMATION_VAR;
if ( m_bClientSideAnimation )
    flags |= EXCLUDE_AUTO_INTERPOLATE;
AddVar( &m_flCycle, &m_iv_flCycle, flags, true );
```

The answer taken was the last line — cycle, pose parameters, encoded controller. The flag two lines
above was read past both times, **while this very memory was being cited elsewhere in the same
session**. It is the whole rule: a client-side-animated entity's cycle is never interpolated, and
`AddVar` enforces that by placing the variable past `m_nInterpolatedEntries`, the bound
`Interp_Interpolate` loops to. This project had been interpolating it for years; a viewmodel stopped
animating and the owner found it, not a test.

**Two things generalise.**

- **A flag being SET is a different fact from a flag existing.** Reading a header of `#define`s
  teaches nothing; `|= FOO` on the line above your answer changes what your answer means.
- **The signal was in what was READ, not in what was written.** The flag never reached a comment, a
  commit or a diff, so no review of the change could have caught it — and that is why
  `.claude/hooks/flag-unread.ps1` watches tool OUTPUT, firing once per flag per session on a flag
  that is composed or tested rather than merely named.

---

## `decoding-a-field-is-not-honouring-it`

Adding a decode is two jobs: reading the value, and reaching **every** place the engine consults it.
The first has obvious tests and the second has none, so a field can arrive, be carried through the
pose, be asserted on, and still change nothing on screen.

**Measured, 2026-08-29 (B221 → B231).** `m_nRenderMode` was decoded and routed to the render GROUP,
where `RenderGroups.For` classifies an entity at alpha 255 and mode 10 as translucent — and
translucent at alpha 255 draws solid. So the decode was correct, the group was correct, and the
picture was identical. The engine consults the same field in a second place this project had no
equivalent for:

```c
bool C_BaseEntity::ShouldDraw()          // c_baseentity.cpp:1437
{
    if ( m_nRenderMode == kRenderNone )  // some rendermodes prevent rendering
        return false;
    return (model != 0) && !IsEffectActive(EF_NODRAW) && (index != 0);
}
```

`EF_NODRAW` was already honoured in `IsDrawn`, one line away. The render mode was not, because
nothing had ever decoded it — so when it arrived it went to the consumer somebody was thinking
about rather than to all of them.

**Cost:** eighteen invisible `func_door` movers on `cp_fulgur` drawn as solid slabs, which is what
sent an evening into a "rotated grate".

**How to apply:** when a field is newly decoded, grep the SDK for **every** use of it, not the one
that motivated the work — `grep -rn m_nRenderMode` finds `ShouldDraw`, `IsTransparent`,
`ComputeFxBlend` and the leaf classifier, and they are four different decisions. Then ask which of
them this project already has a home for, and where the others belong. A field consulted in four
places and honoured in one is three quarters unimplemented, and the tests for the one look exactly
like the tests for all four.

Related: [[output-level-assertion-or-it-is-not-done]], [[measure-the-output-not-the-capability]].

---

## `the-cited-line-may-be-the-wrong-branch`

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
  [[death-is-ef-nodraw-not-an-animation]].

**The tell is a branch keyed on something a demo settles globally.** `IsLocalPlayer`, `IsDormant`,
`GetLocalPlayer() != NULL` — a recording answers these once for the whole file rather than per case,
so one branch is taken always and the other never. Work out which before reading further, or the
reading is about code that will not run.

**And the failure is invisible.** Implementing the wrong branch produces something that draws, cites
the engine, and passes every test written against it — the defect is only that it is the answer to a
question the demo never asks. Both of these were caught by looking at output, not by tests.

---

## `follow-the-call-not-the-value`

**When a function rewrites one of its own arguments, find every CALLER — do not trace the argument
forward from where it is computed.** The two searches give different answers, and the caller search
is the complete one.

`STUDIO_REALTIME` (B309, 2026-09-04) is decided inside `CalcPoseSingle`, which discards the cycle it
was handed. There are four places that hand it one, and the fourth is
`MaintainSequenceTransitions` — which computes and CLAMPS `flCycle` on the line directly above
`AccumulatePose`, so the clamp reads as the last word on that cycle. Following the cycle forward
from each site where it is computed found three of four; grepping for `AccumulatePose` found all
four.

**The general shape: the override sits one call deeper than the arithmetic it overrides.** Anything
Valve decides inside a leaf function is invisible from every caller, and callers are where our code
is organised.

**Then check each site is EXECUTED, not merely written.** Two of the four branches had tests that
passed while nothing reached them — no wire layers in the fixture, no autolayers declared. A
sabotage that reddens nothing is the only thing that says so, which is why the question to ask a
sabotage is *which* tests reddened rather than whether the right one did (see the entry above on a
census that survives removing its own cause, in [[instrument-bugs-outnumber-decoder-bugs]]).

---

## `ask-which-engine-mechanism-you-are-copying`

The free camera flew at 600 units a second, a number reasoned from the keyboard-repeat defect it
replaced rather than from the engine. The parity audit found that correctly, then offered
`CalcDemoViewOverride` (`view.cpp:153`) as the reference — **the engine's demo-playback camera**, so
apparently the obvious match for a demo viewer. 320 u/s at scale 1.

The owner picked the other one:

> *"the correct speeds to use are spectator speeds, im pretty sure, idk what the demo cam speed is
> even actually for becasue ive never seen a tf2 server which has spectators off really"*

and they were right for a reason the question never surfaced: **`cl_demoviewoverride` ships `"0"`**,
so it is a feature almost nobody has ever switched on. The roaming spectator is what a demo viewer is
actually imitating, and its numbers are four times different — 960 u/s, via
`FullObserverMove` → `FullNoClipMove( sv_specspeed 3, sv_specaccelerate 5 )` and a
`sv_maxspeed × factor` clamp.

**The danger is specifically that a citation makes a wrong reference look settled.** An uncited number
invites the question "where did that come from"; `view.cpp:153` closes it. Had this shipped, the free
camera would have been wrong at a *quarter* of the correct speed with a source comment defending it,
and the next reader would have had no reason to look.

**How to apply:** before citing an engine mechanism, ask whether it is the ONLY one for that job, and
check what its enabling convar defaults to — a mechanism that ships off is rarely the one users
experience. When there are two, which one we copy is a decision to record (D102), not a detail to
pick. Related: [[name-the-reading-you-picked]], [[a-default-is-not-a-constant]],
[[ask-valve-before-designing-not-after]].

---

## `audit-means-verify-what-exists`

The owner, 2026-09-01, correcting an audit that had been ranking engine functions by branch count
and filing the unimplemented ones: *"i wanted you to make sure everything we have implemented is
right, had valve partity, and is not buggy. Theres far more useful thinks than ragdolls still
available, like interp, and out fps being way way too low."*

**Why:** an unimplemented mechanism is VISIBLE — it is absent, somebody notices, it can be filed any
time. A wrong implementation is INVISIBLE: it draws something, the suite is green, it looks
finished. Only the second class needs an audit to find it, and branch-counting cannot rank it,
because a function implemented well and one implemented badly have the same branch count.

The concrete cost: the audit's top-ranked, most-measured finding was 299 undrawn corpses — and the
owner runs a comp config with ragdolls off, so players vanish on death in his real game. The best
measurement of the session was aimed at a feature he would switch off if it existed.

**How to apply:** rank by what we ALREADY DRAW, and ask of each whether it matches the engine on
every branch and uses the value the engine would use. `InterpolationDelayTicks = 7` hardcoded beside
a declared-but-unread `cl_interp` is the shape to look for — right for stock defaults, wrong for the
config he runs. Performance counts as correctness here too: TF2 plays these demos at 600+ fps, and a
gap that large is a defect in code we wrote, not a budget.

Does not retract the ragdoll findings — see [[a-gap-can-be-filed-backwards]]. They are correct and
recorded; they are simply not the priority they were written up as. Related:
[[valve-parity-is-the-first-principle]], [[not-every-setting-needs-a-bind]].

---

## `a-bug-is-a-divergence-search-first`

**The owner, 2026-08-30**, after a fix shipped and had to be pulled:

> *"diversions from valve cause issues like this, any bug we find should be a diversion search for
> the first like hour"*

> *"not a rule, just a kinda standard in a way, its loose i dont expect a real timed hour, the point
> is that none of our issues are not solved problems within the source engine, we have no reason to
> not use those answers, and by not using those answers we run into bugs and compatability issues"*

**Why:** this is a viewer for Valve's format, reading Valve's maps, drawing Valve's models. Anything
that looks wrong is something the engine already does right, so the opening question is *which
mechanism are we missing or doing differently* — not *what is wrong with our code*. The hour is a
posture, not a stopwatch.

**How to apply, and the failure mode is stopping too early.** B231 found a real, cited divergence —
`C_BaseEntity::ShouldDraw` refuses `kRenderNone`, and every `func_door` on `cp_fulgur` carries it —
implemented it, and deleted the map's gates. The search had answered "does the engine draw this
entity" and never asked "then what DOES draw the gate". The answer sat in the same entity lump:

```
func_door 'setupgate_stage1_1_bottom'  rendermode 10   <- invisible mover
  prop_dynamic door_grate003_bottom.mdl parentname 'setupgate_stage1_1_bottom'
```

Every gate is an invisible mover plus a **parented** visible prop. The door brushwork we should not
have drawn was standing in for the grate we were not drawing.

So: read until the mechanism is **whole**, not until a line of C++ agrees with you. A citation that
explains why something should be hidden is half an answer; the other half is what the engine shows
instead. Related: [[valve-parity-is-the-first-principle]], [[ask-valve-before-designing-not-after]].

---

## `a-divergence-is-asked-not-documented`

**Any departure from what Valve's code does is a QUESTION for the owner, not a decision to record.**
The owner, 2026-08-25, after catching the third one in a session:

> "if you diverge i need to be asked"

**The owner's framing, which says WHY the rule has no exceptions:**

> "Valve can be thought of as god in this project, and this project has to follows gods rules because
> it exists in gods universe lol"

That is not reverence, it is scope. A demo is a recording made BY the engine, of a world defined by
the engine, in a format the engine wrote — so every question about what a value means has an answer
that already exists, and any answer of ours that differs is simply wrong about the universe it is
in. There is no design space to have an opinion in. Reasoning toward what a thing "should" do is
therefore not analysis, it is guessing at something already written down.

**Why:** parity is the project's first principle (D89), and every measured win on the viewer has been
a move TOWARD the engine. A divergence chosen unilaterally and explained in a comment reads as
settled to the next person — mine or anyone's — so the wrong turn survives precisely because it was
written up well. The owner is the one who decides what the program is allowed to differ on.

**The failure mode is specific and I did it three times in one session, each time in the same
words.** The doc comments said "stated rather than dropped" and "a divergence stated rather than
hidden", which sounds like diligence and is the tell: writing it down felt like discharging it.

The three, for shape rather than for the detail:

- `MapOverview` — `CanPlayerBeSeen` rejects a player at exactly the origin (`// Invalid guy`); ours
  does not. I reasoned that demo entities are read rather than networked and `Drawn` already covers
  it. Plausible, mine, unasked.
- `LeafVis` — the leaf box is projected to clip space and ignores depth rather than being drawn in
  world space. Partly inherited from an existing overlay pass, which is not a reason to keep quiet.
- `LevelSystems` — explicit wiring instead of Valve's `IGameSystem` list-walk.

**The third one is the argument for asking, because my reason was simply WRONG.** I claimed a shared
`ILevelSystem` was impossible: it would need `LoadedMap` (Scene) visible to `SoundscapeSystem`
(Audio), and Audio does not reference Scene. One grep of `igamesystem.h` killed it —
**`virtual void LevelInitPreEntity() = 0;` takes NO parameters.** Valve's systems pull what they need
from globals, so the interface carries no payload and the boundary was never in the way. The owner:

> "there we go, i knew there was no reason to drift away from valves decisions"

### The test for an ACCEPTABLE departure, in the owner's words

> "that departure is completely fine, if we know exactly why valve is doing somethign and exactly
> why we dont have to, then its fine"

**Both halves, and EXACTLY rather than approximately.** A departure is legitimate when the reason
the engine does it is known and the reason it does not apply here is known. Neither alone is enough:
"Valve does X for reasons we have not read" is the mistake this whole entry is about, and "we do not
need X because it seems unnecessary" is the same mistake wearing a conclusion.

**The worked example, accepted 2026-08-26.** `IGameSystem::IsPerFrame()` was dropped from our
interface. Why Valve has it: that codebase avoids RTTI, so a system must RETURN whether it runs per
frame. Why we do not need it: the question is a type test — `system is IGameSystemPerFrame` — which
cannot disagree with itself, where Valve's flag can (a class may derive from
`CBaseGameSystemPerFrame` and still answer false). The SPLIT it guards was kept as two interfaces,
because the header is explicit that the two tiers must not be interchangeable. Both halves known,
so the departure stands.

**The counter-example from the same day**: claiming a shared `ILevelSystem` was impossible because
it would need `LoadedMap` visible to Audio. Half one was never checked —
`LevelInitPreEntity()` takes no parameters — so the departure was invented to serve a reconstruction
rather than the engine.

**How to apply:** when a departure looks necessary, stop and ask before writing the code — and
before asking, go and read the actual declaration rather than reconstructing it from what the
divergence would need. Bring both halves to the question: what the engine does and why, and why it
does not bind here. Most "we cannot do what Valve does" claims are claims about a reconstruction.
Present the cost of both sides; the owner has said they can be influenced, so a recommendation is
wanted, but the choice is not mine.

Related: [[valve-parity-is-the-first-principle]], [[name-the-reading-you-picked]],
[[never-revert-without-asking]], [[an-optimisation-is-not-a-skippable-departure]],
[[name-the-trade-before-fixing-valve]], [[nothing-is-closed]].

---

## `the-half-you-have-may-be-the-wrong-half`

**When a mechanism spans several engine sites, implementing some of them can be WORSE than
implementing none.** Ask which half depends on the other having run.

`BONE_FIXED_ALIGNMENT` is three sites and one mechanism (B308, 2026-09-03): align a decoded rotation
once against the bone's `qAlignment` (`bone_setup.cpp:470`), then use the `NoAlign` variants in both
blends (`:1492`, `:1608`) because the choice is already settled. We had the `NoAlign` slerp and
neither of the others — so nothing aligned anywhere, and an antipodal pair blends the LONG way
round. Implementing the slerp alone was the defect.

**The tell is a `NoAlign`, a `Fast`, a `Unchecked` or a `Raw` variant.** Those names mean "the
precondition was established elsewhere". Reaching for one without finding where is the mistake.

**Two structural reasons this project keeps meeting it**, both worth checking directly:

- **A field in a struct GAP is never missed.** `qAlignment` sits between `poseToBone` and `flags`,
  96 + 48 = 144; every field on either side read correctly, so nothing failed.
- **A flag no content sets does NOT make the branch untestable**, and believing it did was this
  session's own wrong turn. `bone-flags` measured 0 of 924 bones across 37 models, and that was
  written up as an unclosable gap. It is a fact about ONE input. The flag is the variable under
  test, so setting it on a real model is the manipulation; what actually had to be found was the
  OTHER input — a track using the encoding the branch needs. **A zero denominator on one input is
  not a zero on the experiment.**

**Select the input by the condition the engine branches on.** Two hand-picked animation numbers
failed before that; `CalcBoneQuaternion` returns early for the raw encodings, so only a track
carrying `STUDIO_ANIM_ANIMROT` reaches the alignment — and `StudioAnimation.Tracks` already reported
each track's flags, so the instrument existed (see
[[instrument-bugs-outnumber-decoder-bugs]]).

**Ask a sabotage WHICH tests reddened, not whether the right one did.** Deleting the flag gate here
reddened nothing: both tests set the flag on their own subject, so the gate was always true for
everything they exercised, and each stated its expectation relative to a reading that ran through
the same code. `Failed: 0` presents that as success.

Same shape as [[the-player-send-table-excludes-the-animation]]. Related:
[[an-empty-search-needs-a-control]], [[instrument-bugs-outnumber-decoder-bugs]].
