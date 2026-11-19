using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where a prop is in the world, following its move parents up — <c>CalcAbsolutePosition</c>.</summary>
/// <remarks>
/// **One implementation for the model pass and for everything else that asks <c>GetAbsOrigin()</c>** — a beam's start, a
/// sprite trail's attached entity. It was <c>EntityModels.Absolute</c>, private to the model pass and fed by a table that
/// pass refills three times a frame, so nothing outside it could ask the same question without a second copy of the
/// composition — the drift `docs/memory/one-place-or-it-drifts.md` is about.
/// </remarks>
public static class ParentChain
{
    /// <summary>The pose a prop is AT, walked as deep as a follow chain may legitimately go.</summary>
    /// <param name="prop">The prop.</param>
    /// <param name="byEntity">This frame's props by entity index, or null where an index names none.</param>
    /// <returns>The absolute pose.</returns>
    /// <remarks>Bounded by <c>AnimatingEntity.MaximumFollowDepth</c>, the depth a merge chain is allowed too.</remarks>
    public static ScenePose Absolute(SceneProp prop, Func<int, SceneProp?> byEntity) =>
        Absolute(prop, byEntity, Animation.Animating.AnimatingEntity.MaximumFollowDepth);

    /// <summary>The pose a prop is actually AT, following its parent chain up.</summary>
    /// <param name="prop">The prop.</param>
    /// <param name="byEntity">This frame's props by entity index, or null where an index names none.</param>
    /// <param name="budget">How many parents may still be walked; a chain past it keeps the prop's own pose.</param>
    /// <returns>The absolute pose: origin, angles, and every other field the prop's own.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **<c>CalcAbsolutePosition</c>'s two branches** (<c>c_baseentity.cpp:4387</c>): a bone-merged follower takes its
    /// parent's place outright (<c>MoveToAimEnt</c>), and anything else concatenates its own local transform onto the
    /// parent's (<c>:4396</c>):
    ///
    /// <code>
    ///   AngleMatrix( GetLocalAngles(), matEntityToParent );
    ///   MatrixSetColumn( GetLocalOrigin(), 3, matEntityToParent );
    ///   ConcatTransforms( GetParentToWorldTransform( … ), matEntityToParent, m_rgflCoordinateFrame );
    /// </code>
    ///
    /// **The angle shortcut is Valve's and it is CONDITIONAL** (<c>:4406</c>): a child with no angles of its own and no
    /// parent attachment copies the parent's absolute angles; anything else extracts them from the composed matrix.
    /// Applying it unconditionally is what drew a setup gate a quarter turn out (B241).
    ///
    /// **Bounded**, because the wire should never carry a cycle and a demo this project exists to open may carry
    /// anything: a chain past the budget keeps the prop's own pose, which draws it in the wrong place rather than not
    /// at all.
    /// </remarks>
    public static ScenePose Absolute(SceneProp prop, Func<int, SceneProp?> byEntity, int budget) =>
        Absolute(prop, byEntity, budget, attachmentOf: null);

    /// <summary>The absolute pose, with a parent's attachments resolvable — <c>GetParentToWorldTransform</c> in full.</summary>
    /// <param name="prop">The prop.</param>
    /// <param name="byEntity">This frame's props by entity index, or null where an index names none.</param>
    /// <param name="budget">How many parents may still be walked.</param>
    /// <param name="attachmentOf">
    /// An entity's attachment world matrix by its one-based number, as a 3×4 row-major <c>matrix3x4_t</c>, or null
    /// when it cannot be resolved; null itself for a caller with no posed skeletons.
    /// </param>
    /// <returns>The absolute pose.</returns>
    /// <remarks>
    /// **An attachment, when named and resolvable, replaces the parent's transform** (<c>c_baseentity.cpp:4330-4342</c>):
    /// <c>GetAttachment( m_iParentAttachment, vOrigin, vAngles )</c>, <c>AngleMatrix( vAngles, vOrigin, tempMatrix )</c>,
    /// and the child's local transform is concatenated onto THAT. Unresolvable falls through to the parent's own.
    ///
    /// **The parent's model scale is not in its frame.** <c>m_rgflCoordinateFrame</c> is
    /// <c>AngleMatrix( GetLocalAngles(), GetLocalOrigin(), … )</c> at the root (<c>:4380</c>) and a
    /// <c>ConcatTransforms</c> of two such matrices below it (<c>:4401</c>) — angles and origins only. The model scale
    /// is applied when the bones are built, so a child's offset is not stretched by its parent's size.
    /// </remarks>
    public static ScenePose Absolute(
        SceneProp prop, Func<int, SceneProp?> byEntity, int budget, Func<int, int, float[]?>? attachmentOf)
    {
        ArgumentNullException.ThrowIfNull(prop);
        ArgumentNullException.ThrowIfNull(byEntity);

        if (prop.AttachedTo is not { } wearer || budget <= 0 || byEntity(wearer) is not { } parent)
        {
            return prop.Pose;
        }

        ScenePose above = Absolute(parent, byEntity, budget - 1, attachmentOf);

        if (prop.BoneMerged)
        {
            return AimEntOrigin(prop, parent, above);
        }

        PropTransform parentToWorld =
            prop.AttachmentPoint is { } point && point > 0 && attachmentOf?.Invoke(wearer, point) is { } attachment
                ? PropTransform.FromMatrix(attachment)
                : new PropTransform(above.X, above.Y, above.Z, above.Pitch, above.Yaw, above.Roll, 1f);

        PropTransform composed = parentToWorld
            .Concat(new PropTransform(
                prop.Pose.X, prop.Pose.Y, prop.Pose.Z,
                prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll, prop.Pose.Scale));

        (float x, float y, float z) = composed.Apply(0f, 0f, 0f);

        bool declaresNoAngles =
            prop.Pose.Pitch == 0f && prop.Pose.Yaw == 0f && prop.Pose.Roll == 0f;

        (float pitch, float yaw, float roll) = declaresNoAngles && prop.AttachmentPoint is null
            ? (above.Pitch, above.Yaw, above.Roll)
            : composed.Angles();

        return prop.Pose with
        {
            X = x,
            Y = y,
            Z = z,
            Pitch = pitch,
            Yaw = yaw,
            Roll = roll,
        };
    }

    /// <summary>Where <c>MoveToAimEnt</c> puts a bone-merged entity — <c>C_BaseAnimating::GetAimEntOrigin</c> (B69).</summary>
    /// <remarks>
    /// <code>
    ///   if ( IsEffectActive( EF_BONEMERGE ) &amp;&amp; IsEffectActive( EF_BONEMERGE_FASTCULL ) &amp;&amp; (pMoveParent = GetMoveParent()) != NULL )
    ///   {
    ///       *pAbsOrigin = pMoveParent-&gt;WorldSpaceCenter();
    ///       *pAbsAngles = pMoveParent-&gt;GetRenderAngles();
    ///   }
    ///   else if ( !m_pBoneMergeCache || !m_pBoneMergeCache-&gt;GetAimEntOrigin( pAbsOrigin, pAbsAngles ) )
    ///       BaseClass::GetAimEntOrigin( pAttachedTo, pAbsOrigin, pAbsAngles );
    /// </code>
    ///
    /// (<c>c_baseanimating.cpp:5986-5999</c>; the base is the parent's abs origin and angles, <c>c_baseentity.cpp:3156-3163</c>.)
    /// The origin is what every bone the merge does NOT write is built from, so an item none of whose bones match its
    /// wearer's — the spellbook's lone <c>mvm</c> — stands at the middle of his hull, not his feet.
    ///
    /// **Not ported, and named:** the cache's own answer for a merge WITHOUT fast-cull — the parent bone times the inverse
    /// of the first merged bone's local, at cycle 0 (<c>bone_merge_cache.cpp:227-259</c>). Every TF2 wearable and weapon
    /// fast-culls, so that branch takes the parent's origin here, as it does when nothing matched.
    /// </remarks>
    private static ScenePose AimEntOrigin(SceneProp prop, SceneProp parent, ScenePose above)
    {
        // `GetRenderAngles` is the parent's pose angles: a player prop's are `m_angRender` (`PlayerProps`).
        Vector3 offset = AimEntOffset(prop, parent, above);

        return above with { X = above.X + offset.X, Y = above.Y + offset.Y, Z = above.Z + offset.Z };
    }

    /// <summary>How far a bone-merged entity's origin sits from its parent's: <see cref="AimEntOrigin"/>'s, in world space.</summary>
    /// <param name="prop">The bone-merged entity.</param>
    /// <param name="parent">Its move parent.</param>
    /// <param name="parentAt">The parent's absolute pose.</param>
    /// <returns>The parent's <c>WorldSpaceCenter</c> less its origin under fast-cull; zero otherwise.</returns>
    internal static Vector3 AimEntOffset(SceneProp prop, SceneProp parent, ScenePose parentAt) =>
        prop.BoneMergeFastCull
            ? WorldSpaceCenter.Of(parentAt, parent.Collision) - new Vector3(parentAt.X, parentAt.Y, parentAt.Z)
            : Vector3.Zero;
}
