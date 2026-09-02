---
name: never-edit-a-running-script
description: Bash reads a script by byte offset as it executes, so editing gate.sh mid-run truncates the rest — and it exits 0, reporting a pass for tests that never ran.
metadata:
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-02T00:00:00.000Z
---

**Do not edit a shell script while it is running.** `bash` does not load the file up front; it reads
and executes by BYTE OFFSET, so an edit that changes the length shifts everything the interpreter
has not reached yet. It resumes at the old offset in the new bytes.

Seen 2026-09-02, backgrounding `build/gate.sh` and editing a floor in it while it ran:

```
core: 1669 executed, 0 failed (floor 1668)
cli: 74 executed, 0 failed (floor 74)

[exited with code 0]
```

**Two of twelve assemblies, and exit code 0.** No error, no truncation warning, nothing that reads
as wrong — the same family as the crashed test host that reports `Passed!` with a short total, and
the `--filter` that matches nothing and exits clean. The gate's own count assertions cannot help:
the ten runs that would have made them fire never executed.

**The habit that causes it is backgrounding a long run and using the wait productively.** That is
usually right; the exception is any file the running command is READING — the script itself, and
anything it sources. Edit docs, edit source the next build will compile, but leave the script alone
until it exits.

**How to notice**: compare the number of assemblies reported against the number the gate runs. A run
that stops early looks exactly like a run that succeeded, and this project already knows that shape
from [[read-the-trx-total-not-the-console]]. The rule there — count what came back, do not read the
last line — is the same rule.
