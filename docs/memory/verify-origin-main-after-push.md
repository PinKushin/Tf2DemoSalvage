---
name: verify-origin-main-after-push
description: The main checkout can sit on a detached HEAD; a merge there never reaches main. Check origin/main after every push
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-23T21:41:34.586Z
---

After `git merge` and `git push origin main`, run `git log --oneline -1 origin/main`. It must show the merge commit.

**Why:** on 2026-09-23 the main checkout was on a detached HEAD (from `597a783e`). Three `git merge --no-ff` commits
landed on that HEAD, and `git push origin main` pushed an unmoved `main` without any error. I reported "merged and
pushed" three times while `origin/main` stayed at `0ac84ab7`. A new branch cut from `main` then silently lacked the day's
work. The fix was a fast-forward: `git branch -f main <merge>` (main was not checked out anywhere), then push.

**How to apply:** check `git branch --show-current` in the main checkout before merging there. If it is empty, merge
somewhere else or fast-forward the `main` ref. Never report a push without reading `origin/main`. See
[[read-the-trx-total-not-the-console]] for the same rule applied to test counts.
