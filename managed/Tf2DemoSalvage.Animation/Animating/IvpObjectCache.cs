using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// An object's cached placement at the current PSI — the 0xd0-byte cache object <c>FUN_18008c420</c> hands out and
/// <c>FUN_180080a60</c> refreshes (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The object cache*). Its `+0x40` matrix is the "current matrix"
/// <see cref="IvpMotionCache"/> is built over, and the pair creation `FUN_180096680` puts a point into the object's frame through
/// it. **An elapsed time of zero, or a NaN, copies the core's committed orientation and position** — `UCOMISS` sets the zero
/// flag for both — and anything else interpolates them as <see cref="IvpRigidBody.TransformAt"/> does, the position's product
/// the destination of its sum. Pinned by the `vphysics-object-cache` probe (`IvpObjectCacheConformanceTests`).
/// </remarks>
public sealed class IvpObjectCache
{
    /// <summary>The core's position, <c>+0x0</c>.</summary>
    public (double X, double Y, double Z) CorePosition { get; private set; }

    /// <summary>The object's orientation, <c>+0x20</c>.</summary>
    public (double X, double Y, double Z, double W) Rotation { get; private set; }

    /// <summary>The object's matrix, <c>+0x40</c>, whose translation <c>+0xa0</c> is the object's origin.</summary>
    public IvpMatrix Matrix { get; private set; }

    /// <summary>The PSI it was refreshed at, <c>+0xc0</c>, from <c>env+0x1a0</c>.</summary>
    public int RefreshedAt { get; private set; }

    /// <summary>Refreshes the cache — <c>FUN_180080a60</c>.</summary>
    /// <param name="core">The object's core, <c>object+0xe8</c>.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <param name="psi">The environment's PSI count, <c>env+0x1a0</c>.</param>
    /// <param name="offset">The object's place in its core, <c>object+0x60</c>, or null when bit <c>0x800</c> of <c>object+0x78</c> marks it zero.</param>
    /// <param name="objectRotation">The object's rotation in its core, the quaternion <c>object+0x58</c> points at, or null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    /// <remarks>
    /// <code>
    /// +0xc0 = psi;  dt = (float)(now − core+0x1d0)
    /// dt zero or NaN → q = core+0x180, p = core+0x150
    /// else → q = FUN_180071060(core+0x180, core+0x1a0, (double)(dt·core+0x1d8)), p = (double)core+0x170·(double)dt + core+0x150 per lane
    /// +0xa0 = +0x0 = p;  +0x20 = q;  +0x40 = FUN_180071330(q)
    /// an offset → +0xa0 = (s.z·m[i,2] + (s.x·m[i,0] + s.y·m[i,1])) + t[i]              -- FUN_180070b20
    /// a rotation → +0x20 = q·r (its own operand orders), +0x40 = FUN_180071330(+0x20), the translation kept
    /// </code>
    /// </remarks>
    public void Refresh(
        IvpRigidBody core, double now, int psi, (float X, float Y, float Z)? offset, (double X, double Y, double Z, double W)? objectRotation)
    {
        ArgumentNullException.ThrowIfNull(core);

        RefreshedAt = psi;

        float elapsed = (float)(now - core.LastStepped);
        (double X, double Y, double Z, double W) rotation;
        (double X, double Y, double Z) position;

        if (elapsed is < 0f or > 0f)
        {
            rotation = IvpQuaternion.Interpolate(core.Orientation, core.WorkingOrientation, IvpMath.Mulss(elapsed, core.InverseStep));

            double narrowed = elapsed;

            position = (
                IvpMath.Addsd(IvpMath.Mulsd(core.PreviousVelocity.X, narrowed), core.Position.X),
                IvpMath.Addsd(IvpMath.Mulsd(core.PreviousVelocity.Y, narrowed), core.Position.Y),
                IvpMath.Addsd(IvpMath.Mulsd(core.PreviousVelocity.Z, narrowed), core.Position.Z));
        }
        else
        {
            rotation = core.Orientation;
            position = core.Position;
        }

        CorePosition = position;
        Rotation = rotation;
        Matrix = IvpMatrix.FromRotation(rotation, position);

        if (offset is { } shift)
        {
            IvpMatrix m = Matrix;
            (double x, double y, double z) = (shift.X, shift.Y, shift.Z);

            Matrix = m with
            {
                Translation = (
                    IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(z, m.M2), IvpMath.Addsd(IvpMath.Mulsd(x, m.M0), IvpMath.Mulsd(y, m.M1))), m.Translation.X),
                    IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(z, m.M6), IvpMath.Addsd(IvpMath.Mulsd(x, m.M4), IvpMath.Mulsd(y, m.M5))), m.Translation.Y),
                    IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(z, m.M10), IvpMath.Addsd(IvpMath.Mulsd(x, m.M8), IvpMath.Mulsd(y, m.M9))), m.Translation.Z)),
            };
        }

        if (objectRotation is { } turn)
        {
            Rotation = Compose(Rotation, turn);
            Matrix = IvpMatrix.FromRotation(Rotation, Matrix.Translation);
        }
    }

    /// <summary>The object's rotation composed onto the core's, as <c>FUN_180080a60</c> inlines it.</summary>
    /// <remarks>
    /// The values of <see cref="IvpQuaternion.Product"/>`(q, r)`, with the operands each product and sum keeps as its destination
    /// here — <c>r·q</c> where the product routine has <c>q·r</c> in three lanes — which a NaN would show.
    /// </remarks>
    private static (double X, double Y, double Z, double W) Compose(
        (double X, double Y, double Z, double W) q, (double X, double Y, double Z, double W) r) =>
        (IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(r.X, q.W), IvpMath.Mulsd(q.X, r.W)), IvpMath.Mulsd(r.Z, q.Y)) - IvpMath.Mulsd(q.Z, r.Y),
         IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(r.W, q.Y), IvpMath.Mulsd(r.Y, q.W)), IvpMath.Mulsd(r.X, q.Z)) - IvpMath.Mulsd(r.Z, q.X),
         IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(r.Z, q.W), IvpMath.Mulsd(r.W, q.Z)), IvpMath.Mulsd(r.Y, q.X)) - IvpMath.Mulsd(r.X, q.Y),
         ((IvpMath.Mulsd(q.W, r.W) - IvpMath.Mulsd(q.X, r.X)) - IvpMath.Mulsd(r.Y, q.Y)) - IvpMath.Mulsd(r.Z, q.Z));
}
