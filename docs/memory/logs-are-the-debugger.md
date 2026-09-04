---
name: logs-are-the-debugger
description: No debugger here, so logs must report state and decisions — what a log must say, what a wrong log costs, and why only the running app can prove one still exists; plus an optional dependency's null-object default silencing a forgotten wiring, a background launch's "completed" notification describing the wrapper rather than the app, and when to close a viewer you booted yourself.
metadata:
  type: feedback
---

There is no debugger in this environment. Logs are the only way to watch a variable, so they have
to carry **what the code decided and what it was working with**, not only what went wrong.

**Three memories were merged into this one on 2026-08-27** — `log-what-is-about-to-be-drawn`,
`a-log-must-name-what-it-measured` and `a-log-level-regression-is-invisible-to-unit-tests`. Their
headings are kept below.

**Why:** the owner said it directly — *"you don't have or are simply not using a debugger so logs are
the only way you can watch variables and actually get the information I could get from a debugger."*
It was said after watching an hour go into finding that 42 of 189 materials on cp_process declare
`$envmap`, which the renderer does not implement. Nothing was logged the whole time, because
nothing *failed*: every material resolved, every texture decoded, and a control point drew as a
black disc in silence. The fix was one line of startup log that states what the map asked for, and
its first run also named `$vertexcolor`/`$vertexalpha` on 55 materials — a bigger gap nobody had
suspected, sitting in VMTs that had already been read aloud and not noticed.

## Every subsystem's log states FOUR things

This is the default shape rather than something to reach for. The owner had to say it out loud on
2026-08-16 — *"you have to know whats being spat out, whats needed, what we have, and what we need,
all need logs"* — after watching a prop-lighting fix land with failure-only logging. That is basic
reverse engineering and it should not have needed saying; the rule was already written on this page
and was not applied.

| Category | The question it answers |
|---|---|
| **ASKED FOR** | what the file wants — placements, materials, parameters declared |
| **HAVE** | what was found and read successfully |
| **PRODUCED** | what actually came out the far end — triangles, textures bound, values decoded |
| **MISSING** | what is absent, unimplemented, or REFUSED, each kind counted apart |

The fourth splits further, and conflating its kinds is its own bug: "the compiler never made this"
and "it exists and we would not use it" are unrelated events. `PropModels` returned one `null` for
both, so four refused vertex-lighting files sat inside an ordinary-looking "without baked lighting"
total while B83 spent four hypotheses on the props they belonged to.

When something cannot be explained, add the log before adding the hypothesis. Prefer one line
stating a whole picture ("48 unimplemented parameters across 189 materials: …") over a line per
event, which is unreadable at map scale — but name the individual items for the MISSING category,
because a count says something is wrong and a name says which object to go and look at.

## READ the logs already being written, before adding more

The viewmodel spent four rounds of new instrumentation being invisible while the renderer printed, on
every frame:

```
WARN [render] a model was posed but the renderer has no geometry for it
```

with a comment above it in the source reading "the renderer's copy of the packed set is older than
the caller's, which draws nothing and reports nothing" — a description of the exact bug, written
before it happened. A past session had anticipated the failure, logged it, explained it, and nobody
looked. **Diagnosis starts by reading the existing output, not by writing new output**; a log added
in preference to one already there also costs the time it takes to write.

---

## `log-what-is-about-to-be-drawn` — and run the app before the suite

**Run the app before running the suite.** The corpus suite takes minutes and the viewer suite more;
a launch takes fifteen seconds and has caught defects the suite could not. Stated 2026-08-13 after
the suite twice failed to show what a single launch showed immediately.

**And that only works if the log says what is about to be drawn.** The viewer logged map, asset
and render counts but nothing about the scene, so "every player is grey" was invisible in the log
and had to be noticed by eye. One line fixes it:

```
roster: 6 red, 6 blu, 1 watching, 0 unknown, 12 of 13 with a class
12 players drawn at the midpoint of the demo
```

A team colour that never arrives reads as `0 red, 0 blu` the moment a file opens.

Counts of what the code is about to draw are the cheapest possible instrument, they cost nothing per
frame when written once per load, and they turn a class of defect from "something looks wrong, go and
investigate" into a number that is obviously wrong on sight. **When adding anything that draws, log
its composition once per load** — how many of each kind, how many skipped, how many unknown. Prefer a
launch for the first check and the suite for the guarantee. See [[ui-tests-run-every-time]].

---

## `a-log-must-name-what-it-measured` — a wrong log is worse than none

A log line is an instrument. **One that measures the wrong quantity gets trusted exactly as much as
one that measures the right quantity**, so it does not merely fail to help — it misdirects, and it
does so with authority.

The owner put it as the cost of overlogging — *"logs that are measuring the wrong thing being used as
the measure for what you want to measure"*. It has now happened repeatedly in one project:

- `"material not found; tried …"` fired when the VMT resolved fine and its TEXTURE did not. Reading
  it literally sent an investigation into path joining and archive mounting, both of which were
  correct.
- `"baked frame 0 of 1"` printed for every skinned player however it was moving. True, and about a
  quantity nobody wanted.
- `"skinning … over 2 animations"` printed the local animation count while the number beside it was
  computed from 469 merged sequences.
- `"fastest 1990 units a second"` was the probe's own 2000-unit filter being hit, not a speed.
- An extents line said `ON ITS SIDE` for models that were correct, because it kept its wording when
  a second kind of model arrived.
- A seam probe indexed baked frames a skinned model does not have and **crashed the viewer** — a
  diagnostic taking down the thing it was meant to explain.

**Name the quantity in the message, and say which case you are in when one line serves two.** Prefer
"VMT missing" and "VMT found, texture missing" over one "not found". When a second kind of subject
starts flowing through a log, re-read what its words now claim. And a log that can be wrong about its
subject is worse than no log, because the absence of a line invites a measurement while a wrong line
ends one.

---

## `a-log-level-regression-is-invisible-to-unit-tests`

**Change `LogDebug` to `LogTrace` and the unit suite does not notice.** Measured 2026-08-26 on
`FrameReporter`: 8/8 green, while the running viewer stopped writing its per-second frame line
entirely.

A test double like `RecordingLogger` implements `IsEnabled` as `true` for every level and records
`(Level, Message)` pairs. Assertions are written as `log.Count("frames a second")`, which matches on
the message. The level is captured and never asserted, so it is free to change. The real application
is the opposite: it runs at a configured minimum level — here `+developer 1`, which maps to `Debug` —
and anything below it is discarded before the message is formatted. **So the level is the ONLY thing
that decides whether the line exists, and it is the one thing the unit test does not look at.**

**What makes this worth keeping is what it costs.** The line lost this way was the instrument this
page describes — the one B191 (a log line taking a machine-wide mutex) and B163 (freezes with no
frame-rate drop) were both found with. Losing a diagnostic is silent by construction: nothing fails,
the log is just quieter, and the next investigation starts without it.

**For any log line that is an INSTRUMENT rather than a note, assert it at the third level** — the
real application, launched, with its real log configuration. That is the only place the level is
real. `WiringUiTests` is where those live in this repo.

**The related trap, same session:** deleting a call outright often will NOT compile, because the
arguments the call site builds become orphaned and the analyzers report them (S1144 unused method,
S4487 unread field). That is a genuine structural guard — but it only covers the crudest regression,
and reaching for it as reassurance is how the subtler one survives.

---

## `a-null-object-default-hides-a-missed-wiring`

Optional dependencies with null-object defaults — `ILogger? log = null` falling back to
`NullLogger.Instance` — are the idiomatic way to keep tests convenient. They are also a way to make
a forgotten argument produce **nothing at all**, with a green suite.

**Measured, 2026-08-24 (D83).** After converting 193 log call sites to injected `ILogger`, the
viewer logged **13 `assets` lines and zero warnings**, against **215 and 16** before. Every test
passed. The whole gate — 3,231 across eight projects — was green.

`MainForm` was calling `MapAssets.Load`, `MapWorldBuilder.Build` and `new EntityModelSet(...)`
without passing its `ILoggerFactory`. Each parameter was optional, so each silently took the null
logger and threw its output away. Nothing was broken; nothing was reported.

**Why no test could catch it.** The tests construct those types directly and pass no factory ON
PURPOSE — they want geometry, not commentary. So the exact call shape that was wrong in production
is the shape every test deliberately uses. A unit test proves the component logs when handed a
logger; it says nothing about whether production hands it one.

**How it was found:** launching the viewer and comparing category counts against a log from before
the change. `assets` 215, `map` 58, `config` 16, `demo` 8 — all matched once the wiring was fixed.

**How to apply:** when a dependency is optional for tests and required in production, the production
call site is the thing to verify, and only the real artefact can verify it — see
[[output-level-assertion-or-it-is-not-done]] and [[measure-the-output-not-the-capability]]. Prefer a
required parameter where every production caller genuinely has the dependency; where optional is
right, comment the production call site saying that omitting it is silent, and check the output
once. Related: [[one-place-or-it-drifts]].

**It happened again on 2026-08-29, and the second instance sharpens the rule.**
`PropModels.Load` took `ILogger? props = null` with a comment saying *"most callers of this are
tests that want geometry, not commentary"*. **There was exactly ONE caller in the whole repository**
— `MapAssets` — and it passed nothing. So the static-prop path had been mute since it was written:
four categories of summary, every refused lighting file by name, and both warnings that name a model
whose mesh will draw in the missing-material chequer.

**Count the callers before writing the comment.** "Most callers are tests" was a guess about a
population of one, and it read as a considered trade for a year. An optional parameter with a single
caller is not a convenience — it is an unwired sink with a rationale attached.

**And the log looked populated, which is why a grep did not find it.** The same `props` area carried
125 `pairing` lines — exactly the count of ENTITY models, a different path handed a real logger
twenty lines away in the same method. "Did that subsystem say anything" answers yes while half of it
is silent, so the question has to be "did THIS call site's lines arrive", which means asserting on a
line only it can produce.

**Cost:** four hypotheses on B229, one of them — *"both of `Register`'s warnings fired zero times"* —
read as evidence about the geometry when it was evidence about the sink. See
[[instrument-bugs-outnumber-decoder-bugs]].

---

## `a-launch-notification-is-not-an-exit`

**Launching the viewer through `run-exclusive.ps1` as a background task reports "completed" while the
application is still running.** The notification describes the wrapper, not the app. Measured twice on
2026-08-20: the task reported completion, and `Get-Process -Name tf2demoview` showed the process alive
minutes later, once for eighteen minutes with the owner using it.

**The compounding half is worse.** Reading the viewer's log after that notification shows a file that
stops mid-load — because the app is still writing it. One such log was 860 lines when read and **79 MB**
by the time the session ended. I concluded "it exited during load, you saw nothing", and the owner had
in fact been looking at it and taking screenshots the whole time. The owner corrected it: "i dont think
the earlier run exited", "there was a app up on my pc".

**Why:** a truncated log and a crashed process produce identical evidence at one instant. The
difference is only visible over time, or by asking the operating system.

**How to apply:** to know whether a launched application is still running, ask for the process —
`Get-Process -Name tf2demoview` — never the task notification and never the log's last line. If the
log must be the instrument, read it twice and compare, because growth is the signal. The same shape as
[[instrument-bugs-outnumber-decoder-bugs]]: a single reading of a moving quantity is not a measurement of it.

And when reporting to a person who is sitting in front of the machine, remember they can see the screen
and you cannot — [[instrument-bugs-outnumber-decoder-bugs]]. Saying "it exited, you saw nothing" to
somebody looking at the running window spends credibility that the actual findings then need.

---

## `close-what-you-launched`

**Close a viewer you launched on your own initiative, once it has answered whatever you launched it
for.** Owner, 2026-08-21: *"if you boot if yourself shut it down when your done"*. Nothing breaks if
you leave it — it does hold the exclusive lock and lock the build DLLs, so the next build fails with
`MSB3027 … The file is locked by: "tf2demoview"`, which names a copy step rather than the cause.

**When the owner asked for the launch, leave it running**, and if it then disappears they closed it.
That is ordinary and needs no comment. The owner's framing: the distinction *"only matters because
you would get confused at me shutting it down myself"*.

**An exit neither of you asked for is worth looking at.** *"it is a signal if it closes on you, as a
crash, when you or I didnt tell it to. Some crashes dont actually full crash and they just exit a
program, those can be a pita to debug."* The tail of `viewer-*.log` in
`%LOCALAPPDATA%\Tf2DemoSalvage\` is the cheapest first look — a tidy ending versus one that stops
mid-sentence. Not common, on the owner's read, given the analyzers this project runs.

**On how much of this to write down, which the owner raised directly:**

> *"getting all the nuance of situations can be a pita to try to write down, you can basically never
> write down every single scenario a 'rule' will ever be put into, so the best thing to do IMO, is to
> basically always hedge, id rather understate things than overstate them most of the time"*

So this entry is deliberately loose, and that is the standing preference rather than a property of
this one note. **Prefer the understated version.** A rule written as an absolute gets applied
confidently to the case it was never meant for, and the confidence is the damage — the reader cannot
tell an inferred edge from a stated one. This entry has already been rewritten three times in an
hour, each time because it claimed more than the owner meant.

Related, and the opposite error: the entry above on a launch notification not being an exit — a
background task reporting "completed" describes the wrapper, not the app. `Get-Process` before
concluding either way.

---

Related: [[measure-the-output-not-the-capability]] is the same failure seen from the reporting side,
[[instrument-bugs-outnumber-decoder-bugs]] is why the log itself needs checking before it is
believed, and [[output-level-assertion-or-it-is-not-done]] is where a log line gets proved to still
exist.
