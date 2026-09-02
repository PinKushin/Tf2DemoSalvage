---
name: a-tick-encoded-value-expires
description: A property encoded against the tick count must be converted at receipt, against the SERVER's tick — and a histogram bimodal at its clamp is what a wrong base looks like.
metadata:
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-02T00:00:00.000Z
---

**`SPROP_ENCODED_AGAINST_TICKCOUNT` means the value stops meaning anything when the packet ends.**
`m_flSimulationTime` is eight unsigned bits holding an offset from
`100 * floor( (tick − entindex % 32) / 100 )`, re-centred within ±127 ticks of now
(`server/baseentity.cpp:265`, `client/c_baseentity.cpp:344`). Two consequences, and both were
learned by getting them wrong:

**Convert at receipt, never on read.** This decoder RETAINS properties across packets by design, so
an offset read one packet later is decoded against a different base and yields a plausible tick up
to 128 out. The engine cannot make this mistake — its receive proxy runs during decode and the raw
offset never survives the packet. Ours had to move into `EntityStateTable.Apply` for the same
reason.

**The base is the SERVER's tick, from `net_Tick` — not the demo's command tick.** A demo's own ticks
start near zero while the server has been up for hours, so the two are unrelated numbers of similar
shape. `net_Tick` was decoded by this project and used by nothing, which is why the difference had
never surfaced. Same family as [[demo-ticks-do-not-start-at-zero]], one level up: there the demo's
ticks do not start at zero; here they are not the server's ticks at all.

## The signature: bimodal at the clamp

Both mistakes produced the same picture — a histogram of "packet tick minus decoded tick" with
roughly half the mass in each end bucket of a ±8 clamp and almost nothing between:

```
  delta  -8:  1503 (50.02%)
  delta  -1:    37 (1.23%)
  delta   0:    43 (1.43%)
  delta   8:  1421 (47.29%)
```

**That is noise wearing the shape of a distribution.** A quantity decoded against the wrong base is
uniform over its window, and a clamp turns uniform into two spikes — which reads like a finding.
With the base right, the same demo shows clusters: 81% at −4, 6% at 0, 13% at ≥ +8.

**So: before believing a spread, check whether the ends are the clamp.** Label the end buckets
`<=-8` and `>=+8` rather than `-8` and `+8`, so a clamp cannot be read as a measurement. And keep a
control bucket for "the value never arrived" — while that is zero, the distribution describes the
demo rather than describing which entities happened to answer.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[an-empty-search-needs-a-control]],
[[a-dropped-field-falls-to-a-computed-default]].
