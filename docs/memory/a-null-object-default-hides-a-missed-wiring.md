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
