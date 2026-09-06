---
name: a-flag-with-no-field-is-set-by-the-loop
description: A flag the file does not store is not therefore constant — read the loop that unserialises the record, not the site that tests it.
metadata:
  type: feedback
---

`CDetailModel::m_bFlipped` mirrors a detail sprite horizontally and is not a field of
`DetailObjectLump_t`. Reading only the site that TESTS it (`DrawTypeSprite`) supported a plausible
and wrong conclusion: no field, no shape path, therefore every sprite from a file takes one branch
and the swap always applies. That shipped.

The rule lives in the loop that builds the objects:

```cpp
bool bFlipped = true;
while ( --count >= 0 )
{
    bFlipped = !bFlipped;          // detailobjectsystem.cpp:1771
```

It ALTERNATES with position in the file, counting every object rather than every sprite, so half the
map's grass is mirrored. The engine states the same rule a second way — a whole second dictionary
with the coordinates pre-swapped — which makes the two look like different mechanisms.

**Why:** a field's absence from a struct is evidence about the STRUCT, not about the value. Somewhere
between the file and the test site something assigns it, and that assignment is the specification.

**How to apply:** when a flag is tested but not stored, find its every assignment before concluding
anything about its value — grep the member name across the whole file, not just the function you came
for. The tell is a conclusion of the form "so it is always X": if a variable were always X the engine
would not carry it. Related: [[the-base-is-not-the-behaviour]],
[[read-the-encoder-not-the-decoder]], [[a-guard-you-remove-may-be-the-mechanism]].
