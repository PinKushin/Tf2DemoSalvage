# Camera path pacing on a POV demo that stutters in TF2

## Observation

A user reports that `spiked-pl_pier-stutter.dem` stutters when TF2 plays it back. The stutter is in demo playback only; live play
was fine. The demo is a POV recording at protocol 24, build 11138802, on pl_pier, with 2433 ticks at 0.015 s. The coordinator
decompiled it to a trace and found the `dem_packet` tick steps irregular: step 0 x464, 1 x1405, 2 x475, 3 x15, 4 x4, 6 x1 and
11 x1. Only 59% of the steps are 1. The 2013 POV specimen has 95.6% at 1.

## Question

1. Does the viewer's first-person camera path, as `DemoPlayer.InterpolateViewpoint` produces it, show speed unevenness beyond
   what a smooth motion gives?
2. Does that unevenness line up with the irregular packet stamps?
3. Does anything inside each packet carry the true time, so the stamps could be checked against it?

## Hypothesis

The coordinator's working hypothesis, which nobody has read in the engine: each `dem_packet` is a server snapshot. It is
stamped with the recording client's host tick when it arrived, and arrival jitter gives the 0/1/2 pattern. The user's client
has `fps_max 1000`, `cl_updaterate 66` and `net_queued_packet_thread 1`. An earlier idea that the client's frame rate beat
against the tick rate was dropped, because `fps_max` is 1000.

## Method

`tools/Tf2DemoSalvage.Probe/Probes/CameraPathProbe.cs` (D126):

```
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- camerapath <demo> 144
```

The probe steps the camera along the production path.

- It advances a `PlaybackClock` by 1/144 s per frame.
- It passes the raw `Position` to `TimelineEyes.ViewpointAt`, which calls `DemoPlayer.InterpolateViewpoint`. The viewer does
  the same: `PlaybackPresenter.Advance` → `MomentChanged(clock.Position)` → `ShowMoment` → `ShownTick` →
  `CameraPresenter.FirstPersonCamera` → `SpectatorView.Eye`.
- Nothing in the probe recomputes the blend.

What it measures per frame:

- How far the recorded view's origin moved since the last frame, in units, and how far its angles turned (pitch and yaw), in
  degrees.
- Pacing: each frame's speed over the median of the 11 frames around it. On an evenly paced path this ratio is 1 at any speed.
  A frame is judged only when that median is over 0.05 units a frame.
- Snaps: a second `DemoPlayer` runs with `demo_interplimit` and `demo_avellimit` infinite. A frame where the two players answer
  differently is one the limits snapped.
- Ends excluded: frames within 9 ticks of the last packet are not judged. `ParseAheadForInterval` answers false that close to
  `dem_stop`, so the engine plays that tail stepped by design.

It also reads every `dem_packet`'s `net_Tick` with `NetMessageReader`, and reports:

- a cross-tab of stamp step against server-tick step;
- each packet's recorded-origin step over its local median, grouped once by stamp step and once by server step.

This last grouping is the test of whose clock the recorded views follow. Steps of about 0 at stamp step 0 and about 2 at step 2
would mean the views were sampled when the packet arrived. Steps of about 1 across stamp steps, tracking the server step, would
mean the stamps are the uneven part.

### Control

Each control is a synthetic POV demo built with `SyntheticViews`: a straight line at 300 u/s, turning 90 deg/s.

- **(a)** One packet every tick. The instrument must read CV ~0 and no stalls, or it is not believed.
- **(b1)** The demo's own packet rate, with packets evenly spaced in true time and each stamped `floor(time / interval)`. This
  is what the rate alone does against the tick.
- **(b2)** The demo's own stamps, with each packet's view at evenly spaced true times. This is what arrival-stamp jitter alone
  does to a perfectly smooth motion.

Two real controls:

- **(c)** The gcor 2013 POV specimen, `tf2-2013-build1729296-pov-cp_badlands.dem`, a solo and calm recording. It was run from
  the main checkout, because the worktree copy is a 132-byte LFS stub.
- **(d)** `rgl-pug-2026-08-10-pov.dem`, a modern loaded POV match from lcor and the newest modern POV recording there. It has
  141,614 `dem_packet`s with recorded views and a recorder entity.

## Environment

Worktree branch `probe/camera-path-pacing`, based on `06ae76c2`, Release build. CPU only; every run went through
`run-exclusive.ps1 -Shared`. Another agent's shared build and test run held the second slot during the runs. That does not
matter here, because nothing measured is a time.

## Result

The origin pacing column is the CV of speed over the local median, with "frames > 50% off median" as a fraction of the moving
frames. "Stalls" counts zero-move frames whose neighbours moved.

| run | packets/s | stamp steps 0 / 1 / 2 | origin pacing CV | > 50% off | stalls | snaps |
|---|---|---|---|---|---|---|
| (a) line, a packet every tick | 66.67 | 0 / 2398 / 0 | **0.0002** | 0 | 0 | 0 |
| (b1) subject's rate, no jitter | 64.80 | 0 / 2296 / 68 | 0.111 | 23 / 5233 | 0 | 0 |
| (b2) subject's stamps, even true time | 64.78 | 463 / 1405 / 475 | **0.864** | 972 / 5235 (18.6%) | 0 | 0 |
| **subject** spiked pl_pier | 64.80 | 464 / 1405 / 475 | **0.877** | 607 / 2937 (20.7%) | 8 | 11 |
| (b2) on the 2013 stamps | 64.79 | 125 / 7490 / 210 | 0.324 | 329 / 17396 (1.9%) | 0 | 0 |
| (c) 2013 badlands POV | 64.78 | 125 / 7490 / 211 | 0.447 | 320 / 9681 (3.3%) | 0 | 161 |
| (b2) on the rgl stamps | 65.86 | 28420 / 87219 / 22831 | 1.220 | 50434 / 309558 (16%) | 72 | 0 |
| (d) rgl pug POV, modern | 65.86 | 28421 / 87219 / 22831 | 27.9 † | 77353 / 251766 (31%) | 6912 | 842 |

† The rgl CV is dominated by respawns and teleports: the maximum frame step is 4169 units. Its > 50% fraction is the comparable
figure.

**Where the unevenness sits on the subject.** Each frame is classed by the step to the next distinct stamp and by how many
packets share the stamp before it (the "shared" count). The mean speed over the local median, origin, per class:

| class | subject | (b2) |
|---|---|---|
| step 1, stamp not shared | 1.111 (n 1268) | 1.079 |
| step 1, stamp shared with another packet | **2.610** (n 371) | 2.589 |
| step 2, not shared | **0.786** (n 758) | 0.743 |
| step 2, shared | 1.834 (n 431) | 1.874 |
| step 3 or more | 0.97–1.49 | 0.90–1.78 |

**The gaps of 4+ ticks are not where the worst frames are.**
- Gap 502→506: worst ratio 6.2, 3 frames > 50% off.
- Gap 545→549: worst ratio 1.56.
- Gap 712→716: worst ratio 9.8.
- Gaps 2236→2242 and 2245→2249: the player was not moving, so no frames were judged.

The worst ten frames overall all sit at a shared stamp (8 of 10) or a step-2 stamp. Examples: position 716.204 at ratio 9.77,
step 1, shared; 415.278 at ratio 9.22, step 2, shared.

**Angles on the subject are much more even than its origin.** The angle pacing CV is 0.215 and 58 frames are > 50% off. Every
class mean is 0.99–1.04. Control (b2), whose angles move on the same clock as its origin, reads 0.864 for angles.

**`net_Tick` against the stamp on the subject.**
- 2364 packets carry one `net_Tick` and 2 carry none.
- `host_frametime` mean 14.995 ms, minimum 14.950, maximum 15.040; reported stddev 1.032 ms.

```
stamp step x server step (3 = 3 or more): stamp 0 server 1: 456; stamp 0 server 2: 4; stamp 0 server 3: 2;
  stamp 1 server 1: 1393; stamp 1 server 2: 10; stamp 1 server 3: 2; stamp 2 server 1: 457; stamp 2 server 2: 16;
  stamp 2 server 3: 2; stamp 3 server 1: 9; stamp 3 server 2: 7; stamp 3 server 3: 4
stamp - server tick: -79800x1684 -79799x661 -79798x11 -79797x6 -79794x1 -79773x1
recorded origin step / local median, 1288 moving packet steps
  by stamp step: 0: n 264 mean 1.006; 1: n 738 mean 1.015; 2: n 271 mean 1.056; 3: n 15 mean 1.970
  by server step: 1: n 1258 mean 1.007; 2: n 19 mean 1.912; 3: n 8 mean 2.793
```

The same view-step test on the controls:
- **2013:** the server step is 1 for 7732 of 7837 pairs. By stamp step: 1.020 at 0 and 1.364 at 2. By server step: 1.014 at 1
  and 1.978 at 2.
- **rgl:** the server step is 1 for 140,004 of 141,612 pairs. By stamp step: 0.992 at 0 and 1.275 at 2. By server step: 1.219 at
  1 and 1.986 at 2.

## Verdict

1. **Yes, the camera path is uneven, and by about the amount the uneven stamps alone produce.** The origin pacing CV is 0.877
   on the subject against 0.0002 for a regular line (a). A perfectly smooth line given this demo's own stamps (b2) reads 0.864.
   Per class the subject matches (b2) to within a few hundredths: frames after a shared stamp run 2.6x their neighbours, and
   frames in a step-2 span run 0.79x. *Measured, through the production path.* The four 4+ tick gaps are not the cause.
2. **Within each packet, the server's tick is regular and the stamp is not.** The server tick steps by 1 under 456 of 462
   stamp-0 steps and 457 of 475 stamp-2 steps. The recorded origin's step follows the server tick (1.007 per server step 1,
   1.91 per server step 2), not the stamp (1.006 at stamp step 0, 1.056 at 2). *Measured on the demo's own bytes.* So the
   recorded views are server-paced samples carrying the client's uneven arrival stamp, which confirms the coordinator's
   hypothesis from the data. The engine code that writes the stamp is still unread.
3. **The modern loaded control (d) has the same defect, more heavily**: 20% of its steps are stamp 0, against 0 in the
   no-jitter case (b1). The 2013 specimen (c) shows it mildly, 1.6% at stamp 0. Severity tracks the recording client's arrival
   jitter, not the era.

What changes: the stutter is in the data as the engine's own interpolation reads it. Our `DemoPlayer` is a port of
`CDemoPlayer::InterpolateViewpoint`, keyed on the stamp exactly as the engine is. That fits the user's report that TF2's own
playback stutters (*interpolated*: TF2 was not run here). Fixing it would be an addition above Valve's behaviour, not a parity
fix, and that is the owner's call. Nothing was fixed or designed here.

### Hypotheses for re-timing (not tested)

- **Re-time each view sample by the packet's `net_Tick`** rather than its stamp. The data supports it: 2315 of 2362 subject
  pairs (consecutive packets that both carry a `net_Tick`) step by exactly 1, and the recorded origins follow that clock. Two packets carry no `net_Tick` and would need the
  stamp.
- **`host_frametime` is no help for sub-tick time:** it is the server's frame time (14.95–15.04 ms on the subject) and says
  nothing about when the client received the packet.
- **The `democmdinfo` carries no time.** `dem_usercmd` blocks were not read for this measurement.

## Unresolved

- **Angles pace far more evenly than the origin** on the subject: CV 0.215, every class at 0.99–1.04. That would fit the angles
  being the client's own view at the moment the packet was written, sampled at high fps, while the origin comes from the
  snapshot. That reading is not established, and re-timing the origin by `net_Tick` would not obviously suit the angles.
- **Which engine code writes the `dem_packet` stamp** is unread (the `CDemoRecorder` write path, and whether the stamp is
  `host_tickcount` at arrival).
- **Whether TF2 itself shows this stutter** on this demo needs TF2 run on it. That would be a desktop workload, so the
  machine-wide lock.
- **`dem_usercmd` timing fields** were not checked.
