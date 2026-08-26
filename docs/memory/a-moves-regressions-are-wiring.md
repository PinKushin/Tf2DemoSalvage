---
name: a-moves-regressions-are-wiring
description: When code moves out of a class, the logic survives and the WIRING breaks — and that is mechanically auditable, unlike the logic.
metadata:
  type: feedback
---

**Moving code does not break the code. It breaks the assignment that used to be implicit.**

Measured across one day of extracting ~1,100 lines out of `MainForm` (B188, B193). Every regression
was the same shape and **not one was a logic error**:

| what moved | what broke | caught by | shipped? |
|---|---|---|---|
| `EnsureWeaponRoles` | the call was dropped; every weapon suffix answered null | an analyzer noticing the method had become unreachable | no |
| `AddViewmodel` | `MomentScene.Viewmodels` was never assigned; **the first-person weapon never drew** | reading the wiring, two commits later | **yes** |
| `ShowMoment`'s upload | `MomentScene.Upload` was assigned NOWHERE; **no entity geometry ever reached the GPU** | the audit below | **yes** |

The viewer suite reported **620/620 green** through all three.

**Why the logic is safe and the wiring is not.** A moved method's body is covered by the tests
written with it, and a compiler catches a broken call. But `new TimelineViewmodels(timeline)` written
INLINE becomes `Viewmodels` written as a property — and a property nobody sets is null, which is a
legal state the guard already handles. The guard was written for "no demo open yet". It cannot tell
that from "nobody wired this".

## The audit, which is mechanical

Enumerate every settable collaborator on the extracted types, then count assignments in the caller:

```bash
grep -rn "public .* { get; set; }" managed/<extracted files>
for p in "_moment.Upload" "_moment.Viewmodels" ...; do
  printf "%-24s %s\n" "$p" "$(grep -c "$p *=" MainForm.cs)"
done
```

**Zero is a regression. One is usually right. Two or three means several lifetimes** (construction,
map load, teardown) and each needs checking separately.

## Three more passes, each of which found something

- **Diff the log STRINGS before and after**, normalising interpolations:
  `git show main:File.cs | grep -oE '"[^"]{12,}"' | sed 's/{[^}]*}/~/g' | sort -u`, and the same over
  the new file plus every file the code moved to. Found a lost `players` column in the slow-moment
  ledger and a lost denominator on a debug line. Most differences are prose inside comments — read
  them, do not count them.
- **Diff the moved BODY against the original**, not its shape. Found `EnsureWeaponRoles` moved
  INSIDE a timer it had been outside of, where its one-off ICE decryption would report as an
  enormous `sampling` spike.
- **Check that a counter which kept its NAME kept its MEANING.** `_samplingTicks` was fed
  `phases.DrawList` for one commit — the draw-list build under a name that means timeline sampling.
  See [[a-log-must-name-what-it-measured]].

## How to apply

- **A null or default collaborator must REPORT itself, once there is work it would have done.** The
  null object stays — a real object beats a null field (D83) — but silence is what let three of these
  through. Guard the report on there being something to do (`Vertices.Count > 0`, `FirstPerson`,
  `players.Count > 0`), or it fires from an idle viewer and stops being read. Write the control test
  for that; my first upload warning fired on an empty scene and a control caught it.
- **Assign a demo's sources in ONE place, where the demo arrives**, not wherever each collaborator
  happens to be constructed. Two of the three were missed because the assignments were scattered.
- **Run the audit at the END of a move, not only when something looks wrong.** The worst of the
  three was invisible: nothing drew, no test failed, and no analyzer fired.

Related: [[a-null-object-default-hides-a-missed-wiring]], [[output-level-assertion-or-it-is-not-done]],
[[three-test-levels-and-the-third-is-missing]], [[a-partial-thin-view-is-worse-than-none]].
