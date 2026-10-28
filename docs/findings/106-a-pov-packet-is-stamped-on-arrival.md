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

~~Left open: the subject's view angles pace evenly (CV 0.215) where its origin does not, which is not yet explained.~~
Answered below.

**The writer, read** (*read from the disassembly*, x64 `engine.dll`). `CDemoRecorder::RecordPacket` (`0x180073720`) writes
each packet through `0x1800748b0`, and the header tick comes from `GetRecordingTick` (`0x180071f80`). With more than one
client, that is `(int)((float)net_time / interval + 0.5) - m_nStartTick`: the client's wall clock at the moment of writing,
rounded to a tick. `net_time` is the double at `0x180779520`, which `NET_Init` zeroes. The guard is `cl.m_nMaxClients`, the
same global `InterpolateViewpoint` tests for its rollback. So "stamped on arrival" is now read, not interpolated. One part
stays *interpolated*: that `RecordPacket` runs as each packet is processed. Its caller was not read.

The same function fills `democmdinfo_t` from two different places:
- `viewOrigin` is `IPrediction::GetViewOrigin` (slot +0x40), the local player's `GetLocalOrigin` (`prediction.cpp:1820`);
- `localViewAngles` is `GetLocalViewAngles` (+0x60, `pl.v_angle`);
- `viewAngles` is copied from `cl.viewangles` (`0x18053f4e4`), which `IVEngineClient::SetViewAngles` (`0x180070fa0`) writes.

**That answers the angles** (*measured*). The probe now groups the angle steps as it does the origin's. On the subject, the
view-angle step over its local median is 0.016 at a stamp step of 0, 1.31 at 1 and 3.42 at 2. By server step it is 1.51
at a step of 1, 2.79 at 2 and 1.61 at 3 or more, which does not track. So the angles were sampled on the stamp's own clock: the client turned the view
while the packet sat in its queue. Pairing them by the stamp is already right, which is why they pace evenly. The origin is
the snapshot's and follows the server. The two halves of one `democmdinfo_t` run on two clocks.

**The fix, above Valve** (D238). `demo_retimeview 1`, the default, pairs the origin on each packet's `net_Tick`. It is
placed on the stamp's axis by the delay most packets arrive with among the 333 packets either side (about 5 s here, at
65 packets a second; 16 s at `cl_updaterate 20`). The angles stay on the stamp. On
the subject the origin pacing CV falls from 0.877 to 0.246. A synthetic line carrying the subject's own stamps and
`net_Tick`s falls from 0.863 to 0.007. So the jitter is gone, and what the subject keeps is its own motion. The engine's
snap rule had to follow the origin onto the new axis. It measures from the current packet to the pair's end, over the
pair's interval. On the server's axis the pair can start at a packet not yet read, and over one interval that distance
spanned two: rgl's snaps rose from 842 to 1005, and fell to 904 once the interval ran from the current packet. The full table
is in `docs/measurements/2026-10-27-retime-view-by-server-tick.md`.

**A second clock defect, found by the fix** (*measured*). In 2 minutes the 2013 specimen's `stamp - net_Tick` takes seven
values. That is six steps: three of 12 ticks, two of 13 and one of 28. It never steps back. Each time, a stamp gap of about 20 ticks covers a server step of about 7. So
the server lost ticks: it simulated 7 while 20 passed on the wall. A single usual delay over such a step put every
packet on one side 12 ticks off its stamp. The first version then fell back to the stamp there, and the camera regressed
(CV 0.447 to 0.603). So a delay step wider than the parse-ahead now ends a run. Across such a lost-time gap the camera
slows, because the server did, and it holds one frame where the reader has not yet queued the packet after the gap.

**Reproduced in TF2. Stock settings produce the shape, though more mildly** (2026-10-28, *measured*,
`docs/measurements/2026-10-28-stutter-reproduction.md`). The first guess was that the user's network cvars caused it
(`rate 131072`, `net_chokeloop 1`, `net_queued_packet_thread 1`). The prediction written down was that a loopback listen
server would not show it at all. Both were wrong.
- 25 POV recordings on pl_pier with 23 bots show the same 0/2 stamp steps under a regular server tick.
- The delta-1 share ranged 58 to 97%. The user's lines gave 67 to 97% (n 9), and stock at 66 updates a second gave 74
  to 97% (n 10), with no difference by Mann-Whitney. Raising `rate` to the maximum gave 58 to 89%.
- Stock's worst recording had 26% of steps off 1. Only recordings with the user's lines reached the ~40% of the user's
  demo (B1, 42%). Too few reached it to call that an effect, and A and stock also differ in `host_thread_mode`.
- The share loosely follows how evenly a recording's `stamp − net_Tick` splits between two adjacent values. The split
  was picked by hand, and an independent-flip model fits only 2 of 7 checked demos. *Interpolated*: the mechanism is
  the rounding in `GetRecordingTick` meeting a write phase near the half-tick. The phase differs between recordings and
  moves within one at host hitches. What sets it is unread.
- The banner and convars match TF2's shipped `server_casual.cfg` (*measured*). That it was a Valve server reached
  through the relay is *inferred*, and the FakeIP range is recalled.
