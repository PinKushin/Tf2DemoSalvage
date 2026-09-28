---
name: keep-packages-current
description: "Update every package to latest, majors included; hold one back only for a known security issue (D191)."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-25T19:11:05.147Z
---

Keep every package and build tool at its latest release, majors included. Pin back only for a known
security issue (zero-day); write the reason in `Directory.Packages.props`.

**Why:** owner, D191: *"nothing should be pinned really, things need to be kept up to date, unless
theres reason to think the new update is not secure and has some zero day."*

**How to apply:** `dotnet list package --outdated`, take everything; `--vulnerable
--include-transitive` too, then the full gate. Packages sharing one version (e.g. Silk.NET) all move
together.
