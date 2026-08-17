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

Related: [[shipped-data-is-a-source]], [[tf2-game-code-is-in-the-sdk]].
