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

## Hypotheses and predictions (committed as `071b892a`, before the first recorded demo)

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

## Interim, after the first eight recordings (committed as `c724fc61`, before the second round)

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

- Client: the owner's live TF2, build 11138802/24, launched through the tf2 MCP with `+maxplayers 32`. It crashed once,
  during the 22nd `map pl_pier` load in round 3, before E11 recorded. `console.log` ends on a map-load line, and
  `config.cfg` was not rewritten. It was relaunched because no client answered, and E11 is the only recording made on the
  second process. It ran as a listen server on pl_pier with `tf_bot_quota 23 fill`. `status` showed 1 human and 23 bots.
- The recorder: a spectator in first person on a bot (`spec_mode 4`).
- Each recording is ~44–63 s. Each starts with `disconnect; exec sr_<v>; map pl_pier`, so every recording is a fresh
  connect with its own clock phase.
- Every variant's key cvars were read back after `exec`, on the loaded map, before `record`.
- The desktop lock was held through `run-exclusive.ps1` for all three rounds, about 86 minutes in total. The probe ran
  `-Shared` after each round.

The variant cfgs were run from `tf/cfg/`, and copies are kept in
[`2026-10-28-stutter-reproduction/`](2026-10-28-stutter-reproduction/):
- `sr_a.cfg`, the user's network lines;
- `sr_e.cfg`, the engine defaults;
- `sr_server.cfg`, the listen-server bot lines;
- `sr_restore.cfg`, the session's pre-run values.

To reproduce the run, copy them into `tf/cfg/`. They hold cvar lines only, with no password, token or other private value
from the user's autoexec. B, C, D and E66 are `exec sr_a` or `exec sr_e` followed by the one or two cvars in the table:

| v | settings |
|---|---|
| A | the user's network lines, plus their `host_limitlocal 1` and `fps_max 400` |
| B | A with `rate 786432` |
| C | A with `net_chokeloop 0` |
| D | A with `net_queued_packet_thread 0` |
| E | the engine defaults (from `help`) for every cvar A sets |
| E66 | E with `cl_updaterate 66` and `cl_cmdrate 66` |

All variants also set `fps_max 400` and `engine_no_focus_sleep 0`. E1 ran at the stock `cl_updaterate 20` and recorded 20
packets a second. Its 3- and 4-tick steps are that rate, so it is not comparable, and E2 to E11 are E66.

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
| sr_b2 | B | 64.44 | 9.0 / 80.5 / 10.2% | **80.5%** | 2802/2869 | 0.10; 0.23 | 7.2 / 399 |
| sr_b3 | B | 64.67 | 5.0 / 88.5 / 6.2% | **88.5%** | 2982/3048 | 0.00; 0.35 | 7.5 / 406 |
| sr_c1 | C | 64.93 | 18.4 / 62.5 / 17.9% | **62.5%** | 2929/2996 | 0.43 | 7.4 / 213 |
| sr_d1 | D | 64.56 | 2.7 / 93.0 / 4.0% | **93.0%** | 3293/3371 | 0.13 | 7.6 / 408 |
| sr_e1 | E (20/s) | 19.86 | steps 3/4 at 20/s | n/a | — | 0.04 | 7.8 / 118 |
| sr_e2 | E66 | 63.04 | 4.9 / 88.3 / 6.1% | **88.3%** | 2918/2990 | 0.02; 0.34 | 6.6 / 414 |
| sr_e3 | E66 | 65.65 | 2.7 / 93.1 / 4.1% | **93.1%** | 2861/2904 | 0.03 | 6.6 / 62 |
| sr_e4 | E66 | 64.30 | 2.3 / 93.2 / 4.1% | **93.2%** | 2933/3012 | ~0 | 8.2 / 133 |
| sr_e5 | E66 | 64.51 | 11.8 / 74.2 / 13.6% | **74.2%** | 2807/2875 | 0.16 | 8.1 / 149 |
| *round 3* | | | | | | | |
| sr_a4 | A | 65.55 | 15.8 / 67.2 / 16.7% | **67.2%** | 4033/4106 | 0.34 | 6.5 / 177 |
| sr_e6 | E66 | 64.58 | 1.6 / 94.7 / 3.4% | **94.7%** | 2876/2942 | 0.01 | 9.4 / 191 |
| sr_a5 | A | 64.80 | 7.6 / 83.4 / 8.5% | **83.4%** | 2776/2844 | 0.13 | 7.1 / 227 |
| sr_e7 | E66 | 64.17 | 10.8 / 76.0 / 12.8% | **76.0%** | 2852/2932 | 0.28 | 8.2 / 178 |
| sr_a6 | A | 64.87 | 2.8 / 92.9 / 4.0% | **92.9%** | 2894/2963 | 0.15 | 7.0 / 137 |
| sr_e8 | E66 | 63.33 | 8.6 / 80.8 / 9.8% | **80.8%** | 3856/3962 | many clusters † | 10.1 / 461 |
| sr_a7 | A | 64.77 | 3.3 / 91.9 / 4.5% | **91.9%** | 2838/2904 | 0.14 | 7.2 / 215 |
| sr_e9 | E66 | 63.88 | 5.4 / 87.1 / 7.1% | **87.1%** | 3040/3118 | 0.31; 0.33 | 9.3 / 457 |
| sr_a8 | A | 65.59 | 0.5 / 97.4 / 2.0% | **97.4%** | 2859/2919 | 0.01 | 6.7 / 52 |
| sr_e10 | E66 | 64.69 | 0.6 / 96.9 / 2.3% | **96.9%** | 2812/2873 | 0.05 | 7.7 / 234 |
| sr_a9 | A | 64.79 | 2.0 / 94.2 / 3.5% | **94.2%** | 2896/2976 | 0.02 | 7.2 / 217 |
| sr_e11 | E66 | 64.80 | 11.9 / 74.4 / 13.1% | **74.4%** | 2863/2922 | 0.34 | 7.2 / 246 |

† sr_e8's offset walks across 18 values in 62 s rather than sitting in one or two pairs.

Each server-step count is the sum of the cross-tab's server-step-1 cells. The share is 97.3 to 98.5% in every recording,
and so is the user's 98.0%, under stamp steps of 0 and 2 alike.

Spread by variant:

| variant | n | range | mean |
|---|---|---|---|
| A | 9 | 67.2–97.4% | 86.4% |
| B | 3 | 57.9–88.5% | 75.6% |
| C | 1 | 62.5% | — |
| D | 1 | 93.0% | — |
| E66 | 10 | 74.2–96.9% | 85.9% |

- Stamp steps of 3 or more are 0.1 to 1.1% in every demo.
- Every large step (5 to 29 ticks) sits beside a host-frame spike of 100 to 400 ms. The server lost that time, which is
  finding 106's lost-tick case.
- Every demo has a distinct-offset count of 3 to 22. The large counts are the spikes, each leaving one packet at its own
  offset.

## Round 3, pre-registered (committed as `1c199d20`, before any round-3 recording; the round-3 rows above were added after)

**The weakness in rounds 1–2.** The three lowest readings (57.9, 62.5 and 69.2%) all carry the user's lines. Stock at
66/s never went below 74.2%. A and E66 differ in a dozen cvars at once, so rounds 1–2 cannot say whether the user's lines
*as a whole* make a bad recording likelier.

**The test.** Six more A and six more E66, interleaved A, E, A, E. Pooled with rounds 1–2 that is A n=9 against E66 n=4+6=10.

**The decision rule**, a one-sided Mann-Whitney U on the delta-1 shares:
- If A is lower at p < 0.05, report "the user's lines as a whole make it worse" and leave which line does it unresolved.
- If not, report "no difference shown between the user's lines and stock".
- Either way, both arms producing the signature stands.

## Round 3 result

Ranking all 19 delta-1 shares (A n=9, E66 n=10), A's rank sum is 91, so U_A = 46 and U_E = 44, against 45 expected under
no difference. The one-sided p is about 0.5 (*arithmetic*). The means are 86.4% for A and 85.9% for E66. **By the
pre-registered rule: no difference shown between the user's lines and stock.**

A post-hoc observation, not pre-registered: the four lowest of the 24 comparable readings all carry the user's lines
(B1 57.9, C1 62.5, A4 67.2, A2 69.2). The A family (A, B, C and D) is 14 of the 24. The chance that the lowest four are
all from it is C(14,4)/C(24,4) = 0.094. That is suggestive at most, and it is the next thing to test if anyone cares. It
does not reach the rule.

## Verdict

**The signature is reproduced, and stock settings produce it too. No setting tested made a difference that could be
told apart from the run-to-run spread**, though C and D had one recording each. Whether the
user's lines as a whole raise the odds of a bad recording is not shown either way at this n. The pooled A against stock
test found no difference, and the low tail is a post-hoc hint.

**It reproduces with no network path.** On loopback, recordings show stamp steps of 0 and 2 under a regular server
tick, as the user's demo does. B1 reads 57.9% against the user's 59.4%. *Measured.*

**It varies from recording to recording at constant settings, and no single setting separates the variants:**
- A spans 67 to 97% over nine recordings.
- B, which raises `rate` (the obvious remedy under H1), spans 58 to 89% and is no better than A.
- Stock at 66/s (E66) spans 74 to 97% over ten.
- C's single reading, 62.5%, is below every A but inside the overall spread, and at n=1 it is not a result. It also
  points the wrong way for H2: if `net_chokeloop 1` caused the stutter, turning it off should have helped.
- D's single reading, 93.0%, is inside A's range.
- The lowest stock readings have about a quarter of their steps off 1: E5 25.8%, E11 25.6%, E7 24.0%. The user's demo has
  40.6%.

**The share follows the offset split, with exceptions.** This is an *observed pattern*: the split was picked by hand
from each histogram and no coefficient was computed. The demos split well between two adjacent offsets have the most
0/2 steps:

| demo | split | steps off 1 |
|---|---|---|
| B1 | 0.44 | 42% |
| C1 | 0.43 | 37.5% |
| A4 | 0.34 | 33% |
| E11 | 0.34 | 26% |
| user | 0.28 | 41% |

The demos with one dominant offset have the fewest: E3, E4, E6, E10, A8 and A9 have 2.6 to 6.9%. 

**A quantitative check, and where it fails** (*arithmetic*). If each packet fell on either of a pair's two offsets
independently, the expected share of steps off 1 would be 2p(1−p), weighted by the pair's packets:

| demo | expected | observed |
|---|---|---|
| user | 40.1% | 40.6% |
| E7 | ~28% | 24.0% |
| B1 | 48.6% | 42.1% |
| C1 | 44.7% | 37.5% |
| A4 | 26.5% | 32.8% |
| E11 | 45.1% | 25.6% |
| E9 | ~41% | 12.9% |

It fits the user's demo and E7, and it misses E9 and E11 by a factor of 2 to 3. In those two, the flips come in runs:
the phase lingers on one side of the boundary, then the other. So the split bounds the share rather than predicting it,
and the order of the offsets in time matters too. The probe prints the histogram, not the sequence. That was not
examined further.

That fits the stamp being `round(net_time / interval)` (finding 106, *read from the disassembly*). Each packet is
written at a near-constant phase inside its tick. When that phase sits near the +0.5 rounding boundary, frame-to-frame
jitter flips the rounding between two neighbouring ticks. One flip makes a step of 0, and the flip back makes a step
of 2.

**The phase is not fixed for a recording.** It moves at the host hitches: E2, A2, A4, B2, B3, E7 and E9 each have two
or more clusters with different splits, and E8's offset walks across 18 values. So the split varies between
recordings, and within one after a lost-time spike. *Interpolated*: what puts the phase where it is after a connect or a
hitch is not read.

Per hypothesis:

| | hypothesis | verdict |
|---|---|---|
| H1 | `rate 131072` causes bursts | **Not reproduced.** The user's server steps are regular and their bandwidth is below the rate. Raising the rate (B) does not improve the share. |
| H2 | `net_chokeloop 1` | **Not reproduced.** Turning it off (C, n=1) read 62.5%, lower than every A, which is the wrong direction for H2 and within the overall spread. The cvar is loopback only by its own help text, and the user played on a Valve server. |
| H3 | `net_queued_packet_thread 1` | **Not reproduced.** It is the stock value, and D at 93.0% is inside A's range. |
| H4 | smoothing and interp settings | **Not varied alone.** A and E differ in all of them, and the ranges overlap. Nothing in the stamp path reads them. |
| H5 | the server or network path is needed | **Falsified as stated.** Loopback reproduces it with stock settings. The mechanism is the timing phase plus jitter, which any path has. A real internet and relay path may add jitter and make a bad split likelier. That is *untested*, because no srcds is installed. |
| — | the user's lines as a whole | **Unresolved.** In the pooled A against E66 test, U is 46 against 44, so no difference. A post-hoc low-tail hint has p ≈ 0.09. |

**What is inferred, not measured:**
- That the user's live path was a Valve casual server over the relay. The banner and convars are measured. The FakeIP
  range is recalled.
- That TF2's own playback stutters on our recordings. Not watched: the stutter in TF2 is the user's report, and that TF2
  pairs views by the stamp is from finding 106's port of `InterpolateViewpoint`.
- The rounding-phase mechanism. It is consistent with the 25 demos, but nobody set the phase on purpose.

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
3d6f7bf2e44fd9c6adf128e8a62c3659cb51ae70354a6594917c4c1516bc51bf sr_a4.dem
b45ef3b77b995b1eda8e5ac25d8c76cbc0f249ce6010c6e9a0a9f02520782e5f sr_a5.dem
a984e63f87c5adc0cf63f20d0e023c87fcaef07844efd0f99310029d630b40a6 sr_a6.dem
7fdbaa3bb6317d4b879577cc3e3fc830b07738313e11216353a07264feb377d9 sr_a7.dem
9292d7b67e736dd3a677d3e12cd4ec5a2f3519917b8a41a41db0e6a051dd67cb sr_a8.dem
42b23a8c89efccb00e3b52406613fe81cb7752cf6ff520dda0f5222a5f627203 sr_a9.dem
11ad19e2f543017b1e7442c88ebec86d03fc737474b07e51e5c40e17fa225bb6 sr_e6.dem
4d61d15e91d3e1a136f31b5963f8987a37410ba1a709149609b5e58d77e9728d sr_e7.dem
74bfe2cd800662e97fa482198388ada714730ef64aea28c62ffa9062a78e664b sr_e8.dem
6137d89b603100d71947d0542eb5383d4aeba1773a982eb3a82ad0436927e7ef sr_e9.dem
d1db1e9366f53ee6bcb316bd27aeb6c0a9b14ed568d8cb8e7350d61e8f240a43 sr_e10.dem
06792bbb7cd856dd2d9aee5fd49d3256552af3872d648360bd5f2eb26a80dc7a sr_e11.dem
```

`tools/corpus/manifest.json` lists gcor only, so the hashes for these lcor demos are kept here rather than in it.

**Restore, read back after `exec sr_restore` at the end of each round** (the client was disconnected). Every round
read back these same values:
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
- **What sets the phase, at connect and after a hitch,** is unread: the client's `net_time` against the host's tick
  accumulator, and on a remote server the clock-correction code.
- **Whether the user's lines as a whole make a bad recording likelier** stays open. The pooled test shows no difference,
  and the low tail is only a post-hoc hint.
- **Whether a client frame rate change moves the split** is untested. Nothing here varied `fps_max`, so nothing here
  should be suggested.

## For the Discord user

We could not pin this on your settings. We recorded the same kind of demo nine times with your exact network lines and
ten times with TF2's stock settings. Both sets came out stuttery on some recordings and smooth on others, and on
average the two were the same. We also changed one setting at a time: `rate` to the maximum (three recordings),
`net_chokeloop` off (one) and `net_queued_packet_thread` off (one). None of them made things better, and that is all so
few recordings can show. Our very worst recordings happened to use your lines, but there were too few of them to call
that a real effect. One limit: we recorded on a local server with bots, not on Valve's casual servers through Steam's
relay. We could not reproduce that route, so we cannot rule out that it makes a bad recording more likely.

Here is what happens. A POV demo stamps every update it receives with your PC's own clock, rounded to the nearest
server tick. When the updates arrive close to the halfway point between two ticks, small timing wobbles push some of
them onto the same tick and some onto the next-but-one. The game's demo player trusts those stamps, so the camera
speeds up and slows down. Where your updates land against that halfway point changes from recording to recording,
and even partway through one after a hitch. Live play does not use those stamps, which is why it felt fine while you
played.

We did not test any other single setting, so we are not suggesting one. Tf2DemoSalvage's viewer re-times the camera by
the server's own tick (`demo_retimeview`, on by default since 0.1.0-beta.55). On your demo it cuts the share of camera
frames more than 50% off their neighbours' pace from 21% to 3.4%. That was measured in the viewer's own playback code,
not watched on screen.

(Source for that figure: the `camerapath` run on `spiked-pl_pier-stutter.dem` on 2026-10-28, from its world-pacing
lines. With `demo_retimeview 0` it is 622 of 2945 moving frames; with `demo_retimeview 1` it is 101 of 2947.)
