# B685: which term of the draw puts cp_process_f12's far-left pipe over TF2

## Observation

`measurements/2026-11-03-b669-pipe-traces-live.md`: far camera, Vulkan, left pipe upper 67.5 and lower 93.2 in TF2, 74.4 and 101.6 in ours, with the engine's four slots and ours' cube at or under the engine's on every face.

## Question

Which term of `2pipe_straight_64`'s draw holds the excess, and what differs from `industrial_pipe01`, which now agrees?

## Method

Both sides at the same camera and tick, box by box. In TF2, `-vulkan`, `mat_hdr_level 2`, scale 1, bloom off: `mat_phong 0`, `mat_specular 0`, `mat_fullbright 2`, `r_worldlights 0`. In ours, the same switches where they reach a `--shot`, else temporary shader edits, reversed. Each model's VMT read beside it. If that stalls, the constants the engine sets, read with Cheat Engine. Which pixels are which prop is projected from the placement first.

## Control

The concrete box stays 187.0 throughout. `industrial_pipe01`'s box agrees under each switch, or the switch is what differs. A switch that does not move TF2's box did not reach it.

## Environment

Machine and build as in the records above. Ours: branch head at the run.

## Result

## Verdict
