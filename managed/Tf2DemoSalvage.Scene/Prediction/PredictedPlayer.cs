using System;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
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

    /// <summary><c>m_Local.m_bAllowAutoMovement</c>: true as <c>CBasePlayer::Spawn</c> leaves it.</summary>
    public bool AllowAutoMovement { get; set; } = true;

    /// <summary><c>GetTeamNumber()</c>: which enemy contents <c>PlayerSolidMask</c> adds (<c>tf_gamemovement.cpp:269-283</c>).</summary>
    public int? Team { get; set; }

    /// <summary><c>entindex()</c>, which staggers <c>CheckInterval</c> between players (<c>gamemovement.cpp:695</c>).</summary>
    public int EntityIndex { get; set; }

    /// <summary><c>CTFGameMovement::m_isPassingThroughEnemies</c>, set by <c>CheckStuck</c> (<c>tf_gamemovement.cpp:1404</c>).</summary>
    public bool PassingThroughEnemies { get; set; }

    /// <summary><c>m_StuckLast</c>: the next <c>rgv3tStuckTable</c> entry to try (<c>gamemovement.cpp:3362</c>).</summary>
    public int StuckLast { get; set; }

    /// <summary>
    /// <c>m_pSurfaceData</c>: the ground's surfaceprop as <c>CategorizeGroundSurface</c> last set it; null for none, which reads as
    /// factors of 1 (<c>gamemovement.cpp:1004</c>, <c>tf_gamemovement.cpp:1279</c>).
    /// </summary>
    public VphysicsSurface? Surface { get; set; }

    /// <summary><c>GetGravity()</c>, <c>m_flGravity</c>; 0 reads as 1.</summary>
    public float Gravity { get; set; }
}

/// <summary>What <c>CTFGameMovement</c> asks the player's items: <c>CALL_ATTRIB_HOOK_*_ON_OTHER</c> and <c>OwnerCanJump</c> (B450).</summary>
/// <param name="OnPlayer">The hook on <c>m_pTFPlayer</c>: attribute class and value in, hooked value out.</param>
/// <param name="OnActiveWeapon">The hook on <c>GetActiveTFWeapon()</c>; null for no active weapon, which hooks nothing.</param>
/// <param name="OwnerCanJump">The active weapon's <c>OwnerCanJump()</c>; true without one.</param>
public sealed record MovementItems(Func<string, float, float> OnPlayer, Func<string, float, float>? OnActiveWeapon, bool OwnerCanJump)
{
    /// <summary>No attributes and no weapon.</summary>
    public static MovementItems None { get; } = new((_, value) => value, null, true);

    /// <summary><c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( pWpn, … )</c>, skipped without a weapon.</summary>
    /// <param name="attributeClass">The hook.</param>
    /// <param name="value">The value hooked.</param>
    /// <returns>The hooked value.</returns>
    public float OnWeapon(string attributeClass, float value) => OnActiveWeapon?.Invoke(attributeClass, value) ?? value;
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
