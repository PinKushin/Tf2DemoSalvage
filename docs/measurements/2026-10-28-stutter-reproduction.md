# Reproducing a user's POV demo stutter in real TF2, setting by setting

## Question

A Discord user's POV demo `spiked-pl_pier-stutter.dem` stutters in TF2's demo playback but did not stutter live. Finding 106
and `2026-10-27-camera-path-pacing.md` show why in the bytes: the `dem_packet` stamps step 0/1/2 (59% regular), while the
server tick inside each packet steps by 1. The stamp is the client's wall clock at write time, rounded to a tick
(`GetRecordingTick`, read from the disassembly). The owner: *"running his config would reproduce it ourselves"*. Which of
the user's network settings, if any, produce the uneven stamps?

## What the user's demo says about its own server (before any recording)

*Read from the demo's signon* (`Tf2DemoSalvage.Cli -t`):

- The header names the server `169.254.215.101:49368`.
- The first `svc_print` is the banner `Team Fortress / Map: pl_pier / Players: 23 / 32 / Build: 11138802 / Server Number: 23`.
- The `svc_setconvar` block carries `mp_tournament 1`, `mp_tournament_readymode 1`, `mp_tournament_readymode_min 0` and
  `mp_forceautoteam 1`, plus `steamworks_sessionid_server`.

Those four values are exactly what TF2's own `tf/cfg/server_casual.cfg` sets on top of `server_matchmaking_base.cfg`
(*read from the game's shipped data*). So this was a **Valve casual matchmaking server**, not a local VM or a LAN box. The
user's launch line also has `-enablefakeip`. Valve's casual servers are reached through the Steam Datagram Relay, and
the relay's FakeIPs come from a reserved IPv4 range that is not routed on the internet. That this range is 169.254/16 is
*recalled, not re-read*: the Steamworks page found says only "a reserved address space". So the path was most likely
internet, then relay, then a Valve server. Loopback has none of that.

*Arithmetic*: the demo is 2,593,852 bytes over 2366 packets, so ~1.1 KB a packet and ~71 KB/s at 64.8 packets a second.
That figure includes the signon, so it is an upper bound. The user's `rate` was 131072.

## Hypotheses and predictions (written before the first recorded demo)

The TF2 console's own help text, read 2026-10-28 on the running client:
- `net_chokeloop` "Apply bandwidth choke to loopback packets" (default 0);
- `rate` "Max bytes/sec the host can receive data" (default 80000);
- `net_queued_packet_thread` "Use a high priority thread to send queued packets out instead of sending them each frame"
  (default **1**, so the user's value is stock);
- `net_splitrate` "Number of fragments for a splitpacket that can be sent per frame" (default 1);
- `net_maxcleartime` "Max # of seconds we can wait for next packets to be sent based on rate setting (0 == no limit)";
- `host_limitlocal` "Apply cl_cmdrate and cl_updaterate to loopback connection". The user sets it to 1; it is in their
  autoexec, so it is in variant A.

**The signature to explain**: a stamp step of 0 or 2 with a server step of 1, and `stamp − net_Tick` taking about two
values (1684 and 661 of 2366). A choke would look different. A rate-limited server skips that tick's snapshot, so the
server step would be 2 or more and the offsets would spread.

| | hypothesis | prediction |
|---|---|---|
| H1 | `rate 131072` too low, the server bursts | **Already contradicted by the user's own demo**: 2315 of 2362 server steps are 1, and the bandwidth is at most ~71 KB/s against 131 KB/s. Predict B ≈ A. |
| H2 | `net_chokeloop 1` chokes loopback | The help text confines it to loopback, and the user's path was not loopback. On a listen server it can matter only if the rate binds. Predict C ≈ A; if A chokes, the choke shows as server steps of 2, not as the user's signature. |
| H3 | `net_queued_packet_thread 1` | It is the stock value. Predict D ≈ A. |
| H4 | `cl_smooth`/interp shape the view, not arrival | These are not in the packet path. Predict no effect on stamps; not varied separately. |
| H5 | server side or network path | **Expected cause.** Predict that A on a loopback listen server does **not** reproduce the 59% pattern: A ≈ E, both near the 2013 solo control's 95.6%. |

**An instrument caveat, set before measuring**: the stamp is `round(net_time / interval)`. When packets arrive about
once a tick, the delta-1 share depends on where that arrival phase sits against the rounding boundary. A
recording that starts with arrivals near the boundary can read low with no setting changed. So A is recorded three times
and E twice. A difference between variants smaller than the A-to-A spread is not a result. Each demo also reports the
count of distinct `stamp − net_Tick` values and the stamp steps of 3 or more.

**A second caveat**: an unfocused TF2 sleeps `engine_no_focus_sleep` (50 ms here) and runs at ~37 fps. That is ~27 ms a
frame, nearly two ticks, so it would fake 0/2 steps on a listen server. Pin it to 0 for the session. Then check every
demo's `net_tick` frametime: on a listen server it is the host frame, and a demo whose host frame is not ~15 ms is
invalid.

## Method

The task assumed a dedicated server install, but `F:\tf2ds\windows\steamapps` is empty and the TF2 install ships no
srcds. So every variant is a **listen server** in the running client, `+maxplayers 32`, on pl_pier with bots filling to
23 players. Installing srcds (steamcmd app 232250) is a download. It was not done; it is a decision for the lead.

## Interim, after the first eight recordings (written before the second round)

**The H5 prediction failed.** A on a loopback listen server reproduces the user's signature. Three runs show it: A2 at 69%
delta-1, B1 at 58% and C1 at 62%. Each has stamp steps of 0 and 2 under a server step of 1, and the dominant
`stamp − net_Tick` cluster is split between two adjacent values. So no network path is needed. The jitter that the
stamp's rounding turns into 0/2 steps can be the host's own frame on loopback.

**The validity check was wrong too.** It expected a host frame of ~15 ms. The `net_tick` frametime means are 6.6 to
8.0 ms, which is ~130 to 150 fps rather than 400, with spikes of 140 to 410 ms. Those spikes are the 12 to 29 tick steps.
The corrected criterion is a frame well under a tick, and every recording passes it.

**New prediction, registered before recording again.** A recording's share of non-1 steps follows how its dominant offset
cluster splits between two adjacent values. No setting moves that split. The second round is interleaved E, B, E, B, E:
three more E at stock plus `cl_updaterate`/`cl_cmdrate` 66, and two more B. The verdict rule, fixed now:
- If any E reads at or below ~70%, the client settings are not the cause.
- If every E stays at or above 88% while every B stays at or below 62%, that is a setting effect left unexplained, and it
  will be reported as one.

## Environment

- Client: the owner's live TF2, build 11138802/24, launched once through the tf2 MCP with `+maxplayers 32` and never
  restarted. It ran as a listen server on pl_pier with `tf_bot_quota 23 fill`. `status` showed 1 human and 23 bots.
- The recorder: a spectator in first person on a bot (`spec_mode 4`).
- Each recording is ~44–52 s. Each starts with `disconnect; exec sr_<v>; map pl_pier`, so every recording is a fresh
  connect with its own clock phase.
- Every variant's key cvars were read back after `exec`, on the loaded map, before `record`.
- The desktop lock was held through `run-exclusive.ps1` for both rounds. The probe ran `-Shared` after each round.

The variant cfgs are in `tf/cfg/` (`sr_a.cfg`, `sr_e.cfg`, `sr_server.cfg`, `sr_restore.cfg`). They hold network lines only:

| v | settings |
|---|---|
| A | the user's network lines, plus their `host_limitlocal 1` and `fps_max 400` |
| B | A with `rate 786432` |
| C | A with `net_chokeloop 0` |
| D | A with `net_queued_packet_thread 0` |
| E | the engine defaults (from `help`) for every cvar A sets |
| E66 | E with `cl_updaterate 66` and `cl_cmdrate 66` |

All variants also set `fps_max 400` and `engine_no_focus_sleep 0`. E1 ran at the stock `cl_updaterate 20` and recorded 20
packets a second. Its 3- and 4-tick steps are that rate, so it is not comparable, and E2 to E5 are E66.

## Result

Scored with `camerapath` on the `== subject … demo_retimeview 0` and `net_Tick` lines. Delta-1 is `1x` over (packets − 1).
"Split" is the minority share of the largest pair of adjacent `stamp − net_Tick` values (*arithmetic* on the printed
histogram). A second pair, where it is large, is given after it.

| demo | v | packets/s | delta 0 / 1 / 2 | **delta-1** | server step 1 | split | host frame mean / max ms |
|---|---|---|---|---|---|---|---|
| user | — | 64.80 | 19.6 / 59.4 / 20.1% | **59.4%** | 2315/2362 | 0.28 | (server's) 15.0 / 15.0 |
| sr_a1 | A | 65.33 | 4.6 / 89.4 / 5.8% | **89.4%** | 2813/2876 | 0.00; 0.12 | 6.7 / 173 |
| sr_a2 | A | 64.65 | 14.8 / 69.2 / 15.2% | **69.2%** | 2757/2824 | 0.25; 0.21 | 7.5 / 262 |
| sr_a3 | A | 64.80 | 3.2 / 91.9 / 4.6% | **91.9%** | 3262/3339 | 0.10 | 7.9 / 166 |
| sr_b1 | B | 64.70 | 20.3 / 57.9 / 21.0% | **57.9%** | 2759/2831 | 0.44; 0.43 | 8.0 / 143 |
| sr_b2 | B | 64.44 | 9.0 / 80.5 / 10.2% | **80.5%** | — | 0.10; 0.23 | 7.2 / 399 |
| sr_b3 | B | 64.67 | 5.0 / 88.5 / 6.2% | **88.5%** | — | 0.00; 0.35 | 7.5 / 406 |
| sr_c1 | C | 64.93 | 18.4 / 62.5 / 17.9% | **62.5%** | 2929/2996 | 0.43 | 7.4 / 213 |
| sr_d1 | D | 64.56 | 2.7 / 93.0 / 4.0% | **93.0%** | 3293/3371 | 0.13 | 7.6 / 408 |
| sr_e1 | E (20/s) | 19.86 | steps 3/4 at 20/s | n/a | — | 0.04 | 7.8 / 118 |
| sr_e2 | E66 | 63.04 | 4.9 / 88.3 / 6.1% | **88.3%** | 2918/2990 | 0.02; 0.34 | 6.6 / 414 |
| sr_e3 | E66 | 65.65 | 2.7 / 93.1 / 4.1% | **93.1%** | — | 0.03 | 6.6 / 62 |
| sr_e4 | E66 | 64.30 | 2.3 / 93.2 / 4.1% | **93.2%** | — | ~0 | 8.2 / 133 |
| sr_e5 | E66 | 64.51 | 11.8 / 74.2 / 13.6% | **74.2%** | — | 0.16 | 8.1 / 149 |

"—" in the server-step column means the round-2 run printed only the summary lines. Each count is the sum of the
cross-tab's server-step-1 cells. In round 1, 97.5 to 98.0% of pairs step the server by 1, under stamp steps of 0 and 2
alike.

Spread by variant:

| variant | n | range | mean |
|---|---|---|---|
| A | 3 | 69.2–91.9% | 83.5% |
| B | 3 | 57.9–88.5% | 75.6% |
| C | 1 | 62.5% | — |
| D | 1 | 93.0% | — |
| E66 | 4 | 74.2–93.2% | 87.2% |

- Stamp steps of 3 or more are 0.1 to 1.1% in every demo.
- Every large step (5 to 29 ticks) sits beside a host-frame spike of 100 to 400 ms. The server lost that time, which is
  finding 106's lost-tick case.
- Every demo has a distinct-offset count of 3 to 22. The large counts are the spikes, each leaving one packet at its own
  offset.

## Round 3, pre-registered before recording (after the review of rounds 1–2)

**The weakness in rounds 1–2.** The three lowest readings (57.9, 62.5 and 69.2%) all carry the user's lines. Stock at
66/s never went below 74.2%. A and E66 differ in a dozen cvars at once, so rounds 1–2 cannot say whether the user's lines
*as a whole* make a bad recording likelier.

**The test.** Six more A and six more E66, interleaved A, E, A, E. Pooled with rounds 1–2 that is A n=9 against E66 n=4+6=10.

**The decision rule**, a one-sided Mann-Whitney U on the delta-1 shares:
- If A is lower at p < 0.05, report "the user's lines as a whole make it worse" and leave which line does it unresolved.
- If not, report "no difference shown between the user's lines and stock".
- Either way, both arms producing the signature stands.

## Verdict

**The signature is reproduced, and it is not the user's client config.**

**The signature reproduces with no network path.** On loopback, recordings show stamp steps of 0 and 2 under a regular
server tick, as the user's demo does. B1 reads 57.9% against the user's 59.4%; A2 and C1 come close. *Measured.*

**It varies from recording to recording at fixed settings, and no setting separates the variants.**
- A spans 69 to 92%.
- B, which raises `rate` (the obvious fix for H1), spans 58 to 89% and is no better than A.
- The control E66 spans 74 to 93%.
- The single C and D readings sit inside the A range.
- On the pre-registered rule, no E fell to 70% (the lowest is 74.2%), so the strict "any E ≤ 70%" branch did not fire. The
  other branch, every E ≥ 88% with every B ≤ 62%, is plainly false. So no setting effect is shown.
- Stock settings produced a quarter of their steps off 1 (E5, 25.8%). The user's demo has 40.6%.

**What tracks the share is the offset split** (*measured correlation, 13 demos*). The demos split well between two
adjacent offsets have the most 0/2 steps: B1 at 0.44 has 42%, C1 at 0.43 has 37.5%, A2 at 0.25 has 31% and the user at
0.28 has 41%. The demos with one dominant offset have the fewest: E3 and E4 at about 0 have about 7%, as does A1. That
fits the stamp being `round(net_time / interval)` (finding 106, *read from the disassembly*). Each packet is written
at a near-constant phase inside its tick. When that phase sits near the +0.5 rounding boundary, frame-to-frame jitter
flips the rounding between two neighbouring ticks. One flip makes a step of 0, and the flip back makes a step of 2.
*Interpolated*: why one connect lands near the boundary and another does not is not read. The split is set at connect
time, and nothing in the config chooses it.

Per hypothesis:

| | hypothesis | verdict |
|---|---|---|
| H1 | `rate 131072` causes bursts | **Not reproduced.** The user's server steps are regular and their bandwidth is below the rate. Raising the rate (B) does not improve the share. |
| H2 | `net_chokeloop 1` | **Not reproduced.** C at 62.5% is inside A's range. The cvar is loopback only by its own help text, and the user played on a Valve server. |
| H3 | `net_queued_packet_thread 1` | **Not reproduced.** It is the stock value, and D at 93.0% is inside A's range. |
| H4 | smoothing and interp settings | **Not varied alone.** A and E differ in all of them, and the ranges overlap. Nothing in the stamp path reads them. |
| H5 | the server or network path is needed | **Falsified as stated.** Loopback reproduces it. What remains true: the cause is on no client setting the user chose. It is per-recording timing phase plus jitter, which any path has. A real internet and relay path may add jitter and make a bad split likelier. That is *untested*, because no srcds is installed. |

**What is inferred, not measured:**
- That the user's live path was a Valve casual server over the relay. The banner and convars are measured. The FakeIP
  range is recalled.
- That TF2's own playback stutters on our recordings. Not watched: the stutter in TF2 is the user's report, and that TF2
  pairs views by the stamp is from finding 106's port of `InterpolateViewpoint`.
- The rounding-phase mechanism. It is consistent with all 13 demos, but nobody set the phase on purpose.

Recorded demos (lcor, `tools/corpus/local/`, recorded 2026-10-28 by this measurement on build 11138802, listen server
pl_pier, 23 bots, spectator in-eye), as SHA-256:

```
1126c4fbf19c1602d45b648f57c2968b5a3aa0d0c831df9e9f0575535d966905 sr_a1.dem
40c9080c2391d4b7b5936877eabb3c579b8e245116dab96eac2f4970c158406f sr_a2.dem
398bed9c588cf68eec4977048aecece50bcdd2deef2eeda3b2aa11104d852ab5 sr_a3.dem
4f3e9d29d464a1eea63be00eb1912df10c934fd77c943e0cc46f598c5813f656 sr_b1.dem
80ab5711ff310e5111c1db1bb242f06186c16a2ab6bdcac361dc78f26f9fa5d8 sr_b2.dem
d9f79be94b33f472a7c0035bc085b961488dbc4da5048480cb66cfcf5d3c485e sr_b3.dem
d9351546d026b5b35ad4fad83c541826a6ce943e7e484c9c3794eb7614afef6a sr_c1.dem
9ede14947c57521c3d102833b4a4a21fd0f2ad4e0445b8ebe3ba965ee3820ade sr_d1.dem
095e962a525a21ce68592d8be37a8aee64e01e3802ef3fbb359809e540c7c294 sr_e1.dem
6b9bd0cc9ae06093e58f6e010423fc2c11cdc98433eba17b4688cfd1c89bc5a7 sr_e2.dem
cda931eabea968ff90294e080ff3c33749050b1d689f4cd8b0516fa1d5f3c169 sr_e3.dem
214e13828c67660a99b0c2d226f786335df9b3eb3cbc978204f72d3f46f41b3b sr_e4.dem
bef8643e89dd336cdb74dac015f2898941fe8849bff79c900ffcbeaebd428f60 sr_e5.dem
```

**Restore, read back after `exec sr_restore`** (the client was disconnected):
- Network and client: `rate` 786432, `cl_cmdrate` 66, `cl_updaterate` 66, `cl_interp_ratio` 1, `cl_interp` 0 (shown
  as 0.015152 restricted), `cl_smooth` 0, `cl_smoothtime` 0.01, `cl_localnetworkbackdoor` 1, `cl_pred_optimize` 2.
- `net_*`: `net_chokeloop` 0, `net_compresspackets` 1, `_minsize` 1024, `net_splitpacket_maxrate` 80000,
  `net_maxcleartime` 4.0, `net_maxfragments` 1260, `net_maxroutable` 1260, `net_maxpacketdrop` 5000,
  `net_queued_packet_thread` 1, `net_splitrate` 1.
- Host and frame: `lzma_persistent_buffer` 0, `host_thread_mode` 0, `host_limitlocal` 0, `mat_queue_mode` -1, `fps_max` 0,
  `engine_no_focus_sleep` 50.
- Server: `tf_bot_quota` 0, `mp_autoteambalance` 1, `mp_waitingforplayers_cancel` 0, `mp_disable_respawn_times` 0,
  `tf_bot_quota_mode` normal, `tf_bot_join_after_player` 1, `tf_bot_difficulty` 1, `sv_cheats` 0.

Every value is identical to the read-back taken before the session. The owner's `autoexec.cfg` and `config.cfg` were not
touched.

## Unresolved

- **The user's real path was not reproduced.** Steam relay to a Valve server cannot be reproduced locally. A local
  srcds would test a real UDP socket rather than loopback, but none is installed.
- **What sets the phase at connect** is unread: the client's `net_time` against the host's tick accumulator, and on a remote
  server the clock-correction code.
- **Whether a client frame rate change moves the split** is untested. Nothing here varied `fps_max`, so nothing here
  should be suggested.

## For the Discord user

Your network settings are not what makes this demo stutter, and changing them will not fix it. We recorded the same
kind of demo with your exact network lines and with TF2's stock settings, several times each. Both came out stuttery
on some recordings and smooth on others. Raising `rate` to the maximum did not help either. The same goes for
`net_chokeloop` and `net_queued_packet_thread`: neither made a difference we could measure.

Here is what happens. A POV demo stamps every update it receives with your PC's own clock, rounded to the nearest
server tick. If the updates happen to arrive close to the halfway point between two ticks, small timing wobbles push
some of them onto the same tick and some onto the next-but-one. The game's demo player trusts those stamps, so the
camera speeds up and slows down. Whether a given recording lands near that halfway point is decided when you
connect. It is not decided by your config. Live play does not use those stamps, which is why it felt fine while you
played.

What does not fix it: `rate`, `net_chokeloop`, `net_queued_packet_thread`, `cl_interp`/`cl_smooth`. We did not test any
other setting, so we are not suggesting one. Tf2DemoSalvage's viewer re-times the camera by the server's own tick
(`demo_retimeview 1`, the default) and plays this demo without the jitter.
