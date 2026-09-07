# Global hooks, backed up here

**These are not wired by this project.** They live in `~/.claude/hooks/` and are wired by
`~/.claude/settings.json`, so they apply to every repository. The copies here exist for the same
reason `docs/memory/` exists: a machine wipe or a move to another computer should not take them.

**The copies are byte-identical**, so restoring one is a straight copy back into `~/.claude/hooks/`
plus its `PreToolUse` entry in `~/.claude/settings.json`. Keep them that way — a backup that has
drifted from the thing it backs up restores something that was never running.

| file | matches | does |
|---|---|---|
| `block-expensive-subagents.ps1` | `Agent`, `Workflow` | refuses any subagent that is not `haiku` — including a `Workflow` script whose `agent()` calls do not name one |

## Why this directory exists at all, 2026-09-07

`.claude/hooks/subagent-policy.ps1` used to sit beside the project's own hooks and do this job. It
was doing two things at once — enforcing the rule for this repo, and being the backup of it — and
when the rule went global the file was deleted, taking the backup with it. The owner: *"i think it
was put in the repo for github backup"*.

So the two jobs are separated now. `.claude/hooks/` holds hooks this project wires and runs;
`.claude/hooks/global/` holds copies of hooks that run everywhere and are wired elsewhere.

**What the deleted hook did that the global one does not:** a concurrency backstop, tracked through
`SubagentStart` and `SubagentStop`, refusing a spawn past eight running agents. D145 called that
number "a runaway backstop rather than a cap" rather than a working limit, so nothing depends on it
day to day — but an unbounded spawn loop no longer trips anything, and that is a real gap rather
than a tidy-up.
