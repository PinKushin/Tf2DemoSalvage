#!/bin/bash
# The viewer half of the golden comparison (B161): one --shot at 1280x720, the tf2 MCP's window size.
#
#   bash tools/golden-viewer-shot.sh <demo> <tick> <out.png> [viewer args: --first-person, --spectate <name>, +cvar v ...]
#   TF2VIEW_CAMERA="x y z pitch yaw" bash tools/golden-viewer-shot.sh ...     # a fixed camera
#
# Builds first (never run a stale binary), takes the machine-wide lock (the viewer takes the desktop).
# 1576x889 is the WINDOW that yields a 1280x720 viewport: the frame adds 296 x 169 at every size
# measured (1280x720 -> 984x551, 1700x1000 -> 1404x831). The format is WIDTHxHEIGHT; any other form is
# silently ignored (B517). cl_showfps is forced off because the owner's settings.cfg
# turns it on and the overlay would be a divergence of our own making. viewmodel_fov_demo is pinned to 54,
# TF2's default and the value the TF2 side pins: this viewer's own default is 70 (D43), which draws a smaller,
# more centred weapon than the reference (B523). An argument after it still wins. Recipe and pinned cvars:
# docs/findings/76-the-golden-comparison.md.
#
# A path with a SPACE in it does not survive: run-exclusive.ps1 starts a GUI program with
# `Start-Process -ArgumentList`, which joins its arguments unquoted, so the viewer receives the pieces
# (measured 2026-10-11: "3 path(s) from the command line"). Refused here by name rather than left to
# become a picture nobody took. The old picture at <out.png> is removed first for the same reason: the
# check at the end passed on a previous run's file and an old frame was read as the new one.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
demo=$1; tick=$2; out=$3; shift 3
for argument in "$demo" "$out" "$@"; do
  case "$argument" in *" "*)
    echo "golden-viewer-shot: '$argument' has a space in it and would reach the viewer in pieces (run-exclusive.ps1's Start-Process joins arguments unquoted); use a path without one" >&2
    exit 2;;
  esac
done
rm -f -- "$out"
MSBUILDDISABLENODEREUSE=1 dotnet build "$root/managed/Tf2DemoSalvage.Viewer3D" -v q -nologo | grep -E "Warn|Err"
TF2VIEW_WINDOW_SIZE="1576x889" pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  "$root/managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe" "$demo" \
  --tick "$tick" --shot "$out" +cl_showfps 0 +viewmodel_fov_demo 54 "$@"
test -f "$out" || { echo "no shot written: read the newest viewer-*.log in %LOCALAPPDATA%/Tf2DemoSalvage" >&2; exit 1; }
