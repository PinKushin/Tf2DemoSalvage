---
name: read-the-back-buffer-for-a-picture-claim
description: A claim about what the viewer drew rests on its own capture, opened and read, never on a copy of the screen
metadata:
  type: feedback
---

**A claim about what the viewer drew is read from the viewer's own capture, and every PNG is opened before it is reported.**

The B708 comparison copied the window from the screen. The owner looked: *"the comparison shots are Windows desktop
screenshots where the viewer can barely be seen"*, so "the kill feed, chat and player strip change through the walk" had
no evidence under it. He then corrected the rule's strength: he does not mind a desktop capture, but it should almost
never be needed here, because the viewer and TF2 have their own screenshot commands and TF2 works in the background.

**How to apply:** `--shot` for a landing, and `TF2VIEW_PLAY_SHOTS="<folder> <tick> ..."` for a frame on the way, playing or
on a skip's frames. If the capture path cannot reach the frame in question, extend it. A read-back costs its frame, so a run
timed for a walk's duration lists no shots.
