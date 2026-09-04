---
name: a-restarted-timer-is-not-a-lifetime
description: The value handed to a timer says nothing about how long it runs — read the think that may restart it, or you implement a plausible cited wrong number.
metadata:
  type: feedback
---

**Finding where a timeout is SET is not finding how long it lasts.** Read the per-frame think as
well, because a timer something restarts has no relationship to the constant it was given.

`cl_ragdoll_fade_time` defaults to 15 (`c_tf_player.cpp:514`) and `CreateTFRagdoll` ends with
`StartFadeOut( cl_ragdoll_fade_time.GetFloat() )` (`:869`). Read that far and "a corpse lasts 15
seconds" is a cited, confident, wrong answer. The think is where it lives:

```cpp
if ( IsRagdollVisible() )
{
    …
    StartFadeOut( cl_ragdoll_fade_time.GetFloat() * 0.33f );
    return;
}
```

`c_tf_player.cpp:1532-1545`. The timer is re-armed every think the corpse is on screen, at **a
third** of the convar. So **a corpse being looked at never fades at all**, and one that has left view
expires five seconds later. Both halves of "15 seconds" are wrong, and the number and the visibility
dependence are exactly the two things that decide what a viewer sees.

**The consequence for design, not just for the number.** A lifetime that depends on visibility is a
CAMERA question and cannot be baked into a timeline computed once — which is what would have been
built from the convar alone, and it would have been wrong in a way no test of the timeline could
catch.

**And the correct-looking alternative was worse, which is the part to remember.** Having decided the
timer was too subtle, the obvious fallback was to draw each corpse for as long as its ENTITY existed
— no invented number, purely what the demo says. Measured, that put **57 bodies on the map at once
against a twelve-player roster**, because the server keeps one ragdoll per player until that player
next dies. "Use what the demo says" is not automatically the conservative choice: the server's
bookkeeping and the client's drawing are different lifetimes, and only one of them is what a viewer
saw.

**The general shape:** a construction-time `Start(x)` is a hypothesis about duration. Grep the field
the timer writes — here `m_fDeathTime`, four uses in the whole file — and read every one before
believing it. Same family as [[read-the-sdk-for-the-whole-mechanism]] and
[[half-a-mechanism-is-not-parity]]; distinct from [[a-default-is-not-a-constant]], which is about the
value being a setting rather than about the clock being restarted.
