# 95. The exposure histogram is seventeen queries, one a frame (B575)

*2026-10-22. Read from published source (`viewpostprocess.cpp`, `screenspace_general.cpp`,
`luminance_compare_ps2x.fxc`), shipped data (`hl2_misc`: `materials/dev/lumcompare.vmt`), measured
(measurements/2026-10-22-exposure-queries.md).*

## What we had, and what it got wrong besides the cost

Finding 92 put `ExposureHistogram.TryRead` at 3.9% of the render thread: the viewer copied the exposure region of each
frame to staging, read the oldest copy three frames later and binned one pixel in sixteen on the processor. B575 filed
that as "counted on the CPU where Valve counts on the GPU". Reading the engine's loop end to end showed the divergence
was wider than the instrument:

- **Seventeen entries, not sixteen.** Under `mat_tonemap_algorithm 1` the loop runs `N_LUMINANCE_RANGES_NEW` = 17
  (`:391`, `:543-545`); the last is 0 to 100000 (`:885-892`), a leftover of algorithm 0's AA scale test. Algorithm 1 never
  reads it (`:620`, `:636`) but it still takes its turn, so each bin is re-counted every seventeen frames, not sixteen.
- **One query a frame** (`MAX_QUERIES_PER_FRAME 1`, `:392`): every bin comes from a different frame, 3 to 19 frames old.
  Our histogram was one whole frame three frames old.
- **Validity.** `ContainsValidData` is false until an entry's first answer (`:405-408`), and the walk from the bright end
  returns -1 the moment it meets an invalid bin (`:636-639`), which makes the target "pretend we're at the target" — the
  last scale (`:683-686`, `:707-710`). The walk stops at the bin it needs, so an unanswered dark bin is never looked at.
- **The goal history is ten FRAMES.** `DoPreBloomTonemapping` runs the target and `SetToneMapScale` every frame
  (`:1614-1622`), from whatever counts the bins hold. Ours pushed only when a read-back arrived.
- **A reset keeps the counts.** `ResetToneMapping` clears the scale and its average (`:1121-1126`); only an algorithm
  change resets the bins (`:856-861`). For a still capture this matters: a goal from counts measured before the reset is
  of a frame drawn at the old scale, so `PostProcessingSettled` (ours) now also waits for every bin to be re-counted
  since the reset and ten frames after.

## The query itself

`dev/lumcompare.vmt` (shipped): `screenspace_general`, `luminance_compare_ps20` over `_rt_FullFrameFB`, `$ALPHATESTED 1`,
`$DISABLE_COLOR_WRITES 1`. The shader writes `step(min, L) * step(L, max)` on the 0.2125/0.7154/0.0721 luminance
(`luminance_compare_ps2x.fxc:34-48`); `screenspace_general.cpp:189-193` enables the alpha test unconditionally (the
`$ALPHATESTED` check is commented out) with `SHADER_ALPHAFUNC_GREATER, 0.0`. So an occlusion query counts exactly the
in-range pixels; without the alpha test every bin would count the whole region — the sabotage that reddened the new
render tests. `mat_tonemapping_occlusion_use_stencil` defaults to 0 (`:49`), so the stencil path is unused. In D3D11 the
alpha test is a `clip`. *Interpolated*: that `OcclusionQuery_GetNumPixelsRendered` does not flush (`shaderapidx9` is
closed); we read with `DONOTFLUSH`.

## What it changed

Measured on f12 (the file above): the scale after a reset differs by at most 0.27%, decaying to a millionth within about
400 ticks, because the first full histogram now takes about twenty frames instead of three; pictures differ no more than
the old build differs from itself. The frame is 0.10-0.19 ms cheaper, about 1% (measurements/2026-10-22-exposure-queries.md) — a quarter of the leaf's sampled share,
another case of finding 92's lesson that a sampled share is not a cost.

**Weak point, kept**: f12's `env_tonemap_controller` pins 0.5-0.7 and the scale settles on 0.7, so steady agreement there
is forced by the clamp. **Sibling, unchecked**: the engine's bloom reuses the histogram's frame copy
(`s_bScreenEffectTextureIsUpdated`, `:1607-1612`); ours copies the frame twice.
