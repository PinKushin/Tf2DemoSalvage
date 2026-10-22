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

## Result

## Verdict
