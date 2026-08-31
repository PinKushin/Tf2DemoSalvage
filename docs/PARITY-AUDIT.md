# Parity audit — every branch, not just the one that bit

**The owner's instruction, and it earned its place the hard way:** *"keep auditing for parity, if we
have parity for everything we have implemented, all sides of anything that has more than one branch,
then we can start going on and actually implementing the stuff we still dont have"*.

Every expensive bug of 2026-08-30 was **one branch of a multi-branch engine function, implemented on
one side only**:

| bug | the function | what was missing |
|---|---|---|
| B236 | `C_TFPlayer::GetSkin` + `ValidateModelIndex` | the mask is a SKIN and a BODYGROUP; we did the skin |
| B240 | `C_BaseEntity::ShouldDraw` | the `kRenderNone` test, which is its first line |
| B241 | `C_BaseEntity::CalcAbsolutePosition` | branch 3 of 3, so a parented prop lost its angles |
| B233 | `InitPerClassStringArray` | `basename` — one of the key's two forms |
| B232 | `CTFWearable::ShouldDraw` + `CTFWeaponBase::ShouldDraw` | the weapon half of a mirrored pair |

Not one was found by measuring our own output. Every one was found by reading the engine function
end to end.

## The instrument

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -- parity
dotnet run --project tools/Tf2DemoSalvage.Probe -- parity econ_entity
```

It reads every `file.cpp:line` citation in `managed/` — **405 distinct**, which is the denominator —
finds the enclosing function in `source-sdk-2013`, counts its branch points and ranks them.

**A branch count is a SCREEN, not a verdict.** It cannot tell whether a branch is implemented; it
says where the risk is concentrated so the reading starts where there is most to get wrong. The
reading is still the work.

## Findings

### 1. `attached_models` is not implemented at all — OPEN

`CEconEntity::UpdateAttachmentModels` (`econ_entity.cpp:1078`) is this project's **most-cited**
engine function — eleven citations — and the first thing it does is a mechanism we have never
touched:

```cpp
int iAttachedModels = pItemDef->GetNumAttachedModels( iTeamNumber );
for ( int i = 0; i < iAttachedModels; i++ )
{
    attachedmodel_t *pModel = pItemDef->GetAttachedModelData( iTeamNumber, i );
    ...
    m_vecAttachedModels.AddToTail( attachedModelData );
}
```

An item definition can hang **extra models on itself**, per team, and a festivized variant on top.
The string `attached_models` appears **29 times** in the shipped `items_game.txt` and **zero times**
in `managed/`. Two measured examples:

- the Degreaser's pilot light, `c_degreaser_pilotlight.mdl`
- the Quick-Fix's `c_overhealer.mdl`

So twenty-nine items are drawn with a piece missing, silently, on every demo that contains one.
Nothing reports it because nothing asks: the model is never named, so it never fails to load.

**Note what sits three lines below it in the same block: `custom_particlesystem`.** That is the
unusual-effect and weapon-effect mechanism, and it is on the list of things to build next — the same
function carries both, which is an argument for doing them together.

### 2. Where to read next, by concentration of branches

From the ranked output, ignoring the shader helpers (a separate job):

| branches | function | why it matters here |
|---|---|---|
| 40 | `C_TFRagdoll::CreateTFRagdoll` | death is a separate entity; we already know that half |
| 35 | `C_BaseEntity::ComputeFxBlend` | transcribed for B221 — every fade, pulse and cloak |
| 29 | `C_BaseAnimating::SetupBones` | six citations, and the bone pipeline is load-bearing |
| 29 | `C_BaseAnimating::DoAnimationEvents` | not implemented at all; muzzle flashes and sounds |
| 25 | `CClientLeafSystem::CollateRenderablesInLeaf` | what draws and in which list |
| 19 | `CEconEntity::UpdateAttachmentModels` | finding 1 above |

## The rule this audit exists to enforce

**A rule written in our own comments is not an enforced rule.** `WorldRenderer` said *"a SKINNED
model is put there by its bones and its matrix stays at identity"* and nothing checked it; it held
by accident for months and then cost an evening. When an audit finds an invariant stated in prose,
the finding is not "it is documented" — it is "write the assertion or delete the claim".
