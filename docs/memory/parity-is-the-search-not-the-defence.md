---
name: parity-is-the-search-not-the-defence
description: "The owner's \"check for parity\" means SEARCH Valve's code for the mechanism, not justify what we built; a rule written in our own comments is not an enforced rule; and reading the SDK for how a feature is declared rather than how it works, citing the wrong branch or the wrong sibling mechanism, decoding a field without honouring every consumer, and stopping a divergence search too early are the ways that search keeps failing."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:26.664Z
---

Owner, through an evening of measuring the wrong things: *"remember to be checking for divergence
from valve"* … then, landing: *"SEEEEEEE PARITY FIXES EVERYTHING!!!"*

**Why:** every bug that night was a divergence with a citation in the SDK, twice already sitting in
this repo's OWN comments as an unenforced invariant ("a skinned model's matrix stays at identity") —
true when written, false a month later, nothing red.

**How to apply:** when a symptom is visual and the code "looks right", read the engine function that
owns the WHOLE mechanism, check each branch against ours by name. A comment stating an invariant is
not a rule unless an assertion enforces it.

**Corollary that cost the most hours: check the instrument against Valve too** — a log line printing
an ILLUMINATION point was read as position, five conclusions built on it. See
[[instrument-bugs-outnumber-decoder-bugs]], [[a-picture-is-assertable]].

---

## `half-a-mechanism-is-not-parity`

**When Valve splits a behaviour across two systems, port BOTH or neither.** The worked example (B222,
D116): a dead spectated player. `C_HLTVCamera::CalcInEyeCamView` (`hltvcamera.cpp:307`) switches the
CAMERA to third person; this project instead emptied the viewmodel's hands and left the first-person
camera in a dead player's skull — a state the engine cannot produce, reported for days as "the
viewmodel is missing".

**The tell: a silence in the SDK read as an omission.** `C_BaseViewModel::ShouldDraw`
(`c_baseviewmodel.cpp:277`) has no liveness term because the camera GUARANTEES first-person is never
held on a dead target — **one system's invariant is another system's unstated precondition.** A check
that looks missing is often a check something upstream made impossible to need.

Owner: *"i dont think you can force tf2 to spectate a dead player in 1st person like we can force this
viewer to do by fucking up and not having everything implemented."* Ask whether the guarded-against
state is reachable in the engine at all — if not, the guard is the bug.

Related: [[valve-parity-is-the-first-principle]], [[name-the-trade-before-fixing-valve]].

---

## `read-the-sdk-for-the-whole-mechanism`

**Read the SDK routine that IMPLEMENTS the behaviour, not just the one that declares it.** Finding the
flag is the easy half. Two bone-merge defects: unmatched bones given the wrong rest pose (Valve's
merge copies only matches, since the worn model already ran its own full setup); worn models left to
the ordinary bake budget so cosmetics had no bones to merge onto at all. Both from reading the flag
and inventing the mechanism.

**How to apply:** after finding the flag, grep for what CONSUMES it and read that.

**Happened three times in one session, READ-side vs. WRITE-side** — how an entity is decoded against
a baseline was read carefully; how a baseline is STORED was reasoned about instead of read, producing
three wrong answers in a row. The store side was twelve lines further down the SAME function already
open.

**When a mechanism has two sides (read/write, encode/decode), reading one is half the research** — if
an experiment falsifies a hypothesis, that's the signal to read the OTHER side, not form a second
hypothesis.

### The line you came for is usually below the one that changes its meaning

B276, the sharpest instance so far: a flag two lines above the answer being read was read PAST twice,
in the same session this very memory was cited: `flags |= EXCLUDE_AUTO_INTERPOLATE` changes what the
line below it means. **A flag
being SET is different from a flag existing** — reading a header of `#define`s teaches nothing.

---

## `decoding-a-field-is-not-honouring-it`

Adding a decode is two jobs: reading the value, and reaching EVERY place the engine consults it.
Measured (B221 → B231): `m_nRenderMode` was decoded and routed to ONE consumer correctly while
`C_BaseEntity::ShouldDraw` (`c_baseentity.cpp:1437`) consults it in a second place this project had no
equivalent for — `EF_NODRAW` was already honoured one line away, the render mode was not — eighteen
invisible door movers drawn as solid slabs.

**How to apply:** when a field is newly decoded, grep the SDK for EVERY use, not the one that
motivated the work. A field consulted in four places and honoured in one is three-quarters
unimplemented, and its tests look identical to tests for all four.

Related: [[output-level-assertion-or-it-is-not-done]], [[measure-the-output-not-the-capability]].

---

## `the-cited-line-may-be-the-wrong-branch`

**Before implementing a line quoted from the engine, ask which branch this project's own input
takes.** A memorable function name for corpse posing sits in the branch for the LOCAL player — a
SourceTV recording has no local player, so every SourceTV corpse takes the OTHER branch. A citation
was written for the minority case.

**Tell: a branch keyed on something a demo settles globally** (`IsLocalPlayer`, `IsDormant`) — a
recording answers these once for the whole file, so one branch is taken always. Work out which
before reading further.

---

## `follow-the-call-not-the-value`

**When a function rewrites its own argument, find every CALLER — don't trace the argument forward
from where it's computed.** `STUDIO_REALTIME` (B309) is decided inside `CalcPoseSingle`, which
discards the cycle it was handed; a value discarded and recomputed inside a leaf function is invisible
from tracing forward through the sites that hand it in — grepping for the leaf function's OWN call
sites found all four places that mattered where forward-tracing found three.

**Then check each site is EXECUTED, not merely written** — two of four branches had passing tests
that reached nothing (no fixture data exercised them). A sabotage that reddens nothing is the only
thing that says so.

---

## `ask-which-engine-mechanism-you-are-copying`

A free-camera speed was reasoned from the bug it replaced rather than the engine; the audit then
offered the OBVIOUS-looking reference (`CalcDemoViewOverride`, `view.cpp:153`, the demo-playback
camera) — wrong, because that convar ships disabled and almost nobody uses it. The owner picked the
roaming-spectator numbers instead (`FullObserverMove` → `FullNoClipMove`), four times faster,
correctly.

**The danger: a citation makes a wrong reference look settled** — an uncited number invites "where did
that come from"; a citation closes the question.

**How to apply:** before citing an engine mechanism, ask whether it's the ONLY one for that job and
check its enabling convar's default — a mechanism that ships off is rarely what users experience. When
there are two, which one we copy is a decision to record (D102). Related:
[[name-the-reading-you-picked]], [[a-default-is-not-a-constant]].

---

## `audit-means-verify-what-exists`

Owner, correcting an audit that ranked engine functions by branch count and filed unimplemented ones:
*"i wanted you to make sure everything we have implemented is right, had valve partity, and is not
buggy. Theres far more useful thinks than ragdolls still available, like interp, and out fps being
way way too low."*

**Why:** an unimplemented mechanism is VISIBLE — absent, noticed, filed any time. A WRONG
implementation is INVISIBLE — draws something, green suite, looks finished. Only the second class
needs an audit, and branch-counting can't rank it. The audit's top finding (undrawn corpses) was for a
feature the owner runs disabled.

**How to apply:** rank by what we ALREADY DRAW, ask whether it matches the engine on every branch and
uses the value the engine would use. Performance counts as correctness — TF2 plays at 600+ fps, a gap
that large is a defect we wrote.

Related: [[a-gap-can-be-filed-backwards]], [[valve-parity-is-the-first-principle]],
[[no-hardcoded-controls-ever]]#not-every-setting-needs-a-bind.

---

## `a-bug-is-a-divergence-search-first`

Owner, after a fix shipped and had to be pulled: *"diversions from valve cause issues like this, any
bug we find should be a diversion search for the first like hour... not a rule, just a kinda
standard... none of our issues are not solved problems within the source engine."*

**Failure mode: stopping too early.** A real, cited divergence was found and "fixed" by deleting a
map's gates entirely — the search answered "does the engine draw this entity" and never asked "then
what DOES draw the gate", which sat in the same entity lump as a parented visible prop.

**How to apply:** read until the mechanism is WHOLE, not until a line of C++ agrees with you. A
citation explaining why something's hidden is half an answer; the other half is what the engine shows
instead.

---

## `a-divergence-is-asked-not-documented`

**Any departure from Valve's code is a QUESTION for the owner, not a decision to record.** Owner,
after catching the third one in a session: *"if you diverge i need to be asked."* His framing: *"Valve
can be thought of as god in this project."* A demo is a recording made BY the engine, so any answer of
ours that differs is wrong about the universe it's in — there's no design space to have an opinion in.
Parity is the project's first principle (D89), and every measured win has been a move toward the
engine.

**The failure mode, done three times in one session in the same words:** doc comments saying "a
divergence stated rather than hidden", which sounds like diligence and is the tell — writing it down
felt like discharging it, when only asking discharges it. One of the three reasons given was simply
WRONG (an interface claimed to need cross-module visibility it didn't — one grep of the actual
declaration refuted it).

### The test for an ACCEPTABLE departure, owner's words

*"that departure is completely fine, if we know exactly why valve is doing somethign and exactly why
we dont have to, then its fine."* BOTH halves, EXACTLY — neither "Valve does X for reasons we haven't
read" nor "we don't need X because it seems unnecessary" is enough alone.

**How to apply:** when a departure looks necessary, stop and ask before writing code — and before
asking, read the actual declaration rather than reconstructing it from what the divergence would
need. Most "we cannot do what Valve does" claims are claims about a reconstruction.

Related: [[valve-parity-is-the-first-principle]], [[name-the-reading-you-picked]],
[[never-revert-without-asking]], [[name-the-trade-before-fixing-valve]], [[nothing-is-closed]].

---

## `the-half-you-have-may-be-the-wrong-half`

**When a mechanism spans several engine sites, implementing SOME can be worse than implementing
none.** `BONE_FIXED_ALIGNMENT` is three sites, one mechanism (B308): align a rotation once
(`bone_setup.cpp:470`), then use the `NoAlign` variants in both blends (`:1492`, `:1608`) because the
choice is already settled. This project had only the `NoAlign` slerp and neither of the others —
nothing aligned anywhere, antipodal pairs blended the long way round.

**Tell: a `NoAlign`, `Fast`, `Unchecked` or `Raw` variant** — those names mean "the precondition was
established elsewhere". Reaching for one without finding where is the mistake.

**A flag no content sets does NOT make the branch untestable** — a measurement of ZERO on one input
is a fact about that input, not the experiment; select the input by the CONDITION the engine branches
on, not by picking demos at random.

**Ask a sabotage WHICH tests reddened, not whether the right one did** — a gate-removal sabotage
reddening nothing here meant both tests set the flag on their own subject, so the gate was always
true for everything they exercised.

Same shape as [[the-player-send-table-excludes-the-animation]]. Related:
[[instrument-bugs-outnumber-decoder-bugs]]#an-empty-search-needs-a-control.
