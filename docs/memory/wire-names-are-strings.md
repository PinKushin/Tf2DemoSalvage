---
name: wire-names-are-strings
description: SENDINFO_NAME sends under its second argument, so a property's wire name can differ from its C++ member — search for the string, not the identifier.
metadata:
  type: project
---

**A send prop's wire name is not always its C++ member name.** `SENDINFO` names it after the member;
`SENDINFO_NAME(varName, remoteVarName)` sends under the **second** argument. Seventeen uses in the
SDK, six distinct aliases:

| C++ member | wire name |
|---|---|
| `m_hMoveParent` | `moveparent` |
| `m_MoveType` | `movetype` |
| `m_MoveCollide` | `movecollide` |
| `m_nEntIndex` | `entindex` |
| `m_flHDRColorScale` | `HDRColorScale` |
| `m_flValue` | **`m_iRawValue32`** |

**The rule: search the SDK for a property name as a STRING, not as an identifier.** The wire carries
the string, and grepping the member name finds nothing for an aliased property — which reads as "the
engine does not send this".

**The last row is not just a rename, it states the encoding.** `econ_item_view.cpp:67`:

```c
SendPropInt( SENDINFO_NAME(m_flValue, m_iRawValue32), 32, SPROP_UNSIGNED ),
```

The member is `CNetworkVar( float, m_flValue )` and the prop is a **32-bit unsigned int**. An econ
attribute's value therefore travels as the float's bit pattern reinterpreted as an integer —
**1065353216 where the value is 1.0** — and every TF2 item attribute goes through it: paint, unusual
effects, killstreaks, every balance change. Fails as a plausible number, per
[[numeric-decoding-traps]].

**This cost real time twice, both from the same false negative.** A scraper capturing only
`SENDINFO`'s first argument left every alias out of its denominator, so a conformance test accused
correct code of reading a name "no send table declares". And earlier, someone hitting that same gap
concluded `moveparent` was special and wrote it into a test: *"it will never appear in a SENDINFO"*.
A regex limitation recorded as a fact about the format, then defended by an assertion — see
[[an-uncoverable-gap-is-usually-your-reader]].

Related: [[nothing-is-closed]].

## And the receive side records names the send side no longer has

`RECVINFO_NAME(varName, remoteVarName)` is the same trick on the client, and it is the **only**
record of a wire name TF2 has RETIRED. `c_baseanimating.cpp:180`:

```c
RecvPropFloat(RECVINFO(m_flModelScale)),
RecvPropFloat(RECVINFO_NAME(m_flModelScale, m_flModelWidthScale)), // for demo compatibility only
```

Two receivers, one member. `m_flModelWidthScale` is the model scale under the name TF2 used before
2013, and **Valve's comment names demos as the reason it survives** — so it is exactly this
project's business.

**It looked like dead content and it is not.** Nothing in `src/game` reads `m_flModelWidthScale`
outside that one line, which is the same signature as `$modblend` — a parameter declared and
consumed by nothing. The difference is that `$modblend` had no consumer *anywhere* while this one is
the second half of an alias, and telling them apart takes reading the declaration rather than
counting references.

**The corpus splits on it** (B271): the 2007, 2008, 2009 and 2011 era specimens declare
`DT_BaseAnimating.m_flModelWidthScale` and no `m_flModelScale`; the 2013 build and z1800 declare the
reverse. Reading one name meant every entity in every pre-2013 demo silently took the default scale.

**The rule this adds: the SDK is ONE BUILD's snapshot, and this project reads thirteen years of
demos.** "No send table declares it" is not "no demo carries it". Where the two disagree the demo
wins, because [[the-demo-dates-its-own-fields]] — its schema is the contract it was actually
recorded against. A conformance denominator built only from `SENDINFO` will accuse correct code the
moment a name predates the snapshot.
