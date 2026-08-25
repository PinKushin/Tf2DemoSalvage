---
name: build-servers-outlive-the-build
description: MSBuild node reuse and VBCSCompiler stay resident after every dotnet build/test, accumulate across sessions, and are a likely cause of "the machine needs a restart every few days".
metadata:
  type: project
---

**Every `dotnet build` and `dotnet test` leaves daemons running, by design, and nothing reaps
them.** MSBuild's node reuse keeps worker nodes alive for the next build; the Roslyn compiler server
(`VBCSCompiler`) does the same. Both outlive the process that spawned them.

**Measured 2026-08-25, immediately after one green `build/gate.sh` run, with nothing else
building:**

| process | count | each | total |
|---|---|---|---|
| `dotnet` (MSBuild nodes) | 8 | ~110 MB | ~0.9 GB |
| `VBCSCompiler` | 1 | 502 MB, 547 s CPU | 0.5 GB |

**About 1.4 GB still resident with the gate long finished**, and it does not go away on its own.

**Why it matters more than one run suggests: it accumulates.** Several agents build in this
directory, sessions come and go, and each `dotnet test` adds to the pile. The owner's symptom —
needing a restart every few days — is consistent with this, and it was the reason the cleanup got
looked at at all.

**The honest cost to a RUN is small, and overstating it sends the fix the wrong way.** The viewer
stage measured 2m30s inside the gate against 2m18s standalone: twelve seconds. The reason to clean
up is the memory a machine keeps handing over, not the speed.

**How to apply:**

- `dotnet build-server shutdown` is the reaper. `build/gate.sh` runs it from a `trap ... EXIT`, so a
  gate that FAILS cleans up too — which is the run most likely to be followed by another.
- Run it by hand after a batch of ad-hoc `dotnet test` calls. They leave nodes just as the gate does.
- **Shut down rather than disable.** `MSBUILDDISABLENODEREUSE=1` stops them existing at all, but node
  reuse genuinely helps ACROSS the eleven projects a gate run walks. The defect is persistence, not
  reuse. Set it machine-wide only if the restarts continue after reaping is routine.
- **Never `pkill -f`** for this: it matches the shell running it, and a build script's own command
  line contains every pattern worth matching. That one has already cost an SSH session, exit 255,
  looking exactly like a network drop.
- A symptom worth recognising: `Get-Process dotnet` showing several ~110 MB processes with a start
  time matching a build that finished long ago. They are idle, not stuck.

Related: [[do-not-rerun-a-green-gate]], [[read-the-trx-total-not-the-console]].
