using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><see cref="DecalReplay"/>: decals are state, placed in tick order and replayed from the start on a seek back.</summary>
public sealed class DecalReplayTests
{
    private static readonly DecalMaterial Hole = new("decals/concrete/shot1_subrect", 64, 64, 0.16f);

    /// <summary>Two bullets into the wall at x = 0, ticks 10 and 20, far enough apart not to overlap.</summary>
    private static readonly ShotImpact[] Impacts =
    [
        new(0, 0, 10, 5, 2, (100f, 100f, 100f), (0f, 100f, 100f), (-100f, 100f, 100f), 0),
        new(1, 0, 20, 5, 2, (100f, 300f, 300f), (0f, 300f, 300f), (-100f, 300f, 300f), 0),
    ];

    [Test]
    public void AdvanceTo_BetweenTwoImpacts_PlacesTheFirst()
    {
        DecalReplay replay = Replay([]);

        replay.AdvanceTo(15, static _ => false);

        replay.Decals.Count.ShouldBe(1);
    }

    [Test]
    public void AdvanceTo_BackwardsPastAnImpact_RemovesIt()
    {
        DecalReplay replay = Replay([]);

        replay.AdvanceTo(25, static _ => false);
        replay.AdvanceTo(15, static _ => false);

        replay.Decals.Count.ShouldBe(1);
    }

    [Test]
    public void AdvanceTo_ABulletAPlayerStopped_PlacesNothing()
    {
        DecalReplay replay = Replay([]);

        replay.AdvanceTo(25, static impact => impact.Shot == 0);

        replay.Decals.Count.ShouldBe(1);
    }

    /// <remarks>
    /// `Impact()` (`fx_impact.cpp:149`): entity 0 with a nonzero hitbox is a static prop, and goes to
    /// `AddDecalToStaticProp` alone — never into the brushes behind it.
    /// </remarks>
    [Test]
    public void AdvanceTo_ABulletAStaticPropStopped_PlacesNoWorldDecal()
    {
        DecalReplay replay = new(
            new WorldDecals(WorldDecalsConformanceTests.Wall()),
            // A prop whose model declares no `$surfaceprop` is a prop all the same.
            [Impacts[0] with { StaticProp = 4 }, Impacts[1]],
            [],
            static _ => Hole,
            static _ => null);

        replay.AdvanceTo(25, static _ => false);

        replay.Decals.Count.ShouldBe(1);
    }

    /// <remarks>
    /// The bullet stops on prop 4's face at x = 0, which faces +x; the decal is cut to its 64 · 0.16 = 10.24 square and
    /// held under the prop's lump index, and a seek back takes it away with the world's.
    /// </remarks>
    [Test]
    public void AdvanceTo_ABulletAStaticPropStopped_DecalsTheProp()
    {
        WorldVertex[] face =
        [
            new(0f, -100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 0f, 100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
        ];

        DecalReplay replay = new(
            new WorldDecals(WorldDecalsConformanceTests.Wall()),
            [Impacts[0] with { StudioSurfaceProp = 3, StaticProp = 4, End = (0f, 0f, 0f), Normal = (1f, 0f, 0f) }],
            [],
            static _ => Hole,
            static _ => null,
            props: new StaticPropDecalSource(prop => prop == 4 ? face : null, static _ => 9));

        replay.AdvanceTo(15, static _ => false);

        (IReadOnlyList<WorldVertex> vertices, IReadOnlyList<WorldBatch> batches) =
            replay.PropDecals.For(DecalReplay.StaticPropKey(4)).ShouldNotBeNull();
        vertices.Count.ShouldBe(6);
        batches[0].MaterialIndex.ShouldBe(9);
        System.MathF.Abs(vertices[0].Y).ShouldBe(5.12f, 1e-4f);

        replay.AdvanceTo(5, static _ => false);

        replay.PropDecals.Count.ShouldBe(0);
    }

    /// <remarks>
    /// `CStudioRender` holds ONE pool for every model, static props and entities alike, so a prop's decal shares the
    /// `1.5 · r_maxmodeldecal` limit with a player's — and a seek back replays the props' without touching the entities'.
    /// </remarks>
    [Test]
    public void AdvanceTo_BackwardsWithASharedPool_ClearsOnlyThePropsDecals()
    {
        ModelDecals pool = new();
        WorldVertex[] face =
        [
            new(0f, -100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 0f, 100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
        ];

        pool.AddClipped(7, face, System.Numerics.Vector3.Zero, new System.Numerics.Vector3(-1.1f, 0f, 0f), 4f, 1).ShouldBeTrue();

        DecalReplay replay = new(
            new WorldDecals(WorldDecalsConformanceTests.Wall()),
            [Impacts[0] with { StaticProp = 4, End = (0f, 0f, 0f), Normal = (1f, 0f, 0f) }],
            [],
            static _ => Hole,
            static _ => null,
            props: new StaticPropDecalSource(prop => prop == 4 ? face : null, static _ => 9),
            propDecals: pool);

        replay.AdvanceTo(15, static _ => false);
        pool.Count.ShouldBe(2);

        replay.AdvanceTo(5, static _ => false);

        pool.Count.ShouldBe(1);
        pool.For(7).ShouldNotBeNull();
    }

    /// <remarks>`C_TEWorldDecal` shoots at `m_vecOrigin`; a `CTEDecal` on the world with no hitbox does the same.</remarks>
    [Test]
    public void AdvanceTo_WorldDecalEvents_ArePlaced()
    {
        DecalReplay replay = Replay(
        [
            new SceneDecal(3, SceneDecalKind.World, (0f, 100f, 400f), default, 0, 0, 7, 0),
            new SceneDecal(4, SceneDecalKind.Entity, (0f, 400f, 100f), (8f, 400f, 100f), 0, 0, 7, 0),
        ]);

        replay.AdvanceTo(5, static _ => false);

        replay.Decals.Count.ShouldBe(2);
    }

    /// <remarks>A static prop (world, hitbox), another entity, and a spray are each a path not built.</remarks>
    [Test]
    public void AdvanceTo_DecalsOffTheWorldsBrushes_AreNotPlaced()
    {
        DecalReplay replay = Replay(
        [
            new SceneDecal(3, SceneDecalKind.Entity, (0f, 100f, 400f), default, 0, 12, 7, 0),
            new SceneDecal(3, SceneDecalKind.Entity, (0f, 100f, 400f), default, 40, 0, 7, 0),
            new SceneDecal(3, SceneDecalKind.Player, (0f, 100f, 400f), default, 0, 0, 0, 2),
        ]);

        replay.AdvanceTo(5, static _ => false);

        replay.Decals.Count.ShouldBe(0);
    }

    private static DecalReplay Replay(IReadOnlyList<SceneDecal> events) =>
        new(
            new WorldDecals(WorldDecalsConformanceTests.Wall()),
            Impacts,
            events,
            static _ => Hole,
            static index => index == 7 ? Hole : null);
}
