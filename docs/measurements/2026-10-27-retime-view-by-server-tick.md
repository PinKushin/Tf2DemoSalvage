# Retiming the first-person origin by the packet's server tick (`demo_retimeview`, D238)

## Observation

`docs/measurements/2026-10-27-camera-path-pacing.md` measured the camera on `spiked-pl_pier-stutter.dem`. Its origin pacing CV
was 0.877, and 20.7% of moving frames were more than 50% off their neighbours. A smooth line given the same stamps reads
0.864. So the unevenness is the `dem_packet` stamp, which is the client's arrival time (finding 106). The `net_Tick` inside
each packet steps by one, and the recorded origins follow it. The owner approved a fix above Valve, 2026-10-27: *"we would
fix it above valve, the information seems to be there, just need to be properly spread out"*.

## Question

If the recorded origin is interpolated on each packet's server tick rather than its stamp, does the camera's pacing fall to
what the packet rate alone gives? And does any real POV control get worse?

## Hypothesis

The origin follows the server's clock and the view angles follow the stamp's, because the recorder copies them from
`cl.viewangles` (`engine.dll` `0x1800748b0`). Pairing the origin alone by the server tick should collapse its pacing CV
toward a smooth line's. It should leave the angles exactly as they were. No prediction was written before the runs. This
file was written after them.

## Method

`tools/Tf2DemoSalvage.Probe/Probes/CameraPathProbe.cs`, through the production `DemoPlayer`. Each frame, both players get the
same `demo_retimeview`, so a difference between them is a snap and never the setting:

```
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- camerapath <demo> 144
```

Each subject runs twice, with `demo_retimeview 0` and `1`. The probe also reports four other things:
- `stamp - net_Tick` over the whole demo and by thirds, which shows whether the two clocks drift;
- every component's step per packet over its local median, grouped by stamp step and by server step (origin, view angles,
  local angles);
- where the setting placed each packet, read from `DemoPlayer.ServerTimedTicks`, the array the blend reads (B243);
- the production camera (`SpectatorView.Eye`) with the setting on and off at every frame, split by whether a packet the
  setting placed is near. The corpus test `CorpusRetimeViewTests` asserts the same split on the 2013 specimen.

### Control

- **(a)** A packet every tick: CV 0.0002. The instrument reads a regular line as regular.
- **(b1)** The subject's packet rate without jitter: what the rate alone does.
- **(b2)** The subject's stamps, views at even true times, no `net_Tick`. The setting has nothing to place, so on and off
  must read alike. They do, to every printed digit, on all three subjects. This is the fallback's control.
- **(b3)** A smooth line carrying the subject's own stamps AND its own `net_Tick`s, each view at its server tick, as a
  snapshot is. Off must read the jitter, and on must read the line's own pace. Whatever a real subject keeps beyond b3's
  "on" value is its motion, not its timing.
- **(c)** The 2013 gcor POV, a calm solo recording, read from the main checkout's file.
- **(d)** `rgl-pug-2026-08-10-pov.dem`, a modern loaded POV match from lcor.

## Environment

Worktree branch `fix/retime-view-by-server-tick`, off `83f89db7`, Release build. CPU only, and every run went through
`run-exclusive.ps1 -Shared`. Another agent was timing frames on the machine. Nothing measured here is a time.

## Result

Origin pacing is the CV of speed over the local median. "> 50%" is the share of moving frames more than 50% off that
median. "Stalls" counts zero-move frames between moving ones.

| run | setting | origin CV | > 50% off | stalls | snaps | angle CV |
|---|---|---|---|---|---|---|
| (a) line, a packet every tick | either | 0.0002 | 0 | 0 | 0 | 0.0001 |
| (b1) subject rate, no jitter | either | 0.111 | 23 / 5233 | 0 | 0 | 0.111 |
| (b3) subject's two clocks, smooth line | off | 0.863 | 895 / 5235 | 0 | 0 | 0.870 |
| (b3) subject's two clocks, smooth line | **on** | **0.0071** | **0** | 0 | 0 | 0.870 |
| **subject** spiked pl_pier | off | 0.877 | 607 / 2937 (20.7%) | 8 | 11 | 0.2153 |
| **subject** spiked pl_pier | **on** | **0.246** | **95 / 2942 (3.2%)** | 11 | 24 | 0.2153 |
| (b3) on the 2013 clocks | off | 0.308 | 292 / 17396 | 0 | 0 | 0.308 |
| (b3) on the 2013 clocks | **on** | **0.029** | **9** | 2 | 0 | 0.308 |
| (c) 2013 badlands POV | off | 0.447 | 320 / 9681 (3.3%) | 0 | 161 | 13.0 |
| (c) 2013 badlands POV | **on** | **0.251** | **114 / 9681 (1.2%)** | 2 | 161 | 13.0 |
| (b3) on the rgl clocks | off | 1.219 | 49501 / 309558 | 72 | 0 | 1.212 |
| (b3) on the rgl clocks | **on** | **0.196** | **299** | 61 | 0 | 1.212 |
| (d) rgl pug POV, modern | off | 27.9 † | 77353 / 251766 (30.7%) | 6912 | 842 | 8.88 |
| (d) rgl pug POV, modern | **on** | 19.4 † | **53855 / 252738 (21.3%)** | 9916 | 1005 | 8.10 |

† Dominated by respawns: the largest frame step is 4170 units. The "> 50%" column is the comparable one.

**The camera on vs off, frame by frame (`SpectatorView.Eye`).**
- Subject: 679 packets placed. 978 frames have no placed packet near, and every one is identical. 1498 frames near one
  differ.
- 2013: 147 placed, 15930 identical, 0 different away from them, 565 different near them.
- rgl: 41837 placed, 81773 identical, 0 different away from them.

**Where the setting placed packets (stamp minus placement).**
- Subject: 0 ×1687, 1 ×661, 2 ×11, 3 ×6, 6 ×1.
- 2013: −1 ×50, 0 ×7690, 1 ×95, 2 ×2, 5 ×1.
- rgl: −1 ×21694, 0 ×99777, 1 ×17596, then 2 to 8 in a tail of 2,426.

The rgl delays are split between two values, −12772 (68%) and −12773 (23%). The −1 placements are the earlier group.

**Whose clock the angles follow** (*measured*). Each component's step over its local median, by stamp step 0 / 1 / 2:
- Subject view angles: 0.016 / 1.31 / 3.42.
- rgl view angles: 0.44 / 1.44 / 2.45.
- 2013 view angles: 1.08 / 2.11 / 1.66. Inconclusive: a calm solo recording turns in rare, heavy-tailed bursts, and the
  median is a poor reference for that.

By server step 1 / 2, the subject's angles read 1.51 / 2.79, which does not track the server. The origin tracks the server
on all three: by server step 1 / 2 it is 1.007 / 1.91, 1.014 / 1.98 and 1.22 / 1.99.

**Clock steps on the 2013 specimen.** `stamp - net_Tick` takes the values −2719, −2706, −2678, −2666, −2653, −2641 and −2629.
Each step comes with a stamp gap of about 20 ticks over a server step of about 7 (stamp steps of 4+, by server step:
`2x1 5x1 6x1 7x5`). The server lost ticks there. The first version of the fix kept one usual delay across a step. That put
every packet beyond it 12 ticks from its stamp, the guard fell back to the stamp, and (c) regressed to CV 0.603. A delay
step wider than the parse-ahead now ends a run; the table is after that change.

**Stalls, and where they come from.** Every new stall on the subject sits at a stamp where the recording itself repeats the
origin of the packet before while its neighbours move: 22 such packets, all at a server step of 1. The ON stalls fall at
46.76, 253.70, 413.89, 614.81 … and the recording's repeats are at stamps 47, 255, 415, 615 …. Off, the uneven stamps
smeared those repeats across neighbouring frames. That rgl's 6912 → 9916 stalls are the same thing is *interpolated*: rgl
was not rerun with the repeat count. The other stall source is one frame per lost-time gap, where the reader has not yet
queued the packet after the gap and the origin holds the last placed packet (b3: 2 on 2013, 61 against 72 off on rgl).

**Snaps.** Subject 11 → 24. The extra 13 are at 2236–2242, a 6-tick stamp gap. There, `net_Tick` 82036 sits at stamp 2242
and 82042 at 2244, and the origin between them moves faster than `demo_interplimit`. The engine's own teleport rule
therefore now holds the view across the real interval, where off it smeared one server tick across six. rgl 842 → 1005, not
examined.

## Verdict

Retiming the origin by the server tick removes the arrival-stamp unevenness. On a smooth line carrying each subject's own
two clocks, the CV falls from 0.863 to 0.007, from 0.308 to 0.029, and from 1.22 to 0.20. On the subject itself, frames more
than 50% off fall from 20.7% to 3.2% and the CV from 0.877 to 0.246. That is still above b1's 0.111. b3 shows that what the
subject keeps is its own motion and not its timing. The two real controls improve: (c) from 3.3% to 1.2% and (d) from 30.7% to 21.3% of frames.

Two things got worse and are kept, deliberately:
- Stalls rise, because the server's own repeated origins now land on their own tick.
- Some snaps move, because the engine's teleport limit now sees the true interval.

The angles are unchanged by construction, and their source and the subject's measurement agree that the stamp is their
clock.

**What remains for the 4+ tick gaps.** By server step:
- subject `1x3 2x1 3x1`;
- 2013 `2x1 5x1 6x1 7x5`;
- rgl `1x657 2x33 7x1 20x1`.

Where the server stepped 1, the gap was only in the stamp and is gone, unless it exceeds the 8-tick guard. Where the server
also stepped 2 or more, it is a real snapshot gap or lost server time. That stays a linear span, and the camera slows across
it because the server did. rgl's 657 single-step gaps are the bulk of its remaining improvement. Its residual 21% is not
explained here. b3 on rgl's clocks keeps only 299 frames off, so the rest is in the recording.
