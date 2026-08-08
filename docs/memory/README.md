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
