---
name: the-lead-publishes-releases
description: "The lead tags and publishes each release after checking it; \"owner presses Publish by hand\" was an assistant's invention (D227), and betas 16-44 sat unpublished because of it."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-20T12:34:07.527Z
---

Owner, 2026-10-20, shown that 29 releases (beta.16 to beta.44) were still drafts: *"I've never published by hand so no idea where that came from lol"*, *"it's a bug and something I didn't know about"*, then *"do the publishing is yours"*.

**Why:** `release.yml` (commit `d8f65d34`, 2026-10-01) said the owner reviews the draft and presses Publish; an earlier assistant session wrote it under his git name. He never did, so the public repo's latest release stayed beta.15 (2026-10-04) for 16 days. D227.

**How to apply:**
- The workflow only creates a DRAFT on a `v*` tag (it checks the tag equals `<Version>` in `Directory.Build.props`). The lead reviews the draft (zip attached with a size near the last release's, title, body opening on `## Changes in <version>`), then `gh release edit <tag> --draft=false`. It stays a prerelease. **Publishing posts to the owner's Discord automatically** (2026-10-21, `.github/workflows/discord-release.yml`, repository secret `DISCORD_WEBHOOK_URL`, never in the repo): so the draft's notes are what the channel shows, and any correction to a figure goes into the draft BEFORE `--draft=false` (the beta.47 notes were corrected that way). A `workflow_dispatch` run posts a marked test message (HTTP 204 in its log); `-f dry_run=true` prints the payload without posting.
- Publish only when: both gate phases green (phase 2 WITH the lcor junction, or the lcor-dependent tests skip themselves), CI green on the TAGGED commit with zero non-notice annotations, no `TODO(owner)` left in the notes. Tag only a commit whose own CI was seen green.
- `release.yml` publishes `RELEASE-NOTES.md` whole: the block must be headed `## Changes in <version>` like every earlier one, with no empty `## Unreleased` above it at the tag (re-add it after).
- **When to cut one: on performance and visible changes, no schedule** (owner, 2026-10-20: *"just make sure its actually released on performance and visible changes, it doesnt need a schedule"*). After a merge that a user would see or feel (fps, a fixed render, a new flag), cut the next beta; a mechanical split, an internal refactor, or an allocation cut inside the noise is not one. CI going green does not release anything; a `v*` tag does. Check at the end of each batch whether the merge was user-visible, and if so say so and cut it.
- **Priority: fps and bug fixes go first and ship fast** (owner, 2026-10-20: *"FPS fixes especially, get pushed fast, bug and fps fixes get fixed first and fast"*). They jump the queue ahead of refactors, analyzer sweeps and docs work, and a release is cut as soon as one is merged and green, not at the end of a batch of other work.
- **But bundle a wave** (owner, 2026-10-20: *"if we have multiple fps fixes going to come back to back, a release can wait for most or all of them to drop, I don't mind that, no reason to have 20 releases in a day lol, we've done that before"*). When several fps/bug fixes are landing back to back, hold the release until most or all of the wave is merged and green, then cut ONE. Do not cut more than one or two a day. Do not hold a finished fix for unrelated work (refactors, housekeeping), and if the wave stalls (a fix waiting on something), release what is done.
- The owner chose to publish only beta.45, not the 29 old drafts. Do not publish them without being asked.
- Related: [[ci-gates-are-soft-for-now]], [[push-when-work-piles-up]].
