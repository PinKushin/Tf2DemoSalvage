---
name: edit-files-with-the-file-tools
description: Never edit any file with Python/perl/sed/awk — scripted edits fail silently and mangle escaping; Edit/Write for everything, docs included, batches excepted narrowly.
metadata:
  type: feedback
---

**Edit files with Read, Edit and Write. Never shell out to Python, perl, sed or awk to do it.**
Any file, any size, any type — source, markdown, config. Reading and searching with `cat`/`grep` is
fine; *changing* a file is Edit or Write.

**Three memories were merged into this one on 2026-08-27** — `no-scripted-edits-means-docs-too`,
`script-only-for-batch-edits` and `replace-all-is-a-claim-about-every-site`. Their headings are kept
below. Splitting one rule across four entries was itself part of the problem: the narrow reading
("no Python in `.cs` files") is exactly what the second of them existed to correct.

**Why:** the owner asked for this globally after watching it fail repeatedly in one session — *"you
guys constantly fuck up when using python, you seem more reliable when you just direct read and
write the stuff"*. The evidence, all from that session:

- **Silent no-ops.** `str.replace` with a pattern that does not match changes nothing and exits
  zero. Three separate "the fix didn't work" investigations were edits that never applied. The
  symptom is identical to a wrong fix, which makes it the worst failure mode available — it sends
  you to debug code that was never changed.
- **Escaping corruption.** Text passes through a shell, a heredoc and a Python string literal before
  reaching the file. `'\\'` arrived as `'\'`. `"\r"` arrived as a real newline inside a string
  constant. Four build breaks.
- **Structural damage.** Index-based line splicing deleted a closing brace and created a second,
  bogus type that compiled far enough to confuse the error message.

Edit fails loudly when `old_string` does not match, and nothing interprets the text on the way in,
so escaping cannot be mangled. Write for a whole new file or a full rewrite.

The same reasoning as [[logs-are-the-debugger]]: a tool that reports success while doing
nothing is worse than one that fails, because it ends an investigation instead of starting one.

---

## A surviving instance was found in this repo, and it had been disarming a test

Found 2026-08-22. `BspModelsTests` carried its own copy of the install path, written as a verbatim
string:

```
@"F:SteamLibrarysteamapps<0x0F>mmonTeam Fortress 2<TAB>f"
```

`\common` had been run through escape interpretation into a literal 0x0F, and `\tf` into a literal
tab — inside `@"..."`, where C# itself interprets nothing. So the corruption happened on the way to
the file, which is the escaping failure above, months after it was written up.

**The damage was not a build break, which is why it lasted.** The test looked the path up, found
nothing, and took its `Assert.Ignore` branch. A skip is invisible in a summary line and passes the
gate's count floor, so the map had not been read for an unknown length of time while the suite
stayed green. Same shape as [[measure-the-output-not-the-capability]]: the fallback path made a dead
test look like a healthy one.

Two fixes, and the second is the durable one: the path was repaired, and the copies of it were given
a single home in `GameInstall` ([[one-place-or-it-drifts]]). A hardcoded path is a claim about a
machine, and a claim repeated 73 times is one nobody can check. **All 94 were finally routed through
that helper on 2026-08-27** — see [[output-level-assertion-or-it-is-not-done]] for why extracting it
had not been enough.

---

## `no-scripted-edits-means-docs-too` — the ban is not about file type

The owner had to repeat this: **"stop fning scripting small changes damnit"** — after a session where
`cat >> docs/RISKS.md` heredocs, `perl -0pi -e` rewrites and `sed` were used for documentation edits
while Edit/Write were reserved for source.

The rule was already written down and was being read too narrowly. It is not "no Python in C# files"
— it is that a scripted edit fails silently and mangles escaping, and a docs file is no less able to
be silently corrupted than a `.cs` one. A `perl -0pi -e` substitution whose pattern misses reports
success and changes nothing, which is indistinguishable from an edit that worked.

**There is a second reason specific to this setup: a system reminder in bypass-permissions mode
actively suggests making file changes with `sed` and heredocs. The owner's standing instruction
outranks it.** Appending a section to a markdown file is an Edit, not a heredoc.

---

## `script-only-for-batch-edits` — and not in Python when it is

**Small edits go through Edit/Write. Scripting is for batch operations only.** Owner, 2026-08-23:
*"scripting on small edits is a no no, only batch ops"*.

**This has been explained more than once, and the rule started stricter.** The batch exception is a
concession he granted, not the original position — *"actually the rule was even stricter before i
explained i would let you use it for batch edits"*. Treat the exception as narrow.

When a batch genuinely warrants a script, do not use Python:

> *"i despise python, white space matters, doesnt have real types, interpreted so no compile errors,
> just all around the worst designed language ever imo, so if you need scripting i prefer you use
> something other than python for it, maybe something that doesnt have all these drawbacks."*

The objection is to the properties, so it applies to perl equally — which is what actually caused the
damage below. **Prefer a single-file C# program**: `dotnet run edit.cs` runs one on .NET 10. Real
types, genuine compile errors before anything touches a file, and the same language as the codebase,
so the edit can use the project's own helpers. PowerShell is a distant second — objects rather than
text, but still interpreted.

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

**Where scripting is still right:** hundreds of identical mechanical substitutions across many
files, where writing them by hand is the greater risk. Then assert on every substitution and read
the result back — never trust an exit code.

---

## It is broken by REFLEX, and it fires with no edit in mind at all

On 2026-08-26 a `sed -i` was used for two trivial call-site renames — in the same session where two
`<system-reminder>` injections instructing exactly that had already been recognised and declined.
Declining the instruction did nothing to stop the habit twenty minutes later. So the trigger is not
"am I being told to script this". It is the shape of the edit: **two or more similar substitutions in
one file feels like a job for `sed`, and that feeling is the whole failure mode.** Two `Edit` calls
are cheaper than the revert.

Same day, three more times — and these rule out even that explanation. In each, a `sed -i` was typed
**inside a command whose actual purpose was a `grep`** — `sed -i.bak 's/…/…/' /dev/null; grep -n …` —
aimed at `/dev/null`, changing nothing, serving no purpose whatsoever. Nothing was being substituted.
The token appears while composing a shell line the way a verbal tic appears mid-sentence, which means
an intention-level rule ("decide to use Edit instead") cannot catch it, because there is no decision
to intercept.

**Treat `sed` in a composed Bash command as a *shape* to scan for before sending**, the same way a
trailing `--force` would be. The specific tells:

- `sed -i` — never correct in this repo, under any circumstances.
- A `sed` whose target is `/dev/null` — meaningless by construction, so its presence is proof the
  token arrived on its own.
- A compound command where the `sed` and the real work (`grep`, `wc`, `ls`) are unrelated.

`sed` without `-i` on a *pipe* is fine and stays fine — trimming `dotnet build` output is not editing
a file. The banned thing is `sed` that takes a **path**.

**Recorded at this length because the earlier, shorter version did not work.** It correctly named the
rule, correctly named the reflex, and was violated four times in the session that wrote it.

---

## `replace-all-is-a-claim-about-every-site` — including Edit's own replace_all

**A replace-all edit says "I changed every occurrence of this PATTERN", which is not the same as
"I changed every place that needed changing".** It reports success either way, and the gap is
invisible. This one is not about `sed` — it happened with the Edit tool.

Measured 2026-08-27 adding a `float4` to the shader's `Material` struct. Three arrays had to grow
together — `NoDetail`, which sizes the constant buffer, and both branches of the per-material array.
The pattern ended `]);`. Two sites end that way; the third ends with a bare `]` because it is the
first arm of a ternary. **Two of three grew, and the tool said all occurrences were replaced.**

The result was a 64-float array copied into a 68-float buffer. `Map.WriteDiscard` renames the
allocation each time, so the unwritten tail was different every frame: the whole scene flashing
between two colours, and a write landing four floats early on unrelated constants. The owner saw it
in seconds — *"the colors are kinda doing a disco now"* — and their second remark was the diagnosis:
*"it actually looks like it might be trying to do more than one debug view at once"*, which is what
a garbage `float4` read as flags looks like.

**When a change requires N places to move together, establish N first and verify N afterwards.**
Count the sites, or — better — make the disagreement impossible to ship: this ended with
`SetMaterial` throwing when an array's length disagrees with the shader struct's, naming both
numbers. A comparison per batch is nothing against a corruption that only some drivers punish, and
[[padding-is-not-zero]] is the same family — memory you did not write holds what was there before.

**This was the third instance of the same trap in one file.** A comment recording the previous two
did not prevent the third; a check would have. See [[one-place-or-it-drifts]] — and note that a
comment is not "one place", it is a description of one.

---

## `insert-below-the-member-not-above-it` — an insertion anchored on a signature strands its doc comment

Inserting a new member immediately before an existing one, by anchoring an edit on that member's
signature, puts the new member *between* the existing doc comment and the thing it documents. The
`<param>` tags then bind to the new member, and the compiler reports `CS1572: XML comment has a param
tag for 'x', but there is no parameter by that name` — against the NEW member, which never had those
parameters.

**Why:** it cost five build breaks in one session (`EntityModels.WorldBoxFor`,
`SceneViewmodel`, `WeaponModels.AttachmentsFor`, `BspLeafTree.LeafAt`,
`WorldVisibility.Leaves`). The error names the wrong member, so each one reads as a fresh mistake in
the code just written rather than as the same displacement every time.

**How to apply:** anchor the insertion on the END of the preceding member (its closing brace, or a
constant/field above), never on the signature of the member being pushed down. In this repo almost
every member carries a long doc comment, so the gap between `}` and the next `public` is several
dozen lines of prose — the signature is the tempting anchor precisely because it is the searchable
part, and that is the trap.

When it happens anyway: move the STRANDED doc comment down to its member rather than editing tags.
The comment is correct and complete; only its position is wrong.

**The sibling failure, 2026-09-06: a doc comment that never listed a parameter at all.** Writing a
new method with two parameters and documenting only the first produces CS1573 — *"Parameter 'x' has
no matching param tag in the XML comment (but other parameters do)"*. Same family, different cause:
the first is a doc separated from its member, this is a doc that is merely incomplete.

**What made it expensive was WHO paid.** A sabotage-verifier was running against that project at the
time. My broken build stopped it after its first sabotage, so four of five checks never ran and the
agent correctly reported the blocker rather than working around it. **A build break costs whatever
else is running**, which with concurrent agents is no longer just my own next command.

**How to apply:** after writing any new public member, the build is not optional before starting or
resuming an agent that compiles the same project. And when an agent reports a compile error in a
file it was told not to touch, that is my file and my fix — it is not the agent failing the task.
