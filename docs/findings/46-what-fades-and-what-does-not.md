# 46 — what fades, and what only looks as though it might

Written alongside B268 and B269. Two mechanisms fade a Source entity out and only one of them is
alive in TF2; a third field exists solely to feed the dead one. The interesting part is not the
arithmetic, which is short, but that the exclusion was first argued from ignorance and only
afterwards from measurement — and the two arguments have very different shelf lives.

## The chain

`C_BaseEntity::ComputeFxBlend` (`game/client/c_baseentity.cpp:3343`) finishes by multiplying in a
client-side fade:

```
unsigned char nFadeAlpha = GetClientSideFade();
if ( nFadeAlpha != 255 )
{
    float flBlend = blend / 255.0f;
    float flFade  = nFadeAlpha / 255.0f;
    blend = (int)( flBlend * flFade * 255.0f + 0.5f );
    blend = clamp( blend, 0, 255 );
}
```

`C_BaseAnimating::GetClientSideFade` (`c_baseanimating.cpp:6532`) is one line:

```
return UTIL_ComputeEntityFade( this, m_fadeMinDist, m_fadeMaxDist, m_flFadeScale );
```

and `UTIL_ComputeEntityFade` (`client/cdll_util.cpp:1103`) takes the **minimum** of three answers:
`ComputeDistanceFade`, `ComputeLevelScreenFade` and `ComputeViewScreenFade`.

*Evidence class: read from published source.*

## The one that is alive: distance

`ComputeDistanceFade` (`cdll_util.cpp:1074`) is published, and three of its properties would each be
wrong if guessed at:

- **The falloff is computed on SQUARED distances.** Both bounds and the current distance are squared
  before interpolating, so alpha is not linear in distance. Halfway between 826 and 900 units is
  130, not 128 — small at that width and larger the wider the band.
- **A minimum above the maximum is SWAPPED, not rejected.** A model with its bounds the wrong way
  round still fades over the same band.
- **A negative minimum means "start 400 units short of the maximum"**, clamped at zero:
  `flMinDist = flMaxDist - 400` is Valve's literal.

That third branch is not defensive padding. Measured on the 2013 SourceTV foundry demo, **28
entities send `m_fadeMinDist -1`** against 8 that declare an ordinary 826 → 900 band, so the
derive-from-maximum path carries more real content than the obvious one.

*Evidence class: read from published source, with counts measured on the corpus.*

## The ones that are dead: screen size

Both screen fades sit behind `modelinfo`, which is engine-side and closed. Neither runs in TF2:

| Fade | Range comes from | Value |
|---|---|---|
| view | `r_screenfademinsize` / `r_screenfademaxsize` (`viewrender.cpp:166`) | both declared `"0"` |
| level | `CWorld`'s `m_flMinPropScreenSpaceWidth` / `m_flMaxPropScreenSpaceWidth` (`world.cpp:406`), applied by `C_World::OnDataChanged` (`c_world.cpp:121`) | **min 0, max −1**, on every map measured |

The view range is a pair of client convars a demo does not carry, at a default that disables them.
The level range **is on the wire**, and every corpus demo whose schema can be read sends the same
pair — nine maps, five protocols, 2007 to the present, `cp_granary` through `cp_foundry`:

```
MaxPropScreenSpaceWidth = -1    MinPropScreenSpaceWidth = 0
```

A maximum below the minimum cannot describe a band, which settles what −1 means without needing the
closed implementation: it is a disabled sentinel, not a very small threshold. The tenth era
specimen, `tf2-2007-build3258-stv-cp_granary`, reports nothing — its `dem_datatables` is truncated
at 65,536 bytes and no entity in it decodes at all. That is a known property of that file rather
than a gap in this measurement, and it is stated because an unexplained blank row is how a
measurement quietly becomes nine-of-ten.

*Evidence class: measured on the corpus, plus arithmetic on the sentinel.*

## `m_flFadeScale` exists only to feed the dead half

It is the fourth argument to `UTIL_ComputeEntityFade` and reaches only `ComputeLevelScreenFade` and
`ComputeViewScreenFade`. `ComputeDistanceFade` never sees it. So a demo viewer that implements the
distance fade correctly has no use for the field at all, and `docs/WIRE-COVERAGE.md` listing it
under "not mentioned anywhere in a shipped assembly" is the right state rather than a gap.

Same shape as `$modblend` in finding 12: a parameter that is declared, transmitted, and read by
nothing.

## The part worth keeping

**The first version of this exclusion was argued from ignorance and it was wrong.** B268's note said
the screen fades were "driven by `r_screenfademinsize`/`maxsize`, engine convars a demo does not
carry" and therefore unknowable. Half of that is true — of the view fade. The level fade's range is
networked by the world entity, and one `baseline` run against `CWorld` says so.

The outcome did not change: both are off, so implementing neither is correct. What changed is
whether the claim can ever be revisited. "Unknowable" is terminal and nobody re-reads it — see
`docs/memory/an-impossibility-claim-expires.md`. "Measured as −1 on nine maps" invites the obvious
follow-up, which is what happens when a map turns up that sets it.
