using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The rope holiday lights: <c>CRopeManager::IsHolidayLightMode</c> (`c_rope.cpp:637-680`) and the
/// <c>TF_HolidayLight</c> effect's temp entities (`tf_fx_christmaslights.cpp`) (B478).
/// </summary>
public sealed class RopeHolidayLightsConformanceTests
{
    private static readonly Vector3 Player = new(0f, 0f, 3000f);

    private static readonly Vector3 NoSky = Vector3.Zero;

    /// <remarks>
    /// **The gate, in the engine's order**: <c>r_ropes_holiday_lights_allowed</c>, then TF's powerup mode and the map's
    /// <c>ropes_holiday_lights_allowed</c>, then Christmas — latched the first time it is asked with game rules present —
    /// and Pyrovision on top, which turns the lights on in style 1 whatever the date.
    /// </remarks>
    [TestCase(true, false, true, true, false, 0)]
    [TestCase(true, false, true, false, false, null)]
    [TestCase(true, false, true, false, true, 1)]
    [TestCase(true, true, true, true, true, null)]
    [TestCase(true, false, false, true, true, null)]
    [TestCase(false, false, true, true, false, null)]
    [TestCase(false, true, false, false, true, 1)]
    public void Style_TheGate_IsTheRopeManagersOrder(
        bool rulesPresent, bool powerup, bool mapAllows, bool christmas, bool pyrovision, int? style)
    {
        new RopeHolidayMode().Style(rulesPresent, powerup, mapAllows, () => christmas, pyrovision).ShouldBe(style);
    }

    /// <remarks>
    /// **Christmas is asked once**: <c>m_bHolidayInitialized</c> is set the first time game rules exist and never cleared
    /// (`c_rope.cpp:659-663`), so a later date — or a forced holiday turned off — changes nothing.
    /// </remarks>
    [Test]
    public void Style_ChristmasAfterTheFirstAnswer_IsLatched()
    {
        RopeHolidayMode mode = new();
        int asked = 0;

        mode.Style(true, false, true, () => { asked++; return true; }, false).ShouldBe(0);
        mode.Style(true, false, true, () => { asked++; return false; }, false).ShouldBe(0);

        asked.ShouldBe(1);
    }

    /// <remarks>
    /// **A light is made the frame after it is dispatched** — <c>AddHolidayLight</c> queues, <c>CHolidayLightManager::Update</c>
    /// creates — **coloured in turn** red, (2, 110, 197), (117, 193, 8), (255, 151, 29), the global counter advancing per
    /// light (`tf_fx_christmaslights.cpp:21-30`, `:165-174`).
    /// </remarks>
    [Test]
    public void Update_FiveNewLights_TakeTheFourColoursInTurn()
    {
        RopeHolidayLights lights = new();

        for (int sub = 0; sub < 5; sub++)
        {
            lights.Add(new RopeHolidayDispatch(1, sub, new Vector3(sub, 0f, 3000f), 0.055f), Player, NoSky);
        }

        lights.Lights.ShouldBeEmpty("queued, not yet made");

        lights.Update(curtime: 10f, style: 0);

        lights.Lights.Select(light => light.Colour).ShouldBe(
        [
            ((byte)255, (byte)0, (byte)0, (byte)255),
            ((byte)2, (byte)110, (byte)197, (byte)255),
            ((byte)117, (byte)193, (byte)8, (byte)255),
            ((byte)255, (byte)151, (byte)29, (byte)255),
            ((byte)255, (byte)0, (byte)0, (byte)255),
        ]);
        lights.Lights.ShouldAllBe(light => light.Material == RopeHolidayLights.BulbMaterial);
        lights.Lights.Select(light => light.Scale).ShouldAllBe(scale => Math.Abs(scale - 0.055f) < 1e-7f);
    }

    /// <remarks>
    /// **Too far from the local player, or within 2000 of the 3D sky's origin, and the light is not queued**
    /// (`tf_fx_christmaslights.cpp:94-104`); no local player, none at all.
    /// </remarks>
    [Test]
    public void Add_FarFromThePlayerOrNearTheSkyOrWithNoPlayer_QueuesNothing()
    {
        RopeHolidayLights lights = new();

        lights.Add(new RopeHolidayDispatch(1, 0, Player + new Vector3(2001f, 0f, 0f), 0.055f), Player, NoSky);
        lights.Add(new RopeHolidayDispatch(1, 1, new Vector3(0f, 0f, 1999f), 0.055f), new Vector3(0f, 0f, 1000f), NoSky);
        lights.Add(new RopeHolidayDispatch(1, 2, Player, 0.055f), null, NoSky);
        lights.Add(new RopeHolidayDispatch(1, 3, Player + new Vector3(2000f, 0f, 0f), 0.055f), Player, NoSky);

        lights.Update(curtime: 1f, style: 0);

        lights.Lights.Select(light => light.Origin).ShouldBe([Player + new Vector3(2000f, 0f, 0f)]);
    }

    /// <remarks>
    /// **An existing light is found by its ids and moved, never remade**, and one in every fifth rope blinks: alpha 255
    /// while <c>( sub + (int)( curtime · 2 ) ) % ( colour + 5 )</c> is under 4, else 64 (`tf_fx_christmaslights.cpp:182-202`).
    /// </remarks>
    [Test]
    public void Update_AnExistingLightOnAFifthRope_MovesAndBlinks()
    {
        RopeHolidayLights lights = new();

        lights.Add(new RopeHolidayDispatch(5, 0, Player, 0.055f), Player, NoSky);
        lights.Update(curtime: 0f, style: 0);

        // Colour 0 (red): the cycle is ( 0 + (int)( t · 2 ) ) % 5 — 4 at t = 2.0, so 64; 0 at t = 2.5, so 255.
        Vector3 moved = Player + Vector3.UnitX;

        lights.Add(new RopeHolidayDispatch(5, 0, moved, 0.055f), Player, NoSky);
        lights.Update(curtime: 2f, style: 0);

        lights.Lights.Count.ShouldBe(1);
        lights.Lights[0].Origin.ShouldBe(moved);
        lights.Lights[0].Colour.A.ShouldBe((byte)64);

        lights.Add(new RopeHolidayDispatch(5, 0, moved, 0.055f), Player, NoSky);
        lights.Update(curtime: 2.5f, style: 0);

        lights.Lights[0].Colour.A.ShouldBe((byte)255);
    }

    /// <remarks>
    /// **`FTENT_NEVERDIE` keeps a light until the level ends** — <c>IsActive</c> answers <c>die != 0</c>
    /// (`c_te_legacytempents.cpp:286-290`), and a looping animation never zeroes it — so a light no longer dispatched stays
    /// where it was.
    /// </remarks>
    [Test]
    public void Update_ALightNoLongerDispatched_StaysWhereItWas()
    {
        RopeHolidayLights lights = new();

        lights.Add(new RopeHolidayDispatch(1, 0, Player, 0.055f), Player, NoSky);
        lights.Update(curtime: 0f, style: 0);
        lights.Update(curtime: 100f, style: 0);

        lights.Lights.Count.ShouldBe(1);

        lights.Clear();

        lights.Lights.ShouldBeEmpty();
    }

    /// <remarks>
    /// **Pyrovision's style is <c>effects/mtp_fluff</c>, upright** (<c>vec3_angle</c>), where the bulbs take a random roll
    /// in ±180 (`tf_fx_christmaslights.cpp:143-156`).
    /// </remarks>
    [Test]
    public void Update_InStyleOne_MakesUnrolledFluff()
    {
        RopeHolidayLights lights = new();

        lights.Add(new RopeHolidayDispatch(1, 0, Player, 0.055f), Player, NoSky);
        lights.Add(new RopeHolidayDispatch(1, 1, Player, 0.055f), Player, NoSky);
        lights.Update(curtime: 0f, style: 1);

        lights.Lights.ShouldAllBe(light => light.Material == RopeHolidayLights.FluffMaterial && light.Roll == 0f);
    }

    /// <remarks>
    /// **At most <c>MAX_TEMP_ENTITIES / 2</c> are queued in a frame** (`tf_fx_christmaslights.cpp:116`), 250.
    /// </remarks>
    [Test]
    public void Add_PastHalfTheTempEntities_QueuesNoMore()
    {
        RopeHolidayLights lights = new();

        for (int sub = 0; sub < 300; sub++)
        {
            lights.Add(new RopeHolidayDispatch(1, sub, Player, 0.055f), Player, NoSky);
        }

        lights.Update(curtime: 0f, style: 0);

        lights.Lights.Count.ShouldBe(250);
    }

    /// <remarks>**The lights draw as their material's sprites**: one quad per light, coloured by the vertex.</remarks>
    [Test]
    public void Batches_ALight_IsOneQuadOfItsMaterial()
    {
        RopeHolidayLights lights = new();
        MapTexture texture = new(16, 16, 16, 16, TextureImage.None, IsTransparent: true);
        Dictionary<string, EngineSprite> sprites = new(StringComparer.OrdinalIgnoreCase)
        {
            [RopeHolidayLights.BulbMaterial] = EngineSprite.Init(
                texture, [], SpriteBlend.Translucent, SpriteOrientation.ParallelOriented, null, (1f, 1f, 1f, 1f), true,
                "UnlitGeneric", null, vertexColour: true, vertexAlpha: true),
        };

        lights.Add(new RopeHolidayDispatch(1, 0, Player, 0.055f), Player, NoSky);
        lights.Update(curtime: 0f, style: 0);

        IReadOnlyList<ParticleBatch> batches = lights.Batches(sprites, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);

        batches.Count.ShouldBe(1);
        batches[0].Corners.Count.ShouldBe(6);
        batches[0].Corners[0].Red.ShouldBe(1f);
        batches[0].Corners[0].Green.ShouldBe(0f);

        float width = Vector3.Distance(
            new Vector3(batches[0].Corners[1].X, batches[0].Corners[1].Y, batches[0].Corners[1].Z),
            new Vector3(batches[0].Corners[2].X, batches[0].Corners[2].Y, batches[0].Corners[2].Z));

        width.ShouldBe(16f * 0.055f, 0.001f, "a texel per unit, times m_flSpriteScale");
    }
}
