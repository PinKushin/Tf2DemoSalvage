---
name: engine-reader-before-implementer
description: Run tf2-engine-reader (sonnet) to quote the engine into a scratch file before an implementer brief; the implementer reads the file
metadata:
  type: feedback
---

Before an implementer (opus) brief that needs engine reading, run `tf2-engine-reader` (sonnet, read-only) first.
It writes verbatim quotes with `file:line`, every override, and flag set/clear sites to a scratch file.
The brief names that file; the implementer ports from it.

**Why:** owner, 2026-10-12, agreeing to the flow: *"yep sounds fine, i think, you could make the engine reader
wwrite a file to disk that the other subagent can read too if that is easier"*. Opus had been doing all SDK
finding and quoting itself — its dearest tokens on lookup. Came out of the owner asking whether a small model
trained on Valve code would help: no (recall is approximate, the hard part is tracing); retrieval is the cheap win.

**How to apply:**
- In sequence, and that is a DEPENDENCY, not the one-agent policy: the read must finish before any writing.
  Owner, 2026-10-12: *"that can never be made parellel either since the read needs to finish before any writting
  can happen, even if i say use more than one subagent at a time"*. Never launch the pair together.
  ([[one-subagent-and-prefer-cheap-models]] still holds on its own.)
- Output file under session scratch or `%TEMP%\claude\engine-quotes\`, never in a git tree.
- Quotes do not pass through the main session's reply: reader returns path + table of contents only.
- Closed-engine questions still go to the implementer with Ghidra; the reader covers published source only.
- No bulk Ghidra renaming — owner: *"the ghida rename was token heavy as shit though, thats why we stopped"*.
  A name is left only on a function an agent already identified for a fix ([[rename-decompiled-functions]]).
- Agent file is `.claude/agents/tf2-engine-reader.md` (repo agents need the `tf2-` prefix; a hook enforces it).
