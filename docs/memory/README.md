# AI memory, mirrored into the repository

These files are the assistant's working memory — non-obvious things learned building this
project. They live here so they survive a machine wipe or a move to another computer.

## Why this exists

The assistant's own memory directory is outside the repo and local to one machine; a
reinstall would lose findings that cost real time to establish.

## The rule

**Both copies must be updated together.** The assistant's own directory is authoritative;
this one is the portable backup. Updating only one lets the local copy silently diverge or
the backup go stale.

## No personal data in here

**This directory is committed and the repository is public.** Anything personal or
identifying belongs in the assistant's *global* memory (`~/.claude/memory/`), never here.
This cost a history rewrite once, when a note naming the owner was committed before the
distinction was drawn — purged rather than merely deleted, since deletion alone leaves it
in every earlier commit.

Test: would this help a future assistant on a *different* project? If yes, it's global. If
it's only meaningful next to this codebase, it belongs here.

## Checking the two copies agree

**Byte for byte.**
```bash
diff -r --exclude=README.md docs/memory "<the assistant's memory directory>"
```
One difference arrives by design: the assistant's memory tool restamps frontmatter on every
save, so this copy takes the same stamp in the same commit —
[[a-fold-leaves-its-paths-behind]] has the details.

`README.md` exists only here — it explains the folder to a repository reader, which the
assistant's own directory doesn't need.

## What is here

`MEMORY.md` is the index: one line per entry. Each other file holds a single fact, typed:

| Type | Meaning |
|---|---|
| `user` | Who the owner is — preferences, working style. |
| `feedback` | Guidance on how the assistant should work, including corrections. |
| `project` | Ongoing work, constraints, and findings not derivable from the code. |
| `reference` | Pointers to external resources. |

## How to read them

Written as a briefing for a future AI instance that read the code but wasn't present for
the conversation: blunt, records *why* not just what, and several document mistakes —
kept deliberately, since a memory recording only conclusions is the kind that gets
confidently repeated.
