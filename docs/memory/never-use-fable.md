---
name: never-use-fable
description: "Fable is a reviewer: credits queue FIRST while it has money, and also allowed locally as a reviewer subagent; never implementer, lead, or inside workflows."
metadata:
  node_type: memory
  type: user
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-11-06T00:00:00.000Z
---

**The rule now (corrected 2026-11-06):** Fable is a reviewer. **The credits queue first** ([[credits-queue]], model
`claude-fable-5-1`) while it has money; **Fable is ALSO allowed locally** as a reviewer subagent (`model: fable` on
`caveman:cavecrew-reviewer`). Never as implementer, lead, advisor, or inside workflows.

Owner, 2026-11-06, asked who the reviews had been going to (Sonnet, all session): *"fable can be used locally, i never meant
for it to never be used locally, i onyl wanted the Q to be used first ... the Q ... theres only 15 bucks or so on it, so its
only good for like 3 more reviews ... start the Q and run a fable review over the bigger changes, and start using fable
locally for review too, because i never said it wasnt allowed, i only said to use the Q first"*. The earlier "never pass model
fable to Agent" reading below was wrong: it was an ordering (queue first), not a ban.

**How to apply:** a large or structural merge review → a Fable job on the queue while money lasts (note the balance after
each); otherwise, and for reviews in general, a local Fable `caveman:cavecrew-reviewer`. Small diffs may still use Sonnet or
Opus when the hook allows. Never Fable for implementation.

History, kept because the rule changed:

- **2026-10-09, the original ban.** Owner: "fable is far too token heavy".
- **2026-10-14, trial as merge reviewer** — 145k tokens, found a real invariant break; owner: *"yep keep it"*.
- **2026-10-18 / 2026-11-01, via the queue** — the owner wanted the queue used for it; this was read as "never locally", which
  the 2026-11-06 correction above overrules.
