# 19 — Where a networked entity's model comes from, and the packing early protocols applied to it

Everything in a TF2 match that is not the world or a static prop arrives as a **networked entity**:
health packs, ammo packs, dropped weapons, doors, elevators, rockets, buildings, players. Their
models are not in the map file, and the map's entity lump is the wrong place to look for them.

## The client's route, read from published source

`c_baseentity.cpp:449` (evidence: **read from published source**):

```cpp
RecvPropInt( RECVINFO(m_nModelIndex), 0, RecvProxy_IntToModelIndex16_BackCompatible ),
```

The entity carries a *number*. That number indexes the `modelprecache` string table, which the
server sends and the demo therefore contains. Animation comes from the same entity, in
`c_baseanimating.cpp`:

| Property | Line | Meaning |
|---|---|---|
| `m_nSequence` | 173 | which animation |
| `m_flCycle` | 152 | how far through it, 0..1 |
| `m_flPlaybackRate` | 186 | how fast |
| `m_nSkin`, `m_nBody` | 176-177 | which skin and which bodygroup |
| `m_flModelScale` | 180 | uniform scale |

So a viewer that decodes entities already has everything needed to place *and animate* every model
in the match. Nothing about it requires the BSP.

### The wrong turn, recorded because it is the obvious one

The first plan here was to read `item_healthkit_*` and `item_ammopack_*` out of the BSP entity lump
and place Valve's model for each classname, taken from `entity_healthkit.h` and
`entity_ammopack.h`. That works, in the sense that it puts a medkit where the mapper put one.

It is wrong for a reason that only shows up during playback: **a pickup that has been taken is not
there.** The entity lump states where a health pack spawns, not whether it exists at tick 40,000.
It also cannot place anything the map did not author — a dropped weapon, a projectile, a building —
which is most of what moves in a match.

Worth keeping because the mapping it produced is still correct and still documented (Valve's own
classname-to-model table, from the two headers above), and because the naming there is a trap of
its own: `item_healthkit_full` returns `medkit_large.mdl`. The classname says *full*, the model says
*large*, and `item_healthammokit` returns `medkit_medium.mdl` — a third name for a fourth thing.

## The quirk: model indices below −1 were packed on protocol ≤ 20

`recvproxy.cpp:45` (evidence: **read from published source**):

```cpp
void RecvProxy_IntToModelIndex16_BackCompatible( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	int modelIndex = pData->m_Value.m_Int;
	if ( modelIndex < -1 && engine->GetProtocolVersion() <= PROTOCOL_VERSION_20 )
	{
		Assert( modelIndex > -20000 );
		modelIndex = -2 - ( ( -2 - modelIndex ) << 1 );
	}
	*(int16*)pOut = modelIndex;
}
```

Negative model indices are **dynamic models** — ones the client precached for itself rather than
receiving from the server. On protocol 20 and earlier the engine wrote them packed, and the client
unpacks them with `-2 - ((-2 - index) << 1)`.

Three things make this exactly the class of quirk this project exists for:

- **It is era-specific and the boundary is named.** Protocol 20 and below: that is five of the
  protocols in the corpus (11, 14, 15, 16) and none of the modern ones (24). See
  `docs/TIMELINE.md`.
- **It is invisible when got wrong.** A packed index is still a number. It resolves to *some*
  model, or to no model, and nothing anywhere reports an error — the recurring shape of every
  numeric bug in this codebase.
- **Valve wrote the shim rather than the format.** Nothing in the demo says the packing happened.
  The only way to know is to read the client, which is why this is filed here rather than in
  `SPEC.md`.

`-1` is excluded by Valve's own condition. It means "no model", not a packed value, and unpacking
it would produce a plausible negative index out of an explicit absence.

### What a negative index means for a demo

A recording of somebody else's session cannot resolve one: the model was precached by *their*
client and the table carries no entry. `ModelPrecache.Path` answers `null` rather than guessing,
which is the whole point — the alternative is reading an unrelated entry and drawing a confident
wrong model.

## Static props are the exception, and the engine agrees

Static props do come from the map file, and in the engine they are a separate system
(`StaticPropMgr`) precisely because nothing about them is networked. That split is why this project
reads the BSP for props and the entity stream for everything else, rather than picking one source
and forcing it.
