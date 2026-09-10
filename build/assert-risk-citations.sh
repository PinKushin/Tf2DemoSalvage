#!/usr/bin/env bash
#
# Fails when source or a memory cites a B-number that docs/RISKS.md has no entry for.
#
# **This is NOT the decision-number check with a different letter, and copying that one would have
# been wrong on its first run.** `assert-decision-numbers.sh` forbids duplicates and gaps, because a
# decision is written once. `docs/RISKS.md` is append-only by design: measured 2026-09-10, 506 entry
# headings over 381 distinct numbers — B13 carries eleven and B259 nine, each a later measurement or
# correction on one subject — and eight numbers are already absent entirely. Both of that script's
# rules fail here immediately, and neither describes a defect in this file.
#
# **What IS a defect is a citation that arrives nowhere.** A `B###` in a comment is a promise that
# the reasoning is written down; when the entry does not exist the reader gets nothing and cannot
# tell whether it was deleted, renumbered, or never written. That is the same harm B118 records for
# decisions, reached by a different route.
#
# **What it does NOT catch, measured rather than assumed.** The case that prompted it was a
# RENUMBER: two parallel sessions took B386 on 2026-09-10, one moved to B389, and the citations were
# nine files. Reverting one of them to B386 was run against this check and it PASSED — because the
# peer's B386 entry exists, so the stale citation resolves, at somebody else's bug. Nothing textual
# separates "cites the wrong existing entry" from "cites the right one"; a check cannot have that.
# The claim this header made before that sabotage — that it would have caught the miss — was false.
#
# So what it catches is the narrower thing: **a citation that arrives NOWHERE.** Sabotaged to a
# three-digit number nobody has taken, it fails and names the file and line. That covers a deleted
# entry, a typo, and a renumber into a free number — not a renumber that collides with a live one.
#
# **This file is inside its own search, and that is deliberate — so an example cannot be spelled
# out.** The first draft wrote the sabotage number literally in this header, which made the header a
# dangling citation and failed the gate on its first run. A comment in `build/` is held to the same
# promise as a comment in `managed/`, which is the point; it just means naming a fake number here is
# the one thing this file cannot do.
#
# **Two-and-three-digit numbers only.** B1 through B9 are not matched, deliberately: a single letter
# and digit appears in enough unrelated text that the false positives would cost more than the six
# real findings below. Every B-number in this project is at least two digits.
set -euo pipefail

risks="${1:-docs/RISKS.md}"

if [ ! -f "$risks" ]; then
    echo "assert-risk-citations: no such file: $risks" >&2
    exit 1
fi

entries="$(grep -oE '^#{2,4} B[0-9]+' "$risks" | grep -oE 'B[0-9]+' | sort -u || true)"

if [ -z "$entries" ]; then
    # **A check that matches nothing passes**, which is the failure this file guards against. If the
    # heading style changes, this must fail rather than quietly approve every citation in the tree.
    echo "assert-risk-citations: found no risk headings in $risks — the pattern is stale" >&2
    exit 1
fi

cited="$(
    grep -rhoE '\bB[0-9]{2,3}\b' \
        managed tools tests build docs/memory \
        --include=*.cs --include=*.sh --include=*.md 2>/dev/null | sort -u || true
)"

if [ -z "$cited" ]; then
    echo "assert-risk-citations: found no B-number citations at all — the search is stale" >&2
    exit 1
fi

# **The six that already dangled when this check was written, named rather than hidden.** Each is a
# substantive comment in live source citing an entry `RISKS.md` does not have — B264 and B266 from the
# renderer, B265 from the timeline, B251 from a viewmodel test, B295 from a probe, B381 from the brush
# tool-surface work. They are listed here instead of being fixed because writing six entries for
# defects this session did not diagnose would be inventing history, and an entry invented to satisfy a
# check is worse than the dangling citation it replaces.
#
# **This list only shrinks.** Adding to it is how the check stops meaning anything; if a name belongs
# here, the entry it needs belongs in RISKS.md instead.
known="B251 B264 B265 B266 B295 B381"

dangling=""

for number in $cited; do
    echo "$entries" | grep -qx "$number" && continue
    echo " $known " | grep -q " $number " && continue

    dangling="$dangling $number"
done

if [ -n "$dangling" ]; then
    echo "assert-risk-citations: these are cited but have no entry in $risks:" >&2

    for number in $dangling; do
        echo >&2
        echo "  $number, cited at:" >&2
        grep -rn "\b$number\b" managed tools tests build docs/memory \
            --include=*.cs --include=*.sh --include=*.md 2>/dev/null | head -3 | sed 's/^/    /' >&2
    done

    echo >&2
    echo "Either the entry was renumbered and a citation was missed, or it was never written." >&2
    exit 1
fi

# **The known-absent count is PRINTED, so the debt is visible on every run rather than only in this
# file.** A grandfathered list nobody sees is how a temporary exception becomes permanent.
echo "citations: $(echo "$cited" | wc -l | tr -d ' ') B-numbers cited, all resolve;" \
     "$(echo "$known" | wc -w) known-absent still owed an entry"
