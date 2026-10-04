---
name: download-every-demo-found
description: "Owner wants every demo a lead turns up downloaded and kept in lcor, not just new protocols — lcor goes to archive.org later."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-04T20:04:19.546Z
---

2026-10-04, owner on TFTV demo leads: *"dl anything and everything demo really, i dont want to lose these things, the lcor is going to be uploaded to archive sooner or later"*.

**Why:** old demo links rot (20 of 27 TFTV leads dead); lcor is the preservation copy (D81 archive.org).

2026-10-04, owner: *"keep new demos in lcor too, even new eras, we make era specimens for the gcor, not full demos since real demos are long, our specimens can be a few minutes long each"*. A found demo of a new protocol is a decode test input via `Corpus.Demo` (skips when absent), never a gcor file; the gcor specimen is a short recording made on a period client ([[record-specimens-with-the-tf2-mcp]]). Exception, owner: z1800 stays in gcor — the UI, playback and CI test demo.

**How to apply:** a found demo link = download it to `tools/corpus/local/` (lcor, main checkout), whole packs too; gcor still only for a new protocol. Never delete one. Executables in a pack/link list are not demos — skip.
