using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A body colliding with the static world — the thing that stops a corpse (B58).
/// </summary>
/// <remarks>
/// **The observation this suite exists for is one the owner made by looking**: *"the ground didnt
/// collide witht he corpse so the corpse just fell through the ground"*. Every part of the physics
/// path had its own green suite at the time, and none of them could fail for that reason, because
/// none of them involved a world.
///
/// **Synthetic geometry rather than a map (D38).** The floor is written here, so the test knows the
/// height a body must stop at; a map would only let it compare two readings of the same `.bsp`.
/// </remarks>
public sealed class IvpWorldContactConformanceTests
{
    private const float Step = 1f / 66f;

    /// <remarks>
    /// **The control, and it has to come first.** Without it, "the body ends up at z = 0" is
    /// satisfied by a body that never moved at all — which is exactly what a broken contact search
    /// that froze everything would produce.
    /// </remarks>
    [Test]
    public void Simulate_WithNoWorld_FallsUnderGravity()
    {
        IvpEnvironment environment = new(Step);

        IvpRigidBody body = Body(100f);

        environment.Add(body);

        for (int step = 0; step < 66; step++)
        {
            environment.Simulate();
        }

        // One second of Source gravity is 800 units per second, so it is far below where it began.
        body.Position.Z.ShouldBeLessThan(-250d, "half of 800 times one second squared, less the 100 it started at");
    }

    /// <remarks>
    /// **The measurement.** Same body, same second, with a floor under it — it must be resting on
    /// the floor rather than through it. The tolerance is the solver's own contact slop, since a
    /// resting body is deliberately left a fraction inside rather than driven to exactly zero.
    /// </remarks>
    [Test]
    public void Simulate_WithAFloor_StopsOnIt()
    {
        IvpEnvironment environment = new(Step) { World = Floor() };

        IvpRigidBody body = Body(100f);

        environment.Add(body);

        for (int step = 0; step < 66; step++)
        {
            environment.Simulate();
        }

        // The hull is a 2-unit cube about the body's centre, so a resting centre sits a unit up.
        body.Position.Z.ShouldBeGreaterThan(0d);
        body.Position.Z.ShouldBeLessThan(4d);
    }

    /// <remarks>
    /// **This suite could not tell one impulse pass from a hundred, and that was the gap.** Cutting
    /// <see cref="IvpEnvironment.MaximumImpulsePasses"/> from 100 to 1 reddened nothing: every case
    /// here rests a cube on a floor through four corners at once, so four contacts each removing a
    /// fifth of the approach finish it in one pass whatever the bound says, and what the bound
    /// actually governs was never measured.
    ///
    /// **One contact point is what makes the count observable.** The engine's pass applies a FIXED
    /// fraction of the approach speed measured before the loop — `-0.2 · m · v₀`, from
    /// `FUN_18008e290` — so a single contact removes a fifth per pass and needs five of them to stop
    /// a body at 312 units per second.
    ///
    /// **The prediction is arithmetic and exact, and it CHANGED when the effective mass was fixed.**
    /// This hull point sits at `(0, 0, −1)` and the impulse runs along `(0, 0, 1)`, so `r × d` is
    /// zero: an arm parallel to the impulse can produce no torque, and the effective inverse mass
    /// is simply `1/m = 1`. It used to read `1 + 1²·0.1 = 1.1`, because the old expression squared
    /// the arm's components with no cross product and so charged for a rotation this contact cannot
    /// cause. A pass is therefore worth `0.2 · 312 / 1 = 62.4` rather than 56.7, and the loop still
    /// stops on the first pass that carries the approach past zero — never below it, and never more
    /// than one pass above.
    /// </remarks>
    [Test]
    public void Simulate_ApproachingOnOneContact_TakesAsManyPassesAsItNeeds()
    {
        IvpEnvironment environment = new(Step) { World = Floor() };

        // A tenth of a unit inside, which is under the contact slop, so the depth term contributes
        // nothing and what is measured is the impulse loop alone.
        IvpRigidBody body = Body(0.9f);

        body.Velocity = (0f, 0f, -300f);

        // ONE point, so one contact — see the remarks.
        body.Hull = [(0f, 0f, -1f)];

        environment.Add(body);

        environment.Simulate();

        environment.Contacts.ShouldBe(1, "one hull point can raise exactly one contact");

        body.Velocity.Z.ShouldBeGreaterThanOrEqualTo(
            0f, "the loop runs until the approach is gone, and one pass leaves four fifths of it");

        body.Velocity.Z.ShouldBeLessThan(
            63f, "and it stops on the first pass past zero, so it never overshoots by more than one");
    }

    /// <remarks>
    /// **A ragdoll passes through a player clip, and the SDK states it as the purpose of the
    /// override**: *"This allows ragdolls to move through npcclip brushes"*, above
    /// `C_AI_BaseNPC::PhysicsSolidMaskForEntity` returning `MASK_SOLID` for a ragdoll where a live
    /// NPC gets `MASK_NPCSOLID` (`game/client/c_ai_basenpc.cpp:53-62`). `MASK_SOLID` omits
    /// `CONTENTS_PLAYERCLIP`, and the engine refuses any pair the two masks do not share
    /// (`game/client/physics.cpp:249`).
    ///
    /// **The control is the same floor made of `CONTENTS_SOLID`**, which must still stop the body.
    /// Without it "the body fell through" is satisfied by a world that lost its geometry
    /// altogether, which is the exact failure this suite was written for.
    ///
    /// **The condition is a whole second of falling**, because a body one tick into a clip brush
    /// has not yet been pushed anywhere either way. After sixty-six ticks a floor that collides
    /// holds it near zero and one that does not has let it past −250.
    /// </remarks>
    [Test]
    public void Simulate_WithAPlayerClipFloor_FallsStraightThroughIt()
    {
        const int PlayerClip = 0x10000;

        IvpEnvironment through = new(Step) { World = Floor(contents: PlayerClip) };

        IvpRigidBody falls = Body(100f);

        through.Add(falls);

        IvpEnvironment onto = new(Step) { World = Floor() };

        IvpRigidBody rests = Body(100f);

        onto.Add(rests);

        for (int step = 0; step < 66; step++)
        {
            through.Simulate();
            onto.Simulate();
        }

        falls.Position.Z.ShouldBeLessThan(
            -250d, "a ragdoll's MASK_SOLID does not contain CONTENTS_PLAYERCLIP");

        // The control: the identical floor, made of CONTENTS_SOLID, still holds it.
        rests.Position.Z.ShouldBeGreaterThan(0d);
        rests.Position.Z.ShouldBeLessThan(4d);
    }

    /// <remarks>
    /// **A body already at rest must not be pushed UP**, which is the failure a penetration bias
    /// produces when it converts depth into velocity with no slop: the corpse climbs, slowly and
    /// convincingly, and looks like buoyancy.
    /// </remarks>
    [Test]
    public void Simulate_WithABodyAtRest_DoesNotLiftIt()
    {
        IvpEnvironment environment = new(Step) { World = Floor() };

        IvpRigidBody body = Body(1f);

        environment.Add(body);

        for (int step = 0; step < 132; step++)
        {
            environment.Simulate();
        }

        body.Position.Z.ShouldBeLessThan(4d);
    }

    /// <remarks>
    /// **An immovable body is not collided**, matching the island driver, which does not integrate
    /// one either. Without this a piece of static geometry given a hull would push itself out of
    /// the world it IS.
    /// </remarks>
    [Test]
    public void Find_ForAnImmovableBody_ReportsNoContacts()
    {
        IvpRigidBody body = Body(-100f);

        body.Immovable = true;

        List<IvpContact> contacts = [];

        int checks = 0;

        IvpContact.Find(body, Floor(), contacts, Step, lookAhead: 0f, ref checks);

        contacts.ShouldBeEmpty();

        // The control: the same body, movable, is deep inside the floor and must report contacts.
        body.Immovable = false;

        IvpContact.Find(body, Floor(), contacts, Step, lookAhead: 0f, ref checks);

        contacts.ShouldNotBeEmpty();
    }

    /// <remarks>
    /// **The shallowest face wins, and this is the input that tells the two readings apart.** A
    /// point just inside the top of a floor block is barely below its top face and a long way above
    /// its bottom one; pushing it out by the DEEPEST face would drive it down through the block.
    /// </remarks>
    [Test]
    public void Penetration_JustInsideTheTopFace_PushesUp()
    {
        (Vector3 Normal, float Depth)? hit = Floor().Penetration(new Vector3(0f, 0f, -1f));

        hit.ShouldNotBeNull();
        hit.Value.Normal.Z.ShouldBe(1f, 1e-4f);
        hit.Value.Depth.ShouldBe(1f, 1e-3f);
    }

    /// <remarks>
    /// **Terrain is a triangle soup and stops a body exactly as a brush does.** This is the case
    /// the whole map failed on: `koth_harvest_final`'s brush hulls read perfectly — 3,030 ledges,
    /// none of them empty — and its corpses still fell to −24,000, because its ground is
    /// displacement terrain and the compiler puts none of that in `LUMP_PHYSCOLLIDE`.
    ///
    /// **The control is the tilt.** A flat triangle at z = 0 would be satisfied by a reader that
    /// ignored the plane and used the vertices' height; this one slopes, so the body must stop at
    /// the height of the surface UNDER it and not at the mesh's average or its lowest corner.
    /// </remarks>
    [Test]
    public void Simulate_WithATerrainSlope_StopsOnTheSurfaceBeneathIt()
    {
        IvpWorldCollision world = new();

        // A single slope rising one unit in ten across x, over the region the body falls through.
        world.AddTriangle(
            new Vector3(-500f, -500f, -50f),
            new Vector3(500f, -500f, 50f),
            new Vector3(500f, 500f, 50f));

        world.AddTriangle(
            new Vector3(-500f, -500f, -50f),
            new Vector3(500f, 500f, 50f),
            new Vector3(-500f, 500f, -50f));

        IvpEnvironment environment = new(Step) { World = world };

        // Dropped over x = 250, where the slope's own height is 25.
        IvpRigidBody body = Body(200f);

        body.Position = (250d, 0d, 200d);

        environment.Add(body);

        // **Six seconds, not two, and the extra four are a correction the owner spotted.** This ran
        // for 132 steps and asserted a POSITION WINDOW around where the body landed. When the
        // effective mass was fixed the impact stopped being under-applied, the body carried more of
        // its landing speed into a slide, and the window failed at x 284 — which was reported here
        // as the body walking uphill. It was not: *"are you sure its an uphill walk and not just a
        // body going up hill because it is newly dead and still slowing down?"* Measured at the old
        // cutoff it was still travelling 11.5 units a second and decelerating, from about six
        // hundred at touchdown. It was mid-slide, and the test was reading a stopwatch, not a
        // resting place.
        //
        // **So the assertion below is now about REST rather than about a spot**, which is the claim
        // this test always meant to make and a stronger one: a body held by friction stops, and
        // where it stops depends on how hard it arrived.
        for (int step = 0; step < 400; step++)
        {
            environment.Simulate();
        }


        // **Asserted against the surface UNDER it rather than against a predicted spot, and that is
        // a correction the first version of this test earned.** It predicted the body would rest
        // near x = 250 where the slope is 25 high, and measured 12.3 — because nothing here applies
        // surface friction, so a body on a slope slides down it and comes to rest lower. The code
        // was right and the prediction was wrong.
        //
        // **The missing friction is a real divergence and it is filed, not hidden by this
        // assertion**: `objectparams_t` carries a surface property per solid and the map's own
        // collision text carries a material table for exactly this, and neither is read yet. What
        // this test pins is that the body is ON the surface, which is what collision owes it.
        // **A RANGE, because how high a cube's centre sits depends on which part of it is down.**
        // The old `± 1` around a face-resting height happened to fit the residual penetration the
        // old solver left, and it reddened the moment each contact converged properly — the body
        // came to rest 1.03 above the plane instead of 1.00, which is a cube on an edge rather than
        // flat, and there is nothing here to make it lie flat.
        //
        // The bounds are arithmetic and cover every orientation of a 2-unit cube: at least the
        // surface itself, and at most half its diagonal, `sqrt(3)`, for a corner.
        double surface = body.Position.X / 10d;

        body.Position.Z.ShouldBeGreaterThan(surface, "on the slope rather than through it");

        // **Half the cube's diagonal for a corner rest, plus the solver's own slop.** The contact
        // slop is the distance a resting body is deliberately left clear of a surface rather than
        // driven onto it exactly — the same constant the lower bound's "not through it" relies on —
        // so a settled body sits up to that much above where geometry alone would put it. Measured
        // at 0.18, which is inside a slop of 0.25.
        body.Position.Z.ShouldBeLessThan(
            surface + Math.Sqrt(3d) + 0.25d, "and touching it, whichever way up it stopped");

        // **The speed assertion that used to be here is GONE, and it was actively harmful** (B306).
        //
        // It asserted `speed < 1` after 400 steps. That threshold was never measured from TF2 — it
        // is a prediction about a hand-built cube dropped 150 units onto two hand-built triangles,
        // and the owner named it: *"the test is probably not a good one, it probably doesn't sim
        // the engine correctly"*. Its own comment admitted it had "been wrong twice in opposite
        // directions", each time rewritten to match what the code then did, which is the one thing
        // `CLAUDE.md` says a parity test must never become.
        //
        // **It did not merely fail to help — it pointed the wrong way.** Tuning the terrain contact
        // threshold to satisfy it (requiring a body to be measurably inside a face before it is
        // held) took this number from 9.285662f to 6.621238f while burying a real corpse on
        // `cp_granary`: entity 2073 went from resting 39 above its own networked origin to 106
        // BELOW it. A green synthetic number and a body under the map, from the same change.
        //
        // **What survives is the claim that can be checked against the engine**: the body is ON the
        // surface and not through it, asserted above. How fast a cube is still moving after six
        // seconds is a MEASUREMENT (D38), and the instruments that own it are `corpse-drop` and a
        // real demo, where a corpse's resting height can be compared with `m_vecRagdollOrigin`.
    }

    /// <remarks>
    /// **The tunnelling case, reproduced from the map.** A corpse falling onto
    /// `koth_harvest_final` ended sixty-nine units under its floor while reporting sixty-two
    /// contacts, so the fault was never detection. The condition is a THIN brush and a body already
    /// at speed: a body at terminal velocity moves twelve units in a tick, and once a point is past
    /// a thin brush's midplane the shallowest face is the underside, so the push that should stop
    /// it drives it through.
    ///
    /// **A thick slab cannot show this** — the floor in the tests above is a hundred units deep, so
    /// a point never reaches its midplane and every reading passes. That is the "wrong condition"
    /// failure: an input for which correct and broken predict the same observation.
    /// </remarks>
    [Test]
    public void Simulate_FallingFastOntoAThinFloor_DoesNotPassThroughIt()
    {
        IvpEnvironment environment = new(Step) { World = Floor(depth: 16f) };

        IvpRigidBody body = Body(400f);

        // Already falling at terminal speed, which is the condition — a body released from rest
        // above a thin floor is caught on its first step and proves nothing.
        body.Velocity = (0f, 0f, -800f);

        environment.Add(body);

        for (int step = 0; step < 132; step++)
        {
            environment.Simulate();
        }

        body.Position.Z.ShouldBeGreaterThan(0d, "it must be ON the floor, not under it");
        body.Position.Z.ShouldBeLessThan(4d);
    }

    /// <summary>A body with a two-unit cube for a hull, at a height.</summary>
    private static IvpRigidBody Body(float height) =>
        new()
        {
            Position = (0d, 0d, height),
            InverseMass = 1f,
            Inertia = (10f, 10f, 10f),
            InverseInertia = (0.1f, 0.1f, 0.1f),
            Hull =
            [
                (-1f, -1f, -1f), (1f, -1f, -1f), (1f, 1f, -1f), (-1f, 1f, -1f),
                (-1f, -1f, 1f), (1f, -1f, 1f), (1f, 1f, 1f), (-1f, 1f, 1f),
            ],

            // **The cube's own faces, because a body without them is not a body the engine could
            // collide** (B306). A real hull arrives from the `.phy` with the ledge triangles beside
            // its points, and the narrow phase asks the engine's question with them — the hull
            // against a triangle. A fixture that supplied points alone silently exercised the
            // per-vertex path that is being removed, so it measured the substitute rather than the
            // thing under test.
            //
            // Wound outward, two triangles per face, so a face normal from
            // `cross(b - a, c - a)` points out of the cube.
            Faces =
            [
                (0, 3, 2), (0, 2, 1),
                (4, 5, 6), (4, 6, 7),
                (0, 1, 5), (0, 5, 4),
                (2, 3, 7), (2, 7, 6),
                (1, 2, 6), (1, 6, 5),
                (0, 4, 7), (0, 7, 3),
            ],
        };

    /// <summary>A slab whose top face is z = 0, given in IVP metres as a real hull would be.</summary>
    /// <remarks>
    /// **Written in metres and converted by the world, not handed over in Source units**, because
    /// the conversion at that seam is one of the things this suite is here to keep honest: a hull
    /// that arrived already converted would pass every test while the real one was 39 times too
    /// small.
    /// </remarks>
    private static IvpWorldCollision Floor(
        float depth = 100f, int contents = IvpWorldCollision.ContentsSolid)
    {
        const float Wide = 1000f;

        // **Authored in SOURCE units and axes, then stored the way a real file stores it.** A `.phy`
        // and a map's collision lump hold points in IVP's own convention — metres, and Y up where
        // Source has Z up — so a fixture written directly in Source axes describes a file that does
        // not exist, and it passed while the reader converted nothing but the units.
        List<Vector3> points =
        [
            Ivp(-Wide, -Wide, -depth), Ivp(Wide, -Wide, -depth),
            Ivp(Wide, Wide, -depth), Ivp(-Wide, Wide, -depth),
            Ivp(-Wide, -Wide, 0f), Ivp(Wide, -Wide, 0f),
            Ivp(Wide, Wide, 0f), Ivp(-Wide, Wide, 0f),
        ];

        // Wound so every normal points OUT of the slab, which is what makes "behind every plane"
        // mean "inside".
        List<(int A, int B, int C)> triangles =
        [
            (4, 5, 6), (4, 6, 7),       // top,    +Z
            (0, 2, 1), (0, 3, 2),       // bottom, -Z
            (0, 1, 5), (0, 5, 4),       // -Y
            (2, 3, 7), (2, 7, 6),       // +Y
            (1, 2, 6), (1, 6, 5),       // +X
            (3, 0, 4), (3, 4, 7),       // -X
        ];

        IvpWorldCollision world = new();

        world.Add(
            points,
            triangles,
            Ivp(0f, 0f, -depth / 2f),
            Wide * 2f * Metre,
            Vector3.Zero,
            contents);

        return world;
    }

    /// <summary>A point in Source units, as an <c>IVPS</c> section would store it.</summary>
    /// <remarks>
    /// **The inverse of <see cref="IvpWorldCollision.ToSource"/>, written out rather than called**,
    /// so this fixture cannot agree with a wrong reader by sharing its arithmetic. Metres, Y up,
    /// and the handedness flip on the remaining axis.
    /// </remarks>
    private static Vector3 Ivp(float x, float y, float z) =>
        new(x * Metre, z * Metre, -y * Metre);

    /// <summary>Metres per Source unit — <c>METERS_PER_INCH</c>.</summary>
    private const float Metre = 0.0254f;
}
