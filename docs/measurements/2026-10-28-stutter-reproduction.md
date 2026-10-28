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
