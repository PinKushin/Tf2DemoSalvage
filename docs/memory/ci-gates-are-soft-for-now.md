---
name: ci-gates-are-soft-for-now
description: CI coverage floors do not block implementation (D195); only a massive drop is a defect
metadata:
  type: feedback
---
CI gates (coverage floors) are not hard constraints yet (D195, 2026-09-27). A small drop is undertesting, which the
mutation pass will surface. Only a massive drop, one that suggests something got deleted, needs investigating now.

**Why:** the project moves fast; the owner stopped a session that was spending tokens chasing an 84% vs 85% floor.
**How to apply:** keep implementing; do not chase CI floors unless the owner asks or the drop is large.
