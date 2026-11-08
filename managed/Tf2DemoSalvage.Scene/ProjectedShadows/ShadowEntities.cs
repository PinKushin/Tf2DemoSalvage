using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>Where the physics has put an entity it simulates, as the shadow manager reads it.</summary>
/// <param name="Origin">
/// Its render origin: a corpse's <c>m_pRagdoll-&gt;GetRagdollOrigin()</c>, the first physics object's position
/// (`ragdoll.cpp:177-181`); a gib's physics object's position.
/// </param>
/// <param name="Angles">A gib's angles from its physics object; unused for a corpse, whose render angles are none.</param>
/// <param name="RagdollRadius">
/// A corpse's <c>CRagdoll::m_radius</c> (<see cref="ShadowEntities.RagdollRadius"/>), whose cube is its bounds; null for a
/// gib, which is no ragdoll.
/// </param>
public readonly record struct ShadowPhysics(Vector3 Origin, (float Pitch, float Yaw, float Roll) Angles, float? RagdollRadius);

/// <summary>The drawn entities as the client shadow manager reads them — each one's <c>ShadowCastType</c> and bounds.</summary>
/// <remarks>
/// **`C_BaseEntity::ShadowCastType`** (`c_baseentity.cpp:1502-1509`): render-to-texture for a studio model, none otherwise.
/// **`C_BaseAnimating::ShadowCastType`** (`c_baseanimating.cpp:820-851`), which every TF2 studio entity but the player
/// runs: <c>SHADOWS_RENDER_TO_TEXTURE_DYNAMIC</c> for a model with sequences and any pose parameter, bone controller or IK
/// chain — a sentry, a door — redrawn every frame it is in view; plain render-to-texture otherwise.
/// **`C_TFPlayer`** (`c_tf_player.cpp:9255-9303`): a drawn player is <c>SHADOWS_RENDER_TO_TEXTURE_DYNAMIC</c>, and its
/// shadow bounds are its render bounds fattened 36 in x and y — "the rotation of the guy's upper torso can place his gun
/// a ways out of his bbox". A player not drawn — the first-person one — is not on the drawn list, so casts nothing.
///
/// <c>EF_NOSHADOW</c> casts none, for both (`c_baseentity.cpp:1504`, `c_tf_player.cpp:9264`). An attached prop whose
/// parent is not drawn is skipped: its pose is relative to that parent, so it has no origin of its own to cast from.
///
/// **A simulated corpse** (`IsRagdoll()`) is render-to-texture whatever its model, at its first physics object with no
/// angles, bounded by <c>CRagdoll</c>'s radius cube; a gib casts by its model's rule from its physics object's placement
/// (<see cref="ShadowPhysics"/>, finding 107). A corpse with no simulation, and a stuck arrow, cast nothing.
/// </remarks>
public sealed class ShadowEntities
{
    /// <summary><c>g_flFattenAmt</c> as <c>GetShadowRenderBounds</c> sets it.</summary>
    public const float PlayerFatten = 36f;

    /// <summary>
    /// <c>CRagdoll::m_radius</c> — <c>BuildRagdollBounds</c> (`ragdoll.cpp:81-90`): <c>|(maxs − mins) · 0.5|</c> of
    /// <c>modelinfo-&gt;GetModelBounds</c>.
    /// </summary>
    /// <param name="hull">The header's movement hull, <c>hull_min</c>/<c>hull_max</c>.</param>
    /// <param name="firstSolid">The model's first collision solid's box at the origin with no turn (<c>CollideGetAABB</c>),
    /// or null when it has none.</param>
    /// <returns>The radius.</returns>
    /// <remarks>
    /// **`GetModelBounds` reads <c>model_t</c>'s box** (`engine.dll` `0x1801cc160`, `+0x28`/`+0x34`). For a studio model it is
    /// set from the header's HULL, not the view box, when the header loads (`FUN_1801060b0`, `studiohdr_t + 0x68`/`+ 0x74`),
    /// and when the `.phy` loads it is set again and grown to take in both corners of solid 0's box at `vec3_origin` and
    /// `vec3_angle` (`FUN_1800fca70`, `MDLCACHE_VCOLLIDE`: `CollideGetAABB` through slot 23 of `physcollision`, then
    /// `AddPointToBounds` `FUN_1802779a0` twice). `CRagdoll::Init` asks for the collide before it builds the bounds
    /// (`ragdoll.cpp:106`, `:133`), so the `.phy` is loaded by then.
    /// </remarks>
    public static float RagdollRadius(
        StudioBox hull, ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max)? firstSolid)
    {
        StudioBox bounds = firstSolid is { } solid
            ? hull.Union(new StudioBox(solid.Min.X, solid.Min.Y, solid.Min.Z, solid.Max.X, solid.Max.Y, solid.Max.Z))
            : hull;

        return (new Vector3(bounds.MaxX - bounds.MinX, bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ) * 0.5f).Length();
    }

    /// <summary>The entities drawn this frame, kept to be reused.</summary>
    private readonly HashSet<int> _present = [];

    /// <summary>The drawn props as shadow entities.</summary>
    /// <param name="drawn">What the moment draws.</param>
    /// <param name="isPlayer">Whether an entity is a player.</param>
    /// <param name="renderBounds">A prop's render bounds in its own frame.</param>
    /// <param name="animates">Whether a prop's model animates its shadow (<see cref="PropModels.ModelFrames.AnimatingShadow"/>).</param>
    /// <param name="falloffBias">A prop's shadow falloff bias from its last <c>ComputeFxBlend</c>.</param>
    /// <param name="physics">Where the physics has put a corpse or a gib, or null for anything it does not place.</param>
    /// <param name="into">Where they go, cleared first.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Gather(
        IReadOnlyList<SceneProp> drawn,
        Func<int, bool> isPlayer,
        Func<SceneProp, (Vector3 Min, Vector3 Max)> renderBounds,
        Func<SceneProp, bool> animates,
        Func<SceneProp, byte> falloffBias,
        Func<SceneProp, ShadowPhysics?> physics,
        ICollection<ShadowEntity> into)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(isPlayer);
        ArgumentNullException.ThrowIfNull(renderBounds);
        ArgumentNullException.ThrowIfNull(animates);
        ArgumentNullException.ThrowIfNull(falloffBias);
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();
        _present.Clear();

        foreach (SceneProp prop in drawn)
        {
            _present.Add(prop.EntityIndex);
        }

        foreach (SceneProp prop in drawn)
        {
            if (prop.AttachedTo is { } parent && !_present.Contains(parent))
            {
                continue;
            }

            // `C_TFPlayer` is always dynamic; anything else is `C_BaseAnimating`'s, dynamic when its model animates — but
            // never for a ragdoll, whose `ShadowCastType` skips that test (`c_baseanimating.cpp:830-843`).
            bool player = isPlayer(prop.EntityIndex);
            ShadowPhysics? placed = physics(prop);
            float? radius = placed?.RagdollRadius;
            ShadowCast cast = player || (radius is null && animates(prop)) ? ShadowCast.Dynamic : ShadowCast.RenderToTexture;

            if (prop.Kind != SceneModelKind.Studio || (prop.FirstTick is not null && placed is null) || prop.Pose.NoShadow)
            {
                cast = ShadowCast.None;
            }

            (Vector3 min, Vector3 max) = radius is { } r ? (new Vector3(-r * prop.Pose.Scale), new Vector3(r * prop.Pose.Scale)) : renderBounds(prop);

            if (player)
            {
                min -= new Vector3(PlayerFatten, PlayerFatten, 0f);
                max += new Vector3(PlayerFatten, PlayerFatten, 0f);
            }

            (float, float, float) angles = placed?.Angles ?? (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll);

            // A ragdoll's render angles are `vec3_angle` (`c_baseanimating.cpp:4594-4604`).
            if (radius is not null)
            {
                angles = default;
            }

            into.Add(new ShadowEntity(
                prop.EntityIndex,
                prop.AttachedTo,
                cast,
                placed?.Origin ?? new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                angles,
                min,
                max,
                prop.ModelPath,
                (prop.Pose.Sequence, prop.Pose.Cycle),
                falloffBias(prop),
                radius is not null));
        }
    }

    /// <summary>
    /// The drawn brush entities that can take a shadow — `*N` models, at their render origin and angles, bounded by their
    /// model's mins and maxs (<c>GetModelRenderBounds</c>).
    /// </summary>
    /// <param name="drawn">What the moment draws.</param>
    /// <param name="models">The map's models.</param>
    /// <param name="into">Where they go, cleared first.</param>
    /// <remarks>
    /// **`ShouldReceiveProjectedTextures`** (`c_baseentity.cpp:1566-1590`) as `AddShadowToReceiver` asks it, with the whole
    /// type mask: so its flashlight branch, which refuses a render mode above normal at alpha 0 (an undrawn entity is not
    /// on the list). The leaf system's own ask, with the shadow flag (`clientleafsystem.cpp:950-957`), refuses
    /// <c>EF_NORECEIVESHADOW</c> (`c_baseentity.cpp:1583`).
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Brushes(IReadOnlyList<SceneProp> drawn, IReadOnlyList<BspModel> models, ICollection<ShadowBrush> into)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (SceneProp prop in drawn)
        {
            int index = BspModels.IndexOf(prop.ModelPath);

            if (index <= 0 || index >= models.Count || prop.Pose.NoReceiveShadow
                || (prop.Pose.RenderMode > 0 && prop.Pose.RenderAlpha == 0))
            {
                continue;
            }

            BspModel model = models[index];

            into.Add(new ShadowBrush(
                new ShadowEntity(
                    prop.EntityIndex,
                    null,
                    ShadowCast.None,
                    new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                    (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll),
                    new Vector3(model.Minimum.X, model.Minimum.Y, model.Minimum.Z),
                    new Vector3(model.Maximum.X, model.Maximum.Y, model.Maximum.Z),
                    prop.ModelPath),
                model.FirstFace,
                model.FaceCount));
        }
    }
}
