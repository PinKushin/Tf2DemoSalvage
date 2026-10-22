# Measurement 2026-10-22: the flex spread written only where a flex reaches

## Observation

`2026-10-22-f12-steady-frame.md`, trace T1: `EntityModelSet.FlexFor` is 6.0% of the render thread's steady samples on f12,
and `FaceFlex.InBufferOrder` (the spread of each face's deltas into buffer order, clearing and rewriting every slot of the
model) is 3.4% of it. B513's own output test counts 8,105 of 26,922 soldier slots moved by a laugh.

## Question

Does writing only the slots a flex can reach (`FaceFlex.IntoFlexedSlots`) lower the `flex` column of the moment line on the
same scene, and by how much, with the face output unchanged?

## Hypothesis

The spread is most of the column, and most slots are never reached, so the column falls by a third to a half.

## Prediction (numbers fixed before the run)

- The `flex` column (`place … lookups … flex`, ms per rebuild, mean over the steady moment lines) after / before: 0.40-0.75.
- The face tests' printed counts are unchanged: soldier 8,105 of 26,922 (1.791), rest 398 (0.254), medic 3,785, scout 2,548.

Falsified if the ratio is above 0.85, or any of those counts differs.

## Method

The observation: the `flex` column of the `moment cost` lines, which times `FlexFor` alone at its call (`PlaceFlexTicks`),
so it is not diluted by the rest of the frame; the mean frame is reported but cannot resolve a change of this size under the
load seen in the steady-frame record.

Interleaved runs, before (FlexFor calling the full `InBufferOrder`, the one-line inverse edit) and after (`IntoFlexedSlots`),
each built in the same command that runs it: before1, after1, before2, after2. Each run as in the steady-frame record
(`run.ps1`: f12 lcor, `--tick 30000 --first-person --measure 20 +fps_max 0`, 1280x720, load sampled before and after).
The column is averaged over the moment lines after the first two (the opening seconds are tier-0 code).

Output: `dotnet test tests/Tf2DemoSalvage.Rendering.Tests --filter FullyQualifiedName~Face --logger console;verbosity=detailed`
with the after code, its printed counts against the figures recorded in B513 for the full walk.

## Control

- The two before runs must agree within 25% on the column, or the pair is void and repeated (the column is small and noisy).
- `FlexedInstances`-shaped sanity: the `moment cost` line's `built` and `posed` counts must be within 20% between before and
  after runs, or the scenes differ and the comparison is void.
- The output test was shown to see the face (B513: sabotages reddened it); its counts are exact, so any change in the
  spread's output shows there.

## Environment

- Same machine and scene as the steady-frame record; Debug viewer at `754cc2dd` plus the fix (uncommitted at run time,
  committed with this Result), toggled by the one-line inverse edit in `FlexFor` and rebuilt in each run's own command.
- Load before / after: before1 32/12%, after1 13/12%, before2 22/11%, after2 20/12% (last line of each raw file).

## Result

Raw: `2026-10-22-f12-steady-frame/F-before1.txt`, `F-after1.txt`, `F-before2.txt`, `F-after2.txt` (`flex.ps1` and
`sum.ps1` over them):

| run | moment lines | flex | place | pose | built | posed | steady frame |
|---|---:|---:|---:|---:|---:|---:|---:|
| before1 | 9 | 0.832 | 1.244 | 4.722 | 33.3 | 32.3 | 13.68 ms |
| after1 | 10 | 0.499 | 0.950 | 4.400 | 32.8 | 32.0 | 13.66 ms |
| before2 | 9 | 0.863 | 1.322 | 4.800 | 32.9 | 32.2 | 14.16 ms |
| after2 | 10 | 0.495 | 0.930 | 4.390 | 32.6 | 31.8 | 13.42 ms |

Ratio after / before on the `flex` column: 0.497 / 0.848 = **0.59**. Controls: the before pair agrees within 4%; built and
posed within 2% across all four.

Output (`F-face-tests.txt`, 87 of 87 passed): soldier 8,105 of 26,922 (1.791), rest 398 (0.254); medic 3,785 of 24,864;
scout 2,548. **Deviation:** this output run was taken before this record was committed (it is a test, not a measuring
command, and was run as the change's check); its counts were compared to B513's afterwards.

## Verdict

**Confirmed: the `flex` column fell to 0.59 of itself (prediction 0.40-0.75), about 0.35 ms per rebuild, with every face
count unchanged.** The steady frame moved 13.68-14.16 → 13.42-13.66 ms, inside the run-to-run noise, so no frame-rate gain
is claimed from it.
