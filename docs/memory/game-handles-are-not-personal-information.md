---
name: game-handles-are-not-personal-information
description: In-game player names from demos are not personal information; they may appear in committed docs and need no history scrub
metadata:
  type: feedback
---

An in-game handle from a demo is not personal information. It may appear in committed files (RISKS, findings,
proofs), and one already in history is left there: no rewrite, no force-push.

**Why:** owner, 2026-10-13, after I removed a stranger's handle from `docs/RISKS.md` (slice 5 proof) and asked
whether to force-push main to scrub it from commit `8cdb2bab`: *"Yes keeping it is fine, game handles are not
pil so they don't really matter"*. I had been enforcing "no player names in any committed file" on agents; that
was my over-reading of the rule against personal or identifying information in `docs/memory/`.

**How to apply:**
- Do not spend a turn or an agent instruction on removing game handles, and never propose a history rewrite for one.
- The real rule stands: no personal or identifying information (real names, emails, addresses, account
  credentials) in `docs/memory/` or release notes. Steam IDs: not asked; my reading is to prefer a neutral
  description where it costs nothing, and ask if one matters.
- Neutral wording ("the looked-at player") is still fine where it reads as well; it is not required.
