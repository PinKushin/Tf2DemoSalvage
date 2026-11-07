---
name: gate-a-run-on-the-build-log-not-a-grep
description: "Chain a measurement only on \"0 Error(s)\" in the saved build log; a `grep | head` exit code is always 0 and lets a stale binary be measured."
metadata:
  node_type: memory
  type: feedback
  originSessionId: a7745234-ed21-42a5-a0c8-7c85003d2f62
  modified: 2026-11-07T01:05:04.904Z
---

`dotnet build … | grep … | head && run` runs the viewer even when the build failed, because the pipeline's exit
code is `head`'s. On 2026-11-06 three f12 measurements read an old binary: Sonar `error S` had failed every build
since a debug edit, and the numbers stayed oddly constant while the instrumented code changed meaning.

**Why:** a stale binary measures confidently and wrongly. That is the instrument fault
[[instrument-bugs-outnumber-decoder-bugs]] warns about.

**How to apply:** build into a log (`> /tmp/b.log 2>&1`), then
`grep -q " 0 Error(s)" /tmp/b.log && grep -q " 0 Warning(s)" /tmp/b.log && <run>`. Numbers that do not move when the
code under the timer changed are a control failing: check the DLL's timestamp before believing them.
