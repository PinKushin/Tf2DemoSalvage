---
name: split-a-composite-by-fitting-its-alpha
description: "When a golden over a translucent panel goes red, shoot HUD on and off at both builds and fit a single 1-a per pixel: a residual at rounding means only the world moved, and a lock image can record a bug (B688)."
metadata:
  type: feedback
---

B688's three HUD locks went red by up to 26 of 255. Reading cell means could not say whether the HUD or the world moved, as every cell mixes both.

**The instrument:** the old commit and the head, each with the HUD on and with `+cl_drawhud 0`. A pixel is `a·H + (1-a)·W`, so an unmoved HUD gives `dC = (1-a)·dW` with one `1-a` in 0..1 for all three channels. Fit it per pixel and read the residual: at most 3 of 255 there, which is rounding.

**Three controls, because each of these lies alone:** the old build must reproduce the old image to the byte, or the inputs moved rather than the code; the switch must change the box at both builds, since the viewer ignores what it does not know; a repeat shot must match (B677). Then `git bisect run` on one cell of the no-HUD world names the commit.

**Why:** the first bad commit was B636's fix, the world falling to 0.68 in linear light, and three of four elements came nearer TF2. The lock had recorded a bug. A regression lock says only that something moved; regenerating it blind would have been right here by luck, and reading the fix first is what makes it right.

**How to apply:** the viewer writes a golden itself with `--shot <path>`, since the shell hook refuses a copy into a real path; it leaves a `.png.json` sidecar beside it, which is not staged.
