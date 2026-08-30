using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>CalcAbsolutePosition</c> — where a parented entity actually is.
/// </summary>
/// <remarks>
/// **Written off the SDK before the code existed**, and written for the WHOLE mechanism rather than
/// the branch that motivated it. The owner's standing direction, 2026-08-30: *"make sure when you
/// are implementing stuff you implement valves stuff completely"*, and *"we shouldnt be running into
/// half implemented stuff"* — this session had already met three half-implemented mechanisms, one of
/// which is the very thing being fixed here.
///
/// <c>C_BaseEntity::CalcAbsolutePosition</c> (<c>c_baseentity.cpp:4350</c>) is three branches in a
/// fixed order:
///
/// <code>
///   if (!m_pMoveParent)                    { abs = local;      return; }
///   if ( IsEffectActive(EF_BONEMERGE) )    { MoveToAimEnt();   return; }
///
///   AngleMatrix( GetLocalAngles(), matEntityToParent );
///   MatrixSetColumn( GetLocalOrigin(), 3, matEntityToParent );
///   ConcatTransforms( GetParentToWorldTransform( scratch ), matEntityToParent, m_rgflCoordinateFrame );
///   MatrixGetColumn( m_rgflCoordinateFrame, 3, m_vecAbsOrigin );
///
///   if ( m_angRotation == vec3_angle &amp;&amp; m_iParentAttachment == 0 )
///       VectorCopy( m_pMoveParent-&gt;GetAbsAngles(), m_angAbsRotation );
///   else
///       MatrixAngles( m_rgflCoordinateFrame, m_angAbsRotation );
/// </code>
///
/// **This project implemented branches 1 and 2 and treated "has a parent" as "is bone-merged"**, so
/// branch 3 — ordinary parenting — had no home. Measured on `cp_fulgur`: every gate is an invisible
/// `func_door` with a visible <c>prop_dynamic</c> parented to it, and all six grate props sit at
/// <c>(0, 0, 0)</c> because the timeline zeroes a parented entity's origin and then looks for a
/// skeleton the door does not have. 49 of 1228 entities on that map declare a parent.
///
/// **The angle shortcut is part of the mechanism, not an optimisation to skip.** A child with zero
/// local angles and no parent attachment COPIES the parent's absolute angles rather than extracting
/// them from the concatenated matrix. Extraction is lossy at gimbal lock and can differ in the last
/// decimal everywhere else, so skipping the shortcut is a real divergence on the most common case
/// there is.
/// </remarks>
public sealed class EntityTransformConformanceTests
{
    /// <summary>How close two floats must be, in world units or degrees.</summary>
    /// <remarks>
    /// Angles round-trip through a matrix and back, so this is float precision rather than a
    /// tolerance for being approximately right.
    /// </remarks>
    private const float Close = 0.002f;

    [Test]
    public void Absolute_WithNoParent_IsTheLocalTransformUnchanged()
    {
        // `if (!m_pMoveParent) { m_vecAbsOrigin = GetLocalOrigin(); ... }` — the ordinary case, and
        // the one that must not change when parenting is added.
        PropTransform local = new(64f, -32f, 8f, 0f, 90f, 0f, 1f);

        PropTransform absolute = PropTransform.Identity.Concat(local);

        absolute.OriginX.ShouldBe(64f, Close);
        absolute.OriginY.ShouldBe(-32f, Close);
        absolute.OriginZ.ShouldBe(8f, Close);
    }

    [Test]
    public void Absolute_AChildOffsetFromAParent_IsTheParentsTransformAppliedToTheOffset()
    {
        // **The case the gates need.** A parent standing at the origin turned 90 degrees, and a
        // child ten units along its own +X: the child ends up ten units along the parent's facing,
        // which after a 90 degree yaw is +Y.
        //
        // Predicted exactly rather than "it moved": a rotation applied in the wrong order, or
        // transposed, puts it at −Y or leaves it at +X, and all three are "not where it started".
        PropTransform parent = new(0f, 0f, 0f, 0f, 90f, 0f, 1f);
        PropTransform child = new(10f, 0f, 0f, 0f, 0f, 0f, 1f);

        PropTransform absolute = parent.Concat(child);

        absolute.OriginX.ShouldBe(0f, Close);
        absolute.OriginY.ShouldBe(10f, Close);
        absolute.OriginZ.ShouldBe(0f, Close);
    }

    [Test]
    public void Absolute_AChildOfAMovedAndTurnedParent_TakesBoth()
    {
        // **The control for the case above, and it is what separates "applies the rotation" from
        // "applies the whole transform".** With the parent at the origin, an implementation that
        // forgot to add the parent's translation is indistinguishable from a correct one.
        //
        // `out[i][3] = in1.rotation × in2.translation + in1.translation` — the "add in translation
        // vector" step at the end of `ConcatTransforms` (`mathlib_base.cpp:706`).
        PropTransform parent = new(100f, 200f, 300f, 0f, 90f, 0f, 1f);
        PropTransform child = new(10f, 0f, 0f, 0f, 0f, 0f, 1f);

        PropTransform absolute = parent.Concat(child);

        absolute.OriginX.ShouldBe(100f, Close);
        absolute.OriginY.ShouldBe(210f, Close);
        absolute.OriginZ.ShouldBe(300f, Close);
    }

    [Test]
    public void Absolute_TheRotationsOfBothParentAndChild_Compose()
    {
        // Two 45 degree yaws make 90. Asserted through the extracted angles rather than through a
        // position, because a child at the parent's own origin has nowhere to move and only the
        // orientation can carry the error.
        PropTransform parent = new(0f, 0f, 0f, 0f, 45f, 0f, 1f);
        PropTransform child = new(0f, 0f, 0f, 0f, 45f, 0f, 1f);

        PropTransform absolute = parent.Concat(child);

        absolute.Angles().Yaw.ShouldBe(90f, Close);
    }

    [Test]
    public void Angles_FromAMatrix_AreValvesExtraction()
    {
        // `MatrixAngles`, `mathlib_base.cpp:208`:
        //   yaw   = atan2( forward.y, forward.x )
        //   pitch = atan2( -forward.z, sqrt(forward.x^2 + forward.y^2) )
        //   roll  = atan2( left.z, up.z )
        // Round-tripped: angles in, matrix, angles out. Each of the three is distinct and non-zero
        // so a formula that swapped two of them cannot pass.
        PropTransform transform = new(0f, 0f, 0f, 20f, -110f, 35f, 1f);

        (float pitch, float yaw, float roll) = transform.Angles();

        pitch.ShouldBe(20f, Close);
        yaw.ShouldBe(-110f, Close);
        roll.ShouldBe(35f, Close);
    }

    [Test]
    public void Angles_LookingStraightDown_TakeTheGimbalLockBranch()
    {
        // `else // forward is mostly Z, gimbal lock-`, where `xyDist > 0.001f` fails:
        //   yaw  = atan2( -left.x, left.y )
        //   roll = 0        // "Assume no roll in this case as one degree of freedom has been lost"
        //
        // A pitch of 90 is what an overhead camera holds and what a falling entity reaches, so this
        // is not an exotic input. The roll is asserted as ZERO deliberately: the branch discards it,
        // and an implementation using the general formula would return whatever the degenerate
        // atan2 produced.
        PropTransform down = new(0f, 0f, 0f, 90f, 0f, 0f, 1f);

        (float pitch, float _, float roll) = down.Angles();

        pitch.ShouldBe(90f, Close);
        roll.ShouldBe(0f, Close);
    }

    [Test]
    public void Absolute_AChildWithNoAnglesOfItsOwn_CopiesTheParentsAngles()
    {
        // `if ( m_angRotation == vec3_angle && m_iParentAttachment == 0 )
        //      VectorCopy( m_pMoveParent->GetAbsAngles(), m_angAbsRotation );`
        //
        // **The shortcut, and it is the common case rather than an edge one**: a prop parented to a
        // door usually carries no rotation of its own. Valve copies rather than extracts, so this
        // asserts the parent's angles arrive exactly — extraction would be right to within a
        // rounding error, and "within a rounding error" is not what the engine does.
        PropTransform parent = new(0f, 0f, 0f, 20f, -110f, 35f, 1f);

        (float pitch, float yaw, float roll) =
            PropTransform.AbsoluteAngles(parent, (20f, -110f, 35f), (0f, 0f, 0f), parentAttachment: 0);

        pitch.ShouldBe(20f);
        yaw.ShouldBe(-110f);
        roll.ShouldBe(35f);
    }

    [Test]
    public void Absolute_AChildWithItsOwnAngles_ExtractsThemFromTheMatrix()
    {
        // **The control for the shortcut.** The condition is `m_angRotation == vec3_angle` AND no
        // parent attachment; either one failing takes the `MatrixAngles` path. Without this case an
        // implementation that ALWAYS copied the parent's angles would satisfy the test above and
        // ignore every child that turns.
        PropTransform parent = new(0f, 0f, 0f, 0f, 45f, 0f, 1f);

        (float _, float yaw, float _) =
            PropTransform.AbsoluteAngles(parent, (0f, 45f, 0f), (0f, 45f, 0f), parentAttachment: 0);

        yaw.ShouldBe(90f, Close);
    }

    [Test]
    public void Absolute_AChildOnAParentAttachment_ExtractsEvenWithNoAnglesOfItsOwn()
    {
        // The other half of the same condition: `m_iParentAttachment == 0` must ALSO hold for the
        // copy. A child hung on an attachment point takes that attachment's orientation, which is
        // not the parent's own, so copying would be wrong however zero its local angles are.
        PropTransform parent = new(0f, 0f, 0f, 0f, 45f, 0f, 1f);

        (float _, float yaw, float _) =
            PropTransform.AbsoluteAngles(parent, (0f, 45f, 0f), (0f, 0f, 0f), parentAttachment: 3);

        yaw.ShouldBe(45f, Close);
    }
}
