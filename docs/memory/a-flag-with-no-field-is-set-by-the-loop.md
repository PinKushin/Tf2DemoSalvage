---
name: a-flag-with-no-field-is-set-by-the-loop
description: A flag the file does not store is not therefore constant — read the loop that unserialises the record, not the site that tests it.
metadata:
  type: feedback
---

`CDetailModel::m_bFlipped` mirrors a detail sprite and is not a field of `DetailObjectLump_t`.
Reading only the test site (`DrawTypeSprite`) supported a plausible, wrong conclusion: no field, so
every sprite takes one branch and the swap always applies. That shipped.

The rule lives in the loop that builds the objects:
```cpp
bool bFlipped = true;
while ( --count >= 0 )
{
    bFlipped = !bFlipped;          // detailobjectsystem.cpp:1771
```
It alternates with position in the file (counting every object, not every sprite), so half the map's
grass mirrors. The engine states it a second way too — a whole second dictionary, coordinates
pre-swapped — which makes the two look like different mechanisms.

**Why:** a field's absence from a struct is evidence about the STRUCT, not the value.

**How to apply:** when a flag is tested but not stored, grep every assignment of it before concluding
anything. The tell is "so it is always X" — if it were always X, the engine wouldn't carry it.
Related: [[the-base-is-not-the-behaviour]], [[read-the-encoder-not-the-decoder]],
[[a-guard-you-remove-may-be-the-mechanism]].
