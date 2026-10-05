---
name: runner-migration-notices-are-not-defects
description: A GitHub runner-image migration notice (ubuntu-latest moving to a new release) is not an annotation to fix; never pin runners to silence it.
metadata:
  type: feedback
---

A `notice`-level annotation announcing a runner label migration (e.g. "ubuntu-latest will migrate to Ubuntu 26
beginning October 19, 2026") is information, not a defect. Do not pin `runs-on` to an older image to make it go away.

Owner, 2026-10-05, after the Linux jobs were pinned to ubuntu-24.04: *"let it move i want to stay updated, and update
notice is not a real anotation, it is a notice"*. The pin was dropped before it was pushed.

**Why:** the owner wants to track current images; a pin only postpones the move until the old image is retired.
**How to apply:** CI annotations still count (warnings, deprecations, "no files found"), but a runner migration
notice needs no change. After the migration date, read the first run on the new image instead.
