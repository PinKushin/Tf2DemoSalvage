# 74 — A cloak is a second pass, and an overlay is a slot with a history

Two TF2 effects that look like "fade the spy out" and "tint the screen", and are neither. Both were read from
published source (`source-sdk-2013`); the frame copy's binding was settled from `view_scene.h`. B508 and B509 in
`docs/RISKS.md` carry what is done and what is left.

## What we drew before (measured, 2026-10-06)

**A cloaking spy drew solid.** Nothing read `$cloakpassenabled` and no proxy wrote `$cloakfactor`; the only cloak
arithmetic in the viewer was the HUD's (`PlayerInvisibility.Percent`, for the class panel). **No screen overlay was
drawn at all.** The B506 audit had already corrected the filing that counted the `*_overlay` materials as model draws
(findings 73).

## The cloak is the material's own second pass (read from published source)

`VertexLitGeneric_DX9`'s `SHADER_DRAW` (`vertexlitgeneric_dx9.cpp:464-519`) draws three things in order for a material
with `$cloakpassenabled 1`: the standard pass, unless `CloakBlendedPassIsFullyOpaque`; the sheen pass; then
`DrawCloakBlendedPass` while `$cloakfactor` is strictly inside (0, 1). The opaque test assumes `V·N = 0` — the
silhouette, where the cloak is weakest — so `clamp( Lerp( cf, 1, −0.35 ) ) ≤ 0.4` drops the standard pass from
`cf ≥ 0.6 / 1.35 ≈ 0.444` (*arithmetic*). Past that point a cloaking spy is nothing but the cloak pass.

The cloak pass (`cloak_blended_pass_ps2x.fxc`) samples the frame copy at the pixel, displaced by the normal's clip-space
x and y times `lerp( $refractamount, 0, cf )`, blurred by nine Poisson taps scaled by `lerp( 0.05, 0, cf )`, dimmed in
the middle and brightened at the edge by a Fresnel term, and multiplied by `$cloakcolortint` until `cf` passes 0.75.
Its alpha is a smoothstep of the same Fresnel lerp: early in a cloak the facing pixels are already all refraction
while the silhouette still shows the suit.

**The tint is the team, and only on the player himself.** `CSpyInvisProxy::OnBind` writes `$cloakColorTint` — RED
(1, 0.5, 0.4), anything else (0.4, 0.5, 1) — only when the entity bound IS the player (`c_tf_player.cpp:1737-1749`).
`spy_red.vmt` runs `spy_invis` and then `invis`, so the FACTOR on the body is `invis`'s, last-wins.

## Nobody but an enemy ever disappears (read from published source)

`GetEffectiveInvisibilityLevel` caps the level at `tf_teammate_max_invis` 0.95 for anyone who is not an enemy of the
local player — and `IsEnemyPlayer` answers false when the local player has no team. **So a SourceTV viewer sees every
cloaked spy as a 0.95 shimmer**, and a POV recorder sees his own team's the same way; only the recorder's enemies reach
1, where `C_TFPlayer::DrawModel` returns before drawing anything. The one exception: the enemy who just killed the
recorder is capped too, in the deathcam and freezecam.

**The local player's own weapons are not drawn at his level.** `CInvisProxy` remaps the recorder's percent into 0.22
to 0.5 (`tf_viewmodel.cpp:575-594`), drops it to 0 below 0.01 — a double literal, so `0.01f` itself reads as zero —
and pins it at 0.3 on a bump or a dry motion-cloak watch. That is why a cloaked spy's own hands stay visible.

## Which frame the cloak warps (read from published source)

The shader binds `TEXTURE_FRAME_BUFFER_FULL_TEXTURE_0`, which reads like the full-frame texture. It is not:
`UpdateRefractTexture` copies into `_rt_PowerOfTwoFB` and then calls `SetFrameBufferCopyTexture( pTexture )` with
the default index 0 (`view_scene.h:62`), so index 0 IS the power-of-two copy, made before the renderable is drawn. The
copy therefore holds everything opaque behind the spy and none of the spy — the port copies once per model, before its
first batch, for that reason. A cloaking material also answers `IsTranslucent` for the frame, so the spy is collated
with the translucent renderables.

## The overlay is one slot, written by hooks, with a history

`CViewRender::PerformScreenOverlay` draws one material after the viewmodels and before the HUD. The material is what
`view->SetScreenOverlayMaterial` last received, and every caller is a `CTFPlayerShared` condition hook guarded by
`IsLocalPlayer()`. Each `OnAdd*` overwrites the slot; each `OnRemove*` clears it **only when it holds that hook's own
material**. `SyncConditions` runs the hooks bit by bit, ascending, word by word. Consequences the port reproduces:

- **Jarate then bleed shows the bleed; when the bleed ends nothing shows**, though the jarate still holds — no hook
  re-adds it.
- **The swimming curse and urine share `effects/jarate_overlay`**, so ending either clears the other's.
- **Ending the plague clears the bleed's overlay** (`OnRemovePlague`).
- **A spy's own cloak sets no overlay.** `OnAddStealthed` sets `effects/stealth_overlay` only under
  `TF_COND_STEALTHED_USER_BUFF` — the Halloween spell — and `effects/stealth_overlay.vmt` as shipped is not even
  Refract: its Refract block is commented out, leaving a grey VertexLitGeneric.
- **Mad Milk has no overlay**: `OnAddMadMilk`'s is commented out.
- **Catching fire sets `effects/imcookin`, which draws nothing.** It is UnlitTwoTexture, additive, with `$color` written
  by `BurnLevel`; `PerformScreenOverlay` binds it with no entity, so `CProxyBurnLevel` takes its `!pEntity` branch and
  writes 0. It still displaces whatever overlay was showing. *Read from source; not seen in TF2.*

**Measured on the corpus** (`cloak` probe, 2026-10-06): `rgl-pug-2026-08-10-pov`'s recorder takes RED's invulnerability
overlay 13 times; `serveme-627619-stv-2026-08-07`'s spy 7 cloaks from tick 63693 and reaches the 0.95 cap by 63765.

## What the port does not reproduce

The cloak pass's `BUMPMAP` combo rotates the normal map into world space by the mesh's tangent frame, which this
vertex format does not carry; the port derives a cotangent frame from screen derivatives of the same surface
(*interpolated*). It moves only the warp's direction, and the warp shrinks to nothing as the cloak completes. It is
not a cloak question: the model path carries no tangent anywhere, so VertexLitGeneric's own bump lighting lacks the
same frame (RISKS "the highlight uses the VERTEX normal"). The rest is named in B508 and B509.

## The leftovers, and one wrong filing (read from published source, 2026-10-07)

**"A feign-death ragdoll's own cloak" was a wrong filing.** `C_TFRagdoll` does fade itself — `ClientThink` adds
`frametime` to `m_flPercentInvisible` while `m_bCloaked`, to 1 (`c_tf_player.cpp:1392-1399`), and both invisibility
proxies read that off a ragdoll (`spy_invis` writes it with no tint, `invis` leaves it alone) — but the server sets
`m_bCloaked` for one thing only: the victim of a knife that `ShouldDisguiseOnBackstab`, Your Eternal Reward
(`tf_player.cpp:12707-12715`). The Dead Ringer's ragdoll comes from `CreateFeignDeathRagdoll` (`:15795`), which sets
`m_bFeignDeath` and never `m_bCloaked`; the feigning spy himself cloaks by the ordinary player path, his change time
stamped "now" by `OnAddStealthed` (`tf_player_shared.cpp:6999-7003`). So the corpse that vanishes is the backstabbed
one, and it vanishes for everyone, uncapped: the corpse is nobody's teammate.

**A motion cloak is everyone's, not only the recorder's.** `m_bMotionCloak` is latched on the client by
`OnAddStealthed` from the first `TF_WEAPON_INVIS` in `m_hMyWeapons`, which is on the main player table
(`basecombatcharacter.cpp:204`), and the speed fade's other input, `m_flCloakMeter`, is on `DT_TFPlayerShared`
(`tf_player_shared.cpp:586`) — not the local-only table. The earlier note that only the recorder's items carry the
watch was wrong about the wire; it was true of this port's wiring, which asked the HUD's hook.

**The Hightower stealth spell caps its enemies too**: `bLimitedInvis = !IsEnemyPlayer() || bHalloweenSpellStealth`
(`c_tf_player.cpp:6849-6850`), the spell being `TF_COND_STEALTHED_USER_BUFF` while `m_halloweenScenario` is Hightower.

**`effects/stealth_overlay` is drawn by `ViewDrawFade`**, the branch for an overlay needing no frame copy
(`viewrender.cpp:1242-1246`). As shipped it is a translucent VertexLitGeneric over `effects/grey`, whose colour is zero
everywhere (probe `vmt`: mean RGBA 0 0 0 108) — so it darkens the frame by the texture's alpha, and the lighting the
engine gives the fade quad (closed, *interpolated*) multiplies black. **`effects/imcookin` stays nothing**: additive,
`$color` written by `BurnLevel`, and `IVRenderView::ViewDrawFade( byte *color, IMaterial *pMaterial )`
(`ivrenderview.h:255`) carries no entity for the proxy to read, so `CProxyBurnLevel` takes its `!pEntity` branch and
writes 0 (*read from source*; the engine body is closed, the signature is not).

**Jarate, bleed and gas animate their normal map**: `AnimatedTexture` on `$normalmap` at 30 frames a second into
`$bumpframe`, `water/tfwater001_normal` — the overlay now binds that frame, as water does.
