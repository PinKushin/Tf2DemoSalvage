# 106. A POV demo's packet tick is the arrival stamp; the server tick inside it is the regular clock

2026-10-27. A user reported that `spiked-pl_pier-stutter.dem` stutters in TF2's demo playback. The coordinator decompiled the
demo and counted its `dem_packet` tick steps. Only 59% of them are 1: 464 are 0, 475 are 2, and a handful are 3 to 11.

**The first idea, and what killed it.** At ~64.8 packets a second, just under the 66.67 Hz tick, the pattern looked like the
recording client's frame rate beating against the tick. The client runs `fps_max 1000`, so that was dropped before any
measurement. Arithmetic also rules it out: a clean beat at that rate gives 68 steps of 2 and no steps of 0 (synthetic control
b1), not 464 zeros.

**What the bytes say** (*measured*, `docs/measurements/2026-10-27-camera-path-pacing.md`, probe `camerapath`). Each packet's
`net_Tick` advances by exactly 1 for 2315 of 2362 consecutive pairs. That holds under stamp steps of 0 (456 of 462) and of 2
(457 of 475) alike. Among moving packets, the step of each recorded view origin, over the median of the steps around it,
follows the server tick: 1.007 for a server step of 1, 1.91 for a step of 2. Grouped by the stamp's step instead, it reads
1.006 at 0 and 1.056 at 2. So the stamp, and only the stamp, is uneven. The views are server-paced samples carrying the
client's arrival tick.

**What it does to the camera** (*measured, through the production `DemoPlayer`*). The origin speed over its local median has a
CV of 0.877 on the subject. A regular line reads 0.0002. A perfectly smooth line given this demo's own stamps reads 0.864, and
the two agree class by class: 2.6x speed after a shared stamp, 0.79x across a step of 2. The 2013 specimen has the same
defect mildly (1.6% stamp-0 steps). A modern loaded POV match has it heavily (20%). The gaps of 4+ ticks were not the cause.

**Why this is Valve's behaviour, not ours.** `CDemoPlayer::InterpolateViewpoint` pairs packets by that stamp, and
`DemoPlayer` is a port of it. So the unevenness is what the engine reads from the file. That the engine writes the stamp as
`host_tickcount` on arrival is *interpolated* (the recorder's write path is unread), and so is the claim that TF2 shows the same
stutter (TF2 was not run on this demo).

Left open: the subject's view angles pace evenly (CV 0.215) where its origin does not, which is not yet explained.
