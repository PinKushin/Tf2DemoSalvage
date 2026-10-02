using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Prediction;

/// <summary><c>UTIL_TraceRay</c> for a player box placed by its origin and bounds; null when there is no world (D205).</summary>
/// <param name="start">The box's origin at the start.</param>
/// <param name="end">Its origin at the end.</param>
/// <param name="mins">Its low corner relative to the origin.</param>
/// <param name="maxs">Its high corner.</param>
/// <param name="mask">The contents that stop it.</param>
/// <returns>The trace, its fraction of the whole move; null with no world to trace against.</returns>
public delegate BspTrace? PlayerTraceRay(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, int mask);

/// <summary>The local player's movement state as <c>CGameMovement</c> reads and writes it (D205).</summary>
/// <remarks>
/// C# names for <c>C_TFPlayer</c>'s fields (D163): <c>GetAbsOrigin</c>, <c>m_vecVelocity</c>, <c>m_hGroundEntity</c>
/// as <see cref="OnGround"/>, <c>m_Local.m_bDucked</c>, <c>m_bDucking</c>, <c>m_bInDuckJump</c>,
/// <c>m_flDucktime</c>, <c>m_flDuckJumpTime</c>, <c>m_flJumpTime</c>, <c>FL_DUCKING</c>, <c>m_nOldButtons</c>,
/// <c>m_flMaxspeed</c>, <c>m_surfaceFriction</c>, <c>m_flFallVelocity</c>, and <c>m_Shared</c>'s air dash, air duck
/// count and duck timer.
/// </remarks>
public record struct PredictedPlayer
{
    public PredictedPlayer()
    {
    }

    public Vector3 Origin { get; set; }

    public Vector3 Velocity { get; set; }

    public Vector3 BaseVelocity { get; set; }

    public bool OnGround { get; set; }

    public bool Ducked { get; set; }

    public bool Ducking { get; set; }

    public bool InDuckJump { get; set; }

    public bool FlDucking { get; set; }

    public float DuckTime { get; set; }

    public float DuckJumpTime { get; set; }

    public float JumpTime { get; set; }

    public uint OldButtons { get; set; }

    public float OldForwardMove { get; set; }

    /// <summary><c>m_flMaxspeed</c>: the server's <c>TeamFortress_CalculateMaxSpeed</c>, networked — class, conditions and attributes already in it.</summary>
    public float MaxSpeed { get; set; }

    public float SurfaceFriction { get; set; } = 1f;

    public float FallVelocity { get; set; }

    public int PlayerClass { get; set; }

    public PlayerConditions Conditions { get; set; }

    /// <summary><c>m_Shared.m_nPlayerState</c>; null reads as <c>TF_STATE_ACTIVE</c>.</summary>
    public int? PlayerState { get; set; }

    public bool IsDead { get; set; }

    /// <summary><c>GetWaterLevel()</c> as last networked; above <c>WL_Feet</c> (1) the swim code runs, which is not ported.</summary>
    public int WaterLevel { get; set; }

    public int AirDash { get; set; }

    public int AirDucked { get; set; }

    /// <summary><c>m_Shared.GetDuckTimer()</c>, in <see cref="CurTime"/>'s seconds.</summary>
    public float DuckTimer { get; set; }

    /// <summary><c>gpGlobals->curtime</c>: <c>m_nTickBase · TICK_INTERVAL</c> during prediction.</summary>
    public float CurTime { get; set; }
}

/// <summary>The replicated ConVars <c>CGameMovement</c> reads, as the server set them (D106, D205).</summary>
public sealed record MovementConVars(
    float Gravity,
    float StopSpeed,
    float Accelerate,
    float AirAccelerate,
    float Friction,
    float Bounce,
    float MaxVelocity,
    float StepSize,
    float ForwardSpeed,
    float BackSpeed,
    float SideSpeed)
{
    /// <summary>Valve's declared defaults (<c>movevars_shared.cpp</c>, <c>in_main.cpp:76-79</c>).</summary>
    public static MovementConVars Defaults { get; } = From(new ServerConVars());

    /// <summary>The values a demo's server sent, falling back to the defaults.</summary>
    /// <param name="server">The demo's ConVars.</param>
    /// <returns>The movement set.</returns>
    public static MovementConVars From(ServerConVars server)
    {
        System.ArgumentNullException.ThrowIfNull(server);

        return new MovementConVars(
            server.Number("sv_gravity"),
            server.Number("sv_stopspeed"),
            server.Number("sv_accelerate"),
            server.Number("sv_airaccelerate"),
            server.Number("sv_friction"),
            server.Number("sv_bounce"),
            server.Number("sv_maxvelocity"),
            server.Number("sv_stepsize"),
            server.Number("cl_forwardspeed"),
            server.Number("cl_backspeed"),
            server.Number("cl_sidespeed"));
    }
}
