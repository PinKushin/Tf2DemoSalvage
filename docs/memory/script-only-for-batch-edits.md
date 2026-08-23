---
name: script-only-for-batch-edits
description: Use Edit/Write for single-site changes; scripted edits are only for genuinely mechanical batches.
metadata:
  type: feedback
---

**Small edits go through Edit/Write. Scripting is for batch operations only.** Owner, 2026-08-23:
*"scripting on small edits is a no no, only batch ops"*.

**This has been explained more than once, and the rule started stricter.** The batch exception is a
concession he granted, not the original position — *"actually the rule was even stricter before i
explained i would let you use it for batch edits"*. Treat the exception as narrow.

## When a batch genuinely warrants a script, do not use Python

> *"i despise python, white space matters, doesnt have real types, interpreted so no compile errors,
> just all around the worst designed language ever imo, so if you need scripting i prefer you use
> something other than python for it, maybe something that doesnt have all these drawbacks."*

The objection is to the properties, so it applies to perl equally — which is what actually caused the
damage below.

**Prefer a single-file C# program**: `dotnet run edit.cs` runs one on .NET 10. Real types, genuine
compile errors before anything touches a file, and the same language as the codebase, so the edit
can use the project's own helpers. PowerShell is a distant second — objects rather than text, but
still interpreted.

**It cost four real mistakes in a single session**, every one of them from perl on a single site:

1. **`$"` is a perl variable.** Two C# interpolated strings were silently mangled into
   `() =>  step {step} landed on...` — the `$"` vanished and the code no longer compiled. Twice, in
   two different files.
2. **A file was emptied.** A `perl -0777 -ne` intended to cut one method printed nothing and the
   result was moved over the original, leaving a 0-line file. It was untracked, so there was no
   git copy to recover.
3. **Doc comments detached from their methods, twice.** Inserting a field "just before
   `public static X Launch(...)`" put it between the method's `<param>` block and its signature —
   `CS1572`, and the same mistake had already been made an hour earlier on `PressKey`.
4. **Over-consuming regexes.** A loop deleting three methods by pattern also swallowed a fourth
   (`Click`) whose name was a prefix of one of them.

**None of these can happen with Edit**: it fails loudly when `old_string` does not match, nothing
interprets the replacement text on the way in, and the match is anchored to text actually read.

**Where scripting is still right:** hundreds of identical mechanical substitutions across many
files, where writing them by hand is the greater risk. Then assert on every substitution and read
the result back — see [[edit-files-with-the-file-tools]], which this reinforces rather than replaces.
