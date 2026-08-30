---
name: a-null-object-default-hides-a-missed-wiring
description: An optional dependency defaulting to a null object turns a forgotten argument into silence, and no test can see it.
metadata:
  type: project
---

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
once. Related: [[logs-are-the-debugger]], [[one-place-or-it-drifts]].

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
[[an-instrument-unread-is-not-an-instrument]].
