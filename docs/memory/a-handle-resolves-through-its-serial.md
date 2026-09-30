---
name: a-handle-resolves-through-its-serial
description: An entity handle resolves via EntityStateTable.Resolve (slot + serial), never a slot lookup alone.
metadata:
  type: project
---

An EHANDLE is slot + serial. Resolve it with `EntityStateTable.Resolve`, which checks the serial; a slot-only lookup
names whatever now occupies the slot. Made twice: B231, and B112's grapple handle (2026-09-29; a stale-serial row now
guards it, 709e902d).

**Why:** slots are reused within ticks; the serial is the engine's identity ([[an-entity-index-does-not-name-a-track]]).
**How to apply:** every `m_h*` / handle-typed prop: `Resolve`, plus a test row with a stale serial.
