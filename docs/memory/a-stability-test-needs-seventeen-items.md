---
name: a-stability-test-needs-seventeen-items
description: "A sort-stability test with 16 or fewer items cannot fail — .NET's introsort hands short runs to insertion sort, which is stable by accident."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T03:18:34.043Z
---

A test asserting that a sort is **stable** proves nothing unless the input is **larger than
sixteen items**. `Array.Sort` / `List.Sort` are introsort, and `ArraySortHelper`'s
`IntrosortSizeThreshold = 16` hands any partition of sixteen or fewer to **insertion sort**, which
is stable — so a short run comes out in order whether or not the comparison carries a tiebreak.

Measured 2026-08-27 on `OpaqueBuckets.InDrawOrder`. A six-element test survived deleting the
`Order.CompareTo(...)` tiebreak entirely. At twenty-four it failed immediately, because
`PickPivotAndPartition` swaps the middle element to `hi - 1` before comparing anything, so an
all-equal run is reordered on the very first partition.

**Why:** this is [[boundaries-find-what-tests-cannot]]' "wrong condition" case — an input for
which the correct and broken implementations predict the *same* observation. The instinct on
finding a test that will not go red is to strengthen the assertion; here the assertion was already
exact (`ShouldBe` on the full sequence) and only the input was too small. See
[[real-data-hides-bugs-small-inputs-expose]] for the mirror image, where the input was too *large*.

**How to apply:** any test whose subject is ordering-among-equals needs at least 17 items, and 24
is a safer round number. Before trusting it, delete the tiebreak and watch it fail — a stability
test that has never been red is measuring insertion sort, not your comparison. The same threshold
question applies to any claim about a library's algorithm: ask what size the implementation
switches strategies at, because that size is where the test becomes sensitive.
