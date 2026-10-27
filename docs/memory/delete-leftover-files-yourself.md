---
name: delete-leftover-files-yourself
description: "Delete throwaway files, instrument code, captures and measurement builds yourself once no longer needed; never hand the owner a \"please delete\" list."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-27T17:05:23.237Z
---

Owner, 2026-10-27, after an agent's report ended "Please delete (I don't delete files): SplitProbe.cs, captures, `D:\measure-f12\spl-*`": *"Btw yes you do delete files if they are no longer needed"*.

**Why:** a deletion list is chores handed to the owner for files the agent made itself.

**How to apply:**
- Delete what you created and no longer need: throwaway probes, `*.err`, screenshots, scratch builds under `D:\measure-f12`, agent worktrees' junctions (`cmd //c rmdir`, never recursive).
- Look at the target first. Still never touch `tools/corpus/local` contents, tracked files you did not make, or anything the owner or another agent may still use.
- Agent briefs must say "delete your own leftovers and report what you deleted", not "list what you want deleted".
- Related: [[edit-files-with-the-file-tools]].
