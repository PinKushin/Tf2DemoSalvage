# AI memory, mirrored into the repository

These files are the working memory of the AI assistant used on this project — the
non-obvious things learned while building it. They live here so they survive a machine
wipe, a fresh install, or a move to another computer: clone the repo and the assistant
picks up where it left off instead of relearning everything the expensive way.

## Why this exists

The assistant's own memory directory is outside the repository and local to one machine.
Everything in it would be lost on a reinstall — including findings that cost real time to
establish, like the fact that TF2 demos all end one byte short of a complete `dem_stop`
header, or that Stryker fails inscrutably if `TargetFramework` is set in
`Directory.Build.props`.

## The rule

**Both copies must be updated together.** The authoritative location for the assistant is
its own memory directory; this directory is the backup that makes it portable. A change
written to only one of them is a bug — the local copy silently diverges, or the backup
goes stale and restores something wrong.

## No personal data in here

**This directory is committed and this repository is intended to be public.** Anything
personal or identifying — the owner's name, handles, machine details, account identifiers —
belongs in the assistant's *global* memory (`~/.claude/memory/`), not here, and not in the
project memory directory that mirrors here.

That rule cost a history rewrite to establish: a note recording the owner's shell preference,
which named him, was committed here before the distinction was drawn. It was purged from
history rather than merely deleted, because deleting a file leaves it in every earlier commit.
Cheap while the repository had no remote; effectively permanent after a public push.

The test to apply: *would this still be useful to a future assistant working on a different
project?* If yes, it is probably a personal or cross-project preference and belongs globally.
If it is only meaningful next to this codebase, it belongs here.

## Checking the two copies agree

Compare with **line endings normalised**, not byte for byte:

```bash
tr -d '' < docs/memory/FILE.md | sha256sum
```

A plain `diff` reports every shared file as different, because `.gitattributes` normalises
this copy to LF while the assistant's local copy keeps Windows CRLF. That is expected and is
not drift — chasing it wastes a cycle.

`README.md` exists only here, by design: it explains the folder to someone reading the
repository, which is not something the assistant's own memory directory needs.

## What is here

`MEMORY.md` is the index: one line per entry. Each other file holds a single fact, with
frontmatter naming its type:

| Type | Meaning |
|---|---|
| `user` | Who the owner is — preferences, working style. |
| `feedback` | Guidance on how the assistant should work, including corrections. |
| `project` | Ongoing work, constraints, and findings not derivable from the code. |
| `reference` | Pointers to external resources. |

## How to read them

They are written as a briefing for a future AI instance that has read the code but was not
present for the conversation. That means they are blunt, they record *why* rather than just
what, and several of them document mistakes — a wrong inference about the corpus demo's
age, a rule attributed to the owner that he never stated. Those are kept deliberately.
A memory that records only conclusions and not the reasoning that corrected them is the
kind that gets confidently repeated.
