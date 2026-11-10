---
name: an-engine-address-belongs-to-one-build
description: TF2 updates weekly; a decompiled address holds for one build only. Check the Ghidra copy's build against the installed binary, and record addresses with the build stamp.
metadata:
  type: feedback
---

An address read in a TF2 binary (`engine.dll`, `materialsystem.dll`, ...) belongs to one build. Owner, 2026-11-10: *"That might be an old one. Tf2 updates weekly. So the functions will be the same, but their memory placement will be different."*

**Why:** an address cited without its build reads as current after the next update, and a later reader looks in the wrong place or trusts a stale Ghidra project.

**How to apply:**
- Before trusting a Ghidra program, compare its PE header with the installed file. Read `TimeDateStamp` (e_lfanew + 8) and the checksum (optional header + 64) from Ghidra's memory at the image base, and the same from the file on disk. The Ghidra script route needs `GHIDRA_MCP_ALLOW_SCRIPTS=1`; the header read does not.
- Write the build stamp next to any address you record (B718 records `engine.dll` timestamp `0x6ac41507`).
- To find a function again in a new build, use its strings or its callers, not the old address.
- The logic is usually unchanged between builds; the placement is not.

Related: [[rename-decompiled-functions]], [[log-the-engine-when-reading-stops]].
