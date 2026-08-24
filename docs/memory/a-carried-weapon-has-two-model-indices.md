---
name: a-carried-weapon-has-two-model-indices
description: A weapon's m_nModelIndex is its VIEW model; the world model is m_iWorldModelIndex, and reading the wrong one draws first-person arms as scenery.
metadata:
  type: project
---

`CBaseCombatWeapon` precaches and networks **two** model indices
(`basecombatweapon_shared.cpp:290`, `SendPropModelIndex(SENDINFO(m_iWorldModelIndex))` at 2870).
A carried weapon's own `m_nModelIndex` is the **view** model; the world model is
`m_iWorldModelIndex`, which is what the client draws from (`tf_weaponbase.cpp:2144`).

Reading `m_nModelIndex` for weapons made all three of a soldier's weapons resolve to
`c_soldier_arms.mdl` and be drawn in the world at his hand, stacked on the real viewmodel.

**Why:** it presented as a first-person visibility bug and it is not one — POV and SourceTV alike,
stopping on death only because the hand empties. Eight theories died to it, all supported by the
screenshots.

**How to apply:** when an entity's model looks wrong, check whether its class networks a second
model index before theorising about visibility. Related: [[ask-whether-the-data-arrived]],
[[a-property-name-needs-its-declaring-table]], [[measure-every-hop-before-blaming-one]].

A viewmodel is separate again: `DT_BaseViewModel` is `BEGIN_NETWORK_TABLE_NOBASE`, sends no origin,
and carries its owner as `m_hOwner`, not `m_hOwnerEntity`.
