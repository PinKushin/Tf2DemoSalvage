---
name: a-wrong-invocation-exits-zero
description: "A tool given the wrong arguments often prints usage and exits 0, so a mis-invoked command is indistinguishable from a passing run; check the exit code AND the output shape."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T01:02:01.143Z
---

**A command that was invoked wrongly usually exits 0.** Printing a usage banner is not an error to
the program that printed it, so "did it pass" and "did it run at all" collapse into the same answer.

Three measured on this project, all of which cost real time:

- **`pwsh run-exclusive.ps1 dotnet test …`** — the script lives at the PinKushin root, not in the
  repo, so `pwsh` cannot find the bare filename, prints its own usage banner and **exits 0**. It was
  in `CLAUDE.md` in that form for weeks. Correct: `pwsh -File "C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1" …`
- **`dotnet test … | tail`** — the pipeline's exit code is `tail`'s. A broken build came back exit 0
  with an empty grep and read as green. Redirect to a file, then check `$?`.
- **`dotnet test --filter` matching nothing** — exits 0 with no summary at all, so a renamed fixture
  silently tests nothing.

**The general shape: whenever a command's OUTPUT is being read rather than its exit code, the
absence of expected output is the failure signal, and nothing reports it.** So assert on the shape —
a total that matches a known floor, a line that must appear — rather than on the status.

This is the same family as `--no-build` and as a stale binary: the run succeeds at doing nothing.

Related: [[read-the-trx-total-not-the-console]], [[a-skip-is-not-a-pass-or-a-failure]],
[[measure-the-output-not-the-capability]].
