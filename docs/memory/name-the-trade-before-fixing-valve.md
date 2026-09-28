---
name: name-the-trade-before-fixing-valve
description: An apparent defect in expert code is usually a trade whose other side is invisible at the site; name it before changing anything of Valve's.
metadata:
  type: feedback
---

**Before changing anything of Valve's, name what it's trading against. If you can't name it, you
don't understand it well enough to change it.**

Owner's analogy: *"if you were to just randomly come across quakes fast inverse square root function,
you would immediately notice it isnt a perfect approximation and probably call it a bug, try to fix
it, but that would be wrong and bad to do... im sure theres a bunch of that in valves code."* Every
local signal on `0x5f3759df` says defect — a magic constant, a truncated Newton iteration — and the
thing it buys (a reciprocal square root per vertex per frame) appears nowhere in the function.

Valve hires extremely well; TF2's rough edges are ACCRETION (features bolted beside old ones), which
looks different from a bad decision (D46).

**How to apply:**
- Reproducing something correct costs nothing; "fixing" it costs a defect plus hours to find it
  again.
- When Valve's value misbehaves, suspect our variables first — a decal bias was declared wrong twice
  because our depth buffer was the wrong format, both times.
- Things here that looked wrong and weren't: an enum instead of a float, a bias in buffer units not
  world distance, an overlay's face list including 45° faces (B134), a packed field (B135).
- If it still looks wrong after the trade is sought and not found, write it down rather than change
  it — a wrong conclusion kept with what killed it is worth more than a silent "correction".

## The one qualification: the trade may have been against a platform that is gone

Owner: *"some of the optimizations may be dx 9 only or earlier, and rely on bugs which existed then
but dont exist now, but we will find those when they cause issues with the dx11 rendering."* A
faithful transcription can misbehave on DX11 while the reasoning was sound — the fix is reproducing
the INTENT, not the mechanism. Already met: the decal bias constant (D3D9's `D3DRS_DEPTHBIAS` is a
float added to depth; D3D11's is an integer scaled by the buffer format — the number can't mean the
same thing in both, D48).

**Console paths need no weighing — skip them.** Owner: *"for all intents we can ignore tf2 on
console, its not even current."* Read the PC branch, ignore `_X360`/`_PS3` blocks entirely — the
mistake to avoid is treating a console path as evidence of what Valve does on PC.

## The point that remark was actually making: effects built out of hardware quirks

Owner clarified: he meant tricks like Super Mario Bros. 3's non-scrolling status bar — a mid-frame
scroll-register change timed to a scanline, inexplicable from the code alone since the constraint it
answers (a hardware limitation) isn't written down beside it. **When something in the engine looks
arbitrary and precise at once, the hypothesis is a trick, not a mistake.**

Related: [[nothing-is-closed]], [[a-filed-design-choice-may-not-be-one]].
