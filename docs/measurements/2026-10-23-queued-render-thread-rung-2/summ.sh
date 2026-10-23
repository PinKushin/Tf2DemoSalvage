#!/bin/bash
# Summarise each run file: frames, mean, phases, phase sum, residual, and the unweighted mean of the per-sample
# (50-frame window) `camera wait` part. Prints to stdout only.
for f in "$(dirname "$0")"/runs/*.txt; do
  awk -v name="$(basename "$f" .txt)" '
    /^label=/ { match($0, /loadBefore=[0-9 ]+/); lb = substr($0, RSTART+11, RLENGTH-11) }
    /^exit=/  { match($0, /loadAfter=[0-9 ]+/); la = substr($0, RSTART+10, RLENGTH-10); ex = $1 }
    /^measured/ { match($0, /[0-9]+ frames, mean [0-9.]+/); split(substr($0, RSTART, RLENGTH), a, " "); fr = a[1]; mean = a[4] }
    /phases, mean ms:/ {
      line = $0; sub(/.*phases, mean ms: /, "", line); split(line, halves, ";"); n = split(halves[1], kv, ", ")
      sum = 0; ph = ""
      for (i = 1; i <= n; i++) { split(kv[i], p, " "); v[p[1]] = p[2]; sum += p[2] }
      match($0, /render thread draw [0-9.]+/); rt = substr($0, RSTART+19, RLENGTH-19)
    }
    /camera wait [0-9.]+/ { match($0, /camera wait [0-9.]+/); w = substr($0, RSTART+12, RLENGTH-12); cw += w; nc++ }
    /queue verify: [0-9]+ frames/ { qv = $0; sub(/.*queue verify: /, "", qv) }
    END {
      printf "%s | %s | %s | %s | %s | camera %s | draw %s | rt %s | cw(unw) %.2f (%d samples) | sum %.2f | resid %.2f | %s %s\n",
        name, lb, la, fr, mean, v["camera"], v["draw"], rt, (nc ? cw/nc : 0), nc, sum, mean - sum, ex, qv
      printf "    sound %s advance %s hud %s between %s\n", v["sound"], v["advance"], v["hud"], v["between"]
    }' "$f"
done
