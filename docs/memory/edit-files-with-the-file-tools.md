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
