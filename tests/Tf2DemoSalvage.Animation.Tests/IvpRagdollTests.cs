using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A ragdoll on the ported driver, in IVP space, crossing into and out of Source at its boundary (B369, D172).</summary>
/// <remarks>
/// **The conversions are arithmetic** — `Source (x, y, z)` is `IVP (x, −z, y) × 0.0254` — so each prediction below is a number the
/// test chose, carried across by hand. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpRagdollTests
{
    private const float Metre = IvpTransform.MetresPerInch;

    /// <remarks>
    /// **The core stands at the mass centre in IVP space**: the child starts at Source `(3, 4, 0)` turned a quarter about Source Z,
    /// its mass centre one inch along its own X, which the turn sends along Source Y — the core at Source `(3, 5, 0)`, IVP
    /// `(3, 0, 5) × 0.0254`.
    /// </remarks>
    [Test]
    public void Create_AnElementWithAMassCenter_PlacesItsCoreThereInIvpSpace()
    {
        IvpRagdoll ragdoll = Ragdoll(out _, out IvpSimulation simulation, TurnedChild());

        // Asleep until the first PSI revives it (`FUN_180089210`), so the unit is looked for on both lists.
        IvpRigidBody child = simulation.Units.Sleeping.Concat(simulation.Units.Active).Single().Cores.Find(core => core.ObjectOffset.X < -0.5f * Metre)!;

        child.Position.X.ShouldBe(3d * Metre, 1e-5d);
        child.Position.Y.ShouldBe(0d, 1e-5d);
        child.Position.Z.ShouldBe(5d * Metre, 1e-5d);
        ragdoll.Body.Elements.Count.ShouldBe(2);
    }

    /// <remarks>**What comes back is the bone in Source**, the start pose exactly, the turn included.</remarks>
    [Test]
    public void State_BeforeAnyStep_ReportsEachBoneWhereItStartedInSource()
    {
        (Vector3, Quaternion)[] start = TurnedChild();
        IvpRagdoll ragdoll = Ragdoll(out _, out _, start);

        (Vector3 Position, Quaternion Orientation) child = ragdoll.State()[1];

        child.Position.X.ShouldBe(3f, 1e-4f);
        child.Position.Y.ShouldBe(4f, 1e-4f);
        child.Position.Z.ShouldBe(0f, 1e-4f);
        Quaternion.Dot(child.Orientation, start[1].Item2).ShouldBe(1f, 1e-5f);
    }

    /// <remarks>
    /// **The bone is reported at the environment's clock** (`GetPosition` reads `FUN_180073b80`): the first frame runs two PSIs,
    /// the second at the step, and leaves the clock `0.9999895` of a step past it, so the root has moved on by its committed
    /// velocity for that long — not where its core was last stepped.
    /// </remarks>
    [Test]
    public void State_AfterAFrame_IsTheBoneAtTheClock()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out IvpSimulation simulation, Straight());
        IvpRigidBody root = ragdoll.Bodies[0];

        world.Simulate(IvpRagdollWorldFrames.Step);

        float elapsed = (float)(simulation.Now - root.LastStepped);
        elapsed.ShouldBeGreaterThan(0.9f * IvpRagdollWorldFrames.Step, "the control: the clock is well past the last PSI");
        root.PreviousVelocity.Y.ShouldNotBe(0f, "the control: the root is falling");
        (_, _, float z) = IvpTransform.SourcePosition(0f, (float)(root.ObjectOrigin().Y + ((double)root.PreviousVelocity.Y * elapsed)), 0f);
        ragdoll.State()[0].Position.Z.ShouldBe(z, 1e-4f);
    }

    /// <remarks>**Source gravity is IVP +Y**, so a corpse falls along Source −Z and nowhere else.</remarks>
    [Test]
    public void Simulate_UnderGravity_TheRootFallsAlongSourceMinusZ()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out _, Straight());

        world.SimulateFrames(0.5d);

        Vector3 root = ragdoll.State()[0].Position;
        root.Z.ShouldBeLessThan(-50f, "half a second at 800 in/s² is about a hundred inches");
        root.X.ShouldBe(0f, 1f);
        root.Y.ShouldBe(0f, 1f);
    }

    /// <remarks>**The killing force crosses too**: an impulse along Source +X moves the struck bone along Source +X.</remarks>
    [Test]
    public void Kill_AForceAlongSourceX_PushesTheStruckBoneAlongIt()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out _, Straight());

        ragdoll.Kill(new Vector3(2000f, 0f, 0f), forceBone: 0);
        world.SimulateFrames(0.2d);

        ragdoll.State()[0].Position.X.ShouldBeGreaterThan(5f);
    }

    /// <remarks>
    /// **The joint holds the two bones together**: started with the child an inch off its bind offset, a second under the
    /// ported constraints brings it back nearer.
    /// </remarks>
    [Test]
    public void Simulate_AJointPulledApart_BringsTheBonesCloser()
    {
        (Vector3, Quaternion)[] start = [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 1f), Quaternion.Identity)];
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out IvpSimulation simulation, start);
        float before = Separation(ragdoll);

        world.SimulateFrames(1d);

        Separation(ragdoll).ShouldBeLessThan(
            before * 0.5f,
            $"units {simulation.AwakeUnits}, cores in first {simulation.Units.Active[0].Cores.Count}, entries " +
            $"{string.Join(",", simulation.Units.Active[0].Entries.ConvertAll(e => e.Controller.Priority))}; root {ragdoll.State()[0].Position}, child {ragdoll.State()[1].Position}");
    }

    private static float Separation(IvpRagdoll ragdoll)
    {
        (Vector3 Position, Quaternion Orientation)[] state = ragdoll.State();

        return (state[1].Position - (state[0].Position + Vector3.Transform(new Vector3(3f, 4f, 0f), state[0].Orientation))).Length();
    }

    private static IvpRagdoll Ragdoll(out IvpRagdollWorld world, out IvpSimulation simulation, (Vector3, Quaternion)[] start)
    {
        world = new IvpRagdollWorld(1f / 66f, new Vector3(0f, 0f, -800f), new VphysicsSurfaceProps([]));
        simulation = world.Simulation;

        return IvpRagdoll.Create(world, RagdollBody.Build(Physics(), RagdollSkeletons.Straight())!, start);
    }

    private static (Vector3, Quaternion)[] Straight() =>
        [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 0f), Quaternion.Identity)];

    private static (Vector3, Quaternion)[] TurnedChild() =>
        [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f))];

    /// <summary>A root and a child with a joint of ±30° on each axis, the child's mass centre one inch along its own X.</summary>
    private static PhysicsModel Physics()
    {
        ConstraintAxis axis = new(-30f, 30f, 0f);
        const float Squared = IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

        return PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [new RagdollConstraint(0, 1, axis, axis, axis)],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties:
            [
                new PhysicsMassProperties(Vector3.Zero, new Vector3(1f, 1f, 1f) / Squared),
                new PhysicsMassProperties(new Vector3(Metre, 0f, 0f), new Vector3(1f, 1f, 1f) / Squared),
            ]);
    }
}
