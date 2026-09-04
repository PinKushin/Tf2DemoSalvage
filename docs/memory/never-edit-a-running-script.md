
## The SOURCE has the same hazard, and it is not the same rule

2026-09-04, an hour after the fragment above. `build/gate.sh` was untouched this time; the SOURCE was
edited while the gate ran, mid-way through threading a parameter through four files. The gate
reached `Tf2DemoSalvage.Rendering` after the first edits landed and before the last, found a tree
that did not compile, and stopped:

```
content: 993 executed, 0 failed (floor 989)
… error CS0103: The name '_tintBases' does not exist in the current context
exit 1
```

**Nine of twelve, and this one at least exits 1** — the gate's own `run` fails when a project will
not build, so it is louder than the byte-offset case. What it is not is a RESULT: the nine that
passed were measured against a tree the other three never saw, so the run says nothing about the
whole.

**The rule that follows is different from the one above, and weaker on purpose.** The script is
untouchable while it runs because a mid-line edit corrupts execution silently. Source is merely
POINTLESS to edit while it runs — the gate measures whatever is on disk when it reaches each
project, so an edit either arrives too late to be tested or splits the tree in half.

So: while a gate is in flight, edit DOCS. Everything else waits for the exit, and the earlier runs in
this session that appeared to work only did so because the edits happened to finish before the gate
reached those projects.
