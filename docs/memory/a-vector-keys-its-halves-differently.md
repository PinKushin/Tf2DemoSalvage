---
name: a-vector-keys-its-halves-differently
description: A SendPropUtlVector of plain props keys elements FLAT; its length keys by PATH. Copying the m_AnimOverlay reader finds the length and none of the elements.
metadata:
  type: project
---

`EntityStateTable` keys a decoded property by its PATH only when the flattener marked it
element-scoped, and that mark is set for a **datatable** member named `lengthproxy` or all digits
(`SchemaFlattener.cs:237`). A plain property never sets it. So one `SendPropUtlVector` is keyed two
ways at once:

- `m_AnimOverlay` elements ARE sub-tables → `…m_AnimOverlay.000.m_nSequence` (a path).
- `m_hActorList` elements are plain `EHANDLE`s → `_ST_m_hActorList_16.000` (flat, table-prefixed).
- Its length reaches its property THROUGH `lengthproxy` → `m_hActorList.lengthproxy.lengthprop16`.

**Why:** the element's flat name already carries its index, so the collision `ElementScoped` exists to
prevent cannot happen for a plain-prop vector. Both spellings are correct.

**How to apply:** when reading a networked vector, do NOT copy `EntityState.AnimationLayers`'s
`"." + key` leading-dot match — it matches `.m_AnimOverlay.` and never `._ST_m_hActorList_16.`. Match
the member name without a leading dot, treat any key containing `lengthprop` as the count, and take
the index from the tail after the last `.`. Before believing an empty vector, decompile the demo
(`Cli -t -e`) and grep for the property name: the trace prints the flat spelling, which is the one the
reader must accept. 1,824 scene playbacks reporting 0 actors was this, not a fact about TF2.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[key-a-lookup-on-the-question]],
[[an-empty-search-needs-a-control]], [[wire-names-are-strings]].
