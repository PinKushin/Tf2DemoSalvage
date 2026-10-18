---
name: accessibility-here-is-for-tests
description: "In Tf2DemoSalvage, accessibility trees (UIA/AT-SPI) matter for UI testing, not screen readers; TF2 cannot be played blind. Other disabilities (deaf etc.) still count."
metadata:
  node_type: memory
  type: project
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-18T23:08:05.037Z
---

Owner, 2026-10-18, on picking a cross-platform GUI: *"the accesssabiliity in this project is moreso a test thing, blind people cannot play tf2, deaf can, and other disabilities can, but not blindness, my PBJ project needs it all becuse you can play pokemon tradiing card game blind if you have real cards and someone to read them for you."*

**Why:** a demo viewer for a game that needs sight has no screen-reader audience, so automation IDs and accessible names here exist to make the UI testable.

**How to apply:**
- Judge a GUI toolkit for this repo by whether its UI can be driven by automated tests, not by screen-reader support. Dear ImGui with an in-process test hook is acceptable.
- Keep the accessibility that serves sighted players with other disabilities: deaf and hard-of-hearing (captions, visual cues for sounds), motor (rebindable keys, D69/D101), color vision.
- PokemonBattleJournal is the opposite case: full screen-reader accessibility is required there.
- The global CLAUDE.md AutomationId rule still applies, for tests.
