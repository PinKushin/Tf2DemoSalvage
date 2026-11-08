using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>The drawn entities as the client shadow manager reads them — each one's <c>ShadowCastType</c> and bounds.</summary>
/// <remarks>
/// **`C_BaseEntity::ShadowCastType`** (`c_baseentity.cpp:1502-1509`): render-to-texture for a studio model, none otherwise.
/// **`C_TFPlayer`** (`c_tf_player.cpp:9255-9303`): a drawn player is <c>SHADOWS_RENDER_TO_TEXTURE_DYNAMIC</c>, and its
/// shadow bounds are its render bounds fattened 36 in x and y — "the rotation of the guy's upper torso can place his gun
/// a ways out of his bbox". A player not drawn — the first-person one — is not on the drawn list, so casts nothing.
///
/// <c>EF_NOSHADOW</c> casts none, for both (`c_baseentity.cpp:1504`, `c_tf_player.cpp:9264`). An attached prop whose
/// parent is not drawn is skipped: its pose is relative to that parent, so it has no origin of its own to cast from.
/// A corpse casts nothing yet (finding 107).
/// </remarks>
public sealed class ShadowEntities
{
    /// <summary><c>g_flFattenAmt</c> as <c>GetShadowRenderBounds</c> sets it.</summary>
    public const float PlayerFatten = 36f;

    /// <summary>The entities drawn this frame, kept to be reused.</summary>
    private readonly HashSet<int> _present = [];

    /// <summary>The drawn props as shadow entities.</summary>
    /// <param name="drawn">What the moment draws.</param>
    /// <param name="isPlayer">Whether an entity is a player.</param>
    /// <param name="renderBounds">A prop's render bounds in its own frame.</param>
    /// <param name="into">Where they go, cleared first.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Gather(
        IReadOnlyList<SceneProp> drawn,
        Func<int, bool> isPlayer,
        Func<SceneProp, (Vector3 Min, Vector3 Max)> renderBounds,
        ICollection<ShadowEntity> into)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(isPlayer);
        ArgumentNullException.ThrowIfNull(renderBounds);
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

            bool player = isPlayer(prop.EntityIndex);
            ShadowCast cast = player ? ShadowCast.Dynamic : ShadowCast.RenderToTexture;

            if (prop.Kind != SceneModelKind.Studio || prop.FirstTick is not null || prop.Pose.NoShadow)
            {
                cast = ShadowCast.None;
            }

            (Vector3 min, Vector3 max) = renderBounds(prop);

            if (player)
            {
                min -= new Vector3(PlayerFatten, PlayerFatten, 0f);
                max += new Vector3(PlayerFatten, PlayerFatten, 0f);
            }

            into.Add(new ShadowEntity(
                prop.EntityIndex,
                prop.AttachedTo,
                cast,
                new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll),
                min,
                max,
                prop.ModelPath,
                (prop.Pose.Sequence, prop.Pose.Cycle)));
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
