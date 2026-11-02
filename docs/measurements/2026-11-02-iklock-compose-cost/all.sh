#!/usr/bin/env bash
# Three runs of the command in ../2026-10-31-setup-parts.md "Method", each built in the same command it runs.
# repo: the checkout holding the commit measured; out: a folder per commit (before/, c1/, c2/, c3/) under this one.
repo=/c/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/.claude/worktrees/agent-acdedeefe17a7e7cf
out="$repo/docs/measurements/2026-11-02-iklock-compose-cost"
for n in 01 02 03; do
  MSBUILDDISABLENODEREUSE=1 dotnet build "$repo/managed/Tf2DemoSalvage.Viewer3D" -c Release > "$out/$n-build.txt" 2>&1 && \
  TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 \
    pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
    "$repo/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe" \
    D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2 \
    > "$out/$n.txt" 2> "$out/$n.err"
  echo "run $n exit=$?" >> "$out/status.txt"
done
