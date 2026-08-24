---
name: research-before-code
description: Guess, verify against a source, then code — skipping the verify step is what costs. Corroborated by this project's own history — the decoder worked correctly before a specimen ever existed to check it against.
metadata:
  type: project
---

The working method: form a hypothesis, verify it against a published source (SDK, changelog, shipped
binary), only then write the code. Skipping straight to code on a guess is what has cost sessions
here.

**Confirmed by the project's own history in the strongest possible way.** Owner, 2026-08-24: *"i was
going to make this with or without demo examples, and pray it worked for untested demos, because
there is plenty of information available to reverse the changes and account for them without
actually having to have a demo from every protocol, or client ever. we did most of our demo decode
work before we ever had a launch tf2 client, but it worked as soon as we passed a demo in because the
changes had all been documented online or by referencing earlier sdk's."*

The decode logic was built and believed correct **before any demo existed to test it against**, from
published changelogs and earlier SDK branches — and it worked on the first real file. The corpus
never taught the parser anything; it corroborated a design already correct by construction. See
`docs/DECISIONS.md` D5.

**How to apply:** a corpus or dating gap is not blocked work. The schema-driven read (D1/D2) is the
actual bet, not insurance against not having a specimen — treating an open corpus gap as blocking is
the category error this memory exists to prevent. Related: [[era-axis-is-measured]],
[[a-client-dates-a-protocol-a-demo-does-not]].
