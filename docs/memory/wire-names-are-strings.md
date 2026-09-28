---
name: wire-names-are-strings
description: "SENDINFO_NAME sends under its second argument, so a property's wire name can differ from its C++ member — search for the string, not the identifier."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:30.008Z
---

**A send prop's wire name is not always its C++ member name.** `SENDINFO_NAME(varName,
remoteVarName)` sends under the SECOND argument — seventeen SDK uses, six distinct aliases (e.g.
`m_hMoveParent`→`moveparent`, `m_flValue`→`m_iRawValue32`).

**Rule: search the SDK for a property name as a STRING, not an identifier** — grepping the member
name finds nothing for an aliased property, reading as "the engine doesn't send this".

**One alias states an ENCODING, not just a rename:** `econ_item_view.cpp:67` — an econ item's float
value is sent as a 32-bit UNSIGNED INT — the float's bit pattern reinterpreted. Every TF2 item
attribute (paint, unusuals, killstreaks) goes through it. Fails as a plausible number, per
[[numeric-decoding-traps]].

**This cost real time twice, both from the same false negative** — a scraper capturing only the
FIRST `SENDINFO` argument left every alias out of its denominator, so a conformance test accused
correct code of reading an "undeclared" name; earlier, the same gap led someone to write "will never
appear in a SENDINFO" as a fact into a test — a regex limitation defended by an assertion. See
[[the-denominator-decides-what-can-be-lost]].

Related: [[nothing-is-closed]].

## And the receive side records names the send side no longer has

`RECVINFO_NAME` is the same trick client-side, and it's the ONLY record of a wire name TF2 has
RETIRED — an old pre-2013 name kept for demo compatibility, per Valve's own comment
(`c_baseanimating.cpp:180`). It looked like dead content (no other reader touches it) and isn't —
it's the second half of an alias.

**The corpus splits on it** (B271): pre-2013 era specimens declare the OLD name (`m_flModelWidthScale`)
and not the new one; the 2013 build and z1800 declare the reverse. Reading only the modern name
silently gave every pre-2013 entity the default scale.

**Rule: the SDK is ONE BUILD's snapshot; this project reads thirteen years of demos.** "No send
table declares it" is not "no demo carries it" — where they disagree, the demo wins
([[the-demo-dates-its-own-fields]]).
