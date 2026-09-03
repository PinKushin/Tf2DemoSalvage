using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One bone's spring parameters — <c>mstudiojigglebone_t</c>.
/// </summary>
/// <param name="Flags">The <c>JIGGLE_*</c> bits, which decide WHICH of four simulations runs.</param>
/// <param name="Length">How far along the bone its tip sits, in units.</param>
/// <param name="TipMass">Gravity on the tip: it is subtracted from the tip's Z acceleration.</param>
/// <param name="YawStiffness">Spring constant pulling the tip back in the local left axis.</param>
/// <param name="YawDamping">Velocity term opposing that spring.</param>
/// <param name="PitchStiffness">Spring constant in the local up axis.</param>
/// <param name="PitchDamping">Velocity term opposing that spring.</param>
/// <param name="AlongStiffness">Spring constant along the bone, used only without a length constraint.</param>
/// <param name="AlongDamping">Velocity term opposing that spring.</param>
/// <param name="AngleLimit">Maximum deflection of the tip from the goal direction, in radians.</param>
/// <param name="MinYaw">Lower yaw limit, in radians.</param>
/// <param name="MaxYaw">Upper yaw limit, in radians.</param>
/// <param name="YawFriction">Read by nothing — see the remarks.</param>
/// <param name="YawBounce">Read by nothing — see the remarks.</param>
/// <param name="MinPitch">Lower pitch limit, in radians.</param>
/// <param name="MaxPitch">Upper pitch limit, in radians.</param>
/// <param name="PitchFriction">Read by nothing — see the remarks.</param>
/// <param name="PitchBounce">Read by nothing — see the remarks.</param>
/// <param name="BaseMass">Gravity on the base.</param>
/// <param name="BaseStiffness">Spring constant pulling the base back to its goal.</param>
/// <param name="BaseDamping">Velocity term opposing that spring.</param>
/// <param name="BaseMinLeft">Lower limit of base travel along the goal's left axis.</param>
/// <param name="BaseMaxLeft">Upper limit of base travel along the goal's left axis.</param>
/// <param name="BaseLeftFriction">Friction applied while at either left limit.</param>
/// <param name="BaseMinUp">Lower limit of base travel along the goal's up axis.</param>
/// <param name="BaseMaxUp">Upper limit of base travel along the goal's up axis.</param>
/// <param name="BaseUpFriction">Friction applied while at either up limit.</param>
/// <param name="BaseMinForward">Lower limit of base travel along the bone.</param>
/// <param name="BaseMaxForward">Upper limit of base travel along the bone.</param>
/// <param name="BaseForwardFriction">Friction applied while at either forward limit.</param>
/// <param name="BoingImpactSpeed">Speed change that triggers a boing.</param>
/// <param name="BoingImpactAngle">Direction change that triggers a boing.</param>
/// <param name="BoingDampingRate">How fast a boing decays, per second.</param>
/// <param name="BoingFrequency">Radians per second of the boing sinusoid.</param>
/// <param name="BoingAmplitude">How far the boing squashes and stretches.</param>
/// <remarks>
/// **This is the only procedural bone rule TF2 uses**, measured 2026-09-03 across two demos: 22 of
/// 379 bones on `koth_harvest_final` and 4 of 198 on `cp_fulgur` carry `proctype`, every one of them
/// `STUDIO_PROC_JIGGLE`, and none of the four rules `CalcProceduralBone` implements. Earbud cords,
/// weapon chains, cosmetic tassels.
///
/// **Thirty-five floats after one int, and the order is the whole of the format** — there are no
/// counts, no indices and nothing self-describing, so a field read one slot early is a plausible
/// number in the wrong place. <c>studio.h:195</c> is the declaration and
/// <see cref="StudioLayout.JiggleBoneStride"/> is the 140 bytes it comes to.
///
/// **Four of the fields are dead in the shipped engine and are read anyway.**
/// <c>yawFriction</c>, <c>yawBounce</c>, <c>pitchFriction</c> and <c>pitchBounce</c> appear in
/// `studio.h` and in no expression in `jigglebones.cpp`: Valve's own comment says why, at both
/// constraint sites — *"removed friction and velocity clipping against constraint - was causing
/// simulation blowups (MSB 12/9/2010)"* — and the code zeroes the velocity outright instead. They
/// are parsed because they occupy bytes: skipping them would move every later field.
/// </remarks>
public readonly record struct StudioJiggleBone(
    int Flags,
    float Length,
    float TipMass,
    float YawStiffness,
    float YawDamping,
    float PitchStiffness,
    float PitchDamping,
    float AlongStiffness,
    float AlongDamping,
    float AngleLimit,
    float MinYaw,
    float MaxYaw,
    float YawFriction,
    float YawBounce,
    float MinPitch,
    float MaxPitch,
    float PitchFriction,
    float PitchBounce,
    float BaseMass,
    float BaseStiffness,
    float BaseDamping,
    float BaseMinLeft,
    float BaseMaxLeft,
    float BaseLeftFriction,
    float BaseMinUp,
    float BaseMaxUp,
    float BaseUpFriction,
    float BaseMinForward,
    float BaseMaxForward,
    float BaseForwardFriction,
    float BoingImpactSpeed,
    float BoingImpactAngle,
    float BoingDampingRate,
    float BoingFrequency,
    float BoingAmplitude)
{
    /// <summary>Whether the tip springs toward its goal — <c>JIGGLE_IS_FLEXIBLE</c>.</summary>
    public bool IsFlexible => (Flags & StudioJiggleFlags.Flexible) != 0;

    /// <summary>Whether the tip swings without a spring — <c>JIGGLE_IS_RIGID</c>.</summary>
    /// <remarks>
    /// **Rigid still integrates**, and that is easy to get backwards. The gate on the whole tip
    /// section is <c>flags &amp; (JIGGLE_IS_FLEXIBLE | JIGGLE_IS_RIGID)</c>, so a rigid bone still takes
    /// gravity and still moves; what it skips is the spring block that computes the yaw, pitch and
    /// along accelerations.
    /// </remarks>
    public bool IsRigid => (Flags & StudioJiggleFlags.Rigid) != 0;

    /// <summary>Whether the tip section runs at all.</summary>
    public bool HasTipFlex => (Flags & (StudioJiggleFlags.Flexible | StudioJiggleFlags.Rigid)) != 0;

    /// <summary>Whether the yaw is clamped between <c>MinYaw</c> and <c>MaxYaw</c>.</summary>
    public bool HasYawConstraint => (Flags & StudioJiggleFlags.YawConstraint) != 0;

    /// <summary>Whether the pitch is clamped between <c>MinPitch</c> and <c>MaxPitch</c>.</summary>
    public bool HasPitchConstraint => (Flags & StudioJiggleFlags.PitchConstraint) != 0;

    /// <summary>Whether the tip's deflection is clamped to <c>AngleLimit</c>.</summary>
    public bool HasAngleConstraint => (Flags & StudioJiggleFlags.AngleConstraint) != 0;

    /// <summary>Whether the tip is held exactly <c>Length</c> from the base.</summary>
    public bool HasLengthConstraint => (Flags & StudioJiggleFlags.LengthConstraint) != 0;

    /// <summary>Whether the BASE springs as well as the tip.</summary>
    public bool HasBaseSpring => (Flags & StudioJiggleFlags.BaseSpring) != 0;

    /// <summary>Whether the bone squashes and stretches on impact — <c>JIGGLE_IS_BOING</c>.</summary>
    public bool IsBoing => (Flags & StudioJiggleFlags.Boing) != 0;
}

/// <summary>
/// The <c>JIGGLE_*</c> bits, from <c>studio.h:186</c>.
/// </summary>
/// <remarks>
/// **They are not exclusive and the combinations are what the simulation branches on.** A bone can
/// be flexible with a length constraint and a base spring at once; `BuildJiggleTransformations`
/// tests seven of the eight independently, and the eighth — `JIGGLE_IS_BOING` — is reached only in
/// the `else` of the base-spring test, so a bone declaring both gets the base spring and no boing.
/// </remarks>
public static class StudioJiggleFlags
{
    /// <summary><c>JIGGLE_IS_FLEXIBLE</c> — the tip is pulled toward its goal by springs.</summary>
    public const int Flexible = 0x01;

    /// <summary><c>JIGGLE_IS_RIGID</c> — the tip moves under gravity with no spring.</summary>
    public const int Rigid = 0x02;

    /// <summary><c>JIGGLE_HAS_YAW_CONSTRAINT</c> — clamp the yaw to its authored range.</summary>
    public const int YawConstraint = 0x04;

    /// <summary><c>JIGGLE_HAS_PITCH_CONSTRAINT</c> — clamp the pitch to its authored range.</summary>
    public const int PitchConstraint = 0x08;

    /// <summary><c>JIGGLE_HAS_ANGLE_CONSTRAINT</c> — clamp total deflection to <c>angleLimit</c>.</summary>
    public const int AngleConstraint = 0x10;

    /// <summary><c>JIGGLE_HAS_LENGTH_CONSTRAINT</c> — hold the tip exactly one length out.</summary>
    public const int LengthConstraint = 0x20;

    /// <summary><c>JIGGLE_HAS_BASE_SPRING</c> — the bone's BASE moves too, not only its tip.</summary>
    public const int BaseSpring = 0x40;

    /// <summary><c>JIGGLE_IS_BOING</c> — a squash-and-stretch sinusoid on impact.</summary>
    public const int Boing = 0x80;
}

/// <summary>
/// Reads a bone's <c>mstudiojigglebone_t</c> out of a model.
/// </summary>
/// <remarks>
/// **The offset is relative to the BONE, not to the file.** <c>mstudiobone_t::pProcedure</c> is
/// <c>(void *)(((byte *)this) + procindex)</c> (<c>studio.h:293</c>), and <c>this</c> is the bone
/// structure — so the absolute position is the bone's own start plus <c>procindex</c>. Reading it
/// as a file offset lands somewhere plausible in a large model and produces springs with nonsense
/// constants rather than an exception.
///
/// **<c>procindex == 0</c> means none**, which the engine states outright by returning null for it.
/// </remarks>
public static class StudioJiggleBones
{
    /// <summary>The jiggle parameters for one bone, or null when it has none.</summary>
    /// <param name="model">The whole <c>.mdl</c> file.</param>
    /// <param name="bone">Which bone, by index.</param>
    /// <returns>Its spring parameters, or <c>null</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is negative.</exception>
    /// <remarks>
    /// **Null for every reason it could fail**, rather than a default-valued record: a jiggle bone
    /// with all-zero constants is a legitimate authored thing — a bone that hangs limp — so a
    /// zeroed struct and a failed read would be indistinguishable at the call site.
    /// </remarks>
    public static StudioJiggleBone? Read(ReadOnlyMemory<byte> model, int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);

        ReadOnlySpan<byte> bytes = model.Span;

        if (bytes.Length < StudioLayout.HeaderBoneIndexOffset + sizeof(int))
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderBoneCountOffset..]);

        int table = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderBoneIndexOffset..]);

        if (bone >= count ||
            table < 0 ||
            (long)table + ((long)count * StudioLayout.BoneStride) > bytes.Length)
        {
            return null;
        }

        int start = table + (bone * StudioLayout.BoneStride);

        int type = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.BoneProcedureTypeOffset)..]);

        int index = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.BoneProcedureIndexOffset)..]);

        // **`proctype` is tested with `&`, not `==`, and that is Valve's** (`c_baseanimating.cpp:1546`,
        // `(pBone->proctype & STUDIO_PROC_JIGGLE)`). `STUDIO_PROC_JIGGLE` is 5, which is not a power
        // of two, so the test is looser than an equality — but the four rules below it are consumed
        // by `CalcProceduralBone` first, which returns true for each and stops the bone reaching
        // here. Reproduced as written rather than tidied to `==`.
        if (index == 0 || (type & StudioProcedureType.Jiggle) == 0)
        {
            return null;
        }

        long at = (long)start + index;

        if (at < 0 || at + StudioLayout.JiggleBoneStride > bytes.Length)
        {
            return null;
        }

        ReadOnlySpan<byte> jiggle = bytes.Slice((int)at, StudioLayout.JiggleBoneStride);

        return new StudioJiggleBone(
            BinaryPrimitives.ReadInt32LittleEndian(jiggle),
            Float(jiggle, 1), Float(jiggle, 2), Float(jiggle, 3), Float(jiggle, 4),
            Float(jiggle, 5), Float(jiggle, 6), Float(jiggle, 7), Float(jiggle, 8),
            Float(jiggle, 9), Float(jiggle, 10), Float(jiggle, 11), Float(jiggle, 12),
            Float(jiggle, 13), Float(jiggle, 14), Float(jiggle, 15), Float(jiggle, 16),
            Float(jiggle, 17), Float(jiggle, 18), Float(jiggle, 19), Float(jiggle, 20),
            Float(jiggle, 21), Float(jiggle, 22), Float(jiggle, 23), Float(jiggle, 24),
            Float(jiggle, 25), Float(jiggle, 26), Float(jiggle, 27), Float(jiggle, 28),
            Float(jiggle, 29), Float(jiggle, 30), Float(jiggle, 31), Float(jiggle, 32),
            Float(jiggle, 33), Float(jiggle, 34));
    }

    /// <summary>The nth four-byte float of the structure.</summary>
    /// <remarks>
    /// **By slot rather than by named offset, because the struct is one run of floats.** Thirty-five
    /// named constants would each be four times its index and would give thirty-five chances to
    /// write one of them wrong; the arithmetic here can be wrong in only one place.
    /// </remarks>
    private static float Float(ReadOnlySpan<byte> jiggle, int slot) =>
        BinaryPrimitives.ReadSingleLittleEndian(jiggle[(slot * sizeof(float))..]);
}
