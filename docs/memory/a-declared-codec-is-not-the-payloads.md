---
name: a-declared-codec-is-not-the-payloads
description: "svc_voiceinit's codec is the server's legacy setting; 2011-2016 vaudio_speex demos carry Steam Voice (SILK). Choose by the payload's CRC32 tail."
metadata:
  node_type: memory
  type: feedback
  modified: 2026-10-01
---

B441 spent a day hunting a Speex framing for packets that were never Speex. Every candidate was a Speex
layout because `svc_voiceinit` said `vaudio_speex`. The engine (2011 `engine.dll`) routes by the
`sv_use_steam_voice` ConVar, which no demo records, and hands the whole payload to Steam — SILK inside.

**Why:** a label in the stream describes what the sender was configured to say, not what the bytes are.
Six demos and eleven SourceTV recordings failed identically before the engine was read.

**How to apply:**
- When a declared format fails on many files at once, read the engine's dispatch before trying more
  framings of the declared format (`nothing-is-closed.md#read-the-spec-before-measuring-our-data`).
- Voice: `SteamVoicePayload.TryDecode` (frames exactly AND tail == CRC32 of the rest) picks Steam Voice;
  otherwise the 2007 raw 28-byte Speex path.
- An empty SILK frame must be decoded with `lostFlag` 1; without it the native decoder crashes the host.
