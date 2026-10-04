using System;

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
    public static ScenePose Absolute(SceneProp prop, Func<int, SceneProp?> byEntity, int budget)
    {
        ArgumentNullException.ThrowIfNull(prop);
        ArgumentNullException.ThrowIfNull(byEntity);

        if (prop.AttachedTo is not { } wearer || budget <= 0 || byEntity(wearer) is not { } parent)
        {
            return prop.Pose;
        }

        ScenePose above = Absolute(parent, byEntity, budget - 1);

        if (prop.BoneMerged)
        {
            return above;
        }

        PropTransform composed = new PropTransform(
                above.X, above.Y, above.Z, above.Pitch, above.Yaw, above.Roll, above.Scale)
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
}
