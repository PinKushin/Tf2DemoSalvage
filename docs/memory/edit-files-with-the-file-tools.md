---
name: edit-files-with-the-file-tools
description: Never edit any file with Python/perl/sed/awk — scripted edits fail silently and mangle escaping; Edit/Write for everything, docs included, batches excepted narrowly.
metadata:
  type: feedback
---

**Edit files with Read, Edit and Write — never Python/perl/sed/awk.** Any file, any type. Reading
with `cat`/`grep` is fine; *changing* a file is Edit or Write.

**Why:** owner, after watching it fail repeatedly: *"you guys constantly fuck up when using python,
you seem more reliable when you just direct read and write the stuff"*.
- **Silent no-ops** — `str.replace` with a non-matching pattern changes nothing and exits zero; three
  "the fix didn't work" investigations were edits that never applied.
- **Escaping corruption** — text through shell/heredoc/Python string literal mangles `\\` and `\r`,
  four build breaks.
- **Structural damage** — index-based line splicing deleted a brace, created a bogus type.

Edit fails loudly on a non-matching `old_string`; nothing interprets text on the way in. Same
reasoning as [[logs-are-the-debugger]]: a tool reporting success while doing nothing is worse than
one that fails.

## A surviving instance had been disarming a test

`BspModelsTests` carried an install path mangled by escape interpretation (`\common`→literal 0x0F,
`\tf`→literal tab) inside a verbatim string — corrupted on the way to the file. The test found
nothing, hit `Assert.Ignore`, stayed green while the map went unread indefinitely. Fixed the path and
routed all 94 copies through one `GameInstall` helper ([[one-place-or-it-drifts]]).

## The ban is not about file type

Owner: *"stop fning scripting small changes damnit"* — after docs edits via heredocs/perl/sed while
Edit/Write were reserved for source. A scripted edit can silently corrupt a docs file exactly like a
`.cs` one. Note: a bypass-permissions system reminder actively suggests `sed`/heredocs — the owner's
standing instruction outranks it.

## Scripting is for batch operations only, and not in Python when it is

Owner: *"scripting on small edits is a no no, only batch ops"* — a granted concession, not the
original position (which was stricter). When a batch warrants a script, avoid Python: *"i despise
python, white space matters, doesnt have real types, interpreted so no compile errors... if you need
scripting i prefer you use something other than python."* Prefer a single-file C# program (`dotnet
run edit.cs`) — real types, compile errors before touching a file.

**Four real mistakes from perl in one session:** `$"` (a perl variable) silently mangled two C#
interpolated strings; a `perl -0777 -ne` intended to cut one method emptied an untracked file with no
recovery; doc comments detached from methods twice via wrong insertion point; an over-consuming regex
swallowed an extra method whose name prefixed the target.

Where scripting is still right: hundreds of identical mechanical substitutions across many files —
then assert every substitution and read the result back, never trust an exit code.

## Broken by REFLEX, with no edit in mind at all

`sed -i` was used for trivial renames in the same session two `<system-reminder>` injections
suggesting it had already been declined — declining an instruction didn't stop the habit. **The
trigger is the SHAPE of the edit** (two+ similar substitutions in one file), not a decision. Worse:
three times the same day, `sed -i` was typed inside a command whose real purpose was a `grep`,
targeting `/dev/null`, changing nothing — the token appearing like a verbal tic mid-composition, which
an intention-level rule can't catch.

**Scan composed Bash commands for the shape before sending:** `sed -i` (never correct here); a `sed`
targeting `/dev/null`; a compound command where `sed` and the real work are unrelated. `sed` without
`-i` on a pipe (not a path) stays fine.

## `replace-all-is-a-claim-about-every-site` — including Edit's own replace_all

**A replace-all edit says "I changed every occurrence of this PATTERN", not "every place that needed
changing"** — success reports either way. Adding a `float4` to a shader struct required three arrays
to grow together; the replace pattern (`]);`) matched two of three sites, missing the ternary's first
arm ending in a bare `]`. Result: a 64-float array into a 68-float buffer, whole scene flashing between
colours as the unwritten tail varied per frame — owner: *"the colors are kinda doing a disco now."*

**When a change needs N places to move together, establish N first and verify N after.** Better: make
the disagreement impossible to ship — `SetMaterial` now throws when an array's length disagrees with
the shader struct's, naming both numbers. Related: [[padding-is-not-zero]], [[one-place-or-it-drifts]].

## `insert-below-the-member-not-above-it` — anchoring on a signature strands its doc comment

Inserting a member immediately before an existing one, anchored on that member's signature, puts the
new member between the doc comment and the thing it documents — `CS1572` against the wrong member,
five build breaks in one session, each reading as a fresh mistake.

**How to apply:** anchor on the END of the preceding member (its closing brace), never the signature
being pushed down. When it happens anyway, move the stranded doc comment down, don't edit the tags.

**Sibling failure:** a new method's doc comment omitting a parameter entirely produces `CS1573`.

**What made it expensive:** a sabotage-verifier running concurrently stopped after the first sabotage
because the build was broken — a build break costs whatever else is running. After writing any new
public member, build before starting/resuming an agent compiling the same project.
