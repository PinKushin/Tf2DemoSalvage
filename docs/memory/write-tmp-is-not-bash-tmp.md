---
name: write-tmp-is-not-bash-tmp
description: "The Write tool's /tmp and Git Bash's /tmp are different folders; a commit message written with Write to /tmp/msg.txt is not what `git commit -F /tmp/msg.txt` reads."
metadata:
  node_type: memory
  type: feedback
  originSessionId: a7745234-ed21-42a5-a0c8-7c85003d2f62
  modified: 2026-11-07T20:59:15.409Z
---

Write `/tmp/msg.txt` lands in `C:\tmp\msg.txt`; Git Bash's `/tmp` is its own temp folder. On 2026-11-07 the beam
cull (8da12601) was pushed to main under the PREVIOUS commit's message because `git commit -F /tmp/msg.txt` read a
stale file; fixed forward with an empty commit (e34ab054) carrying the real description.

**Why:** a stale message is silent: the commit succeeds and the hook checks pass on the old text.

**How to apply:** write the message with Write to an absolute Windows path (e.g. the scratchpad) and pass that same
path to `git commit -F`; `git show -s --format=%B HEAD` before pushing.
