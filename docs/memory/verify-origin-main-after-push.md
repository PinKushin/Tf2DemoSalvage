---
name: verify-origin-main-after-push
description: The main checkout can sit on a detached HEAD; a merge there never reaches main. Check origin/main after every push
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-23T21:41:34.586Z
---

After `git merge` and `git push origin main`, run `git log --oneline -1 origin/main` — it must show
the merge commit.

**Why:** the main checkout was once on a detached HEAD; three merge commits landed on that HEAD, and
`git push origin main` pushed an unmoved `main` ref with no error. "Merged and pushed" was reported
three times while `origin/main` never moved. A new branch cut from `main` then silently lacked the
day's work.

**How to apply:** check `git branch --show-current` in the main checkout before merging there. If
empty, merge elsewhere or fast-forward the `main` ref manually. Never report a push without reading
`origin/main`. See [[read-the-trx-total-not-the-console]] for the same rule applied to test counts.
