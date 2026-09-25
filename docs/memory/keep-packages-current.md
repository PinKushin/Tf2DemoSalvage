---
name: keep-packages-current
description: "Update every package to latest, majors included; hold one back only for a known security issue (D191)."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-25T19:11:05.147Z
---

Keep every package and build tool at its latest release. Nothing is pinned back unless the newer release has a known security problem, such as a zero-day. If one is held back, write that reason beside it in `Directory.Packages.props`.

**Why:** the owner said (2026-09-25): "nothing should be pinned really, things need to be kept up to date, unless theres reason to think the new update is not secure and has some zero day". This is recorded as D191.

Newer is the safer default. A new release rarely has security documentation, but a trusted publisher with few regressions, such as Microsoft, can be taken as an improvement. Open-source packages may warrant an audit, though they are routinely audited already. Build tools and checkers carry the least risk, because a flaw there exposes this program alone.

**How to apply:** when updating, run `dotnet list package --outdated` and take everything, majors too. Run `--vulnerable --include-transitive` too, then the full gate. Packages that share one version, like Silk.NET, still all move to the latest.
