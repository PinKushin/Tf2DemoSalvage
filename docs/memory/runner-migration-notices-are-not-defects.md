---
name: runner-migration-notices-are-not-defects
description: A pending update is a soft block - take it unless it is a huge API-breaking change; never pin to an older version to silence an update notice.
metadata:
  type: feedback
---

**A needed update is a soft block: do it**, unless it is literally impossible because of a huge API-breaking change.
Owner, 2026-10-05: *"if an update is not literally impossible because its a huge API breaking change, then it needs to be
done, updates are security most of the time, like even non security updates contribute to you being secure really"*.
Extends [[keep-packages-current]] (D191) to everything: packages, runner images, actions, SDKs, tools.

**Never pin backwards to make an update notice go away.** I pinned the Linux jobs to ubuntu-24.04 when
`ubuntu-latest` announced its move to Ubuntu 26 (2026-10-19). Owner: *"let it move i want to stay updated, and update
notice is not a real anotation, it is a notice"*. Pin dropped before push.

**How to apply:** an update notice means move forward (or let a `-latest` label move), then read the first run on the
new version. A deprecation/warning annotation is still a defect to fix.
