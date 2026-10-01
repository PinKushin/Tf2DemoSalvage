---
name: no-interim-status-replies
description: Do not reply to interim subagent "still running" notifications; answer only when a result arrives.
metadata:
  type: feedback
---

When a subagent notification is interim ("still running", "waiting on gate"), say nothing — no
"still running" reply. Owner, 2026-09-30: *"stop doing that update, thats a waste"*.

**Why:** each reply costs tokens and tells the owner nothing.

**How to apply:** respond to a subagent only when it reports a result, a failure, or needs a
decision. Brief subagents to wait on a gate with one blocking command, not short polling turns that
each fire a notification. Related: [[spend-fewer-tokens]].
