#!/usr/bin/env bash
#
# Fails when docs/DECISIONS.md numbers two different decisions the same, or skips a number.
#
# **Why this is a gate check rather than a convention.** D20 through D28 each named two unrelated
# decisions for weeks (B118). A session restarted the count at D20 without reading the file, the two
# series interleaved rather than sitting apart, and the ONLY thing distinguishing them was the
# heading level — `## D20` against `### D20`. A citation carries no heading level, so both resolved
# to two entries at once, and both were cited from live source comments: half the "D20" references
# in the repository pointed each way.
#
# Nothing failed while that was true. The log read normally, every entry was present, and the damage
# was entirely in what a reader following a citation arrived at. That is the same shape as the
# test-count floors next door — a silent loss that needs a machine to notice.
#
# **What counts as an entry heading.** A number followed immediately by `.` or ` —`. Addenda name
# themselves between the number and the dash (`### D15 addendum —`, `#### D21 outcome,`,
# `#### D24 correction —`) and are deliberately NOT entries: several may hang off one decision, which
# is how a correction is recorded without renumbering anything.
set -euo pipefail

file="${1:-docs/DECISIONS.md}"

if [ ! -f "$file" ]; then
    echo "assert-decision-numbers: no such file: $file" >&2
    exit 1
fi

# Entry headings only: `## D17 — ...` and `## D1. ...`, never `### D15 addendum — ...`.
numbers="$(grep -oE '^#{2,4} D[0-9]+( —|\.)' "$file" | grep -oE '[0-9]+' || true)"

if [ -z "$numbers" ]; then
    # **A check that matches nothing passes**, which is the failure this whole file guards against.
    # If the heading style ever changes, this must fail rather than quietly approve everything.
    echo "assert-decision-numbers: found no decision headings in $file — the pattern is stale" >&2
    exit 1
fi

count="$(echo "$numbers" | wc -l | tr -d ' ')"
highest="$(echo "$numbers" | sort -n | tail -1)"

duplicates="$(echo "$numbers" | sort -n | uniq -d || true)"

if [ -n "$duplicates" ]; then
    echo "assert-decision-numbers: these numbers name more than one decision:" >&2
    for n in $duplicates; do
        echo >&2
        grep -nE "^#{2,4} D$n( —|\.)" "$file" >&2
    done
    echo >&2
    echo "Renumber the later one and repoint its citations. See B118." >&2
    exit 1
fi

# A gap means an entry was deleted or misnumbered. Either way a citation to it now goes nowhere,
# and the next writer taking "highest + 1" is working from a number that is already wrong.
if [ "$count" -ne "$highest" ]; then
    # Collected first rather than printed in the loop: a typo'd heading (D99 for D43) otherwise
    # prints one line per number up to it, burying the real one.
    missing=""
    for n in $(seq 1 "$highest"); do
        echo "$numbers" | grep -qx "$n" || missing="$missing D$n"
    done

    echo "assert-decision-numbers: $count decisions but the highest is D$highest." >&2
    echo "Missing:$(echo "$missing" | cut -c1-200)" >&2
    echo "Either a heading is misnumbered, or an entry was deleted and its citations now go nowhere." >&2
    exit 1
fi

echo "decisions: D1..D$highest, each used once"
