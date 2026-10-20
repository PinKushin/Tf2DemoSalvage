---
name: the-lead-publishes-releases
description: "The lead tags and publishes each release after checking it; \"owner presses Publish by hand\" was an assistant's invention (D227), and betas 16-44 sat unpublished because of it."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-20T12:34:07.527Z
---

Owner, 2026-10-20, shown that 29 releases (beta.16 to beta.44) were still drafts: *"ive never punbliished by hand sso no idea where that came from lol"*, *"its a bug and somehing i didnt know aboiut"*, then *"do the publishing is yours"*.

**Why:** `release.yml` (commit `d8f65d34`, 2026-10-01) said the owner reviews the draft and presses Publish; an earlier assistant session wrote it under his git name. He never did, so the public repo's latest release stayed beta.15 (2026-10-04) for 16 days. D227.

**How to apply:**
- The workflow only creates a DRAFT on a `v*` tag (it checks the tag equals `<Version>` in `Directory.Build.props`). The lead reviews the draft (zip attached with a size near the last release's, title, body opening on `## Changes in <version>`), then `gh release edit <tag> --draft=false`. It stays a prerelease.
- Publish only when: both gate phases green (phase 2 WITH the lcor junction, or the lcor-dependent tests skip themselves), CI green on the TAGGED commit with zero non-notice annotations, no `TODO(owner)` left in the notes. Tag only a commit whose own CI was seen green.
- `release.yml` publishes `RELEASE-NOTES.md` whole: the block must be headed `## Changes in <version>` like every earlier one, with no empty `## Unreleased` above it at the tag (re-add it after).
- The owner chose to publish only beta.45, not the 29 old drafts. Do not publish them without being asked.
- Related: [[ci-gates-are-soft-for-now]], [[push-when-work-piles-up]].
