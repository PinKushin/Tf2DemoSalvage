#!/usr/bin/env bash
# Interleaved A/B: three pairs, before then after in each pair, so load drift hits both variants alike.
# before = D:/measure-f12/iklock-before (a worktree at 6e8f2f47, the pushed tree), after = this checkout with perf/iklock-compose-2 merged.
# Each run builds its own checkout in the same command it runs (a run without a build is refused).
here="$(cd "$(dirname "$0")" && pwd)"
after_repo="$(cd "$here/../../.." && pwd)"
before_repo=/d/measure-f12/iklock-before
mkdir -p "$here/before" "$here/after"
run() { # $1 = before|after  $2 = pair number
  local repo="$after_repo"; [ "$1" = before ] && repo="$before_repo"
  MSBUILDDISABLENODEREUSE=1 dotnet build "$repo/managed/Tf2DemoSalvage.Viewer3D" -c Release > "$here/$1/$2-build.txt" 2>&1 && \
  TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 \
    pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
    "$repo/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe" \
    D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2 \
    > "$here/$1/$2.txt" 2> "$here/$1/$2.err"
  echo "$1 pair $2 exit=$?" >> "$here/status.txt"
}
for pair in 01 02 03; do
  run before "$pair"
  run after "$pair"
done
