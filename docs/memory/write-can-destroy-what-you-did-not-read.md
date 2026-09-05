---
name: write-can-destroy-what-you-did-not-read
description: Writing a "new" file over an existing one deleted a complete implementation and ten tests; the tell is "updated" rather than "created", and the count floor is what caught it.
metadata:
  type: feedback
---

**Check whether a file exists before writing it, especially when you are sure it does not.** On
2026-08-20 I wrote `BspCubemaps.cs` and `BspCubemapsTests.cs` believing I was starting a feature. Both
already existed. The write replaced a finished, well-researched implementation and **ten tests** with
a thinner and partly WRONG version of the same thing.

**What was destroyed was better than what replaced it**, which is the part worth dwelling on. The
original resolved `dcubemapsample_t.size` as a CODE — 0 means the default 32, and passing 0 through
`1 << (size - 1)` gives `1 << 31` in C# because the shift count is masked — cited vbsp's actual format
string from `cubemap.cpp:511`, applied its `Q_strlower`, reported through `DecodeLog`, and carried the
history of a stride bug already found and fixed. My replacement derived the path from filenames and
got it wrong: `materials/maps/<map>/c….vtf` where vbsp writes `maps/<map>/c<x>_<y>_<z>`.

**The tell was in the tool's own reply and I read past it**: "has been updated successfully" rather
than "created". A create and an overwrite do not say the same thing.

**What caught it was the test-count floor** — `content: only 592 tests executed, expected at least
598` — while I was ADDING tests. That is exactly the case the exact floors exist for, and the
dangerous moment was reasoning "I added four, so the count moved for my reasons". A floor that drops
while you are adding is never explained by your additions.

**How to apply:**

- Before `Write`, establish whether the path exists. `Read` it, or list the directory. Being certain a
  file is new is not evidence that it is.
- Treat "updated" in a write result as a stop signal unless you meant to overwrite.
- **Search for the feature before building it.** `BspCubemaps` was already complete, so the whole
  premise — that B55 was blocked on reading the lump — was false; the missing half is the renderer.
  The same session had already filed [[a duplicate risk]] for a fixed defect and re-derived a
  measurement that existed. Grep first: the cost is seconds and the alternative is deleting work.
- A count that moves the wrong way is a finding, never an accounting nuisance. See
  [[read-the-trx-total-not-the-console]].

**It happened again on 2026-09-03, and the gate is what caught it.** A new
`ViewmodelAttachmentTests.cs` was written for the viewmodel FOV correction onto a path that already
held `ViewmodelAttachmentTests` for B252's display-flag mask — an unrelated subject. The write
result said **"updated successfully"**, not "created", and that word was read past.

Nothing failed. Every test passed, the build was clean, and the only trace was the SCENE COUNT:
385 expected, **383** executed. Two tests had been deleted and six added.

- **The name collided because the subject sounded the same.** "Viewmodel attachment" is the display
  mask AND the projection correction. `ls` on the directory before writing costs nothing.
- **Exact floors are why this was recoverable.** A floor written as "at least 379" would have
  passed at 383 and the deletion would have shipped.

**Third time, 2026-09-05 — and this one the FLOOR WOULD HAVE PASSED.** `EntityAssemblyRefusalTests.cs`
already held four tests that cut real generated assembly text at a marker and asserted which of three
nested "not closed" messages came back, plus a control round-tripping the uncut text to bytes. A
`Write` believing the path was new replaced all four.

**The count went UP, so every check the two cases above rely on was green.** Nine tests were added in
the same edit, so `Core.Tests` measured **1760** against a floor of **1749** — comfortably over. The
gate would have passed, and the destroyed tests would have shipped.

**What caught it was DELTA arithmetic, not the floor**: passed rose by 5 when 9 tests were added.
1755 + 9 − 4 = 1760, and the 4 is exact. Then `git status` said **`M`**, not `??`.

So the rule gains a step, because "the floor catches it" is now known to be false in the common case
where the same commit adds more than it destroys:

- **Predict the count before running, and subtract.** "I added N, so the total should be exactly
  prior + N." An increase that is smaller than N is a deletion hiding under an addition, and it is
  invisible to any floor.
- **`git status` on the test directory before committing.** `M` on a file you believe you created is
  the whole finding, available in one command and needing no arithmetic at all.
- **Being mid-flow is when it happens.** All three times, the write was a step inside fixing
  something else — a compile error, a rename — where the path had already been "established" earlier
  in the session and the question felt answered. It was answered for the wrong write.
