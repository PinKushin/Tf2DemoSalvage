---
name: an-unanalysed-ghidra-program-shows-no-xrefs
description: An imported but unanalysed Ghidra program returns no strings and no cross-references — run analysis before concluding a string, a field or a caller is absent
metadata:
  type: feedback
---

A Ghidra program that was imported without auto-analysis answers every string search empty and every
`get_xrefs_to` with "no references" — the bytes are there, the analysis is not.

**Why:** B62's HDR-type read stalled on exactly this: `shaderapidx9.dll` sat in the project unanalysed, a
first pass found no xref to anything and filed the question as unanswerable. `run_analysis` (it times out
the MCP call but keeps running; poll `analysis_status`) took the function count from 1349 to 1701, and the
caps dump at `0x18002bb50` then named `m_HDRType` outright.

**How to apply:** before believing a Ghidra absence, ask the program for something that must be there (a
string `search_byte_patterns` can find raw, then its xrefs). If the bytes exist and the xrefs do not, the
program is unanalysed: run analysis, then search again. Same rule as
[an empty search needs a control](instrument-bugs-outnumber-decoder-bugs.md).
