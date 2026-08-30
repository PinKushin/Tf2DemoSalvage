---
name: not-every-setting-needs-a-bind
description: A convar can be config-only; do not make it a ViewerAction just to satisfy the every-action-is-bound test.
metadata:
  type: feedback
---

The viewer has a test — `ConfigConsoleConformanceTests.Unbound_TheShippedDefaults_LeaveNothing
Unreachable` — asserting that the shipped defaults leave **every** `ViewerAction` reachable by some
key. That test is right about actions and says nothing about whether a setting should have BEEN an
action.

**Measured, 2026-08-29 (D123).** Adding `cl_showpos` as a `ViewerAction` with no default key
reddened three tests, and the fix applied was to invent `CTRL+p`. The owner's response:

> *"not every cvar or setting needs a key bind, but that ctrl p works i guess"*

and then the rule itself:

> *"really if its not something valve normally binds a button too we dont NEED the bind, but having
> binds for the debug views is nice and SS's is needed"*

**Why:** a key is scarce and TF2 takes nearly all of them
([[tf2-binds-every-letter-but-o]]), so a binding invented to satisfy a test spends a real resource
on a decision nobody made. And the reasoning runs backwards: the setting was made an action because
that is how settings were done here, then a key was invented because actions must have one.

**How to apply — three tiers, and only the last is a default:**

- **Valve binds it** → bind it, on Valve's key. D101.
- **A debug view** (`mat_wireframe`, `cl_showfps`, `cl_showpos`) → a bind is *nice*. Take a `CTRL`
  combination, which no Source config can name.
- **A screenshot** → *needed*; it is the one action that must be reachable mid-frame.
- **Anything else** → a convar and a menu item are enough. Do not make it a `ViewerAction`.

So ask "is this reachable enough as a convar plus a menu item?" **before** adding the enum member.
Once it is an action the test is correct to demand a key, and by then the wrong question has already
been answered. Related: [[no-hardcoded-controls-ever]], [[a-config-is-a-program]],
[[silence-about-a-missing-feature-is-not-a-preference]].
