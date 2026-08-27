using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// That the free camera flies at the recording server's speeds rather than at two constants.
/// </summary>
/// <remarks>
/// **D106, and the reason it is not tidiness.** The owner: *"the cvars can change by server, some
/// mods will change move speed and all the other settings for the most part, like jailbreak. the
/// only mods we might currently work with are DM and MGE, because those keep most things constant,
/// but jump, surf, and other mods might not run right."*
///
/// `sv_maxspeed` and `sv_specspeed` are what `FullNoClipMove` multiplies (`gamemovement.cpp:2260`),
/// both are `FCVAR_REPLICATED`, and a server that changes them sends the new values in the demo.
/// Until this landed the values arrived, decoded correctly, and were ignored — which is silent,
/// because a camera at the wrong speed still looks like a camera.
///
/// **These predict exact numbers, not "faster".** A camera that got 1.5× when it should get 3× is
/// the defect this replaces, and `ShouldBeGreaterThan` would pass against it.
/// </remarks>
public sealed class FreeFlightServerSpeedTests
{
    /// <summary>A jump server, doubling both terms of the spectator ceiling.</summary>
    private static ServerConVars JumpServer()
    {
        ServerConVars server = new();

        server.Apply(new SetConVarMessage(
        [
            new KeyValuePair<string, string>("sv_maxspeed", "640"),
            new KeyValuePair<string, string>("sv_specspeed", "6"),
        ]));

        return server;
    }

    [Test]
    public void SpeedPerSecond_OnAServerThatDoubledBothTerms_IsFourTimesValves()
    {
        FreeFlightPath.SpeedPerSecond(FreeFlightPath.Shipped).ShouldBe(960f);
        FreeFlightPath.SpeedPerSecond(JumpServer()).ShouldBe(3840f);
    }

    /// <summary>That a raised forward speed cannot beat the ceiling.</summary>
    /// <remarks>
    /// **The clamp is what makes one number serve every axis** (B215), and it is only visible once
    /// a server is in play. At Valve's defaults `cl_forwardspeed * 1.5` = 675 sits under the 960
    /// ceiling by luck; a server that raises `cl_forwardspeed` alone must still be held to
    /// `sv_maxspeed * sv_specspeed`, because `FullNoClipMove` computes `maxspeed` from those two and
    /// clamps the requested move to it.
    /// </remarks>
    [Test]
    public void WalkSpeed_WhenTheServerRaisedOnlyForwardSpeed_IsHeldToTheCeiling()
    {
        ServerConVars server = new();

        server.Apply(new SetConVarMessage(
            [new KeyValuePair<string, string>("cl_forwardspeed", "5000")]));

        FreeFlightPath.WalkSpeed(server).ShouldBe(960f, "the spectator ceiling is unchanged");
        FreeFlightPath.WalkSpeed(FreeFlightPath.Shipped).ShouldBe(675f);
    }

    [Test]
    public void Movement_ForOneSecondUnderAJumpServer_TravelsThatServersSpeed()
    {
        (float X, float Y, float Z) moved = FreeFlightPath.Movement(
            new FlightInput(Forward: 1f, Right: 0f, Up: 0f, Walk: false),
            seconds: 1d,
            pitch: 0f,
            yaw: 0f,
            JumpServer());

        moved.X.ShouldBe(3840f, 0.01);
    }

    /// <summary>That the controller uses the server it was given, and not before.</summary>
    /// <remarks>
    /// **The wiring assertion.** `FreeFlightPath` being able to fly at a server's speed says nothing
    /// about whether the controller ever passes one — and a unit-tested component nothing calls is
    /// the shape this project has shipped three times with a green suite. The precondition is set
    /// to the OPPOSITE state first, so a `SetServer` with an empty body cannot pass it.
    /// </remarks>
    /// <summary>One frame's worth of flight, which the controller clamps to.</summary>
    /// <remarks>
    /// **`Fly` caps a frame at <see cref="FreeCameraController.MaximumFrameSeconds"/>**, so asking
    /// for a whole second measures the clamp rather than the speed. Using the cap itself keeps the
    /// arithmetic exact and keeps the subject the speed: one tenth of 960 is 96.
    /// </remarks>
    private const double OneFrame = FreeCameraController.MaximumFrameSeconds;

    [Test]
    public void Fly_AfterTheDemosServerIsApplied_MovesAtThatServersSpeed()
    {
        // **Angles pinned, because the camera's default pitch is not zero** — it starts looking
        // slightly down, so forward is not along +X and a distance read off X alone would be
        // measuring the pitch.
        FreeCameraController camera = new(NullLogger.Instance) { Angles = (0f, 0f) };

        camera.Fly(Forward, OneFrame, ifUnplaced: (0f, 0f, 0f));
        camera.Origin.ShouldNotBeNull();
        camera.Origin.Value.X.ShouldBe(96f, 0.01, "Valve's defaults until a demo says otherwise");

        camera.SetServer(JumpServer());

        camera.Fly(Forward, OneFrame, ifUnplaced: (0f, 0f, 0f));
        camera.Origin.Value.X.ShouldBe(96f + 384f, 0.01);
    }

    /// <summary>That a demo whose server changed nothing leaves the camera at Valve's speed.</summary>
    /// <remarks>
    /// The control for the test above: with only one server in play, "applied it" and "changed the
    /// speed to something" are the same observation.
    /// </remarks>
    [Test]
    public void Fly_AfterAVanillaServerIsApplied_StillMovesAtValvesSpeed()
    {
        FreeCameraController camera = new(NullLogger.Instance) { Angles = (0f, 0f) };

        camera.SetServer(new ServerConVars());

        camera.Fly(Forward, OneFrame, ifUnplaced: (0f, 0f, 0f));
        camera.Origin.ShouldNotBeNull();
        camera.Origin.Value.X.ShouldBe(96f, 0.01);
    }

    private static FlightInput Forward =>
        new(Forward: 1f, Right: 0f, Up: 0f, Walk: false);
}
