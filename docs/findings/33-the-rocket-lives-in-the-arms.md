# 33 — The rocket lives in the arms, and nothing can hide it

*Evidence class: measured against shipped game content (`tf2_misc_dir.vpk`), by
`ViewmodelArmsContentTests`.*

## What was being chased

A player in first person was drawing **two weapons overlapping** — reported as "thats 2 sticky
launchers overlapping each other", and separately as "soldier has a weird glitch no idea what it
is". Five aimed changes were made at it in one session and four fixed nothing. Every one of them was
reasoned from a screenshot.

## The claim that started it, and why it was not evidence

A production log line reported:

```
pairing models/weapons/c_models/c_soldier_arms.mdl:
  [0] 'soldier_hands'  [1] 'soldier_sleeves_red'  [2] 'models/weapons/w_rocketlauncher/w_rocket01'
```

An arms model listing a **weapon** material is a good enough reason to look. But that log prints
`model.Meshes` **unfiltered**, so it names every alternative of every body part — including the ones
`m_nBody` hides. A weapon mesh that exists as alternative 1 of a part whose selection is 0 is present
in the file, absent from the screen, and completely indistinguishable in that log from a real defect.

**Listing a material is not drawing it.** That distinction is the whole finding, and no amount of
staring at the log could have settled it.

## What the file actually says

```
c_soldier_arms.mdl: 1 body part (base 1, 1 alternative); 3 meshes, 3 shown at body 0
  part 0 alt 0 'models/player/soldier/soldier_hands'          SHOWN
  part 0 alt 0 'models/player/soldier/soldier_sleeves_red'    SHOWN
  part 0 alt 0 'models/weapons/w_rocketlauncher/w_rocket01'   SHOWN
```

One part offering one alternative. **There is no body number that removes any mesh from this
model.** The loaded rocket is part of the soldier's arms unconditionally, because the reload
animation has to hold it — and every other animation parks it outside the frustum instead of hiding
it.

That is a general Source idiom worth stating plainly, because it inverts the usual assumption:

> A mesh with no bodygroup is not necessarily always visible. It can be hidden **by its bones**.

And the consequence for anything that replays a demo rather than running the game: **a mesh parked
off-screen by its bones is only off-screen while the bones are right.** At a rest pose, or at the
wrong sequence, or with an unresolved animation, it snaps back to the model's origin and sits in the
middle of the view. It looks exactly like a duplicate weapon, and it is not one.

## The contrast that proves it is a real mechanism, not an accident

The demoman's models were measured the same way, and they are built the opposite way round:

```
c_demo_arms.mdl                — no weapon material at all
c_grenadelauncher.mdl          — 2 parts (base 1 x1), (base 1 x2); 2 meshes, 1 shown at body 0
    part 0 alt 0  9709v  SHOWN
    part 1 alt 1   199v  hidden        ← the loaded grenade
c_stickybomb_launcher.mdl      — 1 part; 1 mesh, 1 shown
```

The grenade launcher declares a part with **two** alternatives and supplies a mesh for only one of
them, so alternative 0 is an *empty model*. That is Valve hiding the loaded round the other way —
with a bodygroup rather than with bones.

So both idioms ship, in two models of the same class, for the same job. A parser that assumes either
one is universal is wrong half the time.

## What this did NOT explain, recorded deliberately

**The reported bug was the demoman's, and this finding does not reach it.** His arms carry no weapon
material, his weapon models each draw exactly one mesh at body zero, and our body selection matches
Valve's on every one of them. No demoman model can produce a duplicate.

Therefore the second launcher is a second **instance**, not a second mesh — which is a different
search, in the scene layer rather than in the content layer. Writing that down matters more than the
half that worked: a finding that quietly drops the case it failed to explain is how a wrong
conclusion gets confidently repeated.

## The method note

Four theories died today to screenshots and log-reading; two died in twenty minutes to one offline
test that reads Valve's shipped files and prints what it found. The difference is not cleverness, it
is that the test **could have failed** — and one of its assertions did, immediately, in the
direction that killed the theory it was written to support.

The instrument reads the game rather than a fixture on purpose. A fixture authored from this
project's own understanding of an arms model could only ever have confirmed that understanding.
