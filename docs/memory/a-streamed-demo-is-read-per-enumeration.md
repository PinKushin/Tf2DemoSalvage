---
name: a-streamed-demo-is-read-per-enumeration
description: DemoCommandReader.Read(Stream) is single-shot and DemoCommandCollection re-reads the file per foreach; materialise before any assertion that may enumerate twice, and count a pass as a file read (B449).
metadata:
  type: project
---

`DemoCommandReader.Read(Stream)` yields commands off a stream that it consumes; enumerating the result a second
time continues from wherever the stream stopped. `DemoCommandCollection` re-opens the file on each
`GetEnumerator`, so it is safe to walk twice but every walk is a full read of the file.

**Why:** 2026-10-01, B449. A test passed `DemoCommandReader.Read(stream)` straight to Shouldly's
`ShouldHaveSingleItem`, which enumerates more than once — it failed with "Sequence contains no elements" on
correct code, and under sabotage with an unrelated "Unrecognised demo command 0". The instrument broke, not the
reader. Separately, the CLI's `Report` used to walk the command list four times (`Count`, `Any`, `GroupBy`,
`Count`); over a 2 GB streamed demo that is four file reads, so it now counts kinds in one walk.

**How to apply:**
- In a test, `List<DemoCommand> commands = [.. DemoCommandReader.Read(stream)];` before asserting.
- A writer taking `IReadOnlyCollection<DemoCommand>` may get a streamed one: fold its counts into one `foreach`,
  never chain LINQ queries over it.
- Sabotage of a short-read check needs a cut exactly one byte short of each region (prologue, length, payload);
  cuts that land mid-region let `< count - 1` survive.
- Related: [[a-corpus-sweep-holds-one-demo-at-a-time]], [[instrument-bugs-outnumber-decoder-bugs]].
