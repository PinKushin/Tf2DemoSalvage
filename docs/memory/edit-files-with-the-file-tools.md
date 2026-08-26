---
name: edit-files-with-the-file-tools
description: Never edit source with Python/sed/awk — scripted edits fail silently and mangle escaping; use Read/Edit/Write.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T17:31:48.266Z
---

**Edit source files with Read, Edit and Write. Never shell out to Python, sed or awk to do it.**

**Why:** the owner asked for this globally after watching it fail repeatedly in one session — "you
guys constantly fuck up when using python, you seem more reliable when you just direct read and
write the stuff". The evidence, all from that session:

- **Silent no-ops.** `str.replace` with a pattern that does not match changes nothing and exits
  zero. Three separate "the fix didn't work" investigations were edits that never applied. The
  symptom is identical to a wrong fix, which makes it the worst failure mode available — it sends
  you to debug code that was never changed.
- **Escaping corruption.** Text passes through a shell, a heredoc and a Python string literal before
  reaching the file. `'\\'` arrived as `'\'`. `"\r"` arrived as a real newline inside a string
  constant. Four build breaks.
- **Structural damage.** Index-based line splicing deleted a closing brace and created a second,
  bogus type that compiled far enough to confuse the error message.

**How to apply:** Edit fails loudly when `old_string` does not match, and nothing interprets the
text on the way in, so escaping cannot be mangled. Write for a whole new file or a full rewrite. For
a genuinely mechanical change across hundreds of sites, assert on every substitution and read the
file back to verify — never trust an exit code.

The same reasoning as [[a-log-must-name-what-it-measured]]: a tool that reports success while doing
nothing is worse than one that fails, because it ends an investigation instead of starting one.

**A surviving instance was found in this repo on 2026-08-22, and it had been silently disarming a
test.** `BspModelsTests` carried its own copy of the install path, written as a verbatim string:

```
@"F:SteamLibrarysteamapps<0x0F>mmonTeam Fortress 2<TAB>f"
```

`\common` had been run through escape interpretation into a literal 0x0F, and `\tf` into a literal
tab — inside `@"..."`, where C# itself interprets nothing. So the corruption happened on the way to
the file, which is the escaping failure above, months after it was written up.

**The damage was not a build break, which is why it lasted.** The test looked the path up, found
nothing, and took its `Assert.Ignore` branch. A skip is invisible in a summary line and passes the
gate's count floor, so the map had not been read for an unknown length of time while the suite
stayed green. That is the same shape as [[measure-the-output-not-the-capability]]: the fallback path
made a dead test look like a healthy one.

Two fixes, and the second is the durable one: the path was repaired, and the 73 copies of it were
given a single home in `GameInstall` ([[one-place-or-it-drifts]]). A hardcoded path is a claim about
a machine, and a claim repeated 73 times is one nobody can check.

**This rule is broken by REFLEX, not by decision, and that is the part to guard against.** On
2026-08-26 a `sed -i` was used for two trivial call-site renames — in the same session where two
`<system-reminder>` injections instructing exactly that had already been recognised and declined.
Declining the instruction did nothing to stop the habit twenty minutes later.

So the trigger is not "am I being told to script this". It is the shape of the edit: **two or more
similar substitutions in one file feels like a job for `sed`, and that feeling is the whole failure
mode.** Two `Edit` calls are cheaper than the revert. There was a `.bak`, so it cost only a `mv` and
two redone edits and nothing reached a commit — but that was luck in the form of a flag, not a
safeguard.
