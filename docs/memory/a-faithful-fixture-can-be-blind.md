---
name: a-faithful-fixture-can-be-blind
description: A fixture copied from real data can be insensitive precisely because real data is well formed; the distinguishing input is often one no shipped file contains.
metadata:
  type: project
---

**Writing a fixture in the shipped data's exact shape is the right instinct and it is not
sufficient. The input still has to be one where correct and broken differ.**

Measured 2026-08-22. `Load_ACommentedOutEntry_IsNotRead` proved that a `//`-commented manifest line
does not load its script. Its fixture copied Valve's shape exactly:

```
	"precache_file"		"scripts/live.txt"
//	"precache_file"		"scripts/disabled.txt"
```

**It passed with comment handling sabotaged.** With `//` unhandled it becomes a token itself and
shifts the pairing to `("//", "precache_file")`, leaving `"scripts/disabled.txt"` orphaned in key
position — so the script fails to load in *both* worlds. The assertion was true either way and blind
to the thing it was written for.

One extra token ahead of the key fixes it, because an unhandled comment then pairs `("//", "x")` and
`("precache_file", "scripts/disabled.txt")`, which loads:

```
//	x	"precache_file"		"scripts/disabled.txt"
```

**Why:** this is case 2 of the four insensitivity routes — a wrong CONDITION, where the fix is the
input and never the assertion. The instinct on seeing a test that cannot fail is to assert harder,
and here that would have produced a stronger-looking test that was equally blind.

**How to apply:**

- **Ask the question in the required order.** *Is there an input where correct and broken differ?*
  comes first; *does my assertion detect it?* second. Only the second one is about assertions.
- **Real-data faithfulness and sensitivity are different properties**, and a fixture can satisfy the
  first while failing the second — *because* real data is well formed. Valve's manifest cannot
  distinguish the two worlds; a file Valve would never write can.
- Both properties are still wanted. Keep the faithful case for what it proves, and add the
  distinguishing one beside it.
- The only way to find this is to actually run the sabotage and watch **which** tests go red. Here
  the whole-suite run showed the reader's own comment test failing and the catalog's real-data test
  failing, while the one test named for the behaviour stayed green — and that gap was the finding.

Related: [[put-the-real-file-in-the-fixture]] (where a fixture's values must come from),
[[real-data-hides-bugs-small-inputs-expose]] (density supplying behaviour by accident), and
[[author-the-specimen-the-corpus-lacks]] (writing the case no shipped file contains).
